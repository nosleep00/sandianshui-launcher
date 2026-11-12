using System.Text.Json.Serialization;

namespace SandianshuiLauncher.Core.Models;

// ─────────────────────────────────────────────────────────────
// 业务模型
// ─────────────────────────────────────────────────────────────

/// <summary>CurseForge 搜索到的模组项目。</summary>
public sealed class CurseForgeMod
{
    public required long Id { get; init; }

    public required string Name { get; init; }

    public string Slug { get; init; } = string.Empty;

    public string Summary { get; init; } = string.Empty;

    public long DownloadCount { get; init; }

    public string? IconUrl { get; init; }

    public string? WebsiteUrl { get; init; }
}

/// <summary>CurseForge 模组的一个文件（版本）。</summary>
public sealed class CurseForgeFileInfo
{
    public required long Id { get; init; }

    public required string FileName { get; init; }

    public long Length { get; init; }

    /// <summary>SHA1（来自文件 hashes 中 algo=1 的条目）。</summary>
    public string? Sha1 { get; init; }

    /// <summary>直接下载地址（通常为 null，需要经 API 换取）。</summary>
    public string? DownloadUrl { get; init; }

    public IReadOnlyList<string> GameVersions { get; init; } = [];

    public IReadOnlyList<CurseForgeDependency> Dependencies { get; init; } = [];

    /// <summary>1=Release 2=Beta 3=Alpha。</summary>
    public int ReleaseType { get; init; } = 1;
}

/// <summary>CurseForge 文件声明的依赖。</summary>
public sealed class CurseForgeDependency
{
    public required long ModId { get; init; }

    /// <summary>3=RequiredDependency（必需）。</summary>
    public int RelationType { get; init; }

    public bool IsRequired => RelationType == 3;
}

// ─────────────────────────────────────────────────────────────
// API 响应 DTO（CurseForge 返回 PascalCase，显式映射）
// ─────────────────────────────────────────────────────────────

internal sealed class CfSearchResponse
{
    [JsonPropertyName("data")]
    public List<CfModDto> Data { get; set; } = [];
}

internal sealed class CfModDto
{
    [JsonPropertyName("id")]
    public long Id { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("slug")]
    public string Slug { get; set; } = string.Empty;

    [JsonPropertyName("summary")]
    public string Summary { get; set; } = string.Empty;

    [JsonPropertyName("downloadCount")]
    public long DownloadCount { get; set; }

    [JsonPropertyName("logo")]
    public CfLogoDto? Logo { get; set; }

    [JsonPropertyName("links")]
    public CfLinksDto? Links { get; set; }
}

internal sealed class CfLogoDto
{
    [JsonPropertyName("url")]
    public string? Url { get; set; }

    [JsonPropertyName("thumbnailUrl")]
    public string? ThumbnailUrl { get; set; }
}

internal sealed class CfLinksDto
{
    [JsonPropertyName("websiteUrl")]
    public string? WebsiteUrl { get; set; }
}

internal sealed class CfFilesResponse
{
    [JsonPropertyName("data")]
    public List<CfFileDto> Data { get; set; } = [];
}

internal sealed class CfFileDto
{
    [JsonPropertyName("id")]
    public long Id { get; set; }

    [JsonPropertyName("fileName")]
    public string FileName { get; set; } = string.Empty;

    [JsonPropertyName("fileLength")]
    public long FileLength { get; set; }

    [JsonPropertyName("downloadUrl")]
    public string? DownloadUrl { get; set; }

    [JsonPropertyName("releaseType")]
    public int ReleaseType { get; set; }

    [JsonPropertyName("gameVersions")]
    public List<string> GameVersions { get; set; } = [];

    [JsonPropertyName("hashes")]
    public List<CfHashDto> Hashes { get; set; } = [];

    [JsonPropertyName("dependencies")]
    public List<CfDependencyDto> Dependencies { get; set; } = [];
}

internal sealed class CfHashDto
{
    [JsonPropertyName("value")]
    public string Value { get; set; } = string.Empty;

    /// <summary>1=Sha1 2=Md5。</summary>
    [JsonPropertyName("algo")]
    public int Algo { get; set; }
}

internal sealed class CfDependencyDto
{
    [JsonPropertyName("modId")]
    public long ModId { get; set; }

    [JsonPropertyName("relationType")]
    public int RelationType { get; set; }
}

internal sealed class CfDownloadUrlResponse
{
    [JsonPropertyName("data")]
    public string? Data { get; set; }
}
