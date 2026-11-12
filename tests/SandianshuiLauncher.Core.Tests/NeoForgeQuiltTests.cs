using System.Net;
using System.Text;
using SandianshuiLauncher.Core.Models;
using SandianshuiLauncher.Core.Services;
using Xunit;

namespace SandianshuiLauncher.Core.Tests;

/// <summary>
/// NeoForge 与 Quilt 安装链路测试：
/// 版本列表解析过滤、元数据源重写、Quilt profile 安装的 URL 与落盘行为。
/// </summary>
public class NeoForgeQuiltTests : IDisposable
{
    private readonly string _root;

    public NeoForgeQuiltTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "sdsl_loader_" + Guid.NewGuid().ToString("N"));
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

    private static HttpResponseMessage Text(string content)
        => new(HttpStatusCode.OK) { Content = new StringContent(content, Encoding.UTF8) };

    private (ModLoaderService Service, StubHandler Handler) CreateService(StubHandler handler)
    {
        var http = new HttpClient(handler);
        var config = new ConfigService(Path.Combine(_root, "data"));
        var provider = new DownloadSourceProvider(config.Config);
        var downloads = new DownloadService(http);
        var installer = new GameInstaller(http, provider, downloads);
        return (new ModLoaderService(http, provider, downloads, installer), handler);
    }

    [Fact]
    public async Task GetNeoForgeVersions_FiltersByGameVersion_SortedDescending()
    {
        const string metadata = """
        <metadata>
          <groupId>net.neoforged</groupId>
          <artifactId>neoforge</artifactId>
          <versioning>
            <latest>21.1.72</latest>
            <release>21.1.72</release>
            <versions>
              <version>20.4.237</version>
              <version>21.1.72</version>
              <version>20.4.250</version>
              <version>20.2.59</version>
            </versions>
          </versioning>
        </metadata>
        """;
        var handler = new StubHandler { Responder = _ => Text(metadata) };
        (ModLoaderService service, _) = CreateService(handler);

        List<ForgeVersionEntry> versions = await service.GetNeoForgeVersionsAsync("1.20.4");

        Assert.Equal(2, versions.Count);
        Assert.Equal("20.4.250", versions[0].Version);
        Assert.Equal("20.4.237", versions[1].Version);
    }

    [Fact]
    public async Task GetQuiltLoaders_ParsesLoaderList()
    {
        const string quiltList = """[ { "loader": { "version": "0.26.3", "maven": "org.quiltmc:quilt-loader:0.26.3" } }, { "loader": { "version": "0.26.2" } } ]""";
        var handler = new StubHandler { Responder = _ => Text(quiltList) };
        (ModLoaderService service, StubHandler stub) = CreateService(handler);

        List<FabricLoaderListEntry> loaders = await service.GetQuiltLoadersAsync("1.20.4");

        Assert.Equal(2, loaders.Count);
        Assert.Equal("0.26.3", loaders[0].Loader!.Version);
        Assert.Contains(stub.RequestedUrls, u => u.Contains("/quilt-meta/v3/versions/loader/1.20.4")
                                                || u.Contains("meta.quiltmc.org/v3/versions/loader/1.20.4"));
    }

    [Fact]
    public void DownloadSource_RewritesNeoForgeMaven_AndRoundTrips()
    {
        var source = new BmclCompatibleSource("https://bsmirror.hkmc.online");
        const string official = "https://maven.neoforged.net/releases/net/neoforged/neoforge/21.1.72/neoforge-21.1.72-installer.jar";
        const string mirrored = "https://bsmirror.hkmc.online/maven/net/neoforged/neoforge/21.1.72/neoforge-21.1.72-installer.jar";

        Assert.Equal(mirrored, source.Rewrite(official));
        Assert.Equal(official, source.ToOfficialUrl(mirrored));
    }

    [Fact]
    public void DownloadSource_RewritesQuiltMeta_AndRoundTrips()
    {
        var source = new BmclCompatibleSource("https://bsmirror.hkmc.online");
        const string official = "https://meta.quiltmc.org/v3/versions/loader/1.20.4";
        const string mirrored = "https://bsmirror.hkmc.online/quilt-meta/v3/versions/loader/1.20.4";

        Assert.Equal(mirrored, source.Rewrite(official));
        Assert.Equal(official, source.ToOfficialUrl(mirrored));
    }

    [Fact]
    public async Task InstallQuilt_UsesV3ProfileEndpoint_AndWritesVersionJson()
    {
        const string profileJson = """
        {
          "id": "quilt-loader-0.26.3-1.20.4",
          "inheritsFrom": "1.20.4",
          "libraries": []
        }
        """;
        var handler = new StubHandler
        {
            Responder = request =>
                request.RequestUri!.AbsolutePath.EndsWith("profile/json", StringComparison.OrdinalIgnoreCase)
                    ? Text(profileJson)
                    : new HttpResponseMessage(HttpStatusCode.NotFound)
        };
        (ModLoaderService service, StubHandler stub) = CreateService(handler);

        // 预置父版本 JSON（安装时会递归读取继承链）。
        string parentDir = Path.Combine(_root, "versions", "1.20.4");
        Directory.CreateDirectory(parentDir);
        await File.WriteAllTextAsync(
            Path.Combine(parentDir, "1.20.4.json"),
            """{ "id": "1.20.4", "mainClass": "net.minecraft.client.main.Main", "libraries": [] }""");

        await service.InstallQuiltAsync("1.20.4", "0.26.3", _root, threads: 4);

        Assert.Contains(stub.RequestedUrls, u => u.Contains("meta.quiltmc.org/v3/versions/loader/1.20.4/0.26.3/profile/json")
                                                || u.Contains("/quilt-meta/v3/versions/loader/1.20.4/0.26.3/profile/json"));
        string writtenPath = Path.Combine(_root, "versions", "quilt-loader-0.26.3-1.20.4", "quilt-loader-0.26.3-1.20.4.json");
        Assert.True(File.Exists(writtenPath));
        Assert.Contains("inheritsFrom", await File.ReadAllTextAsync(writtenPath));
    }
}
