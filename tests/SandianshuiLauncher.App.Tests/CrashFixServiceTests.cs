using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using SandianshuiLauncher.Core.Models;
using SandianshuiLauncher.Core.Services;
using SandianshuiLauncher.Services;
using Xunit;

namespace SandianshuiLauncher.App.Tests;

/// <summary>
/// <see cref="CrashFixService"/> 测试：
/// 内存调整写入版本覆盖配置、缺失模组自动搜索安装（含依赖）、无动作时抛错。
/// </summary>
public class CrashFixServiceTests : IDisposable
{
    private readonly string _root;

    public CrashFixServiceTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "sdsl_fix_" + Guid.NewGuid().ToString("N"));
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

    private (CrashFixService Service, ConfigService Config, RoutingHandler Handler, string GameDir) Create()
    {
        var handler = new RoutingHandler();
        var config = new ConfigService(Path.Combine(_root, "data-" + Guid.NewGuid().ToString("N")));
        string gameDir = Path.Combine(_root, "mc-" + Guid.NewGuid().ToString("N"));
        config.Config.GameDirectory = gameDir;
        config.Config.MaxMemoryMb = 2048;
        config.Config.MinMemoryMb = 512;
        var http = new HttpClient(handler);
        var downloads = new DownloadService(http);
        var provider = new DownloadSourceProvider(config.Config);
        var modService = new ModService(http, downloads, config);
        var installer = new GameInstaller(http, provider, downloads);
        var versionService = new VersionService(http, provider);
        var modLoaderService = new ModLoaderService(http, provider, downloads, installer);
        var javaDetector = new JavaDetector();
        return (new CrashFixService(config, modService, installer, javaDetector, versionService, modLoaderService), config, handler, gameDir);
    }

    private static CrashDiagnosis WithAction(CrashFixAction action, string target = "")
        => new() { RuleId = "test", Title = "t", FixAction = action, FixTarget = target };

    [Fact]
    public async Task IncreaseMemory_UpdatesVersionOverride()
    {
        (CrashFixService service, ConfigService config, _, _) = Create();

        string message = await service.ApplyAsync(WithAction(CrashFixAction.IncreaseMemory), "1.20.4");

        VersionSetting? setting = LaunchSettingsResolver.Find(config.Config, "1.20.4");
        Assert.NotNull(setting);
        int expected = Math.Min(ConfigService.RecommendMaxMemoryMb(), 4096); // 2048 + 2048 后受推荐值钳制
        Assert.Equal(expected, setting!.MaxMemoryMb);
        Assert.Contains("提高", message);
    }

    [Fact]
    public async Task DecreaseMemory_HalvesCurrent()
    {
        (CrashFixService service, ConfigService config, _, _) = Create();
        config.Config.MaxMemoryMb = 8192;

        string message = await service.ApplyAsync(WithAction(CrashFixAction.DecreaseMemory), "1.20.4");

        VersionSetting? setting = LaunchSettingsResolver.Find(config.Config, "1.20.4");
        Assert.NotNull(setting);
        Assert.Equal(4096, setting!.MaxMemoryMb);
        Assert.Contains("调整", message);
    }

    [Fact]
    public async Task Apply_NoAction_Throws()
    {
        (CrashFixService service, _, _, _) = Create();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.ApplyAsync(WithAction(CrashFixAction.None), "1.20.4"));
    }

    [Fact]
    public async Task InstallMissingMod_SearchesAndInstallsWithDependencies()
    {
        (CrashFixService service, ConfigService config, RoutingHandler handler, string gameDir) = Create();

        // 原版版本目录（供加载器检测与 mods 目录定位）。
        string versionDir = Path.Combine(gameDir, "versions", "1.20.4");
        Directory.CreateDirectory(versionDir);
        await File.WriteAllTextAsync(Path.Combine(versionDir, "1.20.4.json"),
            """{ "id": "1.20.4", "mainClass": "net.minecraft.client.main.Main" }""");

        byte[] sodium = "sodium-jar"u8.ToArray();
        byte[] cloth = "cloth-jar"u8.ToArray();
        string searchJson = JsonSerializer.Serialize(new
        {
            hits = new object[]
            {
                new { project_id = "AanOCJyg", slug = "sodium", title = "Sodium", description = "renderer",
                      display_categories = new[] { "fabric" } }
            },
            total_hits = 1
        });
        string versionJson = JsonSerializer.Serialize(new object[]
        {
            new
            {
                id = "v1",
                project_id = "AanOCJyg",
                name = "Sodium 0.5.8",
                version_number = "0.5.8",
                game_versions = new[] { "1.20.4" },
                loaders = new[] { "fabric" },
                files = new object[]
                {
                    new { url = "https://cdn.modrinth.com/data/AanOCJyg/versions/v1/sodium.jar",
                          filename = "sodium.jar", primary = true, size = sodium.Length,
                          hashes = new { sha1 = Sha1Hex(sodium) } }
                },
                dependencies = new object[]
                {
                    new { project_id = "Cloth", dependency_type = "required" }
                }
            }
        });
        string clothVersionJson = JsonSerializer.Serialize(new object[]
        {
            new
            {
                id = "v2",
                project_id = "Cloth",
                name = "Cloth Config",
                version_number = "13",
                game_versions = new[] { "1.20.4" },
                loaders = new[] { "fabric" },
                files = new object[]
                {
                    new { url = "https://cdn.modrinth.com/data/Cloth/versions/v2/cloth.jar",
                          filename = "cloth.jar", primary = true, size = cloth.Length,
                          hashes = new { sha1 = Sha1Hex(cloth) } }
                },
                dependencies = new object[0]
            }
        });
        handler.Responder = request =>
        {
            string url = request.RequestUri!.ToString();
            if (url.Contains("query=sodium", StringComparison.OrdinalIgnoreCase))
            {
                return JsonResponse(searchJson);
            }
            if (url.Contains("/v2/project/AanOCJyg/version", StringComparison.OrdinalIgnoreCase))
            {
                return JsonResponse(versionJson);
            }
            if (url.Contains("/v2/project/Cloth/version", StringComparison.OrdinalIgnoreCase))
            {
                return JsonResponse(clothVersionJson);
            }
            if (url.EndsWith("/sodium.jar", StringComparison.OrdinalIgnoreCase))
            {
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(sodium) };
            }
            if (url.EndsWith("/cloth.jar", StringComparison.OrdinalIgnoreCase))
            {
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(cloth) };
            }
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        };

        string message = await service.ApplyAsync(
            WithAction(CrashFixAction.InstallMissingMod, "sodium"), "1.20.4");

        Assert.Contains("已自动安装", message);
        Assert.Contains("Sodium", message);
        Assert.Contains("1 个前置依赖", message);
        Assert.True(File.Exists(Path.Combine(gameDir, "mods", "sodium.jar")));
        Assert.True(File.Exists(Path.Combine(gameDir, "mods", "cloth.jar")));
    }

    [Fact]
    public async Task UpgradeLoader_Fabric_InstallsLatestAndSelectsIt()
    {
        (CrashFixService service, ConfigService config, RoutingHandler handler, string gameDir) = Create();

        // 原版 1.20.1 已安装（升级目标）。
        string versionDir = Path.Combine(gameDir, "versions", "1.20.1");
        Directory.CreateDirectory(versionDir);
        await File.WriteAllTextAsync(Path.Combine(versionDir, "1.20.1.json"),
            """{ "id": "1.20.1", "mainClass": "net.minecraft.client.main.Main", "libraries": [] }""");
        config.Config.SelectedVersionId = "1.20.1";

        string loaderListJson = JsonSerializer.Serialize(new object[]
        {
            new { loader = new { version = "0.15.11", maven = "net.fabricmc:fabric-loader:0.15.11" } }
        });
        const string profileJson = """{ "id": "fabric-loader-0.15.11-1.20.1", "inheritsFrom": "1.20.1", "libraries": [] }""";
        handler.Responder = request =>
        {
            string url = request.RequestUri!.ToString();
            if (url.Contains("/v2/versions/loader/1.20.1/0.15.11/profile/json", StringComparison.OrdinalIgnoreCase))
            {
                return JsonResponse(profileJson);
            }
            if (url.Contains("/v2/versions/loader/1.20.1", StringComparison.OrdinalIgnoreCase))
            {
                return JsonResponse(loaderListJson);
            }
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        };

        string message = await service.ApplyAsync(
            WithAction(CrashFixAction.UpgradeLoader, "fabric"), "1.20.1");

        Assert.Contains("Fabric", message);
        Assert.Contains("fabric-loader-0.15.11-1.20.1", message);
        Assert.True(File.Exists(Path.Combine(
            gameDir, "versions", "fabric-loader-0.15.11-1.20.1", "fabric-loader-0.15.11-1.20.1.json")));
        // 自动切换到新安装的加载器版本。
        Assert.Equal("fabric-loader-0.15.11-1.20.1", config.Config.SelectedVersionId);
    }
}
