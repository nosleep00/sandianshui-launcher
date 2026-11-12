using System.IO.Compression;
using System.Text.Json;
using SandianshuiLauncher.Core.Models;
using SandianshuiLauncher.Core.Utils;

namespace SandianshuiLauncher.Core.Services;

/// <summary>
/// 整合包导入服务：解析 Modrinth .mrpack（与 CurseForge zip 的 overrides），
/// 下载清单中的模组并应用 overrides 覆盖目录。全程防 zip-slip 路径穿越。
/// </summary>
public class ModpackService
{
    private readonly HttpClient _http;
    private readonly DownloadService _downloadService;
    private readonly DownloadQueue? _queue;

    public ModpackService(HttpClient http, DownloadService downloadService, DownloadQueue? queue = null)
    {
        _http = http;
        _downloadService = downloadService;
        _queue = queue;
    }

    /// <summary>
    /// 导入 Modrinth 整合包到指定版本：
    /// 1) 解析 modrinth.index.json；2) 按清单多源下载 mods 文件（SHA1 校验）；
    /// 3) 应用 overrides/ 覆盖目录（防路径穿越）；4) 校验游戏版本与加载器依赖并给出提示。
    /// </summary>
    public async Task<ModpackImportResult> ImportMrpackAsync(
        string mrpackPath,
        string gameDirectory,
        string versionId,
        bool versionIsolation,
        int threads,
        IProgress<DownloadProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        string actualGameDir = GamePaths.GetActualGameDirectory(gameDirectory, versionId, versionIsolation);
        Directory.CreateDirectory(actualGameDir);

        using ZipArchive archive = ZipFile.OpenRead(mrpackPath);
        ModrinthIndex index = ReadIndex(archive);

        var warnings = new List<string>();
        CollectDependencyWarnings(index, gameDirectory, versionId, warnings);

        // 1. 收集下载任务（客户端可用的全部文件：mods、config/openloader 数据包、光影包等）。
        var tasks = new List<DownloadTask>();
        foreach (ModrinthIndexFile file in index.Files)
        {
            if (file.Env is not null
                && string.Equals(file.Env.Client, "unsupported", StringComparison.OrdinalIgnoreCase))
            {
                continue; // 服务端专用文件。
            }
            string relative = file.Path ?? string.Empty;
            if (string.IsNullOrWhiteSpace(relative))
            {
                continue;
            }
            string destination = SafeCombine(actualGameDir, relative);
            var urls = file.Downloads.Where(u => !string.IsNullOrWhiteSpace(u)).ToList();
            if (urls.Count == 0)
            {
                throw new InvalidDataException($"整合包文件 {relative} 缺少下载地址，无法安装。");
            }
            tasks.Add(new DownloadTask(
                urls[0],
                destination,
                file.Hashes?.Sha1,
                file.FileSize,
                System.IO.Path.GetFileName(relative),
                urls.Count > 1 ? urls.Skip(1).ToList() : null));
        }

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

        // 2. 应用 overrides。
        int overrideCount = 0;
        foreach (ZipArchiveEntry entry in archive.Entries)
        {
            if (entry.FullName.EndsWith('/') || entry.FullName.EndsWith('\\'))
            {
                continue;
            }
            if (!IsUnder(entry.FullName, "overrides"))
            {
                continue;
            }
            string relative = StripPrefix(entry.FullName, "overrides");
            string destination = SafeCombine(actualGameDir, relative);
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(destination)!);
            await using var input = entry.Open();
            await using var output = File.Create(destination);
            await input.CopyToAsync(output, cancellationToken);
            overrideCount++;
        }

        return new ModpackImportResult
        {
            PackName = index.Name ?? index.VersionId ?? System.IO.Path.GetFileNameWithoutExtension(mrpackPath),
            ImportedFiles = tasks.Count,
            OverrideFiles = overrideCount,
            Warnings = warnings
        };
    }

    /// <summary>
    /// 导入 CurseForge 整合包 zip 到指定版本：
    /// 应用 overrides 覆盖目录；清单文件按 CurseForge API 下载（需 API Key），
    /// 未配置 Key 时降级为仅应用 overrides 并在 mods 目录写出“缺失模组清单.txt”。
    /// </summary>
    public async Task<ModpackImportResult> ImportCurseForgeAsync(
        string zipPath,
        string gameDirectory,
        string versionId,
        bool versionIsolation,
        string? apiKey,
        int threads,
        IProgress<DownloadProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        string actualGameDir = GamePaths.GetActualGameDirectory(gameDirectory, versionId, versionIsolation);
        Directory.CreateDirectory(actualGameDir);

        CurseForgeManifest manifest;
        int overrideCount;
        using (ZipArchive archive = ZipFile.OpenRead(zipPath))
        {
            manifest = ReadCurseForgeManifest(archive);
            overrideCount = ApplyOverrides(archive, manifest.Overrides ?? "overrides", actualGameDir, cancellationToken);
        }

        var warnings = new List<string>();
        CollectCurseForgeWarnings(manifest, gameDirectory, versionId, warnings);

        int imported = 0;
        var tasks = new List<DownloadTask>();
        string modsDir = Path.Combine(actualGameDir, "mods");

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            // 无 API Key：仅应用 overrides，写出待下载清单。
            Directory.CreateDirectory(modsDir);
            var missing = new System.Text.StringBuilder();
            foreach (CurseForgeManifestFile file in manifest.Files)
            {
                missing.AppendLine($"{file.ProjectId},{file.FileId}");
            }
            string listPath = Path.Combine(modsDir, "缺失模组清单.txt");
            await File.WriteAllTextAsync(listPath, missing.ToString(), cancellationToken);
            warnings.Add(
                "未配置 CurseForge API Key，整合包中的模组未自动下载；" +
                "已生成 mods/缺失模组清单.txt（每行 projectID,fileID），可在设置中填入 API Key 后重新导入。");
        }
        else
        {
            foreach (CurseForgeManifestFile file in manifest.Files)
            {
                string url = await GetCurseForgeDownloadUrlAsync(file.ProjectId, file.FileId, apiKey, cancellationToken);
                string fileName = CurseForgeFileName(file.ProjectId, file.FileId, url);
                tasks.Add(new DownloadTask(
                    url,
                    Path.Combine(modsDir, fileName),
                    sha1: null,
                    size: 0,
                    fileName));
            }
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
            imported = tasks.Count;
        }

        return new ModpackImportResult
        {
            PackName = manifest.Name ?? Path.GetFileNameWithoutExtension(zipPath),
            ImportedFiles = imported,
            OverrideFiles = overrideCount,
            Warnings = warnings
        };
    }

    /// <summary>解析 CurseForge manifest.json（缺失时报中文错误）。</summary>
    private static CurseForgeManifest ReadCurseForgeManifest(ZipArchive archive)
    {
        ZipArchiveEntry? entry = archive.GetEntry("manifest.json");
        if (entry is null)
        {
            throw new InvalidDataException("压缩包中找不到 manifest.json，不是有效的 CurseForge 整合包。");
        }
        using var stream = entry.Open();
        return JsonSerializer.Deserialize<CurseForgeManifest>(stream, JsonOptions.Minecraft)
               ?? throw new InvalidDataException("manifest.json 解析失败。");
    }

    /// <summary>把 zip 内 overrides/ 前缀的条目安全覆盖到目标目录，返回条目数。</summary>
    private static int ApplyOverrides(
        ZipArchive archive, string overridesPrefix, string destinationRoot, CancellationToken cancellationToken)
    {
        int count = 0;
        foreach (ZipArchiveEntry entry in archive.Entries)
        {
            if (entry.FullName.EndsWith('/') || entry.FullName.EndsWith('\\'))
            {
                continue;
            }
            if (!IsUnder(entry.FullName, overridesPrefix))
            {
                continue;
            }
            string relative = StripPrefix(entry.FullName, overridesPrefix);
            string destination = SafeCombine(destinationRoot, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            using var input = entry.Open();
            using var output = File.Create(destination);
            input.CopyTo(output);
            cancellationToken.ThrowIfCancellationRequested();
            count++;
        }
        return count;
    }

    /// <summary>调用 CurseForge API 获取文件下载地址（需要 x-api-key）。</summary>
    private async Task<string> GetCurseForgeDownloadUrlAsync(
        long projectId, long fileId, string apiKey, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"https://api.curseforge.com/v1/mods/{projectId}/files/{fileId}/download-url");
        request.Headers.TryAddWithoutValidation("x-api-key", apiKey);
        using var response = await _http.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        if (doc.RootElement.TryGetProperty("data", out JsonElement data)
            && data.ValueKind == JsonValueKind.String
            && !string.IsNullOrWhiteSpace(data.GetString()))
        {
            return data.GetString()!;
        }
        throw new InvalidDataException("CurseForge 下载地址解析失败，请检查 API Key 是否有效。");
    }

    /// <summary>从下载地址中提取文件名；提取不到时用 projectID-fileID 兜底。</summary>
    internal static string CurseForgeFileName(long projectId, long fileId, string url)
    {
        string last = url.Split('?')[0].Split('/')[^1];
        return last.EndsWith(".jar", StringComparison.OrdinalIgnoreCase)
            ? last
            : $"project-{projectId}-file-{fileId}.jar";
    }

    private static void CollectCurseForgeWarnings(
        CurseForgeManifest manifest, string gameDirectory, string versionId, List<string> warnings)
    {
        LocalVersion? local = null;
        try
        {
            local = VersionService.ScanLocal(gameDirectory).FirstOrDefault(v => v.Id == versionId);
        }
        catch
        {
            // 本地版本扫描失败不阻断导入。
        }

        if (manifest.Minecraft?.Version is { } requiredMc && local is not null
            && !string.Equals(local.GameVersion, requiredMc, StringComparison.OrdinalIgnoreCase)
            && !local.GameVersion.StartsWith(requiredMc + ".", StringComparison.OrdinalIgnoreCase))
        {
            warnings.Add($"整合包要求 Minecraft {requiredMc}，目标版本为 {local.GameVersion}，可能出现兼容问题。");
        }

        string? primaryLoader = manifest.Minecraft?.ModLoaders?
            .FirstOrDefault(l => l.Primary)?.Id ?? manifest.Minecraft?.ModLoaders?.FirstOrDefault()?.Id;
        if (!string.IsNullOrEmpty(primaryLoader) && local is not null && string.IsNullOrEmpty(local.InheritsFrom))
        {
            warnings.Add($"整合包需要 {primaryLoader} 加载器，目标版本 {versionId} 是原版，请先安装对应加载器。");
        }
    }

    /// <summary>SDLauncherPack 清单入口名。</summary>
    public const string SdlManifestEntry = "sdlauncher-pack.json";

    /// <summary>识别 zip 整合包类型（仅看内部清单文件，不解析内容）。</summary>
    public static PackFileType DetectZipType(string zipPath)
    {
        try
        {
            using ZipArchive zip = ZipFile.OpenRead(zipPath);
            if (zip.GetEntry("modrinth.index.json") is not null)
            {
                return PackFileType.ModrinthMrpack;
            }
            if (zip.GetEntry("manifest.json") is not null)
            {
                return PackFileType.CurseForge;
            }
            if (zip.GetEntry(SdlManifestEntry) is not null)
            {
                return PackFileType.SdlauncherPack;
            }
        }
        catch
        {
            // 打不开的压缩包按未知处理。
        }
        return PackFileType.Unknown;
    }

    /// <summary>
    /// 导出本地实例为 SDLauncherPack 整合包（zip）：
    /// 打包 mods（含禁用状态）、config 目录与实例根配置文件，附 sha1 清单。
    /// 单文件超过 64 MB 或总量超过 512 MB 的部分跳过并记入提示。
    /// </summary>
    public async Task<ModpackImportResult> ExportInstanceAsync(
        string gameDirectory,
        string versionId,
        bool versionIsolation,
        string outputPath,
        CancellationToken cancellationToken = default)
    {
        string actualGameDir = GamePaths.GetActualGameDirectory(gameDirectory, versionId, versionIsolation);
        var notes = new List<string>();
        const long SingleFileCap = 64 * 1024 * 1024;
        const long TotalCap = 512 * 1024 * 1024;
        long totalBytes = 0;

        LocalVersion? local = null;
        try
        {
            local = VersionService.ScanLocal(gameDirectory).FirstOrDefault(v => v.Id == versionId);
        }
        catch
        {
            // 扫描失败不阻断导出。
        }

        var manifest = new SdlauncherPackManifest
        {
            Name = local?.Id ?? versionId,
            GameVersion = local?.GameVersion ?? versionId,
            Loader = VersionService.DetectLoader(gameDirectory, versionId) ?? "vanilla",
            VersionId = versionId
        };

        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
        await using (var output = new FileStream(outputPath, FileMode.Create, FileAccess.Write))
        using (var zip = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            int modCount = 0;
            string modsDir = Path.Combine(actualGameDir, "mods");
            if (Directory.Exists(modsDir))
            {
                foreach (string file in Directory.EnumerateFiles(modsDir))
                {
                    string fileName = Path.GetFileName(file);
                    bool isDisabled = fileName.EndsWith(".jar.disabled", StringComparison.OrdinalIgnoreCase);
                    bool isJar = isDisabled || fileName.EndsWith(".jar", StringComparison.OrdinalIgnoreCase);
                    if (!isJar)
                    {
                        continue;
                    }
                    string storeName = isDisabled ? fileName[..^".disabled".Length] : fileName;
                    if (manifest.Mods.Any(m => string.Equals(m.FileName, storeName, StringComparison.OrdinalIgnoreCase)))
                    {
                        continue; // 同名双态时保留启用版本。
                    }

                    long size = new FileInfo(file).Length;
                    if (size > SingleFileCap || totalBytes + size > TotalCap)
                    {
                        notes.Add($"已跳过过大的文件：{storeName}");
                        continue;
                    }

                    ZipArchiveEntry entry = zip.CreateEntry("mods/" + storeName);
                    await using (var source = File.OpenRead(file))
                    await using (var destination = entry.Open())
                    {
                        await source.CopyToAsync(destination, cancellationToken);
                    }
                    manifest.Mods.Add(new SdlauncherPackMod
                    {
                        FileName = storeName,
                        Sha1 = HashUtils.Sha1File(file),
                        Size = size,
                        Enabled = !isDisabled
                    });
                    totalBytes += size;
                    modCount++;
                }
            }

            int configCount = 0;
            string configDir = Path.Combine(actualGameDir, "config");
            if (Directory.Exists(configDir))
            {
                foreach (string file in Directory.EnumerateFiles(configDir, "*", SearchOption.AllDirectories))
                {
                    long size = new FileInfo(file).Length;
                    if (size > SingleFileCap || totalBytes + size > TotalCap)
                    {
                        notes.Add($"已跳过过大的文件：{file}");
                        continue;
                    }
                    string relative = Path.GetRelativePath(configDir, file).Replace('\\', '/');
                    ZipArchiveEntry entry = zip.CreateEntry("config/" + relative);
                    await using (var source = File.OpenRead(file))
                    await using (var destination = entry.Open())
                    {
                        await source.CopyToAsync(destination, cancellationToken);
                    }
                    manifest.Configs.Add(new SdlauncherPackFile
                    {
                        Path = relative,
                        Sha1 = HashUtils.Sha1File(file)
                    });
                    totalBytes += size;
                    configCount++;
                }
            }

            // 实例根配置（options.txt / servers.dat）。
            foreach (string rootFile in new[] { "options.txt", "servers.dat" })
            {
                string path = Path.Combine(actualGameDir, rootFile);
                if (!File.Exists(path))
                {
                    continue;
                }
                ZipArchiveEntry entry = zip.CreateEntry(rootFile);
                await using (var source = File.OpenRead(path))
                await using (var destination = entry.Open())
                {
                    await source.CopyToAsync(destination, cancellationToken);
                }
            }

            // 清单最后写入。
            ZipArchiveEntry manifestEntry = zip.CreateEntry(SdlManifestEntry);
            await using (var manifestStream = manifestEntry.Open())
            {
                await JsonSerializer.SerializeAsync(
                    manifestStream, manifest, JsonOptions.Minecraft, cancellationToken);
            }
        }

        return new ModpackImportResult
        {
            PackName = manifest.Name,
            ImportedFiles = manifest.Mods.Count,
            OverrideFiles = manifest.Configs.Count,
            Warnings = notes
        };
    }

    /// <summary>
    /// 导入 SDLauncherPack 整合包到指定实例：恢复 mods（含启用状态）、config 与实例根配置。
    /// </summary>
    public async Task<ModpackImportResult> ImportSdlauncherPackAsync(
        string packPath,
        string gameDirectory,
        string versionId,
        bool versionIsolation,
        CancellationToken cancellationToken = default)
    {
        string actualGameDir = GamePaths.GetActualGameDirectory(gameDirectory, versionId, versionIsolation);
        Directory.CreateDirectory(actualGameDir);

        using ZipArchive zip = ZipFile.OpenRead(packPath);
        ZipArchiveEntry? manifestEntry = zip.GetEntry(SdlManifestEntry);
        if (manifestEntry is null)
        {
            throw new InvalidDataException("压缩包中缺少 sdlauncher-pack.json，不是有效的 SDLauncherPack 整合包。");
        }
        await using var manifestStream = manifestEntry.Open();
        var manifest = await JsonSerializer.DeserializeAsync<SdlauncherPackManifest>(
            manifestStream, JsonOptions.Minecraft, cancellationToken)
            ?? throw new InvalidDataException("sdlauncher-pack.json 解析失败。");

        int modCount = 0;
        int configCount = 0;

        foreach (ZipArchiveEntry entry in zip.Entries)
        {
            if (entry.FullName.EndsWith('/') || entry.FullName.EndsWith('\\'))
            {
                continue;
            }
            string normalized = entry.FullName.Replace('\\', '/').TrimStart('/');

            string destination;
            bool isMod = false;
            bool isConfig = false;
            if (normalized.StartsWith("mods/", StringComparison.OrdinalIgnoreCase))
            {
                string fileName = normalized["mods/".Length..];
                if (fileName.Length == 0)
                {
                    continue;
                }
                SdlauncherPackMod? manifestMod = manifest.Mods
                    .FirstOrDefault(m => string.Equals(m.FileName, fileName, StringComparison.OrdinalIgnoreCase));
                bool enabled = manifestMod?.Enabled ?? true;
                destination = SafeCombine(actualGameDir, "mods/" + fileName + (enabled ? "" : ".disabled"));
                isMod = true;
            }
            else if (normalized.StartsWith("config/", StringComparison.OrdinalIgnoreCase))
            {
                destination = SafeCombine(actualGameDir, normalized);
                isConfig = true;
            }
            else if (normalized is "options.txt" or "servers.dat")
            {
                destination = SafeCombine(actualGameDir, normalized);
            }
            else
            {
                continue; // 清单等其它条目跳过。
            }

            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            await using var input = entry.Open();
            await using var output = File.Create(destination);
            await input.CopyToAsync(output, cancellationToken);

            if (isMod)
            {
                modCount++;
            }
            else if (isConfig)
            {
                configCount++;
            }
        }

        return new ModpackImportResult
        {
            PackName = manifest.Name,
            ImportedFiles = modCount,
            OverrideFiles = configCount,
            Warnings = []
        };
    }

    /// <summary>读取并解析 modrinth.index.json（缺失时报中文错误）。</summary>
    private static ModrinthIndex ReadIndex(ZipArchive archive)
    {
        ZipArchiveEntry? entry = archive.GetEntry("modrinth.index.json");
        if (entry is null)
        {
            throw new InvalidDataException("压缩包中找不到 modrinth.index.json，不是有效的 Modrinth 整合包。");
        }
        using var stream = entry.Open();
        return JsonSerializer.Deserialize<ModrinthIndex>(stream, JsonOptions.Minecraft)
               ?? throw new InvalidDataException("modrinth.index.json 解析失败。");
    }

    /// <summary>对比整合包声明与目标版本的游戏版本/加载器，给出非致命提示。</summary>
    private static void CollectDependencyWarnings(
        ModrinthIndex index, string gameDirectory, string versionId, List<string> warnings)
    {
        LocalVersion? local = null;
        try
        {
            local = VersionService.ScanLocal(gameDirectory).FirstOrDefault(v => v.Id == versionId);
        }
        catch
        {
            // 本地版本扫描失败不阻断导入。
        }

        if (index.Dependencies.TryGetValue("minecraft", out string? requiredMc) && local is not null)
        {
            string baseVersion = local.GameVersion;
            if (!string.Equals(baseVersion, requiredMc, StringComparison.OrdinalIgnoreCase)
                && !baseVersion.StartsWith(requiredMc + ".", StringComparison.OrdinalIgnoreCase))
            {
                warnings.Add($"整合包要求 Minecraft {requiredMc}，目标版本为 {baseVersion}，可能出现兼容问题。");
            }
        }

        var loaders = new[] { "fabric", "forge", "quilt", "neoforge" };
        foreach (string loader in loaders)
        {
            if (index.Dependencies.ContainsKey(loader) && local is not null && string.IsNullOrEmpty(local.InheritsFrom))
            {
                warnings.Add($"整合包需要 {loader} 加载器，目标版本 {versionId} 是原版，请先安装对应加载器。");
            }
        }
    }

    /// <summary>把相对路径安全地拼到根目录下（防 zip-slip）。</summary>
    internal static string SafeCombine(string root, string relativePath)
    {
        string fullRoot = System.IO.Path.GetFullPath(root);
        string fullPath = System.IO.Path.GetFullPath(
            System.IO.Path.Combine(fullRoot, relativePath.Replace('/', System.IO.Path.DirectorySeparatorChar)));
        if (!fullPath.Equals(fullRoot, StringComparison.OrdinalIgnoreCase)
            && !fullPath.StartsWith(fullRoot + System.IO.Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException($"整合包包含非法路径：{relativePath}");
        }
        return fullPath;
    }

    private static bool IsUnder(string fullName, string prefix)
    {
        string normalized = fullName.Replace('\\', '/').TrimStart('/');
        string prefixNormalized = prefix.Replace('\\', '/').TrimEnd('/') + "/";
        return normalized.StartsWith(prefixNormalized, StringComparison.OrdinalIgnoreCase);
    }

    private static string StripPrefix(string fullName, string prefix)
    {
        string normalized = fullName.Replace('\\', '/').TrimStart('/');
        string prefixNormalized = prefix.Replace('\\', '/').TrimEnd('/') + "/";
        return normalized[prefixNormalized.Length..].Replace('/', System.IO.Path.DirectorySeparatorChar);
    }
}
