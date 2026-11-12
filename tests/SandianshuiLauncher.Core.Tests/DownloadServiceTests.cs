using System.Net;
using System.Security.Cryptography;
using SandianshuiLauncher.Core.Models;
using SandianshuiLauncher.Core.Services;
using SandianshuiLauncher.Core.Utils;
using Xunit;

namespace SandianshuiLauncher.Core.Tests;

/// <summary>
/// 下载器测试：使用本地 HttpListener 模拟正常下载、中断后 206 续传、中断后 200 重下，
/// 重点验证进度字节数不会因为重试/续传而重复计数（百分比不得超过 100%）。
/// </summary>
public class DownloadServiceTests
{
    private static int _serverCounter;

    /// <summary>服务器行为模式。</summary>
    private enum Mode
    {
        /// <summary>始终 200 返回完整内容。</summary>
        Normal,
        /// <summary>第一次请求发送一半后断开，第二次带 Range 返回 206 剩余部分。</summary>
        Resume206,
        /// <summary>第一次请求发送一半后断开，之后即便带 Range 也返回 200 完整内容。</summary>
        Redownload200,
        /// <summary>始终返回 404，用于测试多源回退。</summary>
        Fail404
    }

    private sealed class ReportCollector : IProgress<DownloadProgress>
    {
        private readonly List<DownloadProgress> _items = [];
        public void Report(DownloadProgress value)
        {
            lock (_items)
            {
                _items.Add(value);
            }
        }
        public DownloadProgress Last
        {
            get
            {
                lock (_items)
                {
                    return _items[^1];
                }
            }
        }
        public bool AnyOverTotal()
        {
            lock (_items)
            {
                return _items.Any(p => p.DownloadedBytes > p.TotalBytes || p.Percent > 100);
            }
        }
    }

    private static string Sha1Hex(byte[] data)
    {
        byte[] hash = SHA1.HashData(data);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    /// <summary>
    /// 在本地启动一个只服务单个文件的 HttpListener。
    /// </summary>
    private static HttpListener StartServer(byte[] content, Mode mode, out string url)
    {
        var listener = new HttpListener();
        // 用静态递增端口避免并发测试冲突。
        int port = 18000 + Interlocked.Increment(ref _serverCounter);
        url = $"http://localhost:{port}/file.bin";
        listener.Prefixes.Add($"http://localhost:{port}/");
        listener.Start();

        int requestCount = 0;
        var thread = new Thread(() =>
        {
            while (listener.IsListening)
            {
                HttpListenerContext ctx;
                try
                {
                    ctx = listener.GetContext();
                }
                catch (HttpListenerException)
                {
                    break;
                }
                catch (ObjectDisposedException)
                {
                    break;
                }

                int n = Interlocked.Increment(ref requestCount);
                try
                {
                    if (mode == Mode.Fail404)
                    {
                        ctx.Response.StatusCode = (int)HttpStatusCode.NotFound;
                        ctx.Response.Close();
                        continue;
                    }

                    long start = 0;
                    string? range = ctx.Request.Headers["Range"];
                    bool hasRange = !string.IsNullOrEmpty(range)
                                    && range!.StartsWith("bytes=", StringComparison.OrdinalIgnoreCase)
                                    && long.TryParse(range.Substring(6).Split('-')[0], out start);

                    // 第一次请求：在 Normal 之外的模式下，发送一半后断开。
                    bool sabotage = n == 1 && mode != Mode.Normal;
                    if (sabotage)
                    {
                        int half = content.Length / 2;
                        ctx.Response.StatusCode = (int)HttpStatusCode.OK;
                        ctx.Response.ContentLength64 = content.Length;
                        ctx.Response.OutputStream.Write(content, 0, half);
                        ctx.Response.OutputStream.Flush();
                        ctx.Response.Abort();
                        continue;
                    }

                    if (mode == Mode.Resume206 && hasRange)
                    {
                        int remaining = content.Length - (int)start;
                        ctx.Response.StatusCode = (int)HttpStatusCode.PartialContent;
                        ctx.Response.ContentLength64 = remaining;
                        ctx.Response.Headers["Content-Range"] =
                            $"bytes {start}-{content.Length - 1}/{content.Length}";
                        ctx.Response.OutputStream.Write(content, (int)start, remaining);
                        ctx.Response.OutputStream.Close();
                    }
                    else
                    {
                        // Redownload200 或 Normal：忽略 Range，返回完整内容。
                        ctx.Response.StatusCode = (int)HttpStatusCode.OK;
                        ctx.Response.ContentLength64 = content.Length;
                        ctx.Response.OutputStream.Write(content, 0, content.Length);
                        ctx.Response.OutputStream.Close();
                    }
                }
                catch
                {
                    // 连接断开等异常在测试场景下属预期，忽略。
                    try { ctx.Response.Abort(); } catch { /* 忽略 */ }
                }
            }
        })
        {
            IsBackground = true
        };
        thread.Start();
        return listener;
    }

    [Fact]
    public async Task Download_Normal_Reaches100Percent()
    {
        using var temp = new TempDirectory();
        byte[] content = new byte[120_000];
        Random.Shared.NextBytes(content);
        string sha1 = Sha1Hex(content);
        string dest = Path.Combine(temp.Path, "file.bin");

        using HttpListener server = StartServer(content, Mode.Normal, out string url);
        using var http = new HttpClient();
        var downloader = new DownloadService(http, retryCount: 2);

        var tasks = new List<DownloadTask>
        {
            new(url, dest, sha1, content.Length, "file.bin")
        };
        var progress = new ReportCollector();
        await downloader.DownloadAsync(tasks, 2, progress);

        byte[] downloaded = await File.ReadAllBytesAsync(dest);
        Assert.Equal(content, downloaded);
        Assert.Equal(content.Length, progress.Last.DownloadedBytes);
        Assert.Equal(100, progress.Last.Percent);
        Assert.False(progress.AnyOverTotal());
    }

    [Fact]
    public async Task Download_ResumeWith206_DoesNotDoubleCount()
    {
        using var temp = new TempDirectory();
        byte[] content = new byte[240_000];
        Random.Shared.NextBytes(content);
        string sha1 = Sha1Hex(content);
        string dest = Path.Combine(temp.Path, "file.bin");

        using HttpListener server = StartServer(content, Mode.Resume206, out string url);
        using var http = new HttpClient();
        var downloader = new DownloadService(http, retryCount: 3);

        var progress = new ReportCollector();
        await downloader.DownloadAsync(
            [new DownloadTask(url, dest, sha1, content.Length, "file.bin")],
            1,
            progress);

        byte[] downloaded = await File.ReadAllBytesAsync(dest);
        Assert.Equal(content, downloaded);
        Assert.Equal(content.Length, progress.Last.DownloadedBytes);
        Assert.Equal(100, progress.Last.Percent);
        Assert.False(progress.AnyOverTotal());
    }

    [Fact]
    public async Task Download_RedownloadWith200_DoesNotDoubleCount()
    {
        using var temp = new TempDirectory();
        byte[] content = new byte[200_000];
        Random.Shared.NextBytes(content);
        string sha1 = Sha1Hex(content);
        string dest = Path.Combine(temp.Path, "file.bin");

        using HttpListener server = StartServer(content, Mode.Redownload200, out string url);
        using var http = new HttpClient();
        var downloader = new DownloadService(http, retryCount: 3);

        var progress = new ReportCollector();
        await downloader.DownloadAsync(
            [new DownloadTask(url, dest, sha1, content.Length, "file.bin")],
            1,
            progress);

        byte[] downloaded = await File.ReadAllBytesAsync(dest);
        Assert.Equal(content, downloaded);
        Assert.Equal(content.Length, progress.Last.DownloadedBytes);
        Assert.Equal(100, progress.Last.Percent);
        Assert.False(progress.AnyOverTotal());
    }

    [Fact]
    public async Task Download_FallbackUrl_UsedWhenPrimaryReturns404()
    {
        using var temp = new TempDirectory();
        byte[] content = new byte[100_000];
        Random.Shared.NextBytes(content);
        string sha1 = Sha1Hex(content);
        string dest = Path.Combine(temp.Path, "file.bin");

        // 主源始终 404，备用源正常返回完整内容。
        using HttpListener failServer = StartServer(content, Mode.Fail404, out string failUrl);
        using HttpListener okServer = StartServer(content, Mode.Normal, out string okUrl);
        using var http = new HttpClient();
        var downloader = new DownloadService(http, retryCount: 1);

        var task = new DownloadTask(
            failUrl,
            dest,
            sha1,
            content.Length,
            "file.bin",
            [okUrl]);
        var progress = new ReportCollector();
        await downloader.DownloadAsync([task], 1, progress);

        // 应自动回退到备用源并完整下载。
        byte[] downloaded = await File.ReadAllBytesAsync(dest);
        Assert.Equal(content, downloaded);
        Assert.Equal(100, progress.Last.Percent);
    }

    private sealed class TempDirectory : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            "sds-launcher-tests-" + Guid.NewGuid().ToString("N"));

        public TempDirectory()
        {
            Directory.CreateDirectory(Path);
        }

        public void Dispose()
        {
            try { Directory.Delete(Path, recursive: true); } catch { /* 忽略 */ }
        }
    }
}
