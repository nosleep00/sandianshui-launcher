using SandianshuiLauncher.Core.Models;
using SandianshuiLauncher.Core.Services;
using Xunit;

namespace SandianshuiLauncher.Core.Tests;

/// <summary>
/// 离线账号规则测试：仿 PCL2 允许用户名为空，
/// 但空用户名的账号既不能启动游戏，也必须能事后补填用户名。
/// </summary>
public class AccountServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "sdsl_account_" + Guid.NewGuid().ToString("N"));

    private AccountService CreateService()
    {
        Directory.CreateDirectory(_root);
        return new AccountService(new ConfigService(_root));
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch
        {
            // 清理失败不影响测试结果。
        }
    }

    [Fact]
    public void AddOfflineAccount_EmptyUsername_CreatesUnnamedAccount()
    {
        var accounts = CreateService();

        var account = accounts.AddOfflineAccount("   ");

        Assert.Equal(string.Empty, account.Username);
        Assert.Equal(string.Empty, account.Uuid);
        Assert.True(account.IsUsernameMissing);
        Assert.Equal("未设置用户名", account.DisplayName);
        Assert.Same(account, accounts.Current);
    }

    [Fact]
    public void AddOfflineAccount_SecondUnnamedAccount_IsRejected()
    {
        var accounts = CreateService();
        accounts.AddOfflineAccount(string.Empty);

        var error = Assert.Throws<ArgumentException>(() => accounts.AddOfflineAccount(string.Empty));

        Assert.Contains("未设置用户名", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("ab")]
    [InlineData("名字")]
    [InlineData("bad name")]
    [InlineData("0123456789abcdefg")]
    public void AddOfflineAccount_InvalidName_IsRejected(string username)
    {
        var accounts = CreateService();

        var error = Assert.Throws<ArgumentException>(() => accounts.AddOfflineAccount(username));

        Assert.Contains("3–16", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AddOfflineAccount_DuplicateName_IsRejected()
    {
        var accounts = CreateService();
        accounts.AddOfflineAccount("Steve");

        var error = Assert.Throws<ArgumentException>(() => accounts.AddOfflineAccount("steve"));

        Assert.Contains("同名", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AddOfflineAccount_NamedAccount_GetsOfflineUuid()
    {
        var accounts = CreateService();

        var account = accounts.AddOfflineAccount(" Steve ");

        Assert.Equal("Steve", account.Username);
        Assert.Equal(32, account.Uuid.Length);
        Assert.False(account.IsUsernameMissing);
        Assert.Equal("Steve", account.DisplayName);
    }

    [Fact]
    public void SetOfflineUsername_FillsNameAndRegeneratesUuid()
    {
        var accounts = CreateService();
        var account = accounts.AddOfflineAccount(string.Empty);

        var updated = accounts.SetOfflineUsername(account.Id, "Alex");

        Assert.Equal("Alex", updated.Username);
        Assert.Equal(32, updated.Uuid.Length);
        Assert.False(updated.IsUsernameMissing);
        // 补填后应落盘，重开服务仍能看到名字。
        Assert.Equal("Alex", CreateService().Current!.Username);
    }

    [Fact]
    public void SetOfflineUsername_EmptyName_IsRejected()
    {
        var accounts = CreateService();
        var account = accounts.AddOfflineAccount(string.Empty);

        Assert.Throws<ArgumentException>(() => accounts.SetOfflineUsername(account.Id, "  "));
        Assert.True(accounts.Current!.IsUsernameMissing);
    }

    [Fact]
    public void SetOfflineUsername_DuplicateName_IsRejected()
    {
        var accounts = CreateService();
        accounts.AddOfflineAccount("Steve");
        var unnamed = accounts.AddOfflineAccount(string.Empty);

        var error = Assert.Throws<ArgumentException>(() => accounts.SetOfflineUsername(unnamed.Id, "STEVE"));

        Assert.Contains("同名", error.Message, StringComparison.Ordinal);
        Assert.True(accounts.Current!.IsUsernameMissing);
    }

    [Fact]
    public void SetOfflineUsername_UnknownAccount_IsRejected()
    {
        var accounts = CreateService();

        var error = Assert.Throws<ArgumentException>(() => accounts.SetOfflineUsername("nope", "Alex"));

        Assert.Contains("未找到", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void SetOfflineUsername_DoesNotChangeAccountType()
    {
        var accounts = CreateService();
        var account = accounts.AddOfflineAccount(string.Empty);

        var updated = accounts.SetOfflineUsername(account.Id, "Alex");

        Assert.Equal(AccountType.Offline, updated.Type);
        Assert.Equal(account.Id, updated.Id);
    }
}
