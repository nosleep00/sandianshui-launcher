using System.Net;
using System.Text;
using System.Text.Json;
using SandianshuiLauncher.Core.Models;
using SandianshuiLauncher.Core.Services;
using Xunit;

namespace SandianshuiLauncher.Core.Tests;

/// <summary>
/// 模组安装标识解析测试（回归 MCIM 镜像不支持 slug 导致的 404）：
/// 1) 必须优先用 project_id 请求版本接口，slug 仅作兜底；
/// 2) 首个标识 404 时自动换第二个标识重试；
/// 3) 全部失败时抛出中文可操作错误，而非英文原始异常。
/// </summary>
public class ModServiceIdentifierTests : IDisposable
{
    private const string ProjectId = "8shC1gFX";
    private const string Slug = "betterf3";

    private readonly string _root;

    public ModServiceIdentifierTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "sdsl_modid_" + Guid.NewGuid().ToString("N"));
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

    /// <summary>记录请求并按 URL 返回 404 或版本 JSON 的假 handler。</summary>
    private sealed class IdentifierHandler : HttpMessageHandler
    {
        /// <summary>被视作可用的标识；null 表示全部 404。</summary>
        public string? WorkingIdentifier { get; set; }

        public List<string> RequestedUrls { get; } = [];

        /// <summary>是否所有请求（含文件下载）都返回 404。</summary>
        public bool FailDownloads { get; set; } = true;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            string url = request.RequestUri!.ToString();
            RequestedUrls.Add(url);

            if (url.Contains("/v2/project/", StringComparison.OrdinalIgnoreCase))
            {
                bool matches = WorkingIdentifier is not null
                               && url.Contains($"/v2/project/{WorkingIdentifier}/", StringComparison.OrdinalIgnoreCase);
                return Task.FromResult(matches
                    ? new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(VersionJson, Encoding.UTF8, "application/json")
                    }
                    : new HttpResponseMessage(HttpStatusCode.NotFound)
                    {
                        Content = new StringContent(
                            """{"error":"Not Found","code":404,"detail":"Modrinth Version not found"}""",
                            Encoding.UTF8,
                            "application/json")
                    });
            }

            // 文件下载请求。
            if (FailDownloads)
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
            }
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent("jar"u8.ToArray())
            });
        }
    }

    private const string VersionJson = $$"""
    [
      {
        "id": "AkivIlyi",
        "project_id": "{{ProjectId}}",
        "name": "BetterF3 Fabric v9.0.2 (1.20.3/1.20.4)",
        "version_number": "9.0.2",
        "game_versions": ["1.20.3", "1.20.4"],
        "loaders": ["fabric"],
        "files": [
          {
            "url": "https://cdn.modrinth.com/data/8shC1gFX/versions/AkivIlyi/BetterF3-9.0.2-Fabric-1.20.4.jar",
            "filename": "BetterF3-9.0.2-Fabric-1.20.4.jar",
            "primary": true,
            "size": 1776259,
            "hashes": { "sha1": "780ae4f29fb249c3f6be75724b3084a075ec36cb" }
          }
        ]
      }
    ]
    """;

    private static ModSearchResult BetterF3() => new()
    {
        ProjectId = ProjectId,
        Slug = Slug,
        Title = "BetterF3",
        Categories = ["fabric"]
    };

    private (ModService Service, IdentifierHandler Handler) CreateService(bool useMirror)
    {
        var handler = new IdentifierHandler();
        var config = new ConfigService(Path.Combine(_root, "data"));
        config.Config.ModUseMirror = useMirror;
        config.Config.ModUserAgent = "SandianshuiLauncher/0.1";
        var http = new HttpClient(handler);
        return (new ModService(http, new DownloadService(http), config), handler);
    }

    // ── 标识优先级 ────────────────────────────────────────────

    [Fact]
    public void BuildProjectReferences_ProjectIdFirst_SlugAsFallback()
    {
        List<string> refs = ModService.BuildProjectReferences(BetterF3());

        Assert.Equal([ProjectId, Slug], refs);
    }

    [Fact]
    public void BuildProjectReferences_WithoutSlug_OnlyProjectId()
    {
        var mod = new ModSearchResult { ProjectId = ProjectId, Slug = string.Empty, Title = "BetterF3" };

        Assert.Equal([ProjectId], ModService.BuildProjectReferences(mod));
    }

    [Fact]
    public void BuildProjectReferences_WithoutProjectId_OnlySlug()
    {
        var mod = new ModSearchResult { ProjectId = string.Empty, Slug = Slug, Title = "BetterF3" };

        Assert.Equal([Slug], ModService.BuildProjectReferences(mod));
    }

    [Fact]
    public void BuildProjectReferences_DeduplicatesCaseInsensitively()
    {
        var mod = new ModSearchResult { ProjectId = "abc", Slug = "ABC", Title = "T" };

        Assert.Single(ModService.BuildProjectReferences(mod));
    }

    // ── 请求行为 ──────────────────────────────────────────────

    [Fact]
    public async Task GetVersions_FirstReferenceSucceeds_NoFallbackRequest()
    {
        (ModService service, IdentifierHandler handler) = CreateService(useMirror: true);
        handler.WorkingIdentifier = ProjectId;

        List<ModVersionInfo> versions = await service.GetVersionsAsync(
            ProjectId, "1.20.4", "fabric");

        Assert.Single(versions);
        Assert.Single(handler.RequestedUrls);
        Assert.Contains($"/v2/project/{ProjectId}/version", handler.RequestedUrls[0]);
    }

    [Fact]
    public async Task GetVersions_FirstReference404_FallsBackToSlug()
    {
        (ModService service, IdentifierHandler handler) = CreateService(useMirror: false);
        handler.WorkingIdentifier = Slug; // 仅 slug 可用（模拟宽松/异常的服务端）

        List<ModVersionInfo> versions = await service.GetVersionsAsync(
            ProjectId, "1.20.4", "fabric", slug: Slug);

        Assert.Single(versions);
        Assert.Equal(2, handler.RequestedUrls.Count);
        Assert.Contains($"/v2/project/{ProjectId}/version", handler.RequestedUrls[0]);
        Assert.Contains($"/v2/project/{Slug}/version", handler.RequestedUrls[1]);
    }

    [Fact]
    public async Task GetVersions_AllReferences404_ThrowsChineseActionableError()
    {
        (ModService service, IdentifierHandler handler) = CreateService(useMirror: true);
        handler.WorkingIdentifier = null;

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.GetVersionsAsync(ProjectId, "1.20.4", "fabric", slug: Slug));

        // 必须是中文可操作提示，不得泄漏英文原始异常。
        Assert.Contains("获取", ex.Message);
        Assert.Contains("镜像", ex.Message);
        Assert.DoesNotContain("Response status code", ex.Message);
        Assert.Equal(2, handler.RequestedUrls.Count);
    }

    [Fact]
    public async Task GetVersions_Timeout_DoesNotFallbackToSecondReference()
    {
        // 非 404 的失败（如超时/服务器错误）不触发标识回退，避免重复无谓请求。
        var handler = new ThrowingHandler();
        var config = new ConfigService(Path.Combine(_root, "data"));
        var http = new HttpClient(handler);
        var service = new ModService(http, new DownloadService(http), config);

        await Assert.ThrowsAnyAsync<Exception>(() =>
            service.GetVersionsAsync(ProjectId, "1.20.4", "fabric", slug: Slug));

        Assert.Equal(1, handler.RequestCount);
    }

    private sealed class ThrowingHandler : HttpMessageHandler
    {
        public int RequestCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestCount++;
            throw new TaskCanceledException("模拟请求超时");
        }
    }

    // ── 端到端安装 ────────────────────────────────────────────

    [Fact]
    public async Task Install_UsesProjectId_ReachesDownloadStage()
    {
        (ModService service, IdentifierHandler handler) = CreateService(useMirror: true);
        handler.WorkingIdentifier = ProjectId;

        // 版本查询成功即可视为修复生效；下载阶段用真实 CDN 无法离线完成，
        // 因此断言“确实走到了文件下载请求”而非“安装成功”。
        Exception ex = await Record.ExceptionAsync(() => service.InstallAsync(
            BetterF3(), "1.20.4", "fabric", _root, "1.20.4", versionIsolation: false))!;

        Assert.NotNull(ex);
        Assert.Contains(handler.RequestedUrls, u => u.Contains(".jar", StringComparison.OrdinalIgnoreCase));
        // 关键：没有走 slug 的 404 路径。
        Assert.DoesNotContain(handler.RequestedUrls,
            u => u.Contains($"/v2/project/{Slug}/", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Install_DownloadFails_ReportsChineseErrorNotRawHttp()
    {
        (ModService service, IdentifierHandler handler) = CreateService(useMirror: true);
        handler.WorkingIdentifier = ProjectId;
        handler.FailDownloads = true;

        Exception ex = await Assert.ThrowsAnyAsync<Exception>(() => service.InstallAsync(
            BetterF3(), "1.20.4", "fabric", _root, "1.20.4", versionIsolation: false));

        Assert.DoesNotContain("Response status code does not indicate success", ex.Message);
        Assert.Contains("下载失败", ex.Message);
    }

    [Fact]
    public void ErrorMessage_ForHttp404_IsChineseAndActionable()
    {
        var response = new HttpResponseMessage(HttpStatusCode.NotFound);
        response.RequestMessage = new HttpRequestMessage(
            HttpMethod.Get, "https://mod.mcimirror.top/modrinth/v2/project/betterf3/version");

        string message = ModService.DescribeHttpFailure(response, isVersionQuery: true);

        Assert.Contains("404", message);
        Assert.Contains("镜像", message);
        Assert.DoesNotContain("Response status code", message);
    }

    [Fact]
    public void ErrorMessage_ForServerError_MentionsStatusAndRetry()
    {
        var response = new HttpResponseMessage(HttpStatusCode.InternalServerError);
        response.RequestMessage = new HttpRequestMessage(
            HttpMethod.Get, "https://mod.mcimirror.top/modrinth/v2/search?query=x");

        string message = ModService.DescribeHttpFailure(response, isVersionQuery: false);

        Assert.Contains("500", message);
        Assert.Contains("重试", message);
    }
}
