using System.Net;
using System.Text;
using SandianshuiLauncher.Core.Models;
using SandianshuiLauncher.Core.Services;
using Xunit;

namespace SandianshuiLauncher.Core.Tests;

/// <summary>
/// Forge 版本列表多源回退测试：
/// BMCLAPI 失败时回退到 Forge 官方 promotions_slim.json（经下载源重写）。
/// </summary>
public class ForgeVersionFallbackTests : IDisposable
{
    private readonly string _root;

    public ForgeVersionFallbackTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "sdsl_forge_" + Guid.NewGuid().ToString("N"));
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

    private sealed class StubHandler : HttpMessageHandler
    {
        public Func<HttpRequestMessage, HttpResponseMessage> Responder { get; set; }
            = _ => new HttpResponseMessage(HttpStatusCode.NotFound);

        public List<string> RequestedUrls { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestedUrls.Add(request.RequestUri!.ToString());
            return Task.FromResult(Responder(request));
        }
    }

    private static HttpResponseMessage Json(string content, HttpStatusCode status = HttpStatusCode.OK)
        => new(status) { Content = new StringContent(content, Encoding.UTF8, "application/json") };

    private ModLoaderService CreateService(StubHandler handler)
    {
        var http = new HttpClient(handler);
        var config = new ConfigService(Path.Combine(_root, "data"));
        var provider = new DownloadSourceProvider(config.Config);
        var downloads = new DownloadService(http);
        var installer = new GameInstaller(http, provider, downloads);
        return new ModLoaderService(http, provider, downloads, installer);
    }

    [Fact]
    public async Task GetForgeVersions_BmclFails_FallsBackToOfficialPromotions()
    {
        const string promotions = """
        {
          "promos": {
            "1.20.4-latest": "49.2.13",
            "1.20.4-recommended": "49.2.13",
            "1.19.2-latest": "43.4.0"
          }
        }
        """;
        var handler = new StubHandler
        {
            Responder = request =>
                request.RequestUri!.AbsolutePath.Contains("promotions_slim.json", StringComparison.OrdinalIgnoreCase)
                    ? Json(promotions)
                    : new HttpResponseMessage(HttpStatusCode.InternalServerError)
        };
        var service = CreateService(handler);

        List<ForgeVersionEntry> versions = await service.GetForgeVersionsAsync("1.20.4");

        // latest 与 recommended 相同版本去重后仅一条。
        ForgeVersionEntry entry = Assert.Single(versions);
        Assert.Equal("49.2.13", entry.Version);
        Assert.Equal("1.20.4", entry.McVersion);
        Assert.Equal(2, handler.RequestedUrls.Count);
    }

    [Fact]
    public async Task GetForgeVersions_BmclSucceeds_NoFallbackRequest()
    {
        const string bmclList = """[ { "version": "49.2.13", "build": 23, "mcversion": "1.20.4" } ]""";
        var handler = new StubHandler { Responder = _ => Json(bmclList) };
        var service = CreateService(handler);

        List<ForgeVersionEntry> versions = await service.GetForgeVersionsAsync("1.20.4");

        Assert.Single(versions);
        Assert.Equal("49.2.13", versions[0].Version);
        Assert.Single(handler.RequestedUrls);
    }

    [Fact]
    public async Task GetForgeVersions_PromotionsMissingVersion_Throws()
    {
        const string promotions = """{ "promos": { "1.19.2-latest": "43.4.0" } }""";
        var handler = new StubHandler
        {
            Responder = request =>
                request.RequestUri!.AbsolutePath.Contains("promotions_slim.json", StringComparison.OrdinalIgnoreCase)
                    ? Json(promotions)
                    : new HttpResponseMessage(HttpStatusCode.InternalServerError)
        };
        var service = CreateService(handler);

        var ex = await Assert.ThrowsAsync<InvalidDataException>(
            () => service.GetForgeVersionsAsync("1.20.4"));

        Assert.Contains("1.20.4", ex.Message);
    }
}
