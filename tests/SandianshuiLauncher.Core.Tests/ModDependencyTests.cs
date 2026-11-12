using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using SandianshuiLauncher.Core.Models;
using SandianshuiLauncher.Core.Services;
using Xunit;

namespace SandianshuiLauncher.Core.Tests;

/// <summary>
/// 模组依赖解析测试：
/// 验证必需依赖的传递闭包、防环、可选依赖跳过、已安装跳过、
/// 依赖解析失败不中断主流程，以及安装计划的执行（下载 + 跳过已存在）。
/// </summary>
public class ModDependencyTests : IDisposable
{
    private readonly string _root;

    public ModDependencyTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "sdsl_moddep_" + Guid.NewGuid().ToString("N"));
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

    private static HttpResponseMessage Json(string content)
        => new(HttpStatusCode.OK)
        {
            Content = new System.Net.Http.StringContent(content, System.Text.Encoding.UTF8, "application/json")
        };

    private static HttpResponseMessage Bytes(byte[] content)
        => new(HttpStatusCode.OK) { Content = new ByteArrayContent(content) };

    /// <summary>构造一个版本 JSON（含一个文件与可选必需依赖）。</summary>
    private static string VersionJson(
        string projectId, string fileName, string sha1, long size, string? depProject = null, string depType = "required")
    {
        var deps = new List<object>();
        if (depProject is not null)
        {
            deps.Add(new { project_id = depProject, dependency_type = depType });
        }
        var payload = new object[]
        {
            new
            {
                id = "v-" + projectId,
                project_id = projectId,
                name = "Mod " + projectId,
                version_number = "1.0.0",
                game_versions = new[] { "1.20.4" },
                loaders = new[] { "fabric" },
                files = new object[]
                {
                    new
                    {
                        url = $"https://cdn.modrinth.com/data/{projectId}/versions/v-{projectId}/{fileName}",
                        filename = fileName,
                        primary = true,
                        size,
                        hashes = new { sha1 }
                    }
                },
                dependencies = deps.ToArray()
            }
        };
        return JsonSerializer.Serialize(payload);
    }

    private sealed class TestEnvironment
    {
        public required ModService Service { get; init; }
        public required RoutingHandler Handler { get; init; }
        public required string TargetDir { get; init; }
        public required Dictionary<string, (string FileName, byte[] Content)> Projects { get; init; }

        /// <summary>注册项目：版本 JSON + 对应 jar 下载。</summary>
        public void Register(string projectId, string? dependsOn = null, string depType = "required")
        {
            byte[] content = System.Text.Encoding.UTF8.GetBytes($"jar-of-{projectId}");
            string fileName = $"mod-{projectId}.jar";
            Projects[projectId] = (fileName, content);
            string versionJson = VersionJson(projectId, fileName, Sha1Hex(content), content.Length, dependsOn, depType);
            Handler.Responder = Chain(Handler.Responder, request =>
            {
                string url = request.RequestUri!.ToString();
                if (url.Contains($"/v2/project/{projectId}/version", StringComparison.OrdinalIgnoreCase))
                {
                    return Json(versionJson);
                }
                if (url.EndsWith("/" + fileName, StringComparison.OrdinalIgnoreCase))
                {
                    return Bytes(content);
                }
                return null!;
            });
        }

        private static Func<HttpRequestMessage, HttpResponseMessage> Chain(
            Func<HttpRequestMessage, HttpResponseMessage> existing,
            Func<HttpRequestMessage, HttpResponseMessage?> next)
            => request => next(request) ?? existing(request);
    }

    private TestEnvironment CreateEnvironment()
    {
        var handler = new RoutingHandler();
        var config = new ConfigService(Path.Combine(_root, "data-" + Guid.NewGuid().ToString("N")));
        config.Config.ModUseMirror = true;
        var http = new HttpClient(handler);
        var service = new ModService(http, new DownloadService(http), config);
        return new TestEnvironment
        {
            Service = service,
            Handler = handler,
            TargetDir = Path.Combine(_root, "mods"),
            Projects = new Dictionary<string, (string, byte[])>(StringComparer.OrdinalIgnoreCase)
        };
    }

    private static ModSearchResult Mod(string projectId)
        => new() { ProjectId = projectId, Slug = "slug-" + projectId.ToLowerInvariant(), Title = "Mod " + projectId };

    // ── 计划解析 ──────────────────────────────────────────────

    [Fact]
    public async Task BuildPlan_TransitiveClosure_ContainsAllRequiredDeps()
    {
        TestEnvironment env = CreateEnvironment();
        env.Register("A", dependsOn: "B");
        env.Register("B", dependsOn: "C");
        env.Register("C");

        ModInstallPlan plan = await env.Service.BuildInstallPlanAsync(
            Mod("A"), "1.20.4", "fabric", env.TargetDir);

        Assert.Equal(["B", "C"], plan.Dependencies.Select(d => d.ProjectId));
        Assert.Empty(plan.SkippedNotes);
    }

    [Fact]
    public async Task BuildPlan_Cycle_TerminatesWithoutDuplicates()
    {
        TestEnvironment env = CreateEnvironment();
        env.Register("A", dependsOn: "B");
        env.Register("B", dependsOn: "A"); // 环 A→B→A

        ModInstallPlan plan = await env.Service.BuildInstallPlanAsync(
            Mod("A"), "1.20.4", "fabric", env.TargetDir);

        ModInstallItem b = Assert.Single(plan.Dependencies);
        Assert.Equal("B", b.ProjectId);
    }

    [Fact]
    public async Task BuildPlan_OptionalDependency_Skipped()
    {
        TestEnvironment env = CreateEnvironment();
        env.Register("A", dependsOn: "B", depType: "optional");
        env.Register("B");

        ModInstallPlan plan = await env.Service.BuildInstallPlanAsync(
            Mod("A"), "1.20.4", "fabric", env.TargetDir);

        Assert.Empty(plan.Dependencies);
    }

    [Fact]
    public async Task BuildPlan_AlreadyInstalledDep_SkippedWithNote()
    {
        TestEnvironment env = CreateEnvironment();
        env.Register("A", dependsOn: "B");
        env.Register("B");

        // 预先放置 B 的 jar（SHA1 校验通过视为已安装）。
        (string fileName, byte[] content) = env.Projects["B"];
        Directory.CreateDirectory(env.TargetDir);
        await File.WriteAllBytesAsync(Path.Combine(env.TargetDir, fileName), content);

        ModInstallPlan plan = await env.Service.BuildInstallPlanAsync(
            Mod("A"), "1.20.4", "fabric", env.TargetDir);

        Assert.Empty(plan.Dependencies);
        Assert.Contains(plan.SkippedNotes, n => n.Contains("已安装"));
    }

    [Fact]
    public async Task BuildPlan_DepResolutionFailure_RecordedInNotesAndContinues()
    {
        TestEnvironment env = CreateEnvironment();
        env.Register("A", dependsOn: "MISSING"); // 依赖项目不存在（镜像 404）

        ModInstallPlan plan = await env.Service.BuildInstallPlanAsync(
            Mod("A"), "1.20.4", "fabric", env.TargetDir);

        Assert.Empty(plan.Dependencies);
        Assert.Equal("mod-A.jar", plan.PrimaryFile.FileName);
        Assert.Contains(plan.SkippedNotes, n => n.Contains("MISSING"));
    }

    [Fact]
    public async Task GetVersions_MapsDependencies()
    {
        TestEnvironment env = CreateEnvironment();
        env.Register("A", dependsOn: "B");

        List<ModVersionInfo> versions = await env.Service.GetVersionsAsync("A", "1.20.4", "fabric");

        ModDependencyInfo dep = Assert.Single(versions.Single().Dependencies);
        Assert.Equal("B", dep.ProjectId);
        Assert.True(dep.IsRequired);
    }

    // ── 计划执行 ──────────────────────────────────────────────

    [Fact]
    public async Task ExecutePlan_DownloadsPrimaryAndDeps()
    {
        TestEnvironment env = CreateEnvironment();
        env.Register("A", dependsOn: "B");
        env.Register("B");

        ModInstallPlan plan = await env.Service.BuildInstallPlanAsync(
            Mod("A"), "1.20.4", "fabric", env.TargetDir);
        InstalledMod installed = await env.Service.ExecuteInstallPlanAsync(plan, env.TargetDir);

        Assert.Equal("mod-A.jar", installed.FileName);
        Assert.True(File.Exists(Path.Combine(env.TargetDir, "mod-A.jar")));
        Assert.True(File.Exists(Path.Combine(env.TargetDir, "mod-B.jar")));
    }

    [Fact]
    public async Task ExecutePlan_SkipsAlreadyPresentFiles()
    {
        TestEnvironment env = CreateEnvironment();
        env.Register("A", dependsOn: "B");
        env.Register("B");

        ModInstallPlan plan = await env.Service.BuildInstallPlanAsync(
            Mod("A"), "1.20.4", "fabric", env.TargetDir);
        await env.Service.ExecuteInstallPlanAsync(plan, env.TargetDir);

        int requestsBefore = env.Handler.RequestedUrls.Count;
        // 二次执行：文件已存在，不应再发起任何下载请求。
        await env.Service.ExecuteInstallPlanAsync(plan, env.TargetDir);
        Assert.Equal(requestsBefore, env.Handler.RequestedUrls.Count);
    }
}
