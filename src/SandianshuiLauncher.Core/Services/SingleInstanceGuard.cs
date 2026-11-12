using System.IO.Pipes;

namespace SandianshuiLauncher.Core.Services;

/// <summary>
/// 单实例保护：第一个实例持有命名 Mutex；
/// 后续实例通过命名管道通知已有实例激活主窗口后自行退出。
/// </summary>
public sealed class SingleInstanceGuard : IDisposable
{
    /// <summary>当前用户作用域的互斥量名称。</summary>
    public const string MutexName = @"Local\SandianshuiLauncher.SingleInstance";

    /// <summary>第二实例 → 第一实例的通知管道名称。</summary>
    public const string PipeName = "SandianshuiLauncher.Activate";

    private const string ActivateMessage = "activate";

    private readonly Mutex? _mutex;
    private readonly string _pipeName;

    /// <summary>创建单实例守卫；测试可注入独立名称以避免与运行中的真实实例冲突。</summary>
    public SingleInstanceGuard(string? mutexName = null, string? pipeName = null)
    {
        _pipeName = pipeName ?? PipeName;
        _mutex = new Mutex(initiallyOwned: true, mutexName ?? MutexName, out bool createdNew);
        IsFirstInstance = createdNew;
    }

    /// <summary>是否为第一个实例（持有互斥量）。</summary>
    public bool IsFirstInstance { get; }

    /// <summary>通知已存在的实例把主窗口置前（失败静默，不影响第二实例退出）。</summary>
    public static void NotifyExistingInstance(string? pipeName = null)
    {
        try
        {
            using var client = new NamedPipeClientStream(".", pipeName ?? PipeName, PipeDirection.Out);
            client.Connect(2000);
            using var writer = new StreamWriter(client) { AutoFlush = true };
            writer.WriteLine(ActivateMessage);
        }
        catch
        {
            // 第一实例可能刚退出，忽略。
        }
    }

    /// <summary>
    /// 启动激活通知监听循环；收到通知时在调用方线程池回调 <paramref name="onActivate"/>。
    /// 返回的 Task 随 <paramref name="cancellationToken"/> 取消而完成。
    /// </summary>
    public Task ListenAsync(Action onActivate, CancellationToken cancellationToken, string? pipeName = null)
        => Task.Run(() => ListenLoopAsync(onActivate, cancellationToken, pipeName ?? _pipeName), cancellationToken);

    private static async Task ListenLoopAsync(
        Action onActivate, CancellationToken cancellationToken, string pipeName)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            using var server = new NamedPipeServerStream(
                pipeName, PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
            try
            {
                await server.WaitForConnectionAsync(cancellationToken);
                using var reader = new StreamReader(server);
                string? line = await reader.ReadLineAsync(cancellationToken);
                if (string.Equals(line, ActivateMessage, StringComparison.Ordinal))
                {
                    onActivate();
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch
            {
                // 连接中断/占用等异常：继续等待下一连接。
            }
        }
    }

    public void Dispose()
    {
        if (_mutex is null)
        {
            return;
        }
        try
        {
            _mutex.ReleaseMutex();
        }
        catch
        {
            // 未持有或已释放时忽略。
        }
        _mutex.Dispose();
    }
}
