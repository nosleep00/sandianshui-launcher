using SandianshuiLauncher.Core.Services;
using Xunit;

namespace SandianshuiLauncher.Core.Tests;

/// <summary>
/// <see cref="LauncherLogger"/> 测试：验证日志落盘、跨天滚动、过期清理与令牌脱敏。
/// </summary>
public class LauncherLoggerTests : IDisposable
{
    private readonly string _root;

    public LauncherLoggerTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "sdsl_logger_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
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
    public void Redact_AccessTokenKeyValue_MasksValue()
    {
        Assert.Equal(
            "access_token: ***",
            LauncherLogger.Redact("access_token: eyJhbGciOiJIUzI1NiJ9.secretvalue.moresecret"));
    }

    [Fact]
    public void Redact_JwtAlone_MasksWholeToken()
    {
        string redacted = LauncherLogger.Redact("token is eyJhbGciOiJIUzI1NiJ9.abcdefghijklmnop.xyz1234567 done");

        Assert.DoesNotContain("eyJhbGciOiJIUzI1NiJ9", redacted);
        Assert.Contains("***[TOKEN]***", redacted);
    }

    [Fact]
    public void Redact_PlainText_Unchanged()
    {
        Assert.Equal("正在下载 1.21.4 客户端", LauncherLogger.Redact("正在下载 1.21.4 客户端"));
    }

    [Fact]
    public void Info_WritesToLogFile()
    {
        using var logger = new LauncherLogger(_root);
        logger.Info("启动器启动。");
        logger.Dispose();

        string path = Path.Combine(_root, "logs", "launcher.log");
        Assert.True(File.Exists(path));
        Assert.Contains("启动器启动。", File.ReadAllText(path));
    }

    [Fact]
    public void Info_WithToken_WritesRedacted()
    {
        using var logger = new LauncherLogger(_root);
        logger.Info("刷新令牌成功 accessToken=supersecret1234567890");
        logger.Dispose();

        string content = File.ReadAllText(Path.Combine(_root, "logs", "launcher.log"));
        Assert.DoesNotContain("supersecret1234567890", content);
        Assert.Contains("***", content);
    }

    [Fact]
    public void Startup_RotatesStaleLog_ToDatedFile()
    {
        string logPath = Path.Combine(_root, "logs", "launcher.log");
        Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);
        File.WriteAllText(logPath, "old session");
        File.SetLastWriteTime(logPath, DateTime.Now.AddDays(-3));

        using var logger = new LauncherLogger(_root);
        logger.Dispose();

        // 旧日志被滚动为带日期的历史文件；本次会话未写日志时不再生成 launcher.log。
        Assert.False(File.Exists(logPath));
        Assert.True(Directory.EnumerateFiles(Path.Combine(_root, "logs"), "launcher-*.log").Any());
    }

    [Fact]
    public void Startup_CleansLogsOlderThanSevenDays()
    {
        string oldPath = Path.Combine(_root, "logs", "launcher-2020-01-01.log");
        string recentPath = Path.Combine(_root, "logs", "launcher-2026-11-01.log");
        Directory.CreateDirectory(Path.GetDirectoryName(oldPath)!);
        File.WriteAllText(oldPath, "old");
        File.WriteAllText(recentPath, "recent");
        File.SetLastWriteTime(oldPath, DateTime.Now.AddDays(-10));
        File.SetLastWriteTime(recentPath, DateTime.Now.AddDays(-3));

        using var logger = new LauncherLogger(_root);
        logger.Dispose();

        Assert.False(File.Exists(oldPath));
        Assert.True(File.Exists(recentPath));
    }
}
