namespace SandianshuiLauncher.Core.Services;

/// <summary>
/// 游戏进程窗口监视器：轮询等待主窗口出现，用于“窗口打开后自动最小化启动器”等场景。
/// </summary>
public static class ProcessWindowWatcher
{
    /// <summary>
    /// 在 <paramref name="timeout"/> 内每隔 <paramref name="interval"/> 查询一次窗口句柄；
    /// 进程提前退出或超时则返回 false。句柄查询异常按“未出现”处理。
    /// </summary>
    public static async Task<bool> WaitForWindowAsync(
        Func<IntPtr> handleProvider,
        Func<bool> hasExited,
        TimeSpan interval,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline && !hasExited())
        {
            try
            {
                if (handleProvider() != IntPtr.Zero)
                {
                    return true;
                }
            }
            catch
            {
                // 句柄查询失败按未出现处理，继续轮询。
            }
            await Task.Delay(interval, cancellationToken);
        }
        return false;
    }
}
