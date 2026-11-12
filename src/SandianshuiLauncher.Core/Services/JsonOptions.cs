using System.Text.Json;
using System.Text.Json.Serialization;

namespace SandianshuiLauncher.Core.Services;

/// <summary>统一的 JSON 序列化选项。</summary>
public static class JsonOptions
{
    /// <summary>Minecraft 生态 JSON（snake_case，模型通过 JsonPropertyName 显式映射）。</summary>
    public static readonly JsonSerializerOptions Minecraft = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    /// <summary>启动器自身配置（snake_case 落盘）。</summary>
    public static readonly JsonSerializerOptions Config = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };
}
