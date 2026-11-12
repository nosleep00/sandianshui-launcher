using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using SandianshuiLauncher.Core.Models;
using SandianshuiLauncher.Core.Services;
using Xunit;

namespace SandianshuiLauncher.Core.Tests;

/// <summary>
/// <see cref="ModpackService"/> 测试：
/// 验证 Modrinth 整合包解析下载、overrides 应用、服务端文件跳过、
/// zip-slip 路径穿越防护与依赖提示。
/// </summary>
public class ModpackServiceTests : IDisposable
{
    private readonly string _root;

    public ModpackServiceTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "sdsl_modpack_" + Guid.NewGuid().ToString("N"));
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

    private static byte[] BuildZip(IReadOnlyDictionary<string, byte[]> entries)
    {
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach ((string name, byte[] data) in entries)
            {
                ZipArchiveEntry entry = zip.CreateEntry(name);
                using var stream = entry.Open();
                stream.Write(data, 0, data.Length);
            }
        }
        return ms.ToArray();
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        public Func<HttpRequestMessage, HttpResponseMessage> Responder { get; set; }
            = _ => new HttpResponseMessage(HttpStatusCode.NotFound);

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(Responder(request));
    }

    private static HttpResponseMessage Bytes(byte[] content)
        => new(HttpStatusCode.OK) { Content = new ByteArrayContent(content) };

    private (ModpackService Service, string GameDir) CreateService(StubHandler handler)
    {
        var http = new HttpClient(handler);
        var downloads = new DownloadService(http);
        string gameDir = Path.Combine(_root, "mc");
        Directory.CreateDirectory(gameDir);
        return (new ModpackService(http, downloads), gameDir);
    }

    private static void WriteLocalVersion(string gameDir, string id)
    {
        string dir = Path.Combine(gameDir, "versions", id);
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, id + ".json"), $"{{\"id\":\"{id}\"}}");
    }

    [Fact]
    public async Task Import_ValidMrpack_DownloadsModsAndAppliesOverrides()
    {
        byte[] modContent = "mod-jar-content"u8.ToArray();
        byte[] overrideContent = "override-config"u8.ToArray();
        string index = $$"""
        {
          "formatVersion": 1,
          "game": "minecraft",
          "versionId": "1.20.4",
          "name": "Test Pack",
          "dependencies": { "minecraft": "1.20.4" },
          "files": [
            { "path": "mods/a.jar", "hashes": { "sha1": "{{Sha1Hex(modContent)}}" },
              "downloads": ["http://localhost/dl/a.jar"], "fileSize": {{modContent.Length}} },
            { "path": "mods/server-only.jar", "env": { "client": "unsupported" },
              "downloads": ["http://localhost/dl/s.jar"], "fileSize": 10 }
          ]
        }
        """;
        string mrpackPath = Path.Combine(_root, "test.mrpack");
        await File.WriteAllBytesAsync(mrpackPath, BuildZip(new Dictionary<string, byte[]>
        {
            ["modrinth.index.json"] = Encoding.UTF8.GetBytes(index),
            ["overrides/config/test.txt"] = overrideContent
        }));

        var handler = new StubHandler
        {
            Responder = request =>
                request.RequestUri!.AbsolutePath.EndsWith("a.jar", StringComparison.OrdinalIgnoreCase)
                    ? Bytes(modContent)
                    : new HttpResponseMessage(HttpStatusCode.NotFound)
        };
        (ModpackService service, string gameDir) = CreateService(handler);
        WriteLocalVersion(gameDir, "1.20.4");

        var result = await service.ImportMrpackAsync(
            mrpackPath, gameDir, "1.20.4", versionIsolation: false, threads: 2);

        Assert.Equal(1, result.ImportedFiles);
        Assert.Equal(1, result.OverrideFiles);
        Assert.Equal(modContent, await File.ReadAllBytesAsync(Path.Combine(gameDir, "mods", "a.jar")));
        Assert.False(File.Exists(Path.Combine(gameDir, "mods", "server-only.jar")));
        Assert.Equal(overrideContent, await File.ReadAllBytesAsync(Path.Combine(gameDir, "config", "test.txt")));
        Assert.Empty(result.Warnings);
    }

    [Fact]
    public async Task Import_NonModsFiles_AlsoDownloaded()
    {
        byte[] shader = "shader-zip"u8.ToArray();
        byte[] openloader = "openloader-zip"u8.ToArray();
        string index = $$"""
        {
          "formatVersion": 1,
          "game": "minecraft",
          "versionId": "1.20.4",
          "name": "Pack With Assets",
          "dependencies": { "minecraft": "1.20.4" },
          "files": [
            { "path": "mods/a.jar", "hashes": { "sha1": "{{Sha1Hex("m"u8.ToArray())}}" },
              "downloads": ["http://localhost/dl/a.jar"], "fileSize": 1 },
            { "path": "shaderpacks/TestShader.zip", "hashes": { "sha1": "{{Sha1Hex(shader)}}" },
              "downloads": ["http://localhost/dl/shader.zip"], "fileSize": {{shader.Length}} },
            { "path": "config/openloader/resources/Data.zip", "hashes": { "sha1": "{{Sha1Hex(openloader)}}" },
              "downloads": ["http://localhost/dl/data.zip"], "fileSize": {{openloader.Length}} }
          ]
        }
        """;
        string mrpackPath = Path.Combine(_root, "assets-pack.mrpack");
        await File.WriteAllBytesAsync(mrpackPath, BuildZip(new Dictionary<string, byte[]>
        {
            ["modrinth.index.json"] = Encoding.UTF8.GetBytes(index)
        }));

        var handler = new StubHandler
        {
            Responder = request =>
            {
                string path = request.RequestUri!.AbsolutePath;
                if (path.EndsWith("a.jar", StringComparison.OrdinalIgnoreCase))
                {
                    return Bytes("m"u8.ToArray());
                }
                if (path.EndsWith("shader.zip", StringComparison.OrdinalIgnoreCase))
                {
                    return Bytes(shader);
                }
                if (path.EndsWith("data.zip", StringComparison.OrdinalIgnoreCase))
                {
                    return Bytes(openloader);
                }
                return new HttpResponseMessage(HttpStatusCode.NotFound);
            }
        };
        (ModpackService service, string gameDir) = CreateService(handler);
        WriteLocalVersion(gameDir, "1.20.4");

        var result = await service.ImportMrpackAsync(
            mrpackPath, gameDir, "1.20.4", versionIsolation: false, threads: 2);

        Assert.Equal(3, result.ImportedFiles);
        Assert.True(File.Exists(Path.Combine(gameDir, "shaderpacks", "TestShader.zip")));
        Assert.True(File.Exists(Path.Combine(gameDir, "config", "openloader", "resources", "Data.zip")));
    }

    [Fact]
    public async Task Import_ZipSlipEntry_ThrowsAndWritesNothingOutside()
    {
        string index = """{ "formatVersion": 1, "dependencies": { "minecraft": "1.20.4" }, "files": [] }""";
        string mrpackPath = Path.Combine(_root, "evil.mrpack");
        await File.WriteAllBytesAsync(mrpackPath, BuildZip(new Dictionary<string, byte[]>
        {
            ["modrinth.index.json"] = Encoding.UTF8.GetBytes(index),
            ["overrides/../../evil.txt"] = "evil"u8.ToArray()
        }));

        var handler = new StubHandler();
        (ModpackService service, string gameDir) = CreateService(handler);
        WriteLocalVersion(gameDir, "1.20.4");

        var ex = await Assert.ThrowsAsync<InvalidDataException>(() =>
            service.ImportMrpackAsync(mrpackPath, gameDir, "1.20.4", false, 1));

        Assert.Contains("非法路径", ex.Message);
        Assert.False(File.Exists(Path.Combine(_root, "evil.txt")));
        Assert.False(File.Exists(Path.Combine(gameDir, "evil.txt")));
    }

    [Fact]
    public async Task Import_MissingIndex_Throws()
    {
        string mrpackPath = Path.Combine(_root, "noindex.mrpack");
        await File.WriteAllBytesAsync(mrpackPath, BuildZip(new Dictionary<string, byte[]>
        {
            ["overrides/config/x.txt"] = "x"u8.ToArray()
        }));

        var handler = new StubHandler();
        (ModpackService service, string gameDir) = CreateService(handler);

        var ex = await Assert.ThrowsAsync<InvalidDataException>(() =>
            service.ImportMrpackAsync(mrpackPath, gameDir, "1.20.4", false, 1));

        Assert.Contains("modrinth.index.json", ex.Message);
    }

    [Fact]
    public async Task Import_VanillaTargetWithFabricDependency_Warns()
    {
        string index = """
        {
          "formatVersion": 1,
          "dependencies": { "minecraft": "1.20.4", "fabric": "0.15.0" },
          "files": []
        }
        """;
        string mrpackPath = Path.Combine(_root, "fabric-pack.mrpack");
        await File.WriteAllBytesAsync(mrpackPath, BuildZip(new Dictionary<string, byte[]>
        {
            ["modrinth.index.json"] = Encoding.UTF8.GetBytes(index)
        }));

        var handler = new StubHandler();
        (ModpackService service, string gameDir) = CreateService(handler);
        WriteLocalVersion(gameDir, "1.20.4");

        var result = await service.ImportMrpackAsync(
            mrpackPath, gameDir, "1.20.4", versionIsolation: false, threads: 1);

        Assert.Contains(result.Warnings, w => w.Contains("fabric"));
    }

    [Fact]
    public async Task Import_MissingDownloadUrl_Throws()
    {
        string index = """
        {
          "formatVersion": 1,
          "dependencies": { "minecraft": "1.20.4" },
          "files": [ { "path": "mods/nourl.jar", "fileSize": 1 } ]
        }
        """;
        string mrpackPath = Path.Combine(_root, "nourl.mrpack");
        await File.WriteAllBytesAsync(mrpackPath, BuildZip(new Dictionary<string, byte[]>
        {
            ["modrinth.index.json"] = Encoding.UTF8.GetBytes(index)
        }));

        var handler = new StubHandler();
        (ModpackService service, string gameDir) = CreateService(handler);
        WriteLocalVersion(gameDir, "1.20.4");

        var ex = await Assert.ThrowsAsync<InvalidDataException>(() =>
            service.ImportMrpackAsync(mrpackPath, gameDir, "1.20.4", false, 1));

        Assert.Contains("下载地址", ex.Message);
    }

    [Fact]
    public async Task ImportCurseForge_NoApiKey_AppliesOverridesAndWritesMissingList()
    {
        const string manifest = """
        {
          "minecraft": { "version": "1.20.4", "modLoaders": [ { "id": "forge-49.2.13", "primary": true } ] },
          "manifestType": "minecraftModpack",
          "manifestVersion": 1,
          "name": "CF Test Pack",
          "version": "1.0",
          "author": "tester",
          "files": [ { "projectID": 111, "fileID": 222, "required": true } ],
          "overrides": "overrides"
        }
        """;
        byte[] overrideContent = "cf-override"u8.ToArray();
        string zipPath = Path.Combine(_root, "cf-pack.zip");
        await File.WriteAllBytesAsync(zipPath, BuildZip(new Dictionary<string, byte[]>
        {
            ["manifest.json"] = Encoding.UTF8.GetBytes(manifest),
            ["overrides/config/x.txt"] = overrideContent
        }));

        var handler = new StubHandler();
        (ModpackService service, string gameDir) = CreateService(handler);
        WriteLocalVersion(gameDir, "1.20.4");

        var result = await service.ImportCurseForgeAsync(
            zipPath, gameDir, "1.20.4", versionIsolation: false, apiKey: null, threads: 1);

        Assert.Equal(0, result.ImportedFiles);
        Assert.Equal(1, result.OverrideFiles);
        Assert.Equal(overrideContent, await File.ReadAllBytesAsync(Path.Combine(gameDir, "config", "x.txt")));
        string list = await File.ReadAllTextAsync(Path.Combine(gameDir, "mods", "缺失模组清单.txt"));
        Assert.Contains("111,222", list);
        Assert.Contains(result.Warnings, w => w.Contains("API Key"));
    }

    [Fact]
    public async Task ImportCurseForge_WithApiKey_DownloadsFilesViaApi()
    {
        const string manifest = """
        {
          "minecraft": { "version": "1.20.4", "modLoaders": [ { "id": "forge-49.2.13", "primary": true } ] },
          "manifestType": "minecraftModpack",
          "manifestVersion": 1,
          "name": "CF Test Pack",
          "files": [ { "projectID": 111, "fileID": 222, "required": true } ],
          "overrides": "overrides"
        }
        """;
        string zipPath = Path.Combine(_root, "cf-pack.zip");
        await File.WriteAllBytesAsync(zipPath, BuildZip(new Dictionary<string, byte[]>
        {
            ["manifest.json"] = Encoding.UTF8.GetBytes(manifest)
        }));

        byte[] jarContent = "cf-mod-jar"u8.ToArray();
        var handler = new StubHandler
        {
            Responder = request =>
            {
                if (request.RequestUri!.AbsolutePath.Contains("download-url", StringComparison.OrdinalIgnoreCase))
                {
                    Assert.Contains("x-api-key", request.Headers.Select(h => h.Key));
                    return Bytes(Encoding.UTF8.GetBytes("""{ "data": "http://localhost/cdn/example-mod.jar" }"""));
                }
                if (request.RequestUri.AbsolutePath.EndsWith("example-mod.jar", StringComparison.OrdinalIgnoreCase))
                {
                    return Bytes(jarContent);
                }
                return new HttpResponseMessage(HttpStatusCode.NotFound);
            }
        };
        (ModpackService service, string gameDir) = CreateService(handler);
        WriteLocalVersion(gameDir, "1.20.4");

        var result = await service.ImportCurseForgeAsync(
            zipPath, gameDir, "1.20.4", versionIsolation: false, apiKey: "test-key", threads: 1);

        Assert.Equal(1, result.ImportedFiles);
        Assert.Equal(jarContent, await File.ReadAllBytesAsync(Path.Combine(gameDir, "mods", "example-mod.jar")));
    }

    [Fact]
    public async Task ImportCurseForge_MissingManifest_Throws()
    {
        string zipPath = Path.Combine(_root, "no-manifest.zip");
        await File.WriteAllBytesAsync(zipPath, BuildZip(new Dictionary<string, byte[]>
        {
            ["overrides/config/x.txt"] = "x"u8.ToArray()
        }));

        var handler = new StubHandler();
        (ModpackService service, string gameDir) = CreateService(handler);

        var ex = await Assert.ThrowsAsync<InvalidDataException>(() =>
            service.ImportCurseForgeAsync(zipPath, gameDir, "1.20.4", false, null, 1));

        Assert.Contains("manifest.json", ex.Message);
    }

    [Fact]
    public async Task ImportCurseForge_VanillaTargetWithLoader_Warns()
    {
        const string manifest = """
        {
          "minecraft": { "version": "1.20.4", "modLoaders": [ { "id": "forge-49.2.13", "primary": true } ] },
          "manifestType": "minecraftModpack",
          "manifestVersion": 1,
          "files": [],
          "overrides": "overrides"
        }
        """;
        string zipPath = Path.Combine(_root, "cf-pack.zip");
        await File.WriteAllBytesAsync(zipPath, BuildZip(new Dictionary<string, byte[]>
        {
            ["manifest.json"] = Encoding.UTF8.GetBytes(manifest)
        }));

        var handler = new StubHandler();
        (ModpackService service, string gameDir) = CreateService(handler);
        WriteLocalVersion(gameDir, "1.20.4");

        var result = await service.ImportCurseForgeAsync(
            zipPath, gameDir, "1.20.4", versionIsolation: false, apiKey: null, threads: 1);

        Assert.Contains(result.Warnings, w => w.Contains("forge"));
    }

    // ───────── SDLauncherPack 导出/导入 ─────────

    [Fact]
    public async Task ExportInstance_Roundtrip_RestoresModsConfigAndStates()
    {
        string gameDir = Path.Combine(_root, "mc");
        string modsDir = Path.Combine(gameDir, "mods");
        string configDir = Path.Combine(gameDir, "config");
        Directory.CreateDirectory(modsDir);
        Directory.CreateDirectory(configDir);
        await File.WriteAllBytesAsync(Path.Combine(modsDir, "sodium.jar"), "sodium"u8.ToArray());
        await File.WriteAllBytesAsync(Path.Combine(modsDir, "iris.jar.disabled"), "iris"u8.ToArray());
        await File.WriteAllTextAsync(Path.Combine(configDir, "test.conf"), "key=value");
        await File.WriteAllTextAsync(Path.Combine(gameDir, "options.txt"), "fov:70");
        WriteLocalVersion(gameDir, "1.20.4");

        var handler = new StubHandler();
        (ModpackService service, string _) = CreateService(handler);
        string packPath = Path.Combine(_root, "export.zip");

        ModpackImportResult exported = await service.ExportInstanceAsync(
            gameDir, "1.20.4", versionIsolation: false, packPath);

        Assert.Equal(2, exported.ImportedFiles);
        Assert.Equal(1, exported.OverrideFiles);
        Assert.Equal(PackFileType.SdlauncherPack, ModpackService.DetectZipType(packPath));

        // 导入到全新目录。
        string restoreDir = Path.Combine(_root, "restore");
        ModpackImportResult imported = await service.ImportSdlauncherPackAsync(
            packPath, restoreDir, "1.20.4", versionIsolation: false);

        Assert.Equal(2, imported.ImportedFiles);
        Assert.Equal(1, imported.OverrideFiles);
        Assert.True(File.Exists(Path.Combine(restoreDir, "mods", "sodium.jar")));
        // 禁用状态恢复为 .disabled。
        Assert.True(File.Exists(Path.Combine(restoreDir, "mods", "iris.jar.disabled")));
        Assert.False(File.Exists(Path.Combine(restoreDir, "mods", "iris.jar")));
        Assert.True(File.Exists(Path.Combine(restoreDir, "config", "test.conf")));
        Assert.True(File.Exists(Path.Combine(restoreDir, "options.txt")));
    }

    [Fact]
    public void DetectZipType_MrpackAndCurseForgeAndSdl()
    {
        string mrpack = Path.Combine(_root, "a.mrpack");
        File.WriteAllBytes(mrpack, BuildZip(new Dictionary<string, byte[]>
        {
            ["modrinth.index.json"] = "{}"u8.ToArray()
        }));
        Assert.Equal(PackFileType.ModrinthMrpack, ModpackService.DetectZipType(mrpack));

        string cfZip = Path.Combine(_root, "b.zip");
        File.WriteAllBytes(cfZip, BuildZip(new Dictionary<string, byte[]>
        {
            ["manifest.json"] = "{}"u8.ToArray()
        }));
        Assert.Equal(PackFileType.CurseForge, ModpackService.DetectZipType(cfZip));

        string sdlZip = Path.Combine(_root, "c.zip");
        File.WriteAllBytes(sdlZip, BuildZip(new Dictionary<string, byte[]>
        {
            [ModpackService.SdlManifestEntry] = "{}"u8.ToArray()
        }));
        Assert.Equal(PackFileType.SdlauncherPack, ModpackService.DetectZipType(sdlZip));

        string junkZip = Path.Combine(_root, "d.zip");
        File.WriteAllBytes(junkZip, BuildZip(new Dictionary<string, byte[]>
        {
            ["readme.txt"] = "x"u8.ToArray()
        }));
        Assert.Equal(PackFileType.Unknown, ModpackService.DetectZipType(junkZip));
    }

    [Fact]
    public async Task ImportSdlauncherPack_ZipSlip_Throws()
    {
        string packPath = Path.Combine(_root, "evil.zip");
        await File.WriteAllBytesAsync(packPath, BuildZip(new Dictionary<string, byte[]>
        {
            [ModpackService.SdlManifestEntry] = """{ "name": "x", "mods": [] }"""u8.ToArray(),
            ["mods/../../evil.txt"] = "evil"u8.ToArray()
        }));

        var handler = new StubHandler();
        (ModpackService service, string gameDir) = CreateService(handler);

        var ex = await Assert.ThrowsAsync<InvalidDataException>(() =>
            service.ImportSdlauncherPackAsync(packPath, gameDir, "1.20.4", versionIsolation: false));

        Assert.Contains("非法路径", ex.Message);
        Assert.False(File.Exists(Path.Combine(_root, "evil.txt")));
    }

    [Fact]
    public async Task ImportSdlauncherPack_MissingManifest_Throws()
    {
        string packPath = Path.Combine(_root, "nomanifest.zip");
        await File.WriteAllBytesAsync(packPath, BuildZip(new Dictionary<string, byte[]>
        {
            ["mods/x.jar"] = "x"u8.ToArray()
        }));

        var handler = new StubHandler();
        (ModpackService service, string gameDir) = CreateService(handler);

        var ex = await Assert.ThrowsAsync<InvalidDataException>(() =>
            service.ImportSdlauncherPackAsync(packPath, gameDir, "1.20.4", versionIsolation: false));

        Assert.Contains("sdlauncher-pack.json", ex.Message);
    }
}
