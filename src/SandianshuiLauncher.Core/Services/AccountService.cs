using SandianshuiLauncher.Core.Models;
using SandianshuiLauncher.Core.Utils;
using System.Text.RegularExpressions;

namespace SandianshuiLauncher.Core.Services;

/// <summary>
/// 账号管理：离线账号的创建、选择、删除。
/// 微软与外置登录将在阶段 4 扩展。
/// </summary>
public partial class AccountService
{
    private readonly ConfigService _configService;

    public AccountService(ConfigService configService)
    {
        _configService = configService;
    }

    /// <summary>当前配置。</summary>
    public LauncherConfig Config => _configService.Config;

    /// <summary>当前选中的账号。</summary>
    public GameAccount? Current =>
        Config.Accounts.FirstOrDefault(a => a.Id == Config.SelectedAccountId);

    /// <summary>
    /// 添加一个离线账号并自动选中。用户名可以留空（仿 PCL2），
    /// 留空的账号会显示为"未设置用户名"，且不能启动游戏。
    /// </summary>
    public GameAccount AddOfflineAccount(string username)
    {
        username = username.Trim();
        if (string.IsNullOrEmpty(username))
        {
            // 未命名账号只保留一个，避免配置里堆一排空白账号。
            if (Config.Accounts.Any(a => a.Type == AccountType.Offline && string.IsNullOrEmpty(a.Username)))
            {
                throw new ArgumentException("已存在一个未设置用户名的离线账号，请先点击它填写用户名。");
            }
        }
        else
        {
            if (!IsValidUsername(username))
            {
                throw new ArgumentException("用户名需为 3–16 位字母、数字或下划线。");
            }

            if (Config.Accounts.Any(a =>
                    a.Type == AccountType.Offline &&
                    string.Equals(a.Username, username, StringComparison.OrdinalIgnoreCase)))
            {
                throw new ArgumentException("已存在同名离线账号。");
            }
        }

        var account = new GameAccount
        {
            Type = AccountType.Offline,
            Username = username,
            // 未命名账号没有 UUID，等用户补上用户名后再生成。
            Uuid = string.IsNullOrEmpty(username) ? string.Empty : OfflineUuid.GenerateDigits(username),
            // 离线模式不联网验证，令牌使用占位字符串（部分版本要求非空）。
            AccessToken = "0"
        };

        Config.Accounts.Add(account);
        Config.SelectedAccountId = account.Id;
        _configService.Save();
        return account;
    }

    /// <summary>
    /// 给离线账号设置（或修改）用户名，并重新生成离线 UUID。
    /// 用于"未设置用户名"的账号补填名称。
    /// </summary>
    public GameAccount SetOfflineUsername(string id, string username)
    {
        var account = Config.Accounts.FirstOrDefault(a => a.Id == id && a.Type == AccountType.Offline)
            ?? throw new ArgumentException("未找到该离线账号。");

        username = username.Trim();
        if (!IsValidUsername(username))
        {
            throw new ArgumentException("用户名需为 3–16 位字母、数字或下划线。");
        }

        if (Config.Accounts.Any(a =>
                a.Id != id &&
                a.Type == AccountType.Offline &&
                string.Equals(a.Username, username, StringComparison.OrdinalIgnoreCase)))
        {
            throw new ArgumentException("已存在同名离线账号。");
        }

        account.Username = username;
        account.Uuid = OfflineUuid.GenerateDigits(username);
        _configService.Save();
        return account;
    }

    /// <summary>添加一个外置登录账号并自动选中（令牌经 TokenCrypto 加密存储）。</summary>
    public GameAccount AddAuthlibAccount(
        string username, string uuid, string accessToken, string authServerUrl)
    {
        if (Config.Accounts.Any(a =>
                a.Type == AccountType.AuthlibInjector &&
                string.Equals(a.Username, username, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(a.AuthServerUrl, authServerUrl, StringComparison.OrdinalIgnoreCase)))
        {
            throw new ArgumentException("已存在同服务器下的同名账号。");
        }

        var account = new GameAccount
        {
            Type = AccountType.AuthlibInjector,
            Username = username,
            Uuid = uuid.Replace("-", string.Empty).ToLowerInvariant(),
            AccessToken = accessToken,
            AuthServerUrl = authServerUrl
        };

        Config.Accounts.Add(account);
        Config.SelectedAccountId = account.Id;
        _configService.Save();
        return account;
    }

    /// <summary>删除账号；若删除的是当前账号，自动改选第一个。</summary>
    public bool RemoveAccount(string id)
    {
        var account = Config.Accounts.FirstOrDefault(a => a.Id == id);
        if (account is null)
        {
            return false;
        }
        Config.Accounts.Remove(account);
        if (Config.SelectedAccountId == id)
        {
            Config.SelectedAccountId = Config.Accounts.FirstOrDefault()?.Id;
        }
        _configService.Save();
        return true;
    }

    /// <summary>选中指定账号。</summary>
    public bool SelectAccount(string id)
    {
        if (!Config.Accounts.Any(a => a.Id == id))
        {
            return false;
        }
        Config.SelectedAccountId = id;
        _configService.Save();
        return true;
    }

    /// <summary>离线用户名校验：3–16 位字母、数字、下划线。</summary>
    public static bool IsValidUsername(string username) =>
        !string.IsNullOrWhiteSpace(username) && UsernameRegex().IsMatch(username.Trim());

    [GeneratedRegex(@"^[A-Za-z0-9_]{3,16}$")]
    private static partial Regex UsernameRegex();
}
