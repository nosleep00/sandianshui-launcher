using System.Net;

namespace SandianshuiLauncher.Core.Services;

/// <summary>统一的 HttpClient 创建（高并发、长连接、自动解压）。</summary>
public static class HttpFactory
{
    public static HttpClient Create(int maxConnectionsPerServer = 128)
    {
        var handler = new SocketsHttpHandler
        {
            MaxConnectionsPerServer = maxConnectionsPerServer,
            AutomaticDecompression = DecompressionMethods.All,
            PooledConnectionLifetime = TimeSpan.FromMinutes(10),
            ConnectTimeout = TimeSpan.FromSeconds(15)
        };
        return new HttpClient(handler)
        {
            // 大文件下载由 CancellationToken 控制超时，避免全局 Timeout 中断。
            Timeout = Timeout.InfiniteTimeSpan
        };
    }
}
