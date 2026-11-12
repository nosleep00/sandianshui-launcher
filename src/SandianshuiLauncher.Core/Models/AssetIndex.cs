using System.Text.Json.Serialization;

namespace SandianshuiLauncher.Core.Models;

/// <summary>assets/indexes/{id}.json 资源索引。</summary>
public class AssetIndexFile
{
    [JsonPropertyName("objects")]
    public Dictionary<string, AssetObject> Objects { get; set; } = [];

    /// <summary>远古版本（1.5 及更早）资源需要虚拟映射到 assets/virtual/legacy。</summary>
    [JsonPropertyName("virtual")]
    public bool? Virtual { get; set; }

    [JsonPropertyName("map_to_resources")]
    public bool? MapToResources { get; set; }
}

/// <summary>单个资源对象。</summary>
public class AssetObject
{
    [JsonPropertyName("hash")]
    public string Hash { get; set; } = string.Empty;

    [JsonPropertyName("size")]
    public long Size { get; set; }
}
