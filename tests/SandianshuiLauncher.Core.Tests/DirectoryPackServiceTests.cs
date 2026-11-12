using System.Net;
using System.Text.Json;
using SandianshuiLauncher.Core.Services;
using Xunit;

namespace SandianshuiLauncher.Core.Tests;

/// <summary>
/// <see cref="DirectoryPackService"/> 测试：
/// 定位 .minecraft、识别整合包版本、复制版本目录并合并 assets/libraries、Java 检测、同名跳过、父版本 JSON 补充。
/// </summary>
public class DirectoryPackServiceTests : IDisposable
{
    private readonly string _root;

    public DirectoryPackServiceTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "sdsl_pack_" + Guid.NewGuid().ToString("N"));
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

    private sealed class RoutingHandler : HttpMessageHandler
    {
        public Func<HttpRequestMessage, HttpResponseMessage> Responder { get; set; }
            = _ => new HttpResponseMessage(HttpStatusCode.NotFound);

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(Responder(request));
    }

    private static DirectoryPackService CreateService(RoutingHandler? handler = null)
    {
        handler ??= new RoutingHandler();
        var http = new HttpClient(handler);
        var provider = new DownloadSourceProvider(new Models.LauncherConfig());
        return new DirectoryPackService(new VersionService(http, provider));
    }

    private static void WriteFile(string path, string content = "x")
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    private string BuildPack()
    {
        // 模拟「解压即玩」整合包：ExamplePack 版本目录 + assets + libraries。
        string mcRoot = Path.Combine(_root, "pack", ".minecraft");
        string versionDir = Path.Combine(mcRoot, "versions", "ExamplePack");
        WriteFile(Path.Combine(versionDir, "ExamplePack.json"), """{ "id": "ExamplePack", "inheritsFrom": "1.20.1" }""");
        WriteFile(Path.Combine(versionDir, "ExamplePack.jar"), "jar-content");
        WriteFile(Path.Combine(versionDir, "mods", "a-mod.jar"), "mod");
        WriteFile(Path.Combine(versionDir, "config", "mod.conf"), "cfg");
        WriteFile(Path.Combine(versionDir, "Java", "bin", "java.exe"), "java");
        WriteFile(Path.Combine(mcRoot, "assets", "objects", "ab", "abc123"), "asset1");
        WriteFile(Path.Combine(mcRoot, "assets", "objects", "cd", "cde456"), "asset2");
        WriteFile(Path.Combine(mcRoot, "assets", "indexes", "5.json"), "{}");
        WriteFile(Path.Combine(mcRoot, "libraries", "net", "minecraftforge", "fmlloader.jar"), "lib");
        return mcRoot;
    }

    [Fact]
    public void FindMinecraftRoot_AcceptsRootItselfAndParent()
    {
        string mcRoot = BuildPack();
        string parent = Directory.GetParent(mcRoot)!.FullName;

        Assert.Equal(mcRoot, DirectoryPackService.FindMinecraftRoot(mcRoot));
        Assert.Equal(mcRoot, DirectoryPackService.FindMinecraftRoot(parent));
    }

    [Fact]
    public void FindMinecraftRoot_NonPackDirectory_ReturnsNull()
    {
        string other = Path.Combine(_root, "not-a-pack");
        Directory.CreateDirectory(other);

        Assert.Null(DirectoryPackService.FindMinecraftRoot(other));
    }

    [Fact]
    public void DetectImportableVersions_RequiresJsonAndModsOrConfig()
    {
        string mcRoot = BuildPack();
        // 加一个无 mods/config 的普通版本目录，不应被识别。
        WriteFile(Path.Combine(mcRoot, "versions", "1.20.1", "1.20.1.json"), "{}");

        List<string> detected = DirectoryPackService.DetectImportableVersions(mcRoot);

        Assert.Equal(["ExamplePack"], detected);
    }

    [Fact]
    public async Task ImportAsync_CopiesVersionAndMergesAssetsAndLibraries()
    {
        string mcRoot = BuildPack();
        string gameDir = Path.Combine(_root, "mc");
        // 目标预置：父版本 json 已存在 + 一个同名 asset（验证跳过合并）。
        WriteFile(Path.Combine(gameDir, "versions", "1.20.1", "1.20.1.json"), "{}");
        WriteFile(Path.Combine(gameDir, "assets", "objects", "ab", "abc123"), "already-there");

        var service = CreateService();
        var result = await service.ImportAsync(mcRoot, gameDir, ["ExamplePack"]);

        Assert.Equal(["ExamplePack"], result.ImportedVersions);
        Assert.Empty(result.SkippedVersions);
        // 版本目录复制。
        Assert.True(File.Exists(Path.Combine(gameDir, "versions", "ExamplePack", "ExamplePack.json")));
        Assert.True(File.Exists(Path.Combine(gameDir, "versions", "ExamplePack", "mods", "a-mod.jar")));
        // assets 合并（已存在的 abc123 被跳过，内容保持）。
        Assert.Equal("already-there", File.ReadAllText(Path.Combine(gameDir, "assets", "objects", "ab", "abc123")));
        Assert.True(File.Exists(Path.Combine(gameDir, "assets", "objects", "cd", "cde456")));
        // libraries 合并。
        Assert.True(File.Exists(Path.Combine(gameDir, "libraries", "net", "minecraftforge", "fmlloader.jar")));
        // 自带 Java 检测。
        Assert.Equal(
            Path.Combine(gameDir, "versions", "ExamplePack", "Java", "bin", "java.exe"),
            result.BundledJavaByVersion["ExamplePack"]);
        Assert.True(result.CopiedFiles > 0);
    }

    [Fact]
    public async Task ImportAsync_ExistingVersion_SkippedWithWarning()
    {
        string mcRoot = BuildPack();
        string gameDir = Path.Combine(_root, "mc");
        WriteFile(Path.Combine(gameDir, "versions", "ExamplePack", "ExamplePack.json"), "{}");

        var service = CreateService();
        var result = await service.ImportAsync(mcRoot, gameDir, ["ExamplePack"]);

        Assert.Empty(result.ImportedVersions);
        Assert.Equal(["ExamplePack"], result.SkippedVersions);
        Assert.Contains(result.Warnings, w => w.Contains("已存在"));
    }

    [Fact]
    public async Task ImportAsync_MissingParentJson_DownloadsIt()
    {
        string mcRoot = BuildPack();
        string gameDir = Path.Combine(_root, "mc");

        var handler = new RoutingHandler
        {
            Responder = request =>
            {
                string url = request.RequestUri!.ToString();
                if (url.Contains("version_manifest", StringComparison.OrdinalIgnoreCase))
                {
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(
                            """{ "versions": [ { "id": "1.20.1", "url": "https://piston-meta.mojang.com/v1/packages/xxx/1.20.1.json" } ] }""",
                            System.Text.Encoding.UTF8, "application/json")
                    };
                }
                if (url.Contains("1.20.1.json", StringComparison.OrdinalIgnoreCase))
                {
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(
                            """{ "id": "1.20.1", "assetIndex": { "id": "5" } }""",
                            System.Text.Encoding.UTF8, "application/json")
                    };
                }
                return new HttpResponseMessage(HttpStatusCode.NotFound);
            }
        };
        var service = CreateService(handler);

        var result = await service.ImportAsync(mcRoot, gameDir, ["ExamplePack"]);

        string parentJson = Path.Combine(gameDir, "versions", "1.20.1", "1.20.1.json");
        Assert.True(File.Exists(parentJson));
        Assert.Contains("assetIndex", File.ReadAllText(parentJson));
        Assert.Empty(result.Warnings);
    }

    [Fact]
    public async Task ImportAsync_NoBundledJava_Warns()
    {
        string mcRoot = BuildPack();
        Directory.Delete(Path.Combine(mcRoot, "versions", "ExamplePack", "Java"), recursive: true);
        string gameDir = Path.Combine(_root, "mc");
        WriteFile(Path.Combine(gameDir, "versions", "1.20.1", "1.20.1.json"), "{}");

        var service = CreateService();
        var result = await service.ImportAsync(mcRoot, gameDir, ["ExamplePack"]);

        Assert.Empty(result.BundledJavaByVersion);
        Assert.Contains(result.Warnings, w => w.Contains("未包含自带 Java"));
    }
}
