using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SandianshuiLauncher.Core.Models;
using SandianshuiLauncher.Core.Services;
using Xunit;

namespace SandianshuiLauncher.Core.Tests;

/// <summary>
/// <see cref="ModpackInstanceService"/> 测试：
/// 依赖解析、实例命名去重、端到端「自动新建实例」（含原版/加载器安装与 mods 导入）。
/// </summary>
public class ModpackInstanceServiceTests : IDisposable
{
    private readonly string _root;

    public ModpackInstanceServiceTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "sdsl_inst_" + Guid.NewGuid().ToString("N"));
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

        public List<string> RequestedUrls { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestedUrls.Add(request.RequestUri!.ToString());
            return Task.FromResult(Responder(request));
        }
    }

    private static HttpResponseMessage Json(string content)
        => new(HttpStatusCode.OK) { Content = new StringContent(content, Encoding.UTF8, "application/json") };

    private (ModpackInstanceService Service, ConfigService Config, RoutingHandler Handler, string GameDir) Create()
    {
        var handler = new RoutingHandler();
        var config = new ConfigService(Path.Combine(_root, "data-" + Guid.NewGuid().ToString("N")));
        string gameDir = Path.Combine(_root, "mc-" + Guid.NewGuid().ToString("N"));
        config.Config.GameDirectory = gameDir;
        var http = new HttpClient(handler);
        var downloads = new DownloadService(http);
        var provider = new DownloadSourceProvider(config.Config);
        var versionService = new VersionService(http, provider);
        var installer = new GameInstaller(http, provider, downloads);
        var modLoader = new ModLoaderService(http, provider, downloads, installer);
        var modpack = new ModpackService(http, downloads);
        var java = new JavaDetector();
        return (new ModpackInstanceService(versionService, installer, modLoader, java, modpack), config, handler, gameDir);
    }

    private string BuildMrpack(string packName, string minecraft, string? loaderType, string? loaderVersion)
    {
        string path = Path.Combine(_root, "pack-" + Guid.NewGuid().ToString("N") + ".mrpack");
        using var fileStream = new FileStream(path, FileMode.Create);
        using var zip = new ZipArchive(fileStream, ZipArchiveMode.Create);

        string dependencies = loaderType is null
            ? $"\"minecraft\": \"{minecraft}\""
            : $"\"minecraft\": \"{minecraft}\", \"{loaderType}\": \"{loaderVersion}\"";
        ZipArchiveEntry index = zip.CreateEntry("modrinth.index.json");
        using (var writer = new StreamWriter(index.Open()))
        {
            writer.Write(
                "{\"formatVersion\":1,\"game\":\"minecraft\",\"versionId\":\"1.0\",\"name\":\"" + packName + "\"," +
                "\"dependencies\":{" + dependencies + "}," +
                "\"files\":[{\"path\":\"mods/test-mod.jar\"," +
                "\"downloads\":[\"https://cdn.modrinth.com/data/AAA/versions/v1/test-mod.jar\"]," +
                "\"fileSize\":3,\"hashes\":{\"sha1\":\"" + Sha1Hex("mod"u8.ToArray()) + "\"}}]}");
        }
        ZipArchiveEntry overrideEntry = zip.CreateEntry("overrides/config/test.conf");
        using var stream = overrideEntry.Open();
        stream.Write("key=value"u8);
        return path;
    }

    private static string Sha1Hex(byte[] data)
        => Convert.ToHexString(SHA1.HashData(data)).ToLowerInvariant();

    [Fact]
    public void ReadRequirements_ForgePack_ParsesMinecraftAndLoaderVersion()
    {
        (ModpackInstanceService _, _, _, _) = Create();
        string pack = BuildMrpack("ExamplePack", "1.20.1", "forge", "47.3.12");

        ModpackRequirements requirements = ModpackInstanceService.ReadRequirements(pack);

        Assert.Equal("1.20.1", requirements.Minecraft);
        Assert.Equal("forge", requirements.LoaderType);
        Assert.Equal("47.3.12", requirements.LoaderVersion);
        Assert.True(requirements.HasLoader);
    }

    [Fact]
    public void ReadRequirements_VanillaPack_HasNoLoader()
    {
        string pack = BuildMrpack("ConfigOnly", "1.20.4", null, null);

        ModpackRequirements requirements = ModpackInstanceService.ReadRequirements(pack);

        Assert.Equal("1.20.4", requirements.Minecraft);
        Assert.False(requirements.HasLoader);
    }

    [Fact]
    public void ReadRequirements_NeoForgePreferredOverForge()
    {
        string path = Path.Combine(_root, "neo.mrpack");
        using (var fileStream = new FileStream(path, FileMode.Create))
        using (var zip = new ZipArchive(fileStream, ZipArchiveMode.Create))
        {
            ZipArchiveEntry entry = zip.CreateEntry("modrinth.index.json");
            using var writer = new StreamWriter(entry.Open());
            writer.Write("""{"formatVersion":1,"game":"minecraft","name":"Neo","dependencies":{"minecraft":"1.20.4","forge":"47.2.0","neoforge":"20.4.237"},"files":[]}""");
        }

        ModpackRequirements requirements = ModpackInstanceService.ReadRequirements(path);

        Assert.Equal("neoforge", requirements.LoaderType);
        Assert.Equal("20.4.237", requirements.LoaderVersion);
    }

    [Fact]
    public void ReadRequirements_MissingMinecraft_Throws()
    {
        string path = Path.Combine(_root, "bad.mrpack");
        using (var fileStream = new FileStream(path, FileMode.Create))
        using (var zip = new ZipArchive(fileStream, ZipArchiveMode.Create))
        {
            ZipArchiveEntry entry = zip.CreateEntry("modrinth.index.json");
            using var writer = new StreamWriter(entry.Open());
            writer.Write("""{"formatVersion":1,"game":"minecraft","name":"x","dependencies":{},"files":[]}""");
        }

        Assert.Throws<InvalidDataException>(() => ModpackInstanceService.ReadRequirements(path));
    }

    [Fact]
    public void ReadPackName_UsesIndexName()
    {
        string pack = BuildMrpack("【导入包】ExamplePack", "1.20.1", "forge", "47.3.12");

        Assert.Equal("【导入包】ExamplePack", ModpackInstanceService.ReadPackName(pack));
    }

    [Fact]
    public void BuildInstanceId_RepeatedName_AppendsSuffix()
    {
        string gameDir = Path.Combine(_root, "game");
        Directory.CreateDirectory(Path.Combine(gameDir, "versions"));

        string first = ModpackInstanceService.BuildInstanceId("ExamplePack", gameDir);
        Directory.CreateDirectory(Path.Combine(gameDir, "versions", first));
        string second = ModpackInstanceService.BuildInstanceId("ExamplePack", gameDir);
        Directory.CreateDirectory(Path.Combine(gameDir, "versions", second));
        string third = ModpackInstanceService.BuildInstanceId("ExamplePack", gameDir);

        Assert.Equal("ExamplePack", first);
        Assert.Equal("ExamplePack-2", second);
        Assert.Equal("ExamplePack-3", third);
    }

    [Theory]
    [InlineData("a/b:c", "a-b-c")]
    [InlineData("  name  ", "name")]
    [InlineData("x--y", "x-y")]
    [InlineData("", "")]
    public void Sanitize_RemovesIllegalCharacters(string input, string expected)
    {
        Assert.Equal(expected, ModpackInstanceService.Sanitize(input));
    }

    [Fact]
    public async Task ImportAsNewInstance_ForgePack_InstallsVanillaLoaderAndMods()
    {
        (ModpackInstanceService service, ConfigService config, RoutingHandler handler, string gameDir) = Create();
        string pack = BuildMrpack("ExamplePack", "1.20.1", "forge", "47.3.12");

        const string manifestJson = """{ "versions": [ { "id": "1.20.1", "url": "https://piston-meta.mojang.com/v1/packages/x/1.20.1.json" } ] }""";
        const string vanillaJson = """{ "id": "1.20.1", "mainClass": "net.minecraft.client.main.Main", "assetIndex": { "id": "5" }, "libraries": [], "downloads": {} }""";
        // Forge 安装器：最小可用的 version.json（模拟已解压安装器）。
        const string forgeVersionJson = """{ "id": "1.20.1-forge-47.3.12", "inheritsFrom": "1.20.1", "mainClass": "cpw.mods.bootstraplauncher.BootstrapLauncher", "libraries": [] }""";
        const string forgeInstallProfile = """{ "spec": 1, "minecraft": "1.20.1", "version": "1.20.1-forge-47.3.12", "data": {}, "processors": [] }""";

        byte[] modBytes = "mod"u8.ToArray();
        handler.Responder = request =>
        {
            string url = request.RequestUri!.ToString();
            if (url.Contains("version_manifest", StringComparison.OrdinalIgnoreCase))
            {
                return Json(manifestJson);
            }
            if (url.Contains("1.20.1.json", StringComparison.OrdinalIgnoreCase))
            {
                return Json(vanillaJson);
            }
            if (url.Contains("installer.jar", StringComparison.OrdinalIgnoreCase))
            {
                // 构造一个含 install_profile.json 与 version.json 的安装器 zip。
                using var buffer = new MemoryStream();
                using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
                {
                    using (var writer = new StreamWriter(zip.CreateEntry("install_profile.json").Open()))
                    {
                        writer.Write(forgeInstallProfile);
                    }
                    using (var writer = new StreamWriter(zip.CreateEntry("version.json").Open()))
                    {
                        writer.Write(forgeVersionJson);
                    }
                }
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(buffer.ToArray()) };
            }
            if (url.Contains("test-mod.jar", StringComparison.OrdinalIgnoreCase))
            {
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(modBytes) };
            }
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        };

        var statuses = new List<string>();
        InstanceImportResult result = await service.ImportAsNewInstanceAsync(
            pack, gameDir, threads: 1, onStatus: statuses.Add);

        Assert.Equal("ExamplePack", result.VersionId);
        Assert.Equal("1.20.1", result.MinecraftVersion);
        Assert.Equal("forge", result.LoaderType);
        Assert.Equal("47.3.12", result.LoaderVersion);
        // 原版版本已就绪。
        Assert.True(File.Exists(Path.Combine(gameDir, "versions", "1.20.1", "1.20.1.json")));
        // 加载器版本已安装。
        Assert.True(File.Exists(Path.Combine(
            gameDir, "versions", "1.20.1-forge-47.3.12", "1.20.1-forge-47.3.12.json")));
        // 独立实例目录（复制自加载器版本，id 已改写为实例名）。
        string instanceJson = Path.Combine(gameDir, "versions", "ExamplePack", "ExamplePack.json");
        Assert.True(File.Exists(instanceJson));
        using (JsonDocument doc = JsonDocument.Parse(File.ReadAllText(instanceJson)))
        {
            Assert.Equal("ExamplePack", doc.RootElement.GetProperty("id").GetString());
        }
        // mods 与 overrides 导入到实例目录。
        Assert.True(File.Exists(Path.Combine(gameDir, "versions", "ExamplePack", "mods", "test-mod.jar")));
        Assert.True(File.Exists(Path.Combine(gameDir, "versions", "ExamplePack", "config", "test.conf")));
        // 过程状态有中文提示。
        Assert.Contains(statuses, s => s.Contains("原版"));
        Assert.Contains(statuses, s => s.Contains("forge", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData(40, 0)]
    [InlineData(50, 4096)]
    [InlineData(120, 6144)]
    [InlineData(160, 8192)]
    [InlineData(229, 8192)]
    public void RecommendedMemoryMb_ByModCount(int modCount, int expected)
    {
        Assert.Equal(expected, ModpackInstanceService.RecommendedMemoryMb(modCount));
    }

    [Fact]
    public async Task ImportAsNewInstance_VanillaPack_CreatesIsolatedCopyWithoutLoader()
    {
        (ModpackInstanceService service, _, RoutingHandler handler, string gameDir) = Create();
        string pack = BuildMrpack("ConfigOnly", "1.20.4", null, null);

        // 原版版本已存在（无需下载）。
        string vanillaDir = Path.Combine(gameDir, "versions", "1.20.4");
        Directory.CreateDirectory(vanillaDir);
        await File.WriteAllTextAsync(Path.Combine(vanillaDir, "1.20.4.json"),
            """{ "id": "1.20.4", "mainClass": "net.minecraft.client.main.Main", "libraries": [] }""");
        handler.Responder = request =>
            request.RequestUri!.ToString().Contains("test-mod.jar", StringComparison.OrdinalIgnoreCase)
                ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent("mod"u8.ToArray()) }
                : new HttpResponseMessage(HttpStatusCode.NotFound);

        InstanceImportResult result = await service.ImportAsNewInstanceAsync(pack, gameDir, threads: 1);

        Assert.Equal("ConfigOnly", result.VersionId);
        Assert.Null(result.LoaderType);
        Assert.True(File.Exists(Path.Combine(gameDir, "versions", "ConfigOnly", "ConfigOnly.json")));
        Assert.True(File.Exists(Path.Combine(gameDir, "versions", "ConfigOnly", "mods", "test-mod.jar")));
    }
}
