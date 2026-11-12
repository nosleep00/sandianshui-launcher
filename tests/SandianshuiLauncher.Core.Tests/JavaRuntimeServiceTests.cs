using System.IO.Compression;
using System.Net;
using System.Text;
using SandianshuiLauncher.Core.Models;
using SandianshuiLauncher.Core.Services;

namespace SandianshuiLauncher.Core.Tests;

/// <summary>
/// 验证「没有本机 Java 时自动下载运行时」的核心逻辑：已安装短路、多源回退、
/// 校验/解压失败的中文提示，以及两个下载源的解析规则。
/// </summary>
public sealed class JavaRuntimeServiceTests : IDisposable
{
    private readonly string _dataDir = Path.Combine(
        Path.GetTempPath(), "sds-java-" + Guid.NewGuid().ToString("N"));

    public JavaRuntimeServiceTests() => Directory.CreateDirectory(_dataDir);

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_dataDir))
            {
                Directory.Delete(_dataDir, recursive: true);
            }
        }
        catch
        {
            // 临时目录清理失败不影响测试结果。
        }
    }

    [Fact]
    public async Task EnsureAsync_WhenRuntimeInstalled_ReturnsItWithoutDownloading()
    {
        string exe = CreateFakeJavaExe(17);
        var source = new RecordingSource("官方", _ => null);
        var service = CreateService(HttpStatusCode.NotFound, [source]);

        JavaInfo info = await service.EnsureAsync(17, _dataDir);

        Assert.Equal(exe, info.Path);
        Assert.Equal(17, info.MajorVersion);
        Assert.Equal(0, source.Calls);
    }

    [Fact]
    public async Task EnsureAsync_FirstSourceUnavailable_FallsBackToSecondSource()
    {
        byte[] zip = BuildJreZip("jdk-17.0.21+9-jre");
        var unavailable = new RecordingSource("官方", _ => null);
        var mirror = new RecordingSource("清华镜像", _ => new JavaRuntimePackage(
            "https://mirror.example/jre.zip",
            "OpenJDK17U-jre_x64_windows_hotspot_17.0.21_9.zip",
            Sha256: null,
            Size: zip.Length,
            Version: "17.0.21+9"));
        var service = CreateService(HttpStatusCode.OK, [unavailable, mirror], zip);

        JavaInfo info = await service.EnsureAsync(17, _dataDir);

        Assert.Equal(17, info.MajorVersion);
        Assert.Equal("17.0.21", info.Version);
        Assert.True(File.Exists(Path.Combine(
            _dataDir, "java", "temurin-17", "jdk-17.0.21+9-jre", "bin", "java.exe")));
        Assert.Equal(1, unavailable.Calls);
        Assert.Equal(1, mirror.Calls);
    }

    [Fact]
    public async Task EnsureAsync_ChecksumMismatch_ReportsSha256Failure()
    {
        byte[] zip = BuildJreZip("jdk-17.0.21+9-jre");
        string wrongChecksum = new('a', 64);
        var source = new RecordingSource("官方", _ => new JavaRuntimePackage(
            "https://official.example/jre.zip", "jre.zip", wrongChecksum, zip.Length, "17.0.21"));
        var service = CreateService(HttpStatusCode.OK, [source], zip);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.EnsureAsync(17, _dataDir));

        Assert.Contains("SHA256", ex.Message, StringComparison.Ordinal);
        Assert.Contains("官方", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task EnsureAsync_PackageWithoutJavaExe_ReportsMissingExe()
    {
        byte[] zip = BuildZip(("readme.txt", "no java here"));
        var source = new RecordingSource("官方", _ => new JavaRuntimePackage(
            "https://official.example/jre.zip", "jre.zip", Sha256: null, Size: zip.Length, Version: "17.0.21"));
        var service = CreateService(HttpStatusCode.OK, [source], zip);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.EnsureAsync(17, _dataDir));

        Assert.Contains("java.exe", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task EnsureAsync_AllSourcesEmpty_ReportsEverySource()
    {
        var first = new RecordingSource("官方", _ => null);
        var second = new RecordingSource("清华镜像", _ => null);
        var service = CreateService(HttpStatusCode.NotFound, [first, second]);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.EnsureAsync(17, _dataDir));

        Assert.Contains("未能在任何下载源", ex.Message, StringComparison.Ordinal);
        Assert.Contains("官方", ex.Message, StringComparison.Ordinal);
        Assert.Contains("清华镜像", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task EnsureAsync_UnsupportedMajor_IsRejected()
    {
        var service = CreateService(HttpStatusCode.NotFound, []);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => service.EnsureAsync(7, _dataDir));
    }

    [Fact]
    public async Task AdoptiumApiSource_ParsesPackageUrlNameChecksumAndVersion()
    {
        const string json = """
            [
              {
                "binary": {
                  "package": {
                    "link": "https://github.com/adoptium/jre.zip",
                    "name": "OpenJDK17U-jre_x64_windows_hotspot_17.0.21_9.zip",
                    "checksum": "abcdef",
                    "size": 43816789
                  }
                },
                "version": { "semver": "17.0.21+9" }
              }
            ]
            """;
        var source = new AdoptiumApiSource(
            new HttpClient(new StubHandler(_ => JsonResponse(json))),
            "https://api.example");

        JavaRuntimePackage? package = await source.FindLatestAsync(17);

        Assert.NotNull(package);
        Assert.Equal("https://github.com/adoptium/jre.zip", package.Url);
        Assert.Equal("OpenJDK17U-jre_x64_windows_hotspot_17.0.21_9.zip", package.FileName);
        Assert.Equal("abcdef", package.Sha256);
        Assert.Equal(43816789, package.Size);
        Assert.Equal("17.0.21+9", package.Version);
        Assert.Contains("image_type=jre", source.BuildQueryUrl(17), StringComparison.Ordinal);
        Assert.Contains("/v3/assets/latest/17/hotspot", source.BuildQueryUrl(17), StringComparison.Ordinal);
    }

    [Fact]
    public async Task AdoptiumApiSource_NonSuccessStatus_ReturnsNull()
    {
        var source = new AdoptiumApiSource(
            new HttpClient(new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable))),
            "https://api.example");

        Assert.Null(await source.FindLatestAsync(17));
    }

    [Fact]
    public void AdoptiumMirrorSource_PicksHighestVersionNumerically()
    {
        const string html = """
            <a href="OpenJDK17U-jre_x64_windows_hotspot_17.0.9_9.zip">17.0.9_9</a>
            <a href="OpenJDK17U-jre_x64_windows_hotspot_17.0.21_9.zip">17.0.21_9</a>
            <a href="OpenJDK17U-jre_x64_windows_hotspot_17.0.12_7.zip">17.0.12_7</a>
            <a href="OpenJDK21U-jre_x64_windows_hotspot_21.0.5_11.zip">21</a>
            """;

        string? newest = AdoptiumMirrorSource.FindNewestFileName(html, 17);

        Assert.Equal("OpenJDK17U-jre_x64_windows_hotspot_17.0.21_9.zip", newest);
    }

    [Fact]
    public void AdoptiumMirrorSource_IndexUrl_PointsAtWindowsJre()
    {
        var source = new AdoptiumMirrorSource(
            new HttpClient(new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound))),
            "https://mirror.example/Adoptium");

        Assert.Equal(
            "https://mirror.example/Adoptium/21/jre/x64/windows/",
            source.BuildIndexUrl(21));
    }

    [Fact]
    public void EnumerateJavaExe_FindsOnlyFilesInsideBinDirectory()
    {
        string jre = Path.Combine(_dataDir, "vendored", "jdk-17-jre");
        string bin = Path.Combine(jre, "bin");
        Directory.CreateDirectory(bin);
        File.WriteAllText(Path.Combine(bin, "java.exe"), string.Empty);
        File.WriteAllText(Path.Combine(jre, "java.exe"), string.Empty);

        var found = JavaRuntimeService.EnumerateJavaExe(_dataDir).ToList();

        Assert.Single(found, path => Path.GetFileName(Path.GetDirectoryName(path)) == "bin");
    }

    private JavaRuntimeService CreateService(
        HttpStatusCode status,
        IReadOnlyList<IJavaRuntimeSource> sources,
        byte[]? zip = null)
    {
        var handler = new StubHandler(_ => zip is null
            ? new HttpResponseMessage(status)
            : new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(zip)
            });
        var probe = (string path) => File.Exists(path)
            ? new JavaInfo
            {
                Path = path,
                MajorVersion = MajorOf(path),
                Version = MajorOf(path) == 17 ? "17.0.21" : "21.0.5",
                Is64Bit = true
            }
            : null;
        return new JavaRuntimeService(new HttpClient(handler), sources, probe);
    }

    private string CreateFakeJavaExe(int major)
    {
        string dir = Path.Combine(_dataDir, "java", $"temurin-{major}", $"jdk-{major}.0.21+9-jre", "bin");
        Directory.CreateDirectory(dir);
        string exe = Path.Combine(dir, "java.exe");
        File.WriteAllText(exe, string.Empty);
        return exe;
    }

    private static int MajorOf(string path)
        => path.Contains("temurin-21", StringComparison.OrdinalIgnoreCase) ? 21 : 17;

    private static byte[] BuildJreZip(string topLevelFolder)
        => BuildZip(($"{topLevelFolder}/bin/java.exe", string.Empty));

    private static byte[] BuildZip(params (string Name, string Content)[] entries)
    {
        using var buffer = new MemoryStream();
        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach ((string name, string content) in entries)
            {
                ZipArchiveEntry entry = archive.CreateEntry(name);
                using Stream stream = entry.Open();
                byte[] bytes = Encoding.UTF8.GetBytes(content);
                stream.Write(bytes, 0, bytes.Length);
            }
        }
        return buffer.ToArray();
    }

    private static HttpResponseMessage JsonResponse(string json)
        => new(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    /// <summary>可记录调用次数的下载源；factory 返回 null 表示「该源没有这个版本」。</summary>
    private sealed class RecordingSource(string displayName, Func<int, JavaRuntimePackage?> factory)
        : IJavaRuntimeSource
    {
        public string DisplayName { get; } = displayName;

        public int Calls { get; private set; }

        public Task<JavaRuntimePackage?> FindLatestAsync(
            int majorVersion, CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.FromResult(factory(majorVersion));
        }
    }

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(respond(request));
    }
}
