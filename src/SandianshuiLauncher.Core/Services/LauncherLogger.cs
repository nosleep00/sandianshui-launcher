using System.Text.RegularExpressions;
using System.Threading.Channels;

namespace SandianshuiLauncher.Core.Services;

/// <summary>
/// 启动器持久日志：异步写入 <c>&lt;dataDir&gt;/logs/launcher.log</c>，
/// 启动时把跨天的旧日志滚动为 launcher-yyyy-MM-dd.log，仅保留最近 7 天；
/// 写入前对访问令牌与疑似 JWT 做脱敏，禁止敏感信息落盘。
/// </summary>
public sealed partial class LauncherLogger : IDisposable
{
    private readonly string _directory;
    private readonly string _logPath;
    private readonly Channel<string> _queue;
    private readonly Task _writerTask;
    private readonly CancellationTokenSource _cts = new();
    private int _disposed;

    public LauncherLogger(string dataDirectory)
    {
        _directory = Path.Combine(dataDirectory, "logs");
        Directory.CreateDirectory(_directory);
        _logPath = Path.Combine(_directory, "launcher.log");

        RotateStaleLog();
        CleanupOldLogs();

        _queue = Channel.CreateUnbounded<string>(new UnboundedChannelOptions { SingleReader = true });
        _writerTask = Task.Run(() => WriteLoopAsync(_cts.Token));
    }

    public void Info(string message) => Log("INFO", message);

    public void Warn(string message) => Log("WARN", message);

    public void Error(string message, Exception? ex = null)
        => Log("ERROR", ex is null ? message : $"{message}{Environment.NewLine}{ex}");

    private void Log(string level, string message)
    {
        try
        {
            string line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level}] {Redact(message)}";
            _queue.Writer.TryWrite(line);
        }
        catch
        {
            // 日志失败不影响主流程。
        }
    }

    private async Task WriteLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            await foreach (string line in _queue.Reader.ReadAllAsync(cancellationToken))
            {
                try
                {
                    await File.AppendAllTextAsync(_logPath, line + Environment.NewLine, cancellationToken);
                }
                catch
                {
                    // 写盘失败（磁盘满/占用）静默跳过，避免日志拖垮启动器。
                }
            }
        }
        catch (OperationCanceledException)
        {
            // 正常关闭。
        }
    }

    /// <summary>把上一会话跨天的 launcher.log 滚动为带日期的历史文件。</summary>
    private void RotateStaleLog()
    {
        try
        {
            if (!File.Exists(_logPath))
            {
                return;
            }
            DateTime lastWrite = File.GetLastWriteTime(_logPath);
            if (lastWrite.Date < DateTime.Now.Date)
            {
                string archived = Path.Combine(_directory, $"launcher-{lastWrite:yyyy-MM-dd}.log");
                File.Move(_logPath, archived, overwrite: true);
            }
        }
        catch
        {
            // 滚动失败不影响日志功能。
        }
    }

    /// <summary>删除 7 天前的日志文件。</summary>
    private void CleanupOldLogs()
    {
        var cutoff = DateTime.Now.Date.AddDays(-7);
        try
        {
            foreach (string file in Directory.EnumerateFiles(_directory, "launcher-*.log"))
            {
                if (File.GetLastWriteTime(file) < cutoff)
                {
                    File.Delete(file);
                }
            }
        }
        catch
        {
            // 清理失败不影响日志功能。
        }
    }

    /// <summary>对日志文本中的访问令牌与疑似 JWT 做脱敏（公开工具，便于复用与测试）。</summary>
    public static string Redact(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return text;
        }
        // 疑似 JWT（三段 base64 用点连接）整体打码。
        string result = JwtRegex().Replace(text, "***[TOKEN]***");
        // access_token / accessToken 键值场景：保留键名，值打码。
        result = AccessTokenRegex().Replace(result, "$1***");
        return result;
    }

    [GeneratedRegex(@"[A-Za-z0-9\-_]{16,}\.[A-Za-z0-9\-_]{16,}\.[A-Za-z0-9\-_]{8,}")]
    private static partial Regex JwtRegex();

    [GeneratedRegex(@"(?i)(access[_-]?token[""':=\s]+)([A-Za-z0-9_\-\.]{8,})")]
    private static partial Regex AccessTokenRegex();

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return; // 幂等：重复释放安全。
        }
        // 先完成通道，让写入循环把队列中剩余行排空后正常退出（先取消会丢弃未写日志）。
        _queue.Writer.TryComplete();
        try
        {
            _writerTask.Wait(TimeSpan.FromSeconds(2));
        }
        catch
        {
            // 关闭等待超时忽略。
        }
        _cts.Dispose();
    }
}
