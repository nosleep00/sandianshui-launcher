namespace SandianshuiLauncher.App.Tests;

/// <summary>
/// 仿 PCL2 的账号规则：离线账号可以没有用户名，
/// 但界面上要显示"未设置用户名"，且不能启动游戏。
/// </summary>
public class LaunchViewModelAccountTests : IDisposable
{
    private readonly TestServices _services = new();

    [Fact]
    public void UnnamedAccount_ShowsPlaceholderName()
    {
        _services.Accounts.AddOfflineAccount(string.Empty);

        _services.Launch.Refresh();

        Assert.Equal("未设置用户名", _services.Launch.AccountName);
        Assert.Equal("离线", _services.Launch.AccountTypeText);
    }

    [Fact]
    public void NamedAccount_ShowsItsName()
    {
        _services.Accounts.AddOfflineAccount("Steve");

        _services.Launch.Refresh();

        Assert.Equal("Steve", _services.Launch.AccountName);
    }

    [Fact]
    public async Task UnnamedAccount_CannotLaunch()
    {
        _services.Accounts.AddOfflineAccount(string.Empty);
        _services.Launch.Refresh();

        await _services.Launch.LaunchCommand.ExecuteAsync(null);

        Assert.Contains(_services.Dialogs.Infos, info => info.Contains("还没有设置用户名", StringComparison.Ordinal));
        Assert.Equal(0d, _services.Launch.LaunchProgress);
        Assert.False(_services.Launch.IsLaunching);
        Assert.Equal("就绪", _services.Launch.StatusText);
    }

    [Fact]
    public async Task AfterFillingUsername_LaunchNoLongerBlockedByUsername()
    {
        var account = _services.Accounts.AddOfflineAccount(string.Empty);
        _services.Accounts.SetOfflineUsername(account.Id, "Alex");
        _services.Launch.Refresh();

        await _services.Launch.LaunchCommand.ExecuteAsync(null);

        // 用户名检查已通过，后续卡在"没有可启动的版本"（测试环境没有装游戏）。
        Assert.DoesNotContain(_services.Dialogs.Infos, info => info.Contains("还没有设置用户名", StringComparison.Ordinal));
        Assert.Contains(_services.Dialogs.Infos, info => info.Contains("没有可启动的版本", StringComparison.Ordinal));
        Assert.Equal("Alex", _services.Launch.AccountName);
    }

    public void Dispose() => _services.Dispose();
}
