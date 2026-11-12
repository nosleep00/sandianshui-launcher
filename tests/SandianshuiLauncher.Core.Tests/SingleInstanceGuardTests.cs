using SandianshuiLauncher.Core.Services;
using Xunit;

namespace SandianshuiLauncher.Core.Tests;

/// <summary>
/// <see cref="SingleInstanceGuard"/> 测试：验证互斥量归属与管道激活通知。
/// 使用独立名称，避免与开发机上正在运行的真实启动器冲突。
/// </summary>
public class SingleInstanceGuardTests
{
    private static string UniqueName() => Guid.NewGuid().ToString("N");

    [Fact]
    public void FirstGuard_IsFirstInstance_True()
    {
        using var first = new SingleInstanceGuard(mutexName: "Local\\Test." + UniqueName());
        Assert.True(first.IsFirstInstance);
    }

    [Fact]
    public void SecondGuard_SameProcess_IsFirstInstanceFalse()
    {
        string mutexName = "Local\\Test." + UniqueName();
        using var first = new SingleInstanceGuard(mutexName: mutexName);
        using var second = new SingleInstanceGuard(mutexName: mutexName);

        Assert.True(first.IsFirstInstance);
        Assert.False(second.IsFirstInstance);
    }

    [Fact]
    public void AfterFirstDisposed_NextGuardBecomesFirstInstance()
    {
        string mutexName = "Local\\Test." + UniqueName();
        using (var first = new SingleInstanceGuard(mutexName: mutexName))
        {
            Assert.True(first.IsFirstInstance);
        }

        using var next = new SingleInstanceGuard(mutexName: mutexName);
        Assert.True(next.IsFirstInstance);
    }

    [Fact]
    public async Task NotifyExistingInstance_TriggersActivationCallback()
    {
        string pipeName = "Test." + UniqueName();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var signal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var first = new SingleInstanceGuard(mutexName: "Local\\Test." + UniqueName(), pipeName: pipeName);
        Assert.True(first.IsFirstInstance);

        Task listener = first.ListenAsync(() => signal.TrySetResult(), cts.Token, pipeName);

        // 监听就绪有延迟：轮询通知直到回调触发（上限 10 次）。
        for (int attempt = 0; attempt < 10 && !signal.Task.IsCompleted; attempt++)
        {
            await Task.Delay(200);
            SingleInstanceGuard.NotifyExistingInstance(pipeName);
            await Task.WhenAny(signal.Task, Task.Delay(300));
        }

        Task completed = await Task.WhenAny(signal.Task, Task.Delay(TimeSpan.FromSeconds(5)));
        Assert.Same(signal.Task, completed);

        cts.Cancel();
        await listener;
    }

    [Fact]
    public async Task NotifyExistingInstance_NoListener_DoesNotThrow()
    {
        // 没有第一实例监听时，通知应静默失败而不是抛异常。
        SingleInstanceGuard.NotifyExistingInstance("Test." + UniqueName());
        await Task.Delay(50);
    }
}
