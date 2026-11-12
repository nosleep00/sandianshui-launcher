using SandianshuiLauncher.Core.Models;
using SandianshuiLauncher.Core.Utils;
using System.IO;
using System.Text.Json;

namespace SandianshuiLauncher.Core.Services;

/// <summary>
/// 游戏安装器：下载版本 JSON、客户端 jar、依赖库、原生库与资源文件。
/// </summary>
public class GameInstaller
{
    private const string AssetsHost = "https://resources.download.minecraft.net";

    private readonly HttpClient _http;
    private readonly DownloadSourceProvider _sourceProvider;
    private readonly DownloadService _downloadService;
    private readonly DownloadQueue? _queue;

    public GameInstaller(
        HttpClient http,
        DownloadSourceProvider sourceProvider,
        DownloadService downloadService,
        DownloadQueue? queue = null)
    {
        _http = http;
        _sourceProvider = sourceProvider;
        _downloadService = downloadService;
        _queue = queue;
    }

    /// <summary>
    /// 安装清单中的一个版本（含父版本 JSON、jar、库、资源）。
    /// </summary>
    public async Task InstallAsync(
        ManifestEntry entry,
        VersionManifest manifest,
        string gameDirectory,
        int threads,
        IProgress<DownloadProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(Path.Combine(gameDirectory, "versions"));

        // 1. 确保整条继承链的版本 JSON 都在本地。
        await EnsureVersionJsonAsync(entry, manifest, gameDirectory, cancellationToken);

        // 2. 递归读取并合并 JSON。
        VersionJson merged = await LoadMergedAsync(entry.Id, gameDirectory, cancellationToken);

        // 3. 收集下载任务。
        List<DownloadTask> tasks = await BuildDownloadTasksAsync(merged, gameDirectory, cancellationToken);

        // 4. 并发下载（任务入持久队列，关闭启动器后下次可继续）。
        _queue?.Enqueue(tasks);
        try
        {
            await _downloadService.DownloadAsync(tasks, threads, progress, cancellationToken);
            _queue?.RemoveCompleted(tasks);
        }
        catch
        {
            throw;
        }
    }

    /// <summary>确保版本 JSON（含父版本）存在于本地 versions 目录。</summary>
    public async Task EnsureVersionJsonAsync(
        ManifestEntry entry,
        VersionManifest manifest,
        string gameDirectory,
        CancellationToken cancellationToken)
    {
        string dir = Path.Combine(gameDirectory, "versions", entry.Id);
        string jsonPath = Path.Combine(dir, entry.Id + ".json");
        Directory.CreateDirectory(dir);

        if (!VersionJsonFileIsValid(jsonPath, entry.Sha1))
        {
            await DownloadVersionJsonWithVerificationAsync(entry, jsonPath, cancellationToken);
        }

        VersionJson json = VersionService.ReadJsonFile(jsonPath);
        if (!string.IsNullOrEmpty(json.InheritsFrom))
        {
            string parentId = json.InheritsFrom!;
            string parentPath = Path.Combine(gameDirectory, "versions", parentId, parentId + ".json");
            if (!File.Exists(parentPath))
            {
                var parentEntry = manifest.Versions.FirstOrDefault(v => v.Id == parentId)
                    ?? throw new InvalidDataException($"版本清单中找不到父版本：{parentId}");
                await EnsureVersionJsonAsync(parentEntry, manifest, gameDirectory, cancellationToken);
            }
        }
    }

    /// <summary>递归读取并合并整条继承链。</summary>
    public async Task<VersionJson> LoadMergedAsync(string id, string gameDirectory, CancellationToken cancellationToken)
    {
        string jsonPath = Path.Combine(gameDirectory, "versions", id, id + ".json");
        VersionJson json = VersionService.ReadJsonFile(jsonPath);
        if (string.IsNullOrEmpty(json.InheritsFrom))
        {
            return json;
        }
        VersionJson parent = await LoadMergedAsync(json.InheritsFrom!, gameDirectory, cancellationToken);
        return VersionJsonMerger.Merge(json, parent);
    }

    /// <summary>根据合并后的版本 JSON 收集全部下载任务。</summary>
    public async Task<List<DownloadTask>> BuildDownloadTasksAsync(
        VersionJson merged,
        string gameDirectory,
        CancellationToken cancellationToken)
    {
        var tasks = new List<DownloadTask>();
        string librariesDir = Path.Combine(gameDirectory, "libraries");
        string versionsDir = Path.Combine(gameDirectory, "versions");

        // 1. 客户端 jar（使用 jar 字段指向的版本，没有则用 id）。
        string jarId = merged.Jar ?? merged.Id ?? throw new InvalidDataException("版本 JSON 缺少 id");
        if (merged.Downloads is not null
            && merged.Downloads.TryGetValue("client", out DownloadInfo? client)
            && !string.IsNullOrEmpty(client.Url))
        {
            string jarPath = Path.Combine(versionsDir, jarId, jarId + ".jar");
            tasks.Add(CreateTask(
                client.Url,
                jarPath,
                client.Sha1,
                client.Size,
                $"客户端 {jarId}.jar"));
        }

        // 2. 依赖库与原生库。
        if (merged.Libraries is not null)
        {
            foreach (var library in merged.Libraries)
            {
                if (!RuleEvaluator.RulesAllow(library.Rules))
                {
                    continue;
                }
                if (library.Name is null)
                {
                    continue;
                }

                // 老版本（1.18 及更早）服务器专用库。
                if (library.Downloads is null && library.ClientReq == false)
                {
                    continue;
                }

                // 2a. 普通构件。
                if (library.Downloads?.Artifact is { } artifact && !string.IsNullOrEmpty(artifact.Url))
                {
                    string relativePath = artifact.Path ?? MavenName.Parse(library.Name).ToRepositoryPath();
                    tasks.Add(CreateTask(
                        artifact.Url,
                        Path.Combine(librariesDir, relativePath.Replace('/', Path.DirectorySeparatorChar)),
                        artifact.Sha1,
                        artifact.Size,
                        Path.GetFileName(relativePath)));
                }
                else if (library.Downloads?.Artifact is null && !string.IsNullOrEmpty(library.Url))
                {
                    // Forge/Fabric 老风格：只给了 Maven 仓库基础地址；
                    // 该地址可能已被镜像改写，先还原为官方 URL，再生成"镜像→BMCL→官方"候选。
                    // Fabric 等加载器会在库顶层提供 sha1/size，一并用于校验。
                    var coordinate = MavenName.Parse(library.Name);
                    string relativePath = coordinate.ToRepositoryPath();
                    string officialBase = _sourceProvider.Current.ToOfficialUrl(library.Url!.TrimEnd('/') + "/");
                    tasks.Add(CreateTask(
                        officialBase + relativePath,
                        Path.Combine(librariesDir, relativePath.Replace('/', Path.DirectorySeparatorChar)),
                        library.Sha1,
                        library.Size ?? 0,
                        Path.GetFileName(relativePath)));
                }

                // 2b. 老格式原生库（1.18-）：classifiers。
                if (library.Natives is not null
                    && library.Downloads?.Classifiers is { } classifiers
                    && library.Natives.TryGetValue("windows", out string? classifierTemplate))
                {
                    string classifier = classifierTemplate.Replace("${arch}", Environment.Is64BitOperatingSystem ? "64" : "32");
                    if (classifiers.TryGetValue(classifier, out var native) && !string.IsNullOrEmpty(native.Url))
                    {
                        string relativePath = native.Path
                            ?? MavenName.Parse(library.Name + ":" + classifier).ToRepositoryPath();
                        tasks.Add(CreateTask(
                            native.Url,
                            Path.Combine(librariesDir, relativePath.Replace('/', Path.DirectorySeparatorChar)),
                            native.Sha1,
                            native.Size,
                            Path.GetFileName(relativePath)));
                    }
                }
            }
        }

        // 3. 资源索引与资源对象。
        if (merged.AssetIndex is { } index && !string.IsNullOrEmpty(index.Url))
        {
            string indexId = index.Id ?? merged.Assets ?? "legacy";
            string indexPath = Path.Combine(gameDirectory, "assets", "indexes", indexId + ".json");

            // 确保索引已在本地（没有就直接下载），再解析对象列表。
            AssetIndexFile indexFile = await EnsureAssetIndexAsync(index, indexPath, cancellationToken);

            foreach (var (resourceName, asset) in indexFile.Objects)
            {
                string prefix = asset.Hash[..2];
                string officialUrl = $"{AssetsHost}/{prefix}/{asset.Hash}";
                string objectPath = Path.Combine(gameDirectory, "assets", "objects", prefix, asset.Hash);
                tasks.Add(CreateTask(
                    officialUrl,
                    objectPath,
                    asset.Hash,
                    asset.Size,
                    resourceName));
            }
        }

        return tasks;
    }

    /// <summary>构造带候选源的下载任务（主源失败时自动回退）。</summary>
    private DownloadTask CreateTask(
        string officialUrl,
        string destinationPath,
        string? sha1,
        long size,
        string? displayName)
    {
        IReadOnlyList<string> candidates = _sourceProvider.Current.BuildCandidates(officialUrl);
        string mainUrl = candidates[0];
        IReadOnlyList<string>? fallback = candidates.Count > 1
            ? candidates.Skip(1).ToList()
            : null;
        return new DownloadTask(mainUrl, destinationPath, sha1, size, displayName, fallback);
    }

    /// <summary>本地版本 JSON 是否可用（存在且 SHA1 校验通过；清单未提供 SHA1 时只检查存在）。</summary>
    private static bool VersionJsonFileIsValid(string jsonPath, string? expectedSha1)
        => File.Exists(jsonPath)
           && (string.IsNullOrEmpty(expectedSha1) || HashUtils.VerifySha1(jsonPath, expectedSha1));

    /// <summary>下载版本 JSON 并校验 SHA1；校验失败删除后重试一次，仍失败抛出中文异常。</summary>
    private async Task DownloadVersionJsonWithVerificationAsync(
        ManifestEntry entry, string jsonPath, CancellationToken cancellationToken)
    {
        string url = _sourceProvider.Current.Rewrite(entry.Url);
        for (int attempt = 0; ; attempt++)
        {
            using var response = await _http.GetAsync(url, cancellationToken);
            response.EnsureSuccessStatusCode();
            byte[] bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
            await File.WriteAllBytesAsync(jsonPath, bytes, cancellationToken);

            if (VersionJsonFileIsValid(jsonPath, entry.Sha1))
            {
                return;
            }

            TryDelete(jsonPath);
            if (attempt >= 1)
            {
                throw new InvalidDataException(
                    $"版本 {entry.Id} 的版本 JSON 连续两次 SHA1 校验失败。\n" +
                    "请检查网络，或到“设置”中切换下载源后重试。");
            }
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // 删除失败时下次重试会覆盖。
        }
    }

    /// <summary>确保资源索引文件存在并返回解析结果。</summary>
    private async Task<AssetIndexFile> EnsureAssetIndexAsync(
        AssetIndexInfo index,
        string indexPath,
        CancellationToken cancellationToken)
    {
        if (!HashUtils.VerifySha1(indexPath, index.Sha1))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(indexPath)!);
            using var response = await _http.GetAsync(_sourceProvider.Current.Rewrite(index.Url!), cancellationToken);
            response.EnsureSuccessStatusCode();
            byte[] bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
            await File.WriteAllBytesAsync(indexPath, bytes, cancellationToken);
        }

        await using var stream = File.OpenRead(indexPath);
        return await JsonSerializer.DeserializeAsync<AssetIndexFile>(stream, JsonOptions.Minecraft, cancellationToken)
               ?? throw new InvalidDataException("资源索引解析失败。");
    }
}
