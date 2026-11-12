using System.Net;
using System.Security.Cryptography;
using SandianshuiLauncher.Core.Models;
using SandianshuiLauncher.Core.Services;
using Xunit;

namespace SandianshuiLauncher.Core.Tests;

/// <summary>
/// 大文件分片下载测试：验证 Range 分片并发、按序合并、SHA1 校验、
/// 服务器不支持 Range 时自动降级单线程整文件。
/// </summary>
public class DownloadChunkedTests : IDisposable
{
    private const int ThresholdForTest = 1024; // 测试用小阈值触发分片路径。

    private static int _serverCounter;
    private readonly string _root;

    public DownloadChunkedTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "sdsl_chunked_" + Guid.NewGuid().ToString("N"));
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

    private enum Mode
    {
        /// <summary>正确响应 Range 请求（206 + 分片内容）。</summary>
        Chunked206,
        /// <summary>无视 Range，总是返回 200 完整内容（触发降级）。</summary>
        Full200
    }

    /// <summary>本地文件服务器；返回监听器、端口与 Range 请求计数（int 包装）。</summary>
    private static HttpListener StartServer(byte[] content, Mode mode, out string url, out Func<int> rangeRequestCount)
    {
        var listener = new HttpListener();
        int port = 19000 + Interlocked.Increment(ref _serverCounter);
        url = $"http://localhost:{port}/big.bin";
        listener.Prefixes.Add($"http://localhost:{port}/");
        listener.Start();

        int rangeCount = 0;
        rangeRequestCount = () => rangeCount;
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

                try
                {
                    string? range = ctx.Request.Headers["Range"];
                    bool hasRange = !string.IsNullOrEmpty(range)
                                    && range!.StartsWith("bytes=", StringComparison.OrdinalIgnoreCase);
                    if (hasRange)
                    {
                        Interlocked.Increment(ref rangeCount);
                    }

                    if (mode == Mode.Chunked206 && hasRange)
                    {
                        // 解析 bytes=start-end。
                        string[] parts = range!["bytes=".Length..].Split('-');
                        long start = long.Parse(parts[0]);
                        long end = parts.Length > 1 && parts[1].Length > 0
                            ? long.Parse(parts[1])
                            : content.Length - 1;
                        int length = (int)(end - start + 1);
                        ctx.Response.StatusCode = (int)HttpStatusCode.PartialContent;
                        ctx.Response.ContentLength64 = length;
                        ctx.Response.Headers["Content-Range"] = $"bytes {start}-{end}/{content.Length}";
                        ctx.Response.OutputStream.Write(content, (int)start, length);
                        ctx.Response.OutputStream.Close();
                    }
                    else
                    {
                        ctx.Response.StatusCode = (int)HttpStatusCode.OK;
                        ctx.Response.ContentLength64 = content.Length;
                        ctx.Response.OutputStream.Write(content, 0, content.Length);
                        ctx.Response.OutputStream.Close();
                    }
                }
                catch
                {
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

    private static string Sha1Hex(byte[] data)
        => Convert.ToHexString(SHA1.HashData(data)).ToLowerInvariant();

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
        public bool AnyOverTotal()
        {
            lock (_items)
            {
                return _items.Any(p => p.DownloadedBytes > p.TotalBytes || p.Percent > 100);
            }
        }
    }

    [Fact]
    public async Task Download_Chunked_MergesPartsAndVerifies()
    {
        byte[] content = new byte[64 * 1024];
        new Random(42).NextBytes(content);
        HttpListener server = StartServer(content, Mode.Chunked206, out string url, out Func<int> rangeCount);

        try
        {
            string destination = Path.Combine(_root, "big.bin");
            var http = new HttpClient();
            var downloader = new DownloadService(http, retryCount: 3, chunkThresholdBytes: ThresholdForTest);
            var task = new DownloadTask(url, destination, Sha1Hex(content), content.Length, "big.bin");
            var progress = new ReportCollector();

            await downloader.DownloadAsync(new[] { task }, maxParallel: 8, progress);

            Assert.Equal(content, await File.ReadAllBytesAsync(destination));
            Assert.Equal(4, rangeCount());
            Assert.False(progress.AnyOverTotal());
            Assert.Empty(Directory.EnumerateFiles(_root, "big.bin.tmp.part*"));
        }
        finally
        {
            server.Stop();
        }
    }

    [Fact]
    public async Task Download_ServerIgnoresRange_FallsBackToSingleThread()
    {
        byte[] content = new byte[64 * 1024];
        new Random(7).NextBytes(content);
        HttpListener server = StartServer(content, Mode.Full200, out string url, out Func<int> rangeCount);

        try
        {
            string destination = Path.Combine(_root, "big.bin");
            var http = new HttpClient();
            var downloader = new DownloadService(http, retryCount: 3, chunkThresholdBytes: ThresholdForTest);
            var task = new DownloadTask(url, destination, Sha1Hex(content), content.Length, "big.bin");

            await downloader.DownloadAsync(new[] { task }, maxParallel: 8);

            Assert.Equal(content, await File.ReadAllBytesAsync(destination));
            // 服务器不支持 Range：至少发起过 1 次分片请求，随后自动降级整文件并成功。
            // （Parallel.ForEachAsync 在首个分片失败后会取消未开始的分片，故不能断言恰为 4 次。）
            Assert.True(rangeCount() >= 1);
            Assert.Empty(Directory.EnumerateFiles(_root, "big.bin.tmp.part*"));
        }
        finally
        {
            server.Stop();
        }
    }

    [Fact]
    public async Task Download_BelowThreshold_NoRangeRequests()
    {
        byte[] content = new byte[512]; // 小于测试阈值。
        new Random(3).NextBytes(content);
        HttpListener server = StartServer(content, Mode.Chunked206, out string url, out Func<int> rangeCount);

        try
        {
            string destination = Path.Combine(_root, "small.bin");
            var http = new HttpClient();
            var downloader = new DownloadService(http, retryCount: 3, chunkThresholdBytes: ThresholdForTest);
            var task = new DownloadTask(url, destination, Sha1Hex(content), content.Length, "small.bin");

            await downloader.DownloadAsync(new[] { task }, maxParallel: 8);

            Assert.Equal(content, await File.ReadAllBytesAsync(destination));
            Assert.Equal(0, rangeCount());
        }
        finally
        {
            server.Stop();
        }
    }
}
