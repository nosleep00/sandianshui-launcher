using System.Text.Json.Serialization;

namespace SandianshuiLauncher.Core.Models;

// ─────────────────────────────────────────────────────────────
// 业务模型（供 UI 使用）
// ─────────────────────────────────────────────────────────────

/// <summary>搜索结果中的一个模组。</summary>
public sealed class ModSearchResult
{
    /// <summary>Modrinth 项目 ID。</summary>
    public required string ProjectId { get; init; }

    /// <summary>URL 友好的项目别名（调用版本接口时可直接用）。</summary>
    public required string Slug { get; init; }

    /// <summary>来源平台：modrinth / curseforge（决定安装走哪套接口）。</summary>
    public string SourcePlatform { get; init; } = "modrinth";

    public required string Title { get; init; }

    /// <summary>本地化中文名（由 ModNameLocalizer 填充，未命中时为空）。</summary>
    public string TitleZh { get; init; } = string.Empty;

    /// <summary>展示标题：优先中文名，其次英文原名。</summary>
    public string DisplayTitle => string.IsNullOrWhiteSpace(TitleZh) ? Title : TitleZh;

    /// <summary>副标题（有中文名时显示英文原名）。</summary>
    public string? DisplaySubtitle => string.IsNullOrWhiteSpace(TitleZh) ? null : Title;

    public string Description { get; init; } = string.Empty;

    public string Author { get; init; } = string.Empty;

    public long Downloads { get; init; }

    public string? IconUrl { get; init; }

    /// <summary>展示分类（含加载器，如 fabric、forge、utility 等）。</summary>
    public IReadOnlyList<string> Categories { get; init; } = [];

    /// <summary>加载器显示文本，如 "fabric / forge"。</summary>
    public string LoadersDisplay =>
        string.Join(" / ", Categories.Where(c =>
            c is "fabric" or "forge" or "neoforge" or "quilt" or "liteloader" or "modloader"));

    /// <summary>是否为纯依赖库（分类含 library），库本身不提供独立游戏内容。</summary>
    public bool IsLibrary =>
        Categories.Contains("library", StringComparer.OrdinalIgnoreCase);

    /// <summary>下载量的友好显示（如 1.2 万、1.0 亿）。</summary>
    public string DownloadsDisplay => Downloads switch
    {
        >= 100_000_000 => (Downloads / 100_000_000.0).ToString("0.0") + " 亿",
        >= 10_000 => (Downloads / 10_000.0).ToString("0.0") + " 万",
        _ => Downloads.ToString()
    };
}

/// <summary>模组的一个可下载文件。</summary>
public sealed class ModFileInfo
{
    public required string FileName { get; init; }

    /// <summary>下载地址（已经过镜像重写）。</summary>
    public required string Url { get; init; }

    public long Size { get; init; }

    public string? Sha1 { get; init; }

    public bool Primary { get; init; }

    /// <summary>文件大小友好显示（KB / MB）。</summary>
    public string SizeDisplay =>
        Size >= 1024 * 1024
            ? (Size / 1024.0 / 1024.0).ToString("0.0") + " MB"
            : (Size / 1024.0).ToString("0.0") + " KB";
}

/// <summary>模组的一个版本（可能包含多个文件，取 primary 为主文件）。</summary>
public sealed class ModVersionInfo
{
    public required string VersionId { get; init; }

    public required string Name { get; init; }

    public string VersionNumber { get; init; } = string.Empty;

    public IReadOnlyList<string> Loaders { get; init; } = [];

    public IReadOnlyList<string> GameVersions { get; init; } = [];

    public IReadOnlyList<ModFileInfo> Files { get; init; } = [];

    /// <summary>该版本声明的依赖（required / optional / embedded / incompatible）。</summary>
    public IReadOnlyList<ModDependencyInfo> Dependencies { get; init; } = [];

    /// <summary>主文件（primary 优先，否则第一个文件）；没有文件时为 null。</summary>
    public ModFileInfo? PrimaryFile =>
        Files.FirstOrDefault(f => f.Primary) ?? Files.FirstOrDefault();
}

/// <summary>Modrinth 版本声明的单个依赖。</summary>
public sealed class ModDependencyInfo
{
    /// <summary>依赖的 Modrinth 项目 ID（为 null 时无法解析）。</summary>
    public string? ProjectId { get; init; }

    /// <summary>固定依赖的版本 ID（通常为 null，表示按游戏版本/加载器选最新）。</summary>
    public string? VersionId { get; init; }

    /// <summary>依赖类型：required / optional / embedded / incompatible。</summary>
    public string Type { get; init; } = "required";

    /// <summary>是否为必需依赖（缺失会导致游戏无法加载）。</summary>
    public bool IsRequired => string.Equals(Type, "required", StringComparison.OrdinalIgnoreCase);
}

/// <summary>安装计划中的单个依赖文件。</summary>
public sealed class ModInstallItem
{
    public required string ProjectId { get; init; }

    /// <summary>展示名（解析失败时为项目 ID）。</summary>
    public string Title { get; init; } = string.Empty;

    public required ModFileInfo File { get; init; }
}

/// <summary>一次模组安装的完整计划：主文件 + 需要一并下载的必需依赖。</summary>
public sealed class ModInstallPlan
{
    public required ModVersionInfo Version { get; init; }

    public required ModFileInfo PrimaryFile { get; init; }

    /// <summary>待下载的必需依赖（按依赖发现顺序）。</summary>
    public IReadOnlyList<ModInstallItem> Dependencies { get; init; } = [];

    /// <summary>解析过程中被跳过/无法解析的说明（已安装、镜像缺收录等）。</summary>
    public IReadOnlyList<string> SkippedNotes { get; init; } = [];
}

/// <summary>已安装到 mods 目录的一个模组文件（含已禁用的 .jar.disabled）。</summary>
public sealed class InstalledMod
{
    public required string FileName { get; init; }

    public required string FullPath { get; init; }

    public long SizeBytes { get; init; }

    /// <summary>是否启用（false 表示文件为 .jar.disabled 状态）。</summary>
    public bool IsEnabled { get; init; } = true;

    public string SizeDisplay =>
        SizeBytes >= 1024 * 1024
            ? (SizeBytes / 1024.0 / 1024.0).ToString("0.0") + " MB"
            : (SizeBytes / 1024.0).ToString("0.0") + " KB";
}

// ─────────────────────────────────────────────────────────────
// Modrinth API 响应 DTO（snake_case，显式映射）
// ─────────────────────────────────────────────────────────────

internal sealed class ModrinthSearchResponse
{
    [JsonPropertyName("hits")]
    public List<ModrinthSearchHit> Hits { get; set; } = [];

    [JsonPropertyName("total_hits")]
    public long TotalHits { get; set; }
}

internal sealed class ModrinthSearchHit
{
    [JsonPropertyName("project_id")]
    public string ProjectId { get; set; } = string.Empty;

    [JsonPropertyName("slug")]
    public string Slug { get; set; } = string.Empty;

    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

    [JsonPropertyName("author")]
    public string? Author { get; set; }

    [JsonPropertyName("downloads")]
    public long Downloads { get; set; }

    [JsonPropertyName("icon_url")]
    public string? IconUrl { get; set; }

    [JsonPropertyName("display_categories")]
    public List<string> DisplayCategories { get; set; } = [];
}

internal sealed class ModrinthVersion
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("version_number")]
    public string VersionNumber { get; set; } = string.Empty;

    [JsonPropertyName("loaders")]
    public List<string> Loaders { get; set; } = [];

    [JsonPropertyName("game_versions")]
    public List<string> GameVersions { get; set; } = [];

    [JsonPropertyName("files")]
    public List<ModrinthFile> Files { get; set; } = [];

    [JsonPropertyName("dependencies")]
    public List<ModrinthVersionDependency> Dependencies { get; set; } = [];
}

internal sealed class ModrinthVersionDependency
{
    [JsonPropertyName("version_id")]
    public string? VersionId { get; set; }

    [JsonPropertyName("project_id")]
    public string? ProjectId { get; set; }

    [JsonPropertyName("file_name")]
    public string? FileName { get; set; }

    [JsonPropertyName("dependency_type")]
    public string DependencyType { get; set; } = "required";
}

internal sealed class ModrinthFile
{
    [JsonPropertyName("url")]
    public string Url { get; set; } = string.Empty;

    [JsonPropertyName("filename")]
    public string Filename { get; set; } = string.Empty;

    [JsonPropertyName("primary")]
    public bool Primary { get; set; }

    [JsonPropertyName("size")]
    public long Size { get; set; }

    [JsonPropertyName("hashes")]
    public ModrinthHashes? Hashes { get; set; }
}

internal sealed class ModrinthHashes
{
    [JsonPropertyName("sha1")]
    public string? Sha1 { get; set; }

    [JsonPropertyName("sha512")]
    public string? Sha512 { get; set; }
}
