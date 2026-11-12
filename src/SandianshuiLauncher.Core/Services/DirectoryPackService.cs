using System.Text.Json;
using SandianshuiLauncher.Core.Models;

namespace SandianshuiLauncher.Core.Services;

/// <summary>解压即玩整合包（目录型）导入结果。</summary>
public sealed class DirectoryPackImportResult
{
    public required string PackName { get; init; }

    /// <summary>成功导入的版本 Id。</summary>
    public List<string> ImportedVersions { get; } = [];

    /// <summary>因目标已存在而跳过的版本 Id。</summary>
    public List<string> SkippedVersions { get; } = [];

    /// <summary>复制的文件数与字节数。</summary>
    public long CopiedFiles { get; set; }

    public long CopiedBytes { get; set; }

    /// <summary>版本目录内自带 Java 的路径（版本 Id → java.exe 路径）。</summary>
    public Dictionary<string, string> BundledJavaByVersion { get; } = [];

    /// <summary>非致命提示（已存在跳过、无自带 Java、父版本补充失败等）。</summary>
    public List<string> Warnings { get; } = [];
}

/// <summary>
/// 「解压即玩」整合包导入：定位 .minecraft 根目录、识别整合包版本、
/// 复制版本目录并合并 assets / libraries，检测版本目录内自带 Java，
/// 缺失的基础版本 JSON 自动从官方清单补充。
/// </summary>
public sealed class DirectoryPackService
{
    private readonly VersionService _versionService;

    public DirectoryPackService(VersionService versionService)
    {
        _versionService = versionService;
    }

    /// <summary>定位整合包的 .minecraft 根：所选目录本身或其下 .minecraft 子目录。</summary>
    public static string? FindMinecraftRoot(string selectedDirectory)
    {
        if (!Directory.Exists(selectedDirectory))
        {
            return null;
        }
        if (IsMinecraftRoot(selectedDirectory))
        {
            return selectedDirectory;
        }
        string child = Path.Combine(selectedDirectory, ".minecraft");
        return IsMinecraftRoot(child) ? child : null;
    }

    private static bool IsMinecraftRoot(string directory)
        => Directory.Exists(Path.Combine(directory, "versions"))
           && Directory.Exists(Path.Combine(directory, "assets"));

    /// <summary>
    /// 识别可导入的整合包版本：versions/{id}/{id}.json 存在，
    /// 且版本目录内含 mods 或 config（完整包的独立目录特征）。
    /// </summary>
    public static List<string> DetectImportableVersions(string minecraftRoot)
    {
        string versionsDir = Path.Combine(minecraftRoot, "versions");
        if (!Directory.Exists(versionsDir))
        {
            return [];
        }

        var result = new List<string>();
        foreach (string directory in Directory.EnumerateDirectories(versionsDir))
        {
            string id = Path.GetFileName(directory);
            if (!File.Exists(Path.Combine(directory, id + ".json")))
            {
                continue;
            }
            if (Directory.Exists(Path.Combine(directory, "mods"))
                || Directory.Exists(Path.Combine(directory, "config")))
            {
                result.Add(id);
            }
        }
        return result.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>
    /// 导入指定版本：复制版本目录、补充缺失的基础版本 JSON、合并 assets 与 libraries，
    /// 检测版本目录内自带 Java。已存在的文件一律跳过（合并语义），同名版本整体跳过。
    /// </summary>
    public async Task<DirectoryPackImportResult> ImportAsync(
        string minecraftRoot,
        string gameDirectory,
        IReadOnlyCollection<string> versionIds,
        Action<string>? onProgress = null,
        CancellationToken cancellationToken = default)
    {
        var result = new DirectoryPackImportResult { PackName = Path.GetFileName(minecraftRoot) };
        Directory.CreateDirectory(Path.Combine(gameDirectory, "versions"));
        Directory.CreateDirectory(Path.Combine(gameDirectory, "assets"));
        Directory.CreateDirectory(Path.Combine(gameDirectory, "libraries"));

        foreach (string versionId in versionIds)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string source = Path.Combine(minecraftRoot, "versions", versionId);
            string destination = Path.Combine(gameDirectory, "versions", versionId);
            if (Directory.Exists(destination))
            {
                result.SkippedVersions.Add(versionId);
                result.Warnings.Add($"版本 {versionId} 已存在，已跳过（未做任何修改）。");
                continue;
            }

            onProgress?.Invoke($"正在复制版本 {versionId} …");
            (long files, long bytes) = CopyDirectory(source, destination);
            result.CopiedFiles += files;
            result.CopiedBytes += bytes;
            result.ImportedVersions.Add(versionId);

            // 自带 Java 检测。
            string javaPath = Path.Combine(destination, "Java", "bin", "java.exe");
            if (File.Exists(javaPath))
            {
                result.BundledJavaByVersion[versionId] = javaPath;
            }
            else
            {
                result.Warnings.Add($"版本 {versionId} 未包含自带 Java，将使用启动器自动检测的 Java。");
            }

            // 补充缺失的基础版本 JSON（inheritsFrom 指向的原版 json）。
            string? inheritsFrom = ReadInheritsFrom(Path.Combine(destination, versionId + ".json"));
            if (!string.IsNullOrEmpty(inheritsFrom))
            {
                await EnsureParentVersionJsonAsync(inheritsFrom, gameDirectory, result, cancellationToken);
            }
        }

        string sourceObjects = Path.Combine(minecraftRoot, "assets", "objects");
        if (Directory.Exists(sourceObjects))
        {
            onProgress?.Invoke("正在合并 assets（资源文件）…");
            (long files, long bytes) = CopyDirectory(sourceObjects, Path.Combine(gameDirectory, "assets", "objects"));
            result.CopiedFiles += files;
            result.CopiedBytes += bytes;
        }
        string sourceIndexes = Path.Combine(minecraftRoot, "assets", "indexes");
        if (Directory.Exists(sourceIndexes))
        {
            CopyDirectory(sourceIndexes, Path.Combine(gameDirectory, "assets", "indexes"));
        }

        string sourceLibraries = Path.Combine(minecraftRoot, "libraries");
        if (Directory.Exists(sourceLibraries))
        {
            onProgress?.Invoke("正在合并 libraries（依赖库）…");
            CopyDirectory(sourceLibraries, Path.Combine(gameDirectory, "libraries"));
        }

        return result;
    }

    private async Task EnsureParentVersionJsonAsync(
        string parentId, string gameDirectory, DirectoryPackImportResult result, CancellationToken cancellationToken)
    {
        string parentJsonPath = Path.Combine(gameDirectory, "versions", parentId, parentId + ".json");
        if (File.Exists(parentJsonPath))
        {
            return;
        }
        try
        {
            VersionManifest manifest = await _versionService.FetchManifestAsync(cancellationToken);
            ManifestEntry? entry = manifest.Versions
                .FirstOrDefault(v => string.Equals(v.Id, parentId, StringComparison.OrdinalIgnoreCase));
            if (entry is null)
            {
                result.Warnings.Add($"未在官方清单中找到基础版本 {parentId}，可能影响启动。");
                return;
            }
            string text = await _versionService.DownloadVersionJsonTextAsync(entry.Url, cancellationToken);
            Directory.CreateDirectory(Path.GetDirectoryName(parentJsonPath)!);
            await File.WriteAllTextAsync(parentJsonPath, text, cancellationToken);
        }
        catch (Exception ex)
        {
            result.Warnings.Add($"自动补充基础版本 {parentId} 失败（{ex.Message}），可到下载页手动安装该版本。");
        }
    }

    private static string? ReadInheritsFrom(string jsonPath)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(File.ReadAllText(jsonPath));
            return document.RootElement.TryGetProperty("inheritsFrom", out JsonElement element)
                   && element.ValueKind == JsonValueKind.String
                ? element.GetString()
                : null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>递归复制目录（已存在文件跳过，即合并语义），返回复制的文件数与字节数。</summary>
    private static (long Files, long Bytes) CopyDirectory(string source, string destination)
    {
        long files = 0;
        long bytes = 0;
        foreach (string file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            string target = Path.Combine(destination, Path.GetRelativePath(source, file));
            if (File.Exists(target))
            {
                continue;
            }
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target);
            files++;
            bytes += new FileInfo(file).Length;
        }
        return (files, bytes);
    }
}
