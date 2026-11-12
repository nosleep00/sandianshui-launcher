using System.Text.Json.Serialization;

namespace SandianshuiLauncher.Core.Models;

/// <summary>
/// version_manifest_v2.json 版本清单。
/// </summary>
public class VersionManifest
{
    [JsonPropertyName("latest")]
    public ManifestLatest Latest { get; set; } = new();

    [JsonPropertyName("versions")]
    public List<ManifestEntry> Versions { get; set; } = [];
}

/// <summary>清单中的最新版本指针。</summary>
public class ManifestLatest
{
    [JsonPropertyName("release")]
    public string Release { get; set; } = string.Empty;

    [JsonPropertyName("snapshot")]
    public string Snapshot { get; set; } = string.Empty;
}

/// <summary>清单中的一个版本条目。</summary>
public class ManifestEntry
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    /// <summary>release / snapshot / old_beta / old_alpha。</summary>
    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    /// <summary>版本 JSON 的下载地址。</summary>
    [JsonPropertyName("url")]
    public string Url { get; set; } = string.Empty;

    [JsonPropertyName("sha1")]
    public string? Sha1 { get; set; }

    [JsonPropertyName("releaseTime")]
    public DateTimeOffset? ReleaseTime { get; set; }
}
