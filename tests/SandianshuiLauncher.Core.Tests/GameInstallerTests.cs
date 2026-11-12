using System.Net;
using System.Security.Cryptography;
using System.Text;
using SandianshuiLauncher.Core.Models;
using SandianshuiLauncher.Core.Services;
using Xunit;

namespace SandianshuiLauncher.Core.Tests;

/// <summary>
/// <see cref="GameInstaller.EnsureVersionJsonAsync"/> 的版本 JSON 校验测试：
/// 验证本地损坏时重新下载、下载内容 SHA1 校验失败重试并报错、清单无 SHA1 时兼容。
/// </summary>
public class GameInstallerTests : IDisposable
{
    private readonly string _root;

    public GameInstallerTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "sdsl_installer_" + Guid.NewGuid().ToString("N"));
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

    private sealed class StubHandler : HttpMessageHandler
    {
        public Func<HttpRequestMessage, byte[]> Responder { get; set; } = _ => "{}"u8.ToArray();
        public int RequestCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestCount++;
            byte[] content = Responder(request);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(content)
            });
        }
    }

    private static string Sha1Hex(byte[] data)
        => Convert.ToHexString(SHA1.HashData(data)).ToLowerInvariant();

    private static ManifestEntry MakeEntry(string id, string? sha1)
        => new()
        {
            Id = id,
            Type = "release",
            Url = $"http://localhost/versions/{id}.json",
            Sha1 = sha1
        };

    private (GameInstaller Installer, StubHandler Handler) CreateInstaller(StubHandler handler)
    {
        var http = new HttpClient(handler);
        var provider = new DownloadSourceProvider(new LauncherConfig());
        var downloads = new DownloadService(http);
        return (new GameInstaller(http, provider, downloads), handler);
    }

    [Fact]
    public async Task EnsureVersionJsonAsync_DownloadsAndVerifies_Success()
    {
        byte[] content = Encoding.UTF8.GetBytes("{\"id\":\"1.0\"}");
        var handler = new StubHandler { Responder = _ => content };
        var (installer, _) = CreateInstaller(handler);
        var manifest = new VersionManifest();
        var entry = MakeEntry("1.0", Sha1Hex(content));

        await installer.EnsureVersionJsonAsync(entry, manifest, _root, CancellationToken.None);

        string jsonPath = Path.Combine(_root, "versions", "1.0", "1.0.json");
        Assert.True(File.Exists(jsonPath));
        Assert.Equal(content, await File.ReadAllBytesAsync(jsonPath));
        Assert.Equal(1, handler.RequestCount);
    }

    [Fact]
    public async Task EnsureVersionJsonAsync_ExistingCorruptFile_Redownloads()
    {
        byte[] content = Encoding.UTF8.GetBytes("{\"id\":\"1.0\"}");
        string jsonPath = Path.Combine(_root, "versions", "1.0", "1.0.json");
        Directory.CreateDirectory(Path.GetDirectoryName(jsonPath)!);
        await File.WriteAllTextAsync(jsonPath, "{\"id\":\"broken\""); // 损坏的 JSON

        var handler = new StubHandler { Responder = _ => content };
        var (installer, _) = CreateInstaller(handler);
        var manifest = new VersionManifest();
        var entry = MakeEntry("1.0", Sha1Hex(content));

        await installer.EnsureVersionJsonAsync(entry, manifest, _root, CancellationToken.None);

        Assert.Equal(content, await File.ReadAllBytesAsync(jsonPath));
        Assert.Equal(1, handler.RequestCount);
    }

    [Fact]
    public async Task EnsureVersionJsonAsync_MismatchedSha1_ThrowsAndDeletesFile()
    {
        byte[] content = Encoding.UTF8.GetBytes("{\"id\":\"1.0\"}");
        string jsonPath = Path.Combine(_root, "versions", "1.0", "1.0.json");

        // 服务器持续返回“正确内容”，但清单声明的 SHA1 与之不符 → 两次尝试后必须报错。
        var handler = new StubHandler { Responder = _ => content };
        var (installer, _) = CreateInstaller(handler);
        var manifest = new VersionManifest();
        var entry = MakeEntry("1.0", new string('0', 40)); // 错误的预期 SHA1

        var ex = await Assert.ThrowsAsync<InvalidDataException>(() =>
            installer.EnsureVersionJsonAsync(entry, manifest, _root, CancellationToken.None));

        Assert.Contains("SHA1", ex.Message);
        Assert.Equal(2, handler.RequestCount);
        Assert.False(File.Exists(jsonPath)); // 校验失败的文件不得残留
    }

    [Fact]
    public async Task EnsureVersionJsonAsync_NoSha1InManifest_SkipsVerification()
    {
        byte[] content = Encoding.UTF8.GetBytes("{\"id\":\"1.0\"}");
        var handler = new StubHandler { Responder = _ => content };
        var (installer, _) = CreateInstaller(handler);
        var manifest = new VersionManifest();
        var entry = MakeEntry("1.0", sha1: null);

        await installer.EnsureVersionJsonAsync(entry, manifest, _root, CancellationToken.None);

        string jsonPath = Path.Combine(_root, "versions", "1.0", "1.0.json");
        Assert.True(File.Exists(jsonPath));
        Assert.Equal(1, handler.RequestCount);
    }

    [Fact]
    public async Task EnsureVersionJsonAsync_ExistingValidFile_NoRequest()
    {
        byte[] content = Encoding.UTF8.GetBytes("{\"id\":\"1.0\"}");
        string jsonPath = Path.Combine(_root, "versions", "1.0", "1.0.json");
        Directory.CreateDirectory(Path.GetDirectoryName(jsonPath)!);
        await File.WriteAllBytesAsync(jsonPath, content);

        var handler = new StubHandler { Responder = _ => content };
        var (installer, _) = CreateInstaller(handler);
        var manifest = new VersionManifest();
        var entry = MakeEntry("1.0", Sha1Hex(content));

        await installer.EnsureVersionJsonAsync(entry, manifest, _root, CancellationToken.None);

        Assert.Equal(0, handler.RequestCount);
    }
}
