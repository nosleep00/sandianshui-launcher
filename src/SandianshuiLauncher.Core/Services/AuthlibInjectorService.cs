using System.Text;
using System.Text.Json;
using SandianshuiLauncher.Core.Models;

namespace SandianshuiLauncher.Core.Services;

/// <summary>
/// 外置登录（authlib-injector / Yggdrasil 协议）：
/// 向第三方认证服务器（如 LittleSkin）认证账号，并按需下载 authlib-injector javaagent。
/// </summary>
public class AuthlibInjectorService
{
    /// <summary>BMCLAPI 镜像的 authlib-injector 最新版元数据。</summary>
    public const string BmclLatestUrl =
        "https://bmclapi2.bangbang93.com/mirrors/authlib-injector/artifact/latest.json";

    /// <summary>authlib-injector 官方发布站最新版元数据。</summary>
    public const string OfficialLatestUrl =
        "https://authlib-injector.yushi.moe/artifact/latest.json";

    /// <summary>authlib-injector 官方发布站基址。</summary>
    public const string OfficialBaseUrl = "https://authlib-injector.yushi.moe";

    private readonly HttpClient _http;
    private readonly DownloadService _downloadService;

    public AuthlibInjectorService(HttpClient http, DownloadService downloadService)
    {
        _http = http;
        _downloadService = downloadService;
    }

    /// <summary>
    /// 使用 Yggdrasil 协议认证，返回可直接用于启动游戏的账号。
    /// 认证失败时抛出含服务器返回原因的中文异常。
    /// </summary>
    public async Task<GameAccount> AuthenticateAsync(
        string authServerUrl, string email, string password, CancellationToken cancellationToken = default)
    {
        string server = (authServerUrl ?? string.Empty).Trim().TrimEnd('/');
        if (string.IsNullOrWhiteSpace(server))
        {
            throw new ArgumentException("认证服务器地址不能为空。");
        }

        var payload = new
        {
            agent = new { name = "Minecraft", version = 1 },
            username = email,
            password,
            clientToken = Guid.NewGuid().ToString("N"),
            requestUser = false
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, server + "/authserver/authenticate")
        {
            Content = new StringContent(
                JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json")
        };
        using var response = await _http.SendAsync(request, cancellationToken);
        string body = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                BuildFriendlyError(body, "认证失败，请检查邮箱、密码与认证服务器地址。"));
        }

        var auth = JsonSerializer.Deserialize<YggdrasilAuthResponse>(body, JsonOptions.Minecraft)
                   ?? throw new InvalidDataException("认证服务器响应解析失败。");

        YggdrasilProfile? profile = auth.SelectedProfile ?? auth.AvailableProfiles?.FirstOrDefault();
        if (string.IsNullOrEmpty(auth.AccessToken)
            || profile is null
            || string.IsNullOrEmpty(profile.Id)
            || string.IsNullOrEmpty(profile.Name))
        {
            throw new InvalidDataException(
                "认证服务器未返回有效的角色信息，请确认该邮箱已注册皮肤站账号。");
        }

        return new GameAccount
        {
            Type = AccountType.AuthlibInjector,
            Username = profile.Name,
            Uuid = profile.Id.Replace("-", string.Empty).ToLowerInvariant(),
            AccessToken = auth.AccessToken,
            AuthServerUrl = server
        };
    }

    /// <summary>
    /// 确保 authlib-injector jar 位于本地：本地已有非空文件则跳过；
    /// 否则按「BMCLAPI 镜像 → 官方」多源下载。返回本地 jar 路径。
    /// </summary>
    public async Task<string> EnsureInjectorAsync(string targetDirectory, CancellationToken cancellationToken = default)
    {
        string jarPath = Path.Combine(targetDirectory, "authlib-injector.jar");
        if (File.Exists(jarPath) && new FileInfo(jarPath).Length > 0)
        {
            return jarPath;
        }

        Directory.CreateDirectory(targetDirectory);
        List<string> urls = await ResolveArtifactUrlsAsync(cancellationToken);
        var task = new DownloadTask(
            urls[0], jarPath, sha1: null, size: 0, "authlib-injector.jar",
            urls.Count > 1 ? urls.Skip(1).ToList() : null);
        await _downloadService.DownloadAsync(new[] { task }, maxParallel: 1, cancellationToken: cancellationToken);
        return jarPath;
    }

    /// <summary>
    /// 解析最新版元数据得到候选下载地址：
    /// 元数据内嵌的 download_url 优先，随后按版本号拼「BMCLAPI 镜像 → 官方」候选。
    /// 全部元数据源失败时抛出中文异常。
    /// </summary>
    private async Task<List<string>> ResolveArtifactUrlsAsync(CancellationToken cancellationToken)
    {
        string? version = null;
        string? downloadUrl = null;
        foreach (string metaUrl in new[] { BmclLatestUrl, OfficialLatestUrl })
        {
            try
            {
                using var response = await _http.GetAsync(metaUrl, cancellationToken);
                response.EnsureSuccessStatusCode();
                byte[] bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
                (version, downloadUrl) = ParseLatestJson(bytes);
                if (!string.IsNullOrEmpty(downloadUrl))
                {
                    break;
                }
            }
            catch
            {
                // 换下一个元数据源。
            }
        }

        var urls = new List<string>();
        if (!string.IsNullOrEmpty(downloadUrl))
        {
            urls.Add(downloadUrl!);
        }
        if (!string.IsNullOrEmpty(version))
        {
            // BMCLAPI 镜像路径规则与官方一致，仅替换域名。
            string bmcl = $"https://bmclapi2.bangbang93.com/mirrors/authlib-injector/artifact/{version}/authlib-injector-{version}.jar";
            if (!urls.Contains(bmcl))
            {
                urls.Add(bmcl);
            }
            string official = $"{OfficialBaseUrl}/artifact/{version}/authlib-injector-{version}.jar";
            if (!urls.Contains(official))
            {
                urls.Add(official);
            }
        }
        if (urls.Count == 0)
        {
            throw new InvalidDataException(
                "无法获取 authlib-injector 下载地址。\n请检查网络，或稍后在“设置”中重试外置登录。");
        }
        return urls;
    }

    /// <summary>宽松解析 latest.json：兼容不同字段命名（version、download_url / downloadUrl）。</summary>
    internal static (string? Version, string? DownloadUrl) ParseLatestJson(byte[] bytes)
    {
        using JsonDocument doc = JsonDocument.Parse(bytes);
        JsonElement root = doc.RootElement;

        string? version = null;
        if (root.TryGetProperty("version", out JsonElement versionElement)
            && versionElement.ValueKind == JsonValueKind.String)
        {
            version = versionElement.GetString();
        }

        string? downloadUrl = null;
        foreach (string key in new[] { "download_url", "downloadUrl" })
        {
            if (root.TryGetProperty(key, out JsonElement urlElement)
                && urlElement.ValueKind == JsonValueKind.String
                && !string.IsNullOrWhiteSpace(urlElement.GetString()))
            {
                downloadUrl = urlElement.GetString();
                break;
            }
        }
        return (version, downloadUrl);
    }

    /// <summary>从认证服务器错误响应中提取人话原因；无法解析时用兜底文案。</summary>
    internal static string BuildFriendlyError(string body, string fallback)
    {
        try
        {
            using JsonDocument doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("errorMessage", out JsonElement message)
                && message.ValueKind == JsonValueKind.String
                && !string.IsNullOrWhiteSpace(message.GetString()))
            {
                return "认证失败：" + message.GetString() + "\n请检查邮箱、密码与认证服务器地址。";
            }
        }
        catch
        {
            // 响应体不是 JSON，用兜底文案。
        }
        return fallback;
    }
}
