using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using SandianshuiLauncher.Core.Models;
using SandianshuiLauncher.Core.Services;
using Xunit;

namespace SandianshuiLauncher.Core.Tests;

/// <summary>
/// 安装计划级加载器感知测试：
/// Forge 目标版本必须挑中 forge 构建（主文件与依赖都算），
/// 无匹配构建时回退 primary 并给出警告。
/// </summary>
public class LoaderAwarePlanTests : IDisposable
{
    private readonly string _root;

    public LoaderAwarePlanTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "sdsl_loader_plan_" + Guid.NewGuid().ToString("N"));
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

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(Responder(request));
    }

    private static HttpResponseMessage JsonResponse(string content)
        => new(HttpStatusCode.OK)
        {
            Content = new StringContent(content, System.Text.Encoding.UTF8, "application/json")
        };

    /// <summary>多文件版本 JSON：每个 (名称, 是否 primary) 对应一个文件条目。</summary>
    private static string MultiFileVersionJson(
        string projectId,
        (string Name, bool Primary)[] files,
        string[] loaders,
        string? depProject = null)
    {
        var deps = new List<object>();
        if (depProject is not null)
        {
            deps.Add(new { project_id = depProject, dependency_type = "required" });
        }
        var fileObjs = files.Select(f =>
        {
            byte[] content = System.Text.Encoding.UTF8.GetBytes($"content-of-{f.Name}");
            return new
            {
                url = $"https://cdn.modrinth.com/data/{projectId}/versions/v-{projectId}/{f.Name}",
                filename = f.Name,
                primary = f.Primary,
                size = content.Length,
                hashes = new { sha1 = Sha1Hex(content) }
            };
        }).Cast<object>().ToArray();

        var payload = new object[]
        {
            new
            {
                id = "v-" + projectId,
                project_id = projectId,
                name = "Mod " + projectId,
                version_number = "1.0.0",
                game_versions = new[] { "1.20.4" },
                loaders,
                files = fileObjs,
                dependencies = deps.ToArray()
            }
        };
        return JsonSerializer.Serialize(payload);
    }

    private (ModService Service, RoutingHandler Handler, string TargetDir) Create()
    {
        var handler = new RoutingHandler();
        var config = new ConfigService(Path.Combine(_root, "data-" + Guid.NewGuid().ToString("N")));
        config.Config.ModUseMirror = true;
        var http = new HttpClient(handler);
        return (new ModService(http, new DownloadService(http), config), handler, Path.Combine(_root, "mods"));
    }

    private static void Register(
        RoutingHandler handler, string projectId, (string, bool)[] files, string[] loaders, string? dep = null)
    {
        string json = MultiFileVersionJson(projectId, files, loaders, dep);
        handler.Responder = Chain(handler.Responder, request =>
        {
            string url = request.RequestUri!.ToString();
            if (url.Contains($"/v2/project/{projectId}/version", StringComparison.OrdinalIgnoreCase))
            {
                return JsonResponse(json);
            }
            foreach ((string name, _) in files)
            {
                if (url.EndsWith("/" + name, StringComparison.OrdinalIgnoreCase))
                {
                    byte[] content = System.Text.Encoding.UTF8.GetBytes($"content-of-{name}");
                    return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(content) };
                }
            }
            return null!;
        });
    }

    private static Func<HttpRequestMessage, HttpResponseMessage> Chain(
        Func<HttpRequestMessage, HttpResponseMessage> existing,
        Func<HttpRequestMessage, HttpResponseMessage?> next)
        => request => next(request) ?? existing(request);

    private static ModSearchResult Mod(string projectId)
        => new() { ProjectId = projectId, Slug = "slug-" + projectId.ToLowerInvariant(), Title = "Mod " + projectId };

    [Fact]
    public async Task BuildPlan_ForgeTarget_PicksForgeFilesForMainAndDep()
    {
        (ModService service, RoutingHandler handler, string targetDir) = Create();
        Register(handler, "A",
            [("mod-A-fabric.jar", true), ("mod-A-forge.jar", false), ("mod-A-neoforge.jar", false)],
            ["fabric", "forge", "neoforge"], dep: "B");
        Register(handler, "B",
            [("mod-B-fabric.jar", true), ("mod-B-forge.jar", false)],
            ["fabric", "forge"]);

        ModInstallPlan plan = await service.BuildInstallPlanAsync(Mod("A"), "1.20.4", "forge", targetDir);

        Assert.Equal("mod-A-forge.jar", plan.PrimaryFile.FileName);
        Assert.Equal("mod-B-forge.jar", Assert.Single(plan.Dependencies).File.FileName);
        Assert.Empty(plan.SkippedNotes);
    }

    [Fact]
    public async Task BuildPlan_NoMatchingBuild_FallsBackToPrimaryWithWarning()
    {
        (ModService service, RoutingHandler handler, string targetDir) = Create();
        // 版本声明支持 forge，但只提供 fabric 文件。
        Register(handler, "A",
            [("mod-A-fabric.jar", true)],
            ["fabric", "forge"]);

        ModInstallPlan plan = await service.BuildInstallPlanAsync(Mod("A"), "1.20.4", "forge", targetDir);

        Assert.Equal("mod-A-fabric.jar", plan.PrimaryFile.FileName);
        Assert.Contains(plan.SkippedNotes, n => n.Contains("警告"));
    }
}
