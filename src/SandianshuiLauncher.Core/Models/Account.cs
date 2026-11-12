using System.Text.Json.Serialization;
using SandianshuiLauncher.Core.Services;

namespace SandianshuiLauncher.Core.Models;

/// <summary>
/// 账号类型。
/// </summary>
public enum AccountType
{
    /// <summary>离线账号：不联网验证，用户名自填。</summary>
    Offline = 0,

    /// <summary>微软正版账号：OAuth 登录（阶段 4 实现）。</summary>
    Microsoft = 1,

    /// <summary>外置登录：authlib-injector 皮肤站（阶段 4 实现）。</summary>
    AuthlibInjector = 2
}

/// <summary>
/// 一个游戏账号。
/// </summary>
public class GameAccount
{
    /// <summary>启动器内部唯一编号。</summary>
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    /// <summary>账号类型。</summary>
    public AccountType Type { get; set; } = AccountType.Offline;

    /// <summary>玩家用户名。</summary>
    public string Username { get; set; } = string.Empty;

    /// <summary>玩家 UUID（无连字符 32 位小写）。</summary>
    public string Uuid { get; set; } = string.Empty;

    /// <summary>访问令牌密文（DPAPI 加密 + base64，落盘字段）。</summary>
    public string? EncryptedToken { get; set; }

    private string? _accessToken;

    /// <summary>
    /// 访问令牌明文（运行时属性，不参与序列化）。
    /// 读取时自动解密；写入时自动加密到 <see cref="EncryptedToken"/>。
    /// </summary>
    [JsonIgnore]
    public string? AccessToken
    {
        get
        {
            if (_accessToken is null && !string.IsNullOrEmpty(EncryptedToken))
            {
                _accessToken = TokenCrypto.Unprotect(EncryptedToken);
            }
            return _accessToken;
        }
        set
        {
            _accessToken = value;
            EncryptedToken = string.IsNullOrEmpty(value) ? null : TokenCrypto.Protect(value);
        }
    }

    /// <summary>
    /// 旧版配置中的明文令牌字段（仅用于反序列化迁移；
    /// 迁移完成后置空，此后不再写盘，见 ConfigService.MigrateAccountTokens）。
    /// </summary>
    [JsonPropertyName("access_token")]
    public string? LegacyAccessToken { get; set; }

    /// <summary>外置登录认证服务器地址。</summary>
    public string? AuthServerUrl { get; set; }

    /// <summary>令牌过期时间。</summary>
    public DateTimeOffset? ExpiresAt { get; set; }

    /// <summary>类型中文显示名。</summary>
    [JsonIgnore]
    public string DisplayType => Type switch
    {
        AccountType.Offline => "离线",
        AccountType.Microsoft => "微软正版",
        AccountType.AuthlibInjector => "外置登录",
        _ => "未知"
    };

    /// <summary>
    /// 是否还没有填写用户名。仿 PCL2：离线账号允许不填用户名，
    /// 但这样的账号不能启动游戏（见 LaunchViewModel.LaunchAsync）。
    /// </summary>
    [JsonIgnore]
    public bool IsUsernameMissing => string.IsNullOrWhiteSpace(Username);

    /// <summary>界面上显示的名字；未填用户名时给出提示文案而不是空白。</summary>
    [JsonIgnore]
    public string DisplayName => IsUsernameMissing ? "未设置用户名" : Username;
}
