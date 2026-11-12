using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using SandianshuiLauncher.Core.Models;
using SandianshuiLauncher.Core.Services;
using Xunit;

namespace SandianshuiLauncher.Core.Tests;

/// <summary>
/// <see cref="CurseForgeService"/> 测试：
/// 无 Key 保护、搜索/文件/下载地址解析、最佳文件选择、
/// 必需依赖传递闭包与已安装跳过。
/// </summary>
public class CurseForgeServiceTests : IDisposable
{
    private readonly string _root;

    public CurseForgeServiceTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "sdsl_cf_" + Guid.NewGuid().ToString("N"));
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

    private static string Sha1Hex(byte[] data)
        => Convert.ToHexString(SHA1.HashData(data)).ToLowerInvariant();

    private sealed class RoutingHandler : HttpMessageHandler
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

    private static HttpResponseMessage JsonResponse(string content)
        => new(HttpStatusCode.OK)
        {
            Content = new StringContent(content, System.Text.Encoding.UTF8, "application/json")
        };

    private CurseForgeService CreateService(RoutingHandler handler, bool withKey = true)
    {
        var config = new ConfigService(Path.Combine(_root, "data-" + Guid.NewGuid().ToString("N")));
        config.Config.CurseForgeApiKey = withKey ? "test-key-123" : null;
        var http = new HttpClient(handler);
        return new CurseForgeService(http, new DownloadService(http), config);
    }

    private static string SearchJson(long id, string name, long downloads = 1000)
        => JsonSerializer.Serialize(new
        {
            data = new object[]
            {
                new
                {
                    id,
                    name,
                    slug = name.ToLowerInvariant(),
                    summary = "summary of " + name,
                    downloadCount = downloads,
                    logo = new { url = $"https://media.forgecdn.net/avatars/{id}/logo.png", thumbnailUrl = $"https://media.forgecdn.net/avatars/{id}/thumb.png" },
                    links = new { websiteUrl = "https://example.com" }
                }
            }
        });

    private static string FilesJson(
        long fileId, string fileName, long length, byte[] content, int releaseType = 1,
        params (long ModId, int RelationType)[] deps)
        => JsonSerializer.Serialize(new
        {
            data = new object[]
            {
                new
                {
                    id = fileId,
                    fileName,
                    fileLength = length,
                    downloadUrl = (string?)null,
                    releaseType,
                    gameVersions = new[] { "1.20.4" },
                    hashes = new object[] { new { value = Sha1Hex(content), algo = 1 } },
                    dependencies = deps.Select(d => new { modId = d.ModId, relationType = d.RelationType }).ToArray()
                }
            }
        });

    [Fact]
    public async Task Search_NoApiKey_ThrowsChineseError()
    {
        var handler = new RoutingHandler();
        var service = CreateService(handler, withKey: false);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.SearchAsync("minimap", "1.20.4", "fabric"));

        Assert.Contains("API Key", ex.Message);
        Assert.Empty(handler.RequestedUrls);
    }

    [Fact]
    public async Task Search_WithKey_ParsesResultsAndFilters()
    {
        var handler = new RoutingHandler { Responder = _ => JsonResponse(SearchJson(12345, "Xaero's Minimap", 99999)) };
        var service = CreateService(handler);

        List<CurseForgeMod> mods = await service.SearchAsync("minimap", "1.20.4", "fabric");

        CurseForgeMod mod = Assert.Single(mods);
        Assert.Equal(12345, mod.Id);
        Assert.Equal("Xaero's Minimap", mod.Name);
        Assert.Equal(99999, mod.DownloadCount);
        Assert.NotNull(mod.IconUrl);
        string url = handler.RequestedUrls.Single();
        Assert.Contains("gameVersion=1.20.4", url);
        Assert.Contains("modLoaderType=4", url); // Fabric
    }

    [Fact]
    public async Task Search_WithoutVersion_OmitsLoaderFilter()
    {
        var handler = new RoutingHandler { Responder = _ => JsonResponse(SearchJson(1, "X")) };
        var service = CreateService(handler);

        await service.SearchAsync("x", gameVersion: null, loader: "fabric");

        string url = handler.RequestedUrls.Single();
        Assert.DoesNotContain("gameVersion=", url);
        Assert.DoesNotContain("modLoaderType=", url);
    }

    [Fact]
    public async Task GetFiles_ParsesSha1AndRequiredDependencies()
    {
        byte[] content = "jar"u8.ToArray();
        var handler = new RoutingHandler
        {
            Responder = _ => JsonResponse(FilesJson(777, "mod-1.0.0.jar", content.Length, content, deps: [(42, 3), (99, 2)]))
        };
        var service = CreateService(handler);

        List<CurseForgeFileInfo> files = await service.GetFilesAsync(12345, "1.20.4", "forge");

        CurseForgeFileInfo file = Assert.Single(files);
        Assert.Equal("mod-1.0.0.jar", file.FileName);
        Assert.Equal(Sha1Hex(content), file.Sha1);
        Assert.Equal([42L], file.Dependencies.Where(d => d.IsRequired).Select(d => d.ModId));
    }

    [Fact]
    public async Task GetDownloadUrl_ParsesData()
    {
        var handler = new RoutingHandler
        {
            Responder = _ => JsonResponse("""{ "data": "https://edge.forgecdn.net/files/1/2/mod.jar" }""")
        };
        var service = CreateService(handler);

        string url = await service.GetDownloadUrlAsync(1, 2);

        Assert.Contains("edge.forgecdn.net", url);
    }

    [Fact]
    public void SelectBestFile_PrefersReleaseOverBeta()
    {
        var beta = new CurseForgeFileInfo { Id = 200, FileName = "beta.jar", ReleaseType = 2 };
        var release = new CurseForgeFileInfo { Id = 100, FileName = "release.jar", ReleaseType = 1 };

        CurseForgeFileInfo? selected = CurseForgeService.SelectBestFile([beta, release]);

        Assert.Equal("release.jar", selected!.FileName);
    }

    [Fact]
    public async Task BuildInstallPlan_ResolvesRequiredDependencyClosure()
    {
        byte[] a = "jar-A"u8.ToArray();
        byte[] b = "jar-B"u8.ToArray();
        var handler = new RoutingHandler();
        handler.Responder = request =>
        {
            string url = request.RequestUri!.ToString();
            if (url.Contains("/mods/100/files?", StringComparison.OrdinalIgnoreCase))
            {
                return JsonResponse(FilesJson(1, "mod-A.jar", a.Length, a, deps: [(200, 3)]));
            }
            if (url.Contains("/mods/200/files?", StringComparison.OrdinalIgnoreCase))
            {
                return JsonResponse(FilesJson(2, "mod-B.jar", b.Length, b));
            }
            if (url.Contains("download-url", StringComparison.OrdinalIgnoreCase))
            {
                if (url.Contains("/mods/100/files/1/download-url", StringComparison.OrdinalIgnoreCase))
                {
                    return JsonResponse("""{ "data": "http://localhost/dl/mod-A.jar" }""");
                }
                if (url.Contains("/mods/200/files/2/download-url", StringComparison.OrdinalIgnoreCase))
                {
                    return JsonResponse("""{ "data": "http://localhost/dl/mod-B.jar" }""");
                }
            }
            if (url.EndsWith("/dl/mod-A.jar", StringComparison.OrdinalIgnoreCase))
            {
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(a) };
            }
            if (url.EndsWith("/dl/mod-B.jar", StringComparison.OrdinalIgnoreCase))
            {
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(b) };
            }
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        };
        var service = CreateService(handler);
        string targetDir = Path.Combine(_root, "mods");

        ModInstallPlan plan = await service.BuildInstallPlanAsync(100, "1.20.4", "forge", targetDir);
        InstalledMod installed = await ModService.ExecuteInstallPlanAsync(
            plan, targetDir, new DownloadService(new HttpClient(handler)));

        Assert.Equal("mod-A.jar", installed.FileName);
        Assert.True(File.Exists(Path.Combine(targetDir, "mod-A.jar")));
        Assert.True(File.Exists(Path.Combine(targetDir, "mod-B.jar")));
        Assert.Single(plan.Dependencies);
        Assert.Equal("200", plan.Dependencies[0].ProjectId);
    }

    [Fact]
    public async Task BuildInstallPlan_AlreadyInstalledDep_SkippedWithNote()
    {
        byte[] a = "jar-A"u8.ToArray();
        byte[] b = "jar-B"u8.ToArray();
        string targetDir = Path.Combine(_root, "mods");
        Directory.CreateDirectory(targetDir);
        await File.WriteAllBytesAsync(Path.Combine(targetDir, "mod-B.jar"), b);

        var handler = new RoutingHandler();
        handler.Responder = request =>
        {
            string url = request.RequestUri!.ToString();
            if (url.Contains("/mods/100/files?", StringComparison.OrdinalIgnoreCase))
            {
                return JsonResponse(FilesJson(1, "mod-A.jar", a.Length, a, deps: [(200, 3)]));
            }
            if (url.Contains("/mods/200/files?", StringComparison.OrdinalIgnoreCase))
            {
                return JsonResponse(FilesJson(2, "mod-B.jar", b.Length, b));
            }
            if (url.Contains("download-url", StringComparison.OrdinalIgnoreCase))
            {
                return JsonResponse("""{ "data": "http://localhost/dl/mod-A.jar" }""");
            }
            if (url.EndsWith("/dl/mod-A.jar", StringComparison.OrdinalIgnoreCase))
            {
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(a) };
            }
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        };
        var service = CreateService(handler);

        ModInstallPlan plan = await service.BuildInstallPlanAsync(100, "1.20.4", "forge", targetDir);

        Assert.Empty(plan.Dependencies);
        Assert.Contains(plan.SkippedNotes, n => n.Contains("已安装"));
    }

    [Fact]
    public async Task BuildInstallPlan_NoMatchingFile_ThrowsChineseError()
    {
        var handler = new RoutingHandler
        {
            Responder = _ => JsonResponse("""{ "data": [] }""")
        };
        var service = CreateService(handler);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.BuildInstallPlanAsync(100, "1.20.4", "forge", Path.Combine(_root, "mods")));

        Assert.Contains("1.20.4", ex.Message);
    }

    [Fact]
    public void DescribeFailure_403_MentionsApiKey()
    {
        var response = new HttpResponseMessage(HttpStatusCode.Forbidden)
        {
            RequestMessage = new HttpRequestMessage(HttpMethod.Get, "https://api.curseforge.com/v1/mods/search")
        };

        Assert.Contains("API Key", CurseForgeService.DescribeFailure(response));
    }
}
