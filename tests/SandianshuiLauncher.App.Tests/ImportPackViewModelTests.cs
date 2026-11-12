using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using SandianshuiLauncher.Core.Models;
using SandianshuiLauncher.Core.Services;
using SandianshuiLauncher.Services;
using SandianshuiLauncher.ViewModels;
using Xunit;

namespace SandianshuiLauncher.App.Tests;

/// <summary>
/// <see cref="ImportPackViewModel"/> 测试：
/// ①压缩包模式（mrpack 导入到目标版本）；②实例模式（解压即玩文件夹导入，含 Java 检测与版本选中）。
/// </summary>
public class ImportPackViewModelTests : IDisposable
{
    private readonly string _root;

    public ImportPackViewModelTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "sdsl_impvm_" + Guid.NewGuid().ToString("N"));
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

    private static HttpResponseMessage JsonResponse(string content)
        => new(HttpStatusCode.OK)
        {
            Content = new StringContent(content, Encoding.UTF8, "application/json")
        };

    private (
        ImportPackViewModel ViewModel,
        ConfigService Config,
        RoutingHandler Handler,
        FakeDialogService Dialogs,
        string GameDir) Create()
    {
        var handler = new RoutingHandler();
        var config = new ConfigService(Path.Combine(_root, "data-" + Guid.NewGuid().ToString("N")));
        string gameDir = Path.Combine(_root, "mc-" + Guid.NewGuid().ToString("N"));
        config.Config.GameDirectory = gameDir;
        var http = new HttpClient(handler);
        var downloads = new DownloadService(http);
        var modpack = new ModpackService(http, downloads);
        var directoryPack = new DirectoryPackService(new VersionService(http, new DownloadSourceProvider(config.Config)));
        var dialogs = new FakeDialogService();
        var packImport = TestServices.CreatePackImportService(http, config, downloads, dialogs);
        var viewModel = new ImportPackViewModel(config, directoryPack, packImport, dialogs);
        return (viewModel, config, handler, dialogs, gameDir);
    }

    private static string BuildMrpack(string root, string projectId, string fileName)
    {
        string packPath = Path.Combine(root, "pack.mrpack");
        byte[] jar = "jar-content"u8.ToArray();
        using var fileStream = new FileStream(packPath, FileMode.Create);
        using var zip = new ZipArchive(fileStream, ZipArchiveMode.Create);
        ZipArchiveEntry index = zip.CreateEntry("modrinth.index.json");
        using (var writer = new StreamWriter(index.Open()))
        {
            writer.Write($$"""
            { "formatVersion": 1, "game": "minecraft", "versionId": "1.20.4", "name": "Test Pack",
              "dependencies": { "minecraft": "1.20.4" },
              "files": [ { "path": "mods/{{fileName}}", "downloads": [ "https://cdn.modrinth.com/data/{{projectId}}/versions/x/{{fileName}}" ], "fileSize": {{jar.Length}}, "hashes": { "sha1": "{{Convert.ToHexString(System.Security.Cryptography.SHA1.HashData(jar)).ToLowerInvariant()}}" } } ] }
            """);
        }
        ZipArchiveEntry modEntry = zip.CreateEntry("overrides/mods/" + fileName);
        using var modStream = modEntry.Open();
        modStream.Write(jar);
        return packPath;
    }

    [Fact]
    public async Task ImportZip_Mrpack_ImportsIntoSelectedVersion()
    {
        (ImportPackViewModel viewModel, ConfigService config, RoutingHandler handler, FakeDialogService dialogs, string gameDir) = Create();

        // 本地版本 1.20.4。
        string versionDir = Path.Combine(gameDir, "versions", "1.20.4");
        Directory.CreateDirectory(versionDir);
        await File.WriteAllTextAsync(Path.Combine(versionDir, "1.20.4.json"),
            """{ "id": "1.20.4", "mainClass": "net.minecraft.client.main.Main" }""");
        config.Config.SelectedVersionId = "1.20.4";
        viewModel.RefreshVersions();
        viewModel.CreateNewInstance = false; // 导入到现有版本模式
        Assert.Equal("1.20.4", viewModel.SelectedTargetVersion?.Id);

        string packPath = BuildMrpack(_root, "P1", "sodium.jar");
        handler.Responder = request =>
            request.RequestUri!.Host.Contains("modrinth", StringComparison.OrdinalIgnoreCase)
            || request.RequestUri.ToString().Contains("sodium.jar")
                ? new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent("jar-content"u8.ToArray())
                }
                : new HttpResponseMessage(HttpStatusCode.NotFound);
        dialogs.PickFileResult = packPath;
        dialogs.ConfirmResults.Enqueue(true);

        bool imported = false;
        viewModel.Imported += () => imported = true;
        await viewModel.ImportZipCommand.ExecuteAsync(null);

        Assert.True(imported);
        Assert.True(File.Exists(Path.Combine(gameDir, "mods", "sodium.jar")));
        Assert.Contains(dialogs.Infos, m => m.Contains("已导入到版本"));
    }

    [Fact]
    public async Task ImportFolder_DirectoryPack_CopiesSelectsAndBundledJava()
    {
        (ImportPackViewModel viewModel, ConfigService config, _, FakeDialogService dialogs, string gameDir) = Create();

        // 构造「解压即玩」整合包目录。
        string mcRoot = Path.Combine(_root, "pack", ".minecraft");
        string versionDir = Path.Combine(mcRoot, "versions", "ExamplePack");
        Directory.CreateDirectory(Path.Combine(versionDir, "mods"));
        Directory.CreateDirectory(Path.Combine(versionDir, "Java", "bin"));
        await File.WriteAllTextAsync(Path.Combine(versionDir, "ExamplePack.json"), """{ "id": "ExamplePack", "inheritsFrom": "1.20.1" }""");
        await File.WriteAllTextAsync(Path.Combine(versionDir, "mods", "a.jar"), "m");
        await File.WriteAllTextAsync(Path.Combine(versionDir, "Java", "bin", "java.exe"), "java");
        Directory.CreateDirectory(Path.Combine(mcRoot, "assets", "objects", "ab"));
        await File.WriteAllTextAsync(Path.Combine(mcRoot, "assets", "objects", "ab", "x"), "a");
        Directory.CreateDirectory(Path.Combine(mcRoot, "assets", "indexes"));
        Directory.CreateDirectory(Path.Combine(mcRoot, "libraries", "net"));
        await File.WriteAllTextAsync(Path.Combine(mcRoot, "libraries", "net", "l.jar"), "l");
        // 目标预置父版本 json。
        string parentDir = Path.Combine(gameDir, "versions", "1.20.1");
        Directory.CreateDirectory(parentDir);
        await File.WriteAllTextAsync(Path.Combine(parentDir, "1.20.1.json"), """{ "id": "1.20.1" }""");

        dialogs.PickFolderResult = mcRoot;
        dialogs.ConfirmResults.Enqueue(true); // 确认导入
        dialogs.ConfirmResults.Enqueue(true); // 确认开启版本隔离

        bool imported = false;
        viewModel.Imported += () => imported = true;
        await viewModel.ImportFolderCommand.ExecuteAsync(null);

        Assert.True(imported);
        Assert.True(File.Exists(Path.Combine(gameDir, "versions", "ExamplePack", "ExamplePack.json")));
        Assert.Equal("ExamplePack", config.Config.SelectedVersionId);
        Assert.True(config.Config.VersionIsolation);
        VersionSetting? setting = LaunchSettingsResolver.Find(config.Config, "ExamplePack");
        Assert.NotNull(setting);
        Assert.NotNull(setting!.JavaPath);
        Assert.EndsWith(@"ExamplePack\Java\bin\java.exe", setting.JavaPath);
        Assert.Contains(dialogs.Infos, m => m.Contains("导入完成"));
    }

    [Fact]
    public async Task ImportZip_NoTargetVersion_ShowsInfo()
    {
        (ImportPackViewModel viewModel, _, _, FakeDialogService dialogs, _) = Create();
        viewModel.CreateNewInstance = false;

        await viewModel.ImportZipCommand.ExecuteAsync(null);

        Assert.Contains(dialogs.Infos, m => m.Contains("导入目标"));
        Assert.Empty(dialogs.PickFileCalls);
    }

    [Fact]
    public async Task ImportZip_NewInstanceMode_NonMrpack_ShowsInfo()
    {
        (ImportPackViewModel viewModel, _, _, FakeDialogService dialogs, _) = Create();
        // 默认即为「自动新建实例」模式。
        Assert.True(viewModel.CreateNewInstance);

        string cfZip = Path.Combine(_root, "cf.zip");
        using (var fileStream = new FileStream(cfZip, FileMode.Create))
        using (var zip = new ZipArchive(fileStream, ZipArchiveMode.Create))
        {
            ZipArchiveEntry entry = zip.CreateEntry("manifest.json");
            using var writer = new StreamWriter(entry.Open());
            writer.Write("{}");
        }
        dialogs.PickFileResult = cfZip;

        await viewModel.ImportZipCommand.ExecuteAsync(null);

        Assert.Contains(dialogs.Infos, m => m.Contains("仅支持 Modrinth") || m.Contains("自动新建实例"));
    }

    [Fact]
    public async Task ImportZip_NewInstanceMode_ForgePack_CreatesInstanceAndSelectsIt()
    {
        (ImportPackViewModel viewModel, ConfigService config, RoutingHandler handler, FakeDialogService dialogs, string gameDir) = Create();
        Assert.True(viewModel.CreateNewInstance);

        // Forge 整合包（依赖 forge 47.3.12 + 一个模组）。
        string packPath = Path.Combine(_root, "examplepack.mrpack");
        byte[] modBytes = "mod"u8.ToArray();
        using (var fileStream = new FileStream(packPath, FileMode.Create))
        using (var zip = new ZipArchive(fileStream, ZipArchiveMode.Create))
        {
            ZipArchiveEntry index = zip.CreateEntry("modrinth.index.json");
            using (var writer = new StreamWriter(index.Open()))
            {
                writer.Write(
                    "{\"formatVersion\":1,\"game\":\"minecraft\",\"versionId\":\"0.1.1\",\"name\":\"ExamplePack\"," +
                    "\"dependencies\":{\"minecraft\":\"1.20.1\",\"forge\":\"47.3.12\"}," +
                    "\"files\":[{\"path\":\"mods/test-mod.jar\"," +
                    "\"downloads\":[\"https://cdn.modrinth.com/data/AAA/versions/v1/test-mod.jar\"]," +
                    "\"fileSize\":3,\"hashes\":{\"sha1\":\"" +
                    Convert.ToHexString(SHA1.HashData(modBytes)).ToLowerInvariant() + "\"}}]}");
            }
        }

        const string manifestJson = """{ "versions": [ { "id": "1.20.1", "url": "https://piston-meta.mojang.com/v1/packages/x/1.20.1.json" } ] }""";
        const string vanillaJson = """{ "id": "1.20.1", "mainClass": "net.minecraft.client.main.Main", "libraries": [] }""";
        const string forgeVersionJson = """{ "id": "1.20.1-forge-47.3.12", "inheritsFrom": "1.20.1", "mainClass": "cpw.mods.bootstraplauncher.BootstrapLauncher", "libraries": [] }""";
        const string forgeProfile = """{ "spec": 1, "minecraft": "1.20.1", "version": "1.20.1-forge-47.3.12", "data": {}, "processors": [] }""";
        handler.Responder = request =>
        {
            string url = request.RequestUri!.ToString();
            if (url.Contains("version_manifest", StringComparison.OrdinalIgnoreCase))
            {
                return JsonResponse(manifestJson);
            }
            if (url.Contains("1.20.1.json", StringComparison.OrdinalIgnoreCase))
            {
                return JsonResponse(vanillaJson);
            }
            if (url.Contains("installer.jar", StringComparison.OrdinalIgnoreCase))
            {
                using var buffer = new MemoryStream();
                using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
                {
                    using (var writer = new StreamWriter(zip.CreateEntry("install_profile.json").Open()))
                    {
                        writer.Write(forgeProfile);
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

        dialogs.PickFileResult = packPath;
        dialogs.ConfirmResults.Enqueue(true); // 确认自动新建实例
        dialogs.ConfirmResults.Enqueue(true); // 确认开启版本隔离

        bool imported = false;
        viewModel.Imported += () => imported = true;
        await viewModel.ImportZipCommand.ExecuteAsync(null);

        Assert.True(imported);
        Assert.Equal("ExamplePack", config.Config.SelectedVersionId);
        Assert.True(config.Config.VersionIsolation);
        Assert.True(File.Exists(Path.Combine(gameDir, "versions", "ExamplePack", "ExamplePack.json")));
        Assert.True(File.Exists(Path.Combine(gameDir, "versions", "ExamplePack", "mods", "test-mod.jar")));
        Assert.Contains(dialogs.Infos, m => m.Contains("已导入为新实例"));
    }
}
