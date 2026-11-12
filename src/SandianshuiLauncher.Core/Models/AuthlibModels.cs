using System.Text.Json.Serialization;

namespace SandianshuiLauncher.Core.Models;

/// <summary>Yggdrasil（authlib-injector）认证响应。</summary>
public class YggdrasilAuthResponse
{
    [JsonPropertyName("accessToken")]
    public string? AccessToken { get; set; }

    [JsonPropertyName("clientToken")]
    public string? ClientToken { get; set; }

    [JsonPropertyName("availableProfiles")]
    public List<YggdrasilProfile>? AvailableProfiles { get; set; }

    [JsonPropertyName("selectedProfile")]
    public YggdrasilProfile? SelectedProfile { get; set; }
}

/// <summary>Yggdrasil 角色档案。</summary>
public class YggdrasilProfile
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }
}
