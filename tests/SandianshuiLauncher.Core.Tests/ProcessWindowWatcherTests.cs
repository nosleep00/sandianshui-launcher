using SandianshuiLauncher.Core.Services;
using Xunit;

namespace SandianshuiLauncher.Core.Tests;

/// <summary>
/// <see cref="ProcessWindowWatcher"/> 测试：窗口出现即返回、进程提前退出返回 false。
/// </summary>
public class ProcessWindowWatcherTests
{
    [Fact]
    public async Task WindowAppearsAfterTwoPolls_ReturnsTrue()
    {
        int polls = 0;
        bool windowAppeared = await ProcessWindowWatcher.WaitForWindowAsync(
            () => ++polls >= 2 ? new IntPtr(1234) : IntPtr.Zero,
            () => false,
            TimeSpan.FromMilliseconds(10),
            TimeSpan.FromSeconds(5),
            CancellationToken.None);

        Assert.True(windowAppeared);
        Assert.True(polls >= 2);
    }

    [Fact]
    public async Task ProcessExitsEarly_ReturnsFalse()
    {
        bool windowAppeared = await ProcessWindowWatcher.WaitForWindowAsync(
            () => IntPtr.Zero,
            () => true, // 进程已退出。
            TimeSpan.FromMilliseconds(10),
            TimeSpan.FromSeconds(5),
            CancellationToken.None);

        Assert.False(windowAppeared);
    }

    [Fact]
    public async Task TimeoutWithoutWindow_ReturnsFalse()
    {
        bool windowAppeared = await ProcessWindowWatcher.WaitForWindowAsync(
            () => IntPtr.Zero,
            () => false,
            TimeSpan.FromMilliseconds(10),
            TimeSpan.FromMilliseconds(60),
            CancellationToken.None);

        Assert.False(windowAppeared);
    }
}
