using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using SandianshuiLauncher.Core.Models;
using SandianshuiLauncher.Core.Services;
using SandianshuiLauncher.Services;
using SandianshuiLauncher.ViewModels;

namespace SandianshuiLauncher.App.Tests;

/// <summary>
/// 模组安装的依赖确认交互测试：
/// 用户取消依赖安装时不下任何文件；确认时主文件与依赖一并落盘。
/// </summary>
public class ModsViewModelDependencyTests : IDisposable
{
    private readonly string _root;

    public ModsViewModelDependencyTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "sdsl_vmdep_" + Guid.NewGuid().ToString("N"));
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

    private static string VersionJson(string projectId, string fileName, string sha1, long size, string? dep = null)
    {
        var deps = new List<object>();
        if (dep is not null)
        {
            deps.Add(new { project_id = dep, dependency_type = "required" });
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

    /// <summary>多加载器文件版本 JSON。</summary>
    private static string MultiFileVersionJson(
        string projectId, (string Name, bool Primary, byte[] Content)[] files, string[] loaders, string? dep = null)
    {
        var deps = new List<object>();
        if (dep is not null)
        {
            deps.Add(new { project_id = dep, dependency_type = "required" });
        }
        var fileObjs = files.Select(f => new
        {
            url = $"https://cdn.modrinth.com/data/{projectId}/versions/v-{projectId}/{f.Name}",
            filename = f.Name,
            primary = f.Primary,
            size = f.Content.Length,
            hashes = new { sha1 = Sha1Hex(f.Content) }
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

    private (ModsViewModel ViewModel, string GameDir, RoutingHandler Handler) CreateViewModel(
        bool mirrorOn, FakeDialogService dialogs)
    {
        var handler = new RoutingHandler();
        var config = new ConfigService(Path.Combine(_root, "data-" + Guid.NewGuid().ToString("N")));
        string gameDir = Path.Combine(_root, "mc-" + Guid.NewGuid().ToString("N"));
        config.Config.GameDirectory = gameDir;
        config.Config.ModUseMirror = mirrorOn;
        var http = new HttpClient(handler);
        var downloads = new DownloadService(http);
        var modService = new ModService(http, downloads, config);
        var modpackService = new ModpackService(http, downloads);
        var curseForgeService = new CurseForgeService(http, downloads, config);
        var groupService = new ModGroupService(Path.Combine(_root, "data-" + Guid.NewGuid().ToString("N")));
        var viewModel = new ModsViewModel(config, modService, curseForgeService, groupService,
            new DirectoryPackService(new VersionService(http, new DownloadSourceProvider(config.Config))),
            TestServices.CreatePackImportService(http, config, downloads, dialogs), dialogs);
        viewModel.SelectedVersion = new LocalVersion { Id = "1.20.4" };
        viewModel.SelectedLoader = "fabric";
        viewModel.SelectedResourceType = "mod";
        return (viewModel, gameDir, handler);
    }

    private static ModSearchResult Mod(string projectId)
        => new() { ProjectId = projectId, Slug = "slug-" + projectId.ToLowerInvariant(), Title = "Mod " + projectId };

    [Fact]
    public async Task Install_DependencyConfirmCancelled_NoFilesWritten()
    {
        var dialogs = new FakeDialogService();
        // 第一次“确定安装”通过，第二次“依赖确认”取消。
        dialogs.ConfirmResults.Enqueue(true);
        dialogs.ConfirmResults.Enqueue(false);
        (ModsViewModel viewModel, string gameDir, RoutingHandler handler) = CreateViewModel(mirrorOn: true, dialogs);

        byte[] a = "jar-A"u8.ToArray();
        byte[] b = "jar-B"u8.ToArray();
        handler.Responder = request =>
        {
            string url = request.RequestUri!.ToString();
            if (url.Contains("/v2/project/A/version", StringComparison.OrdinalIgnoreCase))
            {
                return JsonResponse(VersionJson("A", "mod-A.jar", Sha1Hex(a), a.Length, dep: "B"));
            }
            if (url.Contains("/v2/project/B/version", StringComparison.OrdinalIgnoreCase))
            {
                return JsonResponse(VersionJson("B", "mod-B.jar", Sha1Hex(b), b.Length));
            }
            if (url.EndsWith("/mod-A.jar", StringComparison.OrdinalIgnoreCase))
            {
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(a) };
            }
            if (url.EndsWith("/mod-B.jar", StringComparison.OrdinalIgnoreCase))
            {
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(b) };
            }
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        };

        await viewModel.InstallCommand.ExecuteAsync(Mod("A"));

        Assert.Empty(Directory.Exists(Path.Combine(gameDir, "mods"))
            ? Directory.EnumerateFiles(Path.Combine(gameDir, "mods"))
            : []);
        // 依赖确认弹窗需列出依赖的项目名与文件名。
        Assert.Contains(dialogs.ConfirmMessages, m => m.Contains("前置模组") && m.Contains("Mod B") && m.Contains("mod-B.jar"));
    }

    [Fact]
    public async Task Install_DependencyConfirmed_DownloadsPrimaryAndDeps()
    {
        var dialogs = new FakeDialogService { ConfirmResult = true };
        (ModsViewModel viewModel, string gameDir, RoutingHandler handler) = CreateViewModel(mirrorOn: true, dialogs);

        byte[] a = "jar-A"u8.ToArray();
        byte[] b = "jar-B"u8.ToArray();
        handler.Responder = request =>
        {
            string url = request.RequestUri!.ToString();
            if (url.Contains("/v2/project/A/version", StringComparison.OrdinalIgnoreCase))
            {
                return JsonResponse(VersionJson("A", "mod-A.jar", Sha1Hex(a), a.Length, dep: "B"));
            }
            if (url.Contains("/v2/project/B/version", StringComparison.OrdinalIgnoreCase))
            {
                return JsonResponse(VersionJson("B", "mod-B.jar", Sha1Hex(b), b.Length));
            }
            if (url.EndsWith("/mod-A.jar", StringComparison.OrdinalIgnoreCase))
            {
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(a) };
            }
            if (url.EndsWith("/mod-B.jar", StringComparison.OrdinalIgnoreCase))
            {
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(b) };
            }
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        };

        await viewModel.InstallCommand.ExecuteAsync(Mod("A"));

        Assert.True(File.Exists(Path.Combine(gameDir, "mods", "mod-A.jar")));
        Assert.True(File.Exists(Path.Combine(gameDir, "mods", "mod-B.jar")));
        Assert.Contains(dialogs.Infos, m => m.Contains("已一并安装 1 个前置依赖"));
    }

    [Fact]
    public async Task Install_ForgeTargetVersion_PicksForgeBuildsForMainAndDep()
    {
        var dialogs = new FakeDialogService { ConfirmResult = true };
        (ModsViewModel viewModel, string gameDir, RoutingHandler handler) = CreateViewModel(mirrorOn: true, dialogs);

        // 目标版本是 Forge → 自动识别，覆盖界面筛选（界面筛选默认 fabric）。
        string versionDir = Path.Combine(gameDir, "versions", "1.20.4");
        Directory.CreateDirectory(versionDir);
        await File.WriteAllTextAsync(Path.Combine(versionDir, "1.20.4.json"),
            """{ "id": "1.20.4", "mainClass": "net.minecraft.client.main.Main", "libraries": [ { "name": "net.minecraftforge:forge:1.20.4-49.2.13" } ] }""");

        byte[] af = "af"u8.ToArray();
        byte[] ag = "ag"u8.ToArray();
        byte[] an = "an"u8.ToArray();
        byte[] bf = "bf"u8.ToArray();
        byte[] bg = "bg"u8.ToArray();
        string aJson = MultiFileVersionJson("A",
            [("mod-A-fabric.jar", true, af), ("mod-A-forge.jar", false, ag), ("mod-A-neoforge.jar", false, an)],
            ["fabric", "forge", "neoforge"], dep: "B");
        string bJson = MultiFileVersionJson("B",
            [("mod-B-fabric.jar", true, bf), ("mod-B-forge.jar", false, bg)],
            ["fabric", "forge"]);

        handler.Responder = request =>
        {
            string url = request.RequestUri!.ToString();
            if (url.Contains("/v2/project/A/version", StringComparison.OrdinalIgnoreCase))
            {
                return JsonResponse(aJson);
            }
            if (url.Contains("/v2/project/B/version", StringComparison.OrdinalIgnoreCase))
            {
                return JsonResponse(bJson);
            }
            foreach ((string name, bool primary, byte[] content) in new[]
                     {
                         ("mod-A-fabric.jar", true, af), ("mod-A-forge.jar", false, ag),
                         ("mod-A-neoforge.jar", false, an), ("mod-B-fabric.jar", true, bf),
                         ("mod-B-forge.jar", false, bg)
                     })
            {
                if (url.EndsWith("/" + name, StringComparison.OrdinalIgnoreCase))
                {
                    return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(content) };
                }
            }
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        };

        await viewModel.InstallCommand.ExecuteAsync(Mod("A"));

        Assert.True(File.Exists(Path.Combine(gameDir, "mods", "mod-A-forge.jar")));
        Assert.True(File.Exists(Path.Combine(gameDir, "mods", "mod-B-forge.jar")));
        Assert.False(File.Exists(Path.Combine(gameDir, "mods", "mod-A-fabric.jar")));
        Assert.False(File.Exists(Path.Combine(gameDir, "mods", "mod-A-neoforge.jar")));
        Assert.False(File.Exists(Path.Combine(gameDir, "mods", "mod-B-fabric.jar")));
    }
}
