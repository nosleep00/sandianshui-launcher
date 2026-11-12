namespace SandianshuiLauncher.App.Tests;

/// <summary>
/// 首页启动进度（进度条 + 阶段文案）的 ViewModel 行为测试。
/// </summary>
public class LaunchViewModelProgressTests : IDisposable
{
    private readonly TestServices _services = new();

    [Fact]
    public void NewViewModel_IsReadyWithZeroProgress()
    {
        Assert.Equal("就绪", _services.Launch.StatusText);
        Assert.Equal(0d, _services.Launch.LaunchProgress);
        Assert.False(_services.Launch.IsLaunching);
    }

    [Fact]
    public async Task LaunchWithoutAccount_ShowsPromptAndKeepsProgressAtZero()
    {
        await _services.Launch.LaunchCommand.ExecuteAsync(null);

        Assert.Contains("请先添加一个离线账号。", _services.Dialogs.Infos);
        Assert.Equal(0d, _services.Launch.LaunchProgress);
        Assert.Equal("就绪", _services.Launch.StatusText);
        Assert.False(_services.Launch.IsLaunching);
    }

    [Fact]
    public async Task LaunchWithoutVersion_ShowsPromptAndKeepsProgressAtZero()
    {
        // 只有账号、没有任何已安装版本：应当提示去下载页，而不是把进度条推到一半。
        _services.Config.Config.GameDirectory = Path.Combine(_services.Root, "empty-mc");
        Directory.CreateDirectory(_services.Config.Config.GameDirectory);

        await _services.Launch.LaunchCommand.ExecuteAsync(null);

        Assert.Equal(0d, _services.Launch.LaunchProgress);
        Assert.False(_services.Launch.IsLaunching);
    }

    public void Dispose() => _services.Dispose();
}
