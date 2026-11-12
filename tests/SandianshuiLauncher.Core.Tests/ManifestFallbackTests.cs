using System.Net;
using System.Security.Authentication;
using System.Text;
using SandianshuiLauncher.Core.Models;
using SandianshuiLauncher.Core.Services;
using Xunit;

namespace SandianshuiLauncher.Core.Tests;

/// <summary>
/// 版本清单的回退链测试。
/// 背景：内置「MC 光速镜像」的证书在国内网络下偶尔校验失败，
/// 若只请求单一地址，下载页「刷新版本列表」会直接失败并弹英文 SSL 异常。
/// </summary>
public class ManifestFallbackTests
{
    private const string ManifestJson = """
        {"latest":{"release":"1.21.1","snapshot":"24w14a"},"versions":[{"id":"1.21.1","type":"release","url":"https://piston-meta.mojang.com/v1/packages/x/1.21.1.json","time":"2024-08-08T14:29:32+00:00","releaseTime":"2024-08-08T14:29:32+00:00"}]}
        """;

    [Fact]
    public async Task FetchManifest_FallsBackToBmcl_WhenMirrorCertificateFails()
    {
        var handler = new FallbackHandler(
            failingHost: "bsmirror.hkmc.online",
            succeedHost: "bmclapi2.bangbang93.com");
        var service = CreateService(new LauncherConfig { DownloadSourceId = "guangsu" }, handler);

        VersionManifest manifest = await service.FetchManifestAsync();

        Assert.Equal("1.21.1", Assert.Single(manifest.Versions).Id);
        Assert.Equal(
            [
                "https://bsmirror.hkmc.online/mc/game/version_manifest_v2.json",
                "https://bmclapi2.bangbang93.com/mc/game/version_manifest_v2.json"
            ],
            handler.RequestedUrls);
    }

    [Fact]
    public async Task FetchManifest_AllCandidatesFail_ReportsEverySourceInChinese()
    {
        var handler = new FallbackHandler(failingHost: null, succeedHost: null);
        var service = CreateService(new LauncherConfig { DownloadSourceId = "guangsu" }, handler);

        var error = await Assert.ThrowsAsync<HttpRequestException>(() => service.FetchManifestAsync());

        Assert.Contains("版本清单获取失败", error.Message, StringComparison.Ordinal);
        Assert.Contains("已尝试 3 个下载源", error.Message, StringComparison.Ordinal);
        Assert.Contains("SSL/TLS 证书校验失败", error.Message, StringComparison.Ordinal);
        Assert.Contains("bsmirror.hkmc.online", error.Message, StringComparison.Ordinal);
        Assert.Contains("bmclapi2.bangbang93.com", error.Message, StringComparison.Ordinal);
        Assert.Equal(3, handler.RequestedUrls.Count);
    }

    [Fact]
    public async Task FetchManifest_FirstCandidateSucceeds_DoesNotHitFallbacks()
    {
        var handler = new FallbackHandler(failingHost: null, succeedHost: "bsmirror.hkmc.online");
        var service = CreateService(new LauncherConfig { DownloadSourceId = "guangsu" }, handler);

        await service.FetchManifestAsync();

        Assert.Single(handler.RequestedUrls);
    }

    [Fact]
    public void DescribeError_TranslatesSslFailure()
    {
        var ssl = new HttpRequestException("The SSL connection could not be established.", new AuthenticationException("cert"));

        Assert.Equal("SSL/TLS 证书校验失败，请换用其它下载源", VersionService.DescribeError(ssl));
    }

    [Fact]
    public void DescribeError_KeepsOtherMessages()
    {
        Assert.Equal("404 (Not Found)", VersionService.DescribeError(new HttpRequestException("404 (Not Found)")));
    }

    private static VersionService CreateService(LauncherConfig config, HttpMessageHandler handler)
        => new(new HttpClient(handler), new DownloadSourceProvider(config));

    /// <summary>
    /// 指定失败域名：该域名的请求抛出 SSL 异常，其余域名返回版本清单。
    /// <paramref name="failingHost"/> 与 <paramref name="succeedHost"/> 同时为 null 时全部失败。
    /// </summary>
    private sealed class FallbackHandler(string? failingHost, string? succeedHost) : HttpMessageHandler
    {
        public List<string> RequestedUrls { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            string url = request.RequestUri!.ToString();
            RequestedUrls.Add(url);

            string host = request.RequestUri.Host;
            if (failingHost is not null && string.Equals(host, failingHost, StringComparison.OrdinalIgnoreCase))
            {
                throw new HttpRequestException(
                    "The SSL connection could not be established, see inner exception.",
                    new AuthenticationException("证书链校验失败"));
            }

            if (succeedHost is not null && string.Equals(host, succeedHost, StringComparison.OrdinalIgnoreCase))
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(ManifestJson, Encoding.UTF8, "application/json")
                });
            }

            throw new HttpRequestException(
                "The SSL connection could not be established, see inner exception.",
                new AuthenticationException("证书链校验失败"));
        }
    }
}
