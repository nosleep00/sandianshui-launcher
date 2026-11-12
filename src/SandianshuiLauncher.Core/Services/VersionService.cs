using SandianshuiLauncher.Core.Models;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace SandianshuiLauncher.Core.Services;

/// <summary>本地已安装版本。</summary>
public class LocalVersion
{
    /// <summary>版本文件夹名（也是版本选择器中显示的 Id）。</summary>
    public string Id { get; init; } = string.Empty;

    /// <summary>版本文件夹绝对路径。</summary>
    public string Directory { get; init; } = string.Empty;

    /// <summary>版本 JSON 绝对路径。</summary>
    public string JsonPath { get; init; } = string.Empty;

    /// <summary>是否存在同名客户端 jar。</summary>
    public bool HasJar { get; init; }

    /// <summary>父版本 Id（加载器版本）。</summary>
    public string? InheritsFrom { get; init; }

    /// <summary>版本类型（release/snapshot/modified）。</summary>
    public string? Type { get; init; }

    /// <summary>加载器中文显示名（如 Forge / Fabric；JSON 损坏时为空字符串）。</summary>
    public string LoaderName { get; init; } = string.Empty;

    /// <summary>主类（展示用）。</summary>
    public string? MainClass { get; init; }

    /// <summary>
    /// 用于资源接口（Modrinth 等）按游戏版本过滤的基础版本号。
    /// 加载器 profile（Forge/Fabric 等）有父版本时返回 <see cref="InheritsFrom"/>，
    /// 否则返回自身 <see cref="Id"/>。
    /// 注意：复合 profile 的 Id（如 1.20.4-forge-49.2.13）不是 Modrinth 认可的游戏版本，
    /// 直接用于过滤会得到 0 条结果，必须用基础版本号。
    /// </summary>
    public string GameVersion => string.IsNullOrWhiteSpace(InheritsFrom) ? Id : InheritsFrom;
}

/// <summary>版本清单获取与本地版本扫描。</summary>
public class VersionService
{
    private readonly HttpClient _http;
    private readonly DownloadSourceProvider _sourceProvider;

    public VersionService(HttpClient http, DownloadSourceProvider sourceProvider)
    {
        _http = http;
        _sourceProvider = sourceProvider;
    }

    /// <summary>拉取远程版本清单（按下载源的候选地址依次回退）。</summary>
    public Task<VersionManifest> FetchManifestAsync(CancellationToken cancellationToken = default)
        => FetchWithCandidatesAsync(
            _sourceProvider.Current.BuildManifestCandidates(),
            "版本清单获取",
            async (response, token) =>
            {
                await using var stream = await response.Content.ReadAsStreamAsync(token);
                return await JsonSerializer.DeserializeAsync<VersionManifest>(stream, JsonOptions.Minecraft, token)
                       ?? throw new InvalidDataException("版本清单解析失败。");
            },
            cancellationToken,
            url => LastManifestSourceUrl = url);

    /// <summary>
    /// 最近一次成功获取版本清单所用的地址。
    /// 镜像失效时清单会回退到 BMCLAPI 或官方源，界面据此提示真实来源。
    /// </summary>
    public string? LastManifestSourceUrl { get; private set; }

    /// <summary>下载并解析指定版本的 JSON（从远程，按候选地址依次回退）。</summary>
    public Task<VersionJson> DownloadVersionJsonAsync(string manifestJsonUrl, CancellationToken cancellationToken = default)
        => FetchWithCandidatesAsync(
            _sourceProvider.Current.BuildCandidates(manifestJsonUrl),
            "版本 JSON 下载",
            async (response, token) =>
            {
                await using var stream = await response.Content.ReadAsStreamAsync(token);
                return await JsonSerializer.DeserializeAsync<VersionJson>(stream, JsonOptions.Minecraft, token)
                       ?? throw new InvalidDataException("版本 JSON 解析失败。");
            },
            cancellationToken);

    /// <summary>下载指定版本的原始 JSON 文本（用于补全本地缺失的基础版本 JSON）。</summary>
    public Task<string> DownloadVersionJsonTextAsync(string manifestJsonUrl, CancellationToken cancellationToken = default)
        => FetchWithCandidatesAsync(
            _sourceProvider.Current.BuildCandidates(manifestJsonUrl),
            "版本 JSON 下载",
            (response, token) => response.Content.ReadAsStringAsync(token),
            cancellationToken);

    /// <summary>
    /// 依次尝试候选地址，全部失败时把每个地址的失败原因汇总抛出，
    /// 便于用户判断是镜像证书失效、节点故障还是网络不通。
    /// </summary>
    private async Task<T> FetchWithCandidatesAsync<T>(
        IReadOnlyList<string> candidates,
        string what,
        Func<HttpResponseMessage, CancellationToken, Task<T>> read,
        CancellationToken cancellationToken,
        Action<string>? onServed = null)
    {
        var errors = new List<string>();
        foreach (string url in candidates)
        {
            try
            {
                using var response = await _http.GetAsync(url, cancellationToken);
                response.EnsureSuccessStatusCode();
                onServed?.Invoke(url);
                return await read(response, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                errors.Add($"{HostOf(url)}：{DescribeError(ex)}");
            }
        }

        throw new HttpRequestException(
            $"{what}失败（已尝试 {candidates.Count} 个下载源）：{string.Join("；", errors)}");
    }

    private static string HostOf(string url)
        => Uri.TryCreate(url, UriKind.Absolute, out var uri) ? uri.Host : url;

    /// <summary>把底层异常翻译成中文提示，尤其是最常见的镜像证书失效。</summary>
    public static string DescribeError(Exception exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            if (current is System.Security.Authentication.AuthenticationException)
            {
                return "SSL/TLS 证书校验失败，请换用其它下载源";
            }
        }

        string message = exception.Message;
        if (message.Contains("SSL", StringComparison.OrdinalIgnoreCase)
            || message.Contains("TLS", StringComparison.OrdinalIgnoreCase)
            || message.Contains("证书", StringComparison.Ordinal))
        {
            return "SSL/TLS 证书校验失败，请换用其它下载源";
        }
        return message;
    }

    /// <summary>扫描游戏目录下 versions 文件夹中的全部本地版本。</summary>
    public static List<LocalVersion> ScanLocal(string gameDirectory)
    {
        var result = new List<LocalVersion>();
        string versionsDir = Path.Combine(gameDirectory, "versions");
        if (!Directory.Exists(versionsDir))
        {
            return result;
        }

        foreach (string dir in Directory.EnumerateDirectories(versionsDir))
        {
            string id = Path.GetFileName(dir);
            string jsonPath = Path.Combine(dir, id + ".json");
            if (!File.Exists(jsonPath))
            {
                continue;
            }

            VersionJson? json = null;
            try
            {
                json = ReadJsonFile(jsonPath);
            }
            catch
            {
                // 损坏的 JSON 也要列出来，方便用户删除。
            }

            result.Add(new LocalVersion
            {
                Id = id,
                Directory = dir,
                JsonPath = jsonPath,
                HasJar = File.Exists(Path.Combine(dir, id + ".jar")),
                InheritsFrom = json?.InheritsFrom,
                Type = json?.Type,
                MainClass = json?.MainClass,
                LoaderName = json is null
                    ? string.Empty
                    : ModLoaderDetector.DisplayName(ModLoaderDetector.Detect(json))
            });
        }

        return result.OrderByDescending(v => v.Id).ToList();
    }

    /// <summary>读取并解析本地版本 JSON。</summary>
    public static VersionJson ReadJsonFile(string jsonPath)
    {
        string json = File.ReadAllText(jsonPath);
        return JsonSerializer.Deserialize<VersionJson>(json, JsonOptions.Minecraft)
               ?? throw new InvalidDataException($"版本 JSON 为空：{jsonPath}");
    }

    /// <summary>递归读取并合并本地版本 JSON 的继承链（与原版合并后的视图）。</summary>
    public static VersionJson ReadMergedLocal(string gameDirectory, string versionId)
    {
        string jsonPath = Path.Combine(gameDirectory, "versions", versionId, versionId + ".json");
        VersionJson json = ReadJsonFile(jsonPath);
        if (string.IsNullOrEmpty(json.InheritsFrom))
        {
            return json;
        }
        VersionJson parent = ReadMergedLocal(gameDirectory, json.InheritsFrom!);
        return VersionJsonMerger.Merge(json, parent);
    }

    /// <summary>
    /// 识别本地版本使用的模组加载器（fabric / quilt / forge / neoforge）；
    /// 原版或读取失败返回 null。
    /// </summary>
    public static string? DetectLoader(string gameDirectory, string versionId)
    {
        try
        {
            return ModLoaderDetector.Detect(ReadMergedLocal(gameDirectory, versionId));
        }
        catch
        {
            // 版本 JSON 损坏等场景：按原版处理，不阻断安装。
            return null;
        }
    }

    /// <summary>校验新版本 Id 是否合法（非空、无非法文件名字符）。</summary>
    public static void ValidateVersionId(string id)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            throw new ArgumentException("版本名称不能为空。");
        }
        if (id.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || id is "." or "..")
        {
            throw new ArgumentException($"版本名称包含非法字符：{id}");
        }
    }

    /// <summary>
    /// 重命名本地版本：改目录名、JSON/jar 文件名、JSON 内 id/jar 字段，
    /// 并修正全库其他版本 JSON 中对它的 inheritsFrom 引用。失败时回滚目录名。
    /// </summary>
    public static void Rename(string gameDirectory, string oldId, string newId)
    {
        ValidateVersionId(newId);
        string oldDir = Path.Combine(gameDirectory, "versions", oldId);
        string newDir = Path.Combine(gameDirectory, "versions", newId);
        if (!Directory.Exists(oldDir))
        {
            throw new InvalidDataException($"版本 {oldId} 不存在。");
        }
        if (Directory.Exists(newDir))
        {
            throw new InvalidDataException($"版本 {newId} 已存在，请换一个名称。");
        }

        Directory.Move(oldDir, newDir);
        try
        {
            RenameVersionFiles(newDir, oldId, newId);
            UpdateOwnIdField(newDir, oldId, newId);
            FixInheritsReferences(gameDirectory, oldId, newId);
        }
        catch
        {
            try
            {
                if (Directory.Exists(newDir))
                {
                    Directory.Move(newDir, oldDir);
                }
            }
            catch
            {
                // 回滚失败时保留新目录，用户可手动处理。
            }
            throw;
        }
    }

    /// <summary>复制本地版本：克隆目录内容、改 id（不影响原版本）。失败时清理副本。</summary>
    public static void Copy(string gameDirectory, string sourceId, string newId)
    {
        ValidateVersionId(newId);
        string sourceDir = Path.Combine(gameDirectory, "versions", sourceId);
        string newDir = Path.Combine(gameDirectory, "versions", newId);
        if (!Directory.Exists(sourceDir))
        {
            throw new InvalidDataException($"版本 {sourceId} 不存在。");
        }
        if (Directory.Exists(newDir))
        {
            throw new InvalidDataException($"版本 {newId} 已存在，请换一个名称。");
        }

        Directory.CreateDirectory(newDir);
        try
        {
            foreach (string file in Directory.EnumerateFiles(sourceDir))
            {
                File.Copy(file, Path.Combine(newDir, Path.GetFileName(file)));
            }
            RenameVersionFiles(newDir, sourceId, newId);
            UpdateOwnIdField(newDir, sourceId, newId);
        }
        catch
        {
            try
            {
                Directory.Delete(newDir, recursive: true);
            }
            catch
            {
                // 清理失败不影响异常传播。
            }
            throw;
        }
    }

    /// <summary>把目录内 oldId.json / oldId.jar 改名为 newId.*。</summary>
    private static void RenameVersionFiles(string dir, string oldId, string newId)
    {
        string oldJson = Path.Combine(dir, oldId + ".json");
        if (File.Exists(oldJson))
        {
            File.Move(oldJson, Path.Combine(dir, newId + ".json"));
        }
        string oldJar = Path.Combine(dir, oldId + ".jar");
        if (File.Exists(oldJar))
        {
            File.Move(oldJar, Path.Combine(dir, newId + ".jar"));
        }
    }

    /// <summary>用 JsonNode 修改版本 JSON 内的 id/jar 字段（保留未知字段，避免模型重序列化丢数据）。</summary>
    private static void UpdateOwnIdField(string dir, string oldId, string newId)
    {
        string jsonPath = Path.Combine(dir, newId + ".json");
        if (!File.Exists(jsonPath))
        {
            return;
        }
        JsonNode? node = JsonNode.Parse(File.ReadAllText(jsonPath));
        if (node is not JsonObject obj)
        {
            return;
        }
        if (obj["id"]?.GetValue<string>() == oldId)
        {
            obj["id"] = newId;
        }
        if (obj["jar"]?.GetValue<string>() == oldId)
        {
            obj["jar"] = newId;
        }
        File.WriteAllText(jsonPath, obj.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
    }

    /// <summary>修正其他版本 JSON 中对被重命名版本的 inheritsFrom 引用。</summary>
    private static void FixInheritsReferences(string gameDirectory, string oldId, string newId)
    {
        string versionsDir = Path.Combine(gameDirectory, "versions");
        if (!Directory.Exists(versionsDir))
        {
            return;
        }
        foreach (string dir in Directory.EnumerateDirectories(versionsDir))
        {
            string jsonPath = Path.Combine(dir, Path.GetFileName(dir) + ".json");
            if (!File.Exists(jsonPath))
            {
                continue;
            }
            try
            {
                JsonNode? node = JsonNode.Parse(File.ReadAllText(jsonPath));
                if (node is JsonObject obj && obj["inheritsFrom"]?.GetValue<string>() == oldId)
                {
                    obj["inheritsFrom"] = newId;
                    File.WriteAllText(jsonPath, obj.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
                }
            }
            catch
            {
                // 损坏的 JSON 跳过，不影响其余版本。
            }
        }
    }
}
