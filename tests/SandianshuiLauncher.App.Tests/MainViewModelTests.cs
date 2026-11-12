using SandianshuiLauncher.Infrastructure;

namespace SandianshuiLauncher.App.Tests;

/// <summary>
/// 主窗口导航测试：验证每个页标识映射到正确的 ViewModel，未知标识回退启动页。
/// </summary>
public class MainViewModelTests : IDisposable
{
    private readonly TestServices _services = new();

    public void Dispose() => _services.Dispose();

    [Fact]
    public void Default_CurrentPageIsLaunch()
    {
        Assert.Same(_services.Launch, _services.Main.CurrentPage);
    }

    [Fact]
    public void Navigate_Download_ShowsDownloadPage()
    {
        _services.Main.NavigateCommand.Execute(PageKey.Download);
        Assert.Same(_services.Download, _services.Main.CurrentPage);
    }

    [Fact]
    public void Navigate_Mods_ShowsModsPage()
    {
        _services.Main.NavigateCommand.Execute(PageKey.Mods);
        Assert.Same(_services.Mods, _services.Main.CurrentPage);
    }

    [Fact]
    public void Navigate_Versions_ShowsVersionsPage()
    {
        _services.Main.NavigateCommand.Execute(PageKey.Versions);
        Assert.Same(_services.Versions, _services.Main.CurrentPage);
    }

    [Fact]
    public void Navigate_Settings_ShowsSettingsPage()
    {
        _services.Main.NavigateCommand.Execute(PageKey.Settings);
        Assert.Same(_services.Settings, _services.Main.CurrentPage);
    }

    [Fact]
    public void Navigate_Unknown_FallsBackToLaunchPage()
    {
        _services.Main.NavigateCommand.Execute(PageKey.Download);
        _services.Main.NavigateCommand.Execute((PageKey)999);

        Assert.Same(_services.Launch, _services.Main.CurrentPage);
    }
}
