using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using SandianshuiLauncher.Core.Models;
using SandianshuiLauncher.Core.Utils;

namespace SandianshuiLauncher.Core.Services;

/// <summary>
/// 一个 Java 运行时下载源：负责「给定主版本号，找到该版本最新的 JRE 安装包」。
/// 多个源按顺序尝试，前一个失败或找不到时自动退到下一个。
/// </summary>
public interface IJavaRuntimeSource
{
    /// <summary>来源名称（用于界面与日志，如「Adoptium 官方」「清华镜像」）。</summary>
    string DisplayName { get; }

    /// <summary>查找指定主版本的最新 64 位 Windows JRE；找不到返回 null。</summary>
    Task<JavaRuntimePackage?> FindLatestAsync(int majorVersion, CancellationToken cancellationToken = default);
}

/// <summary>Java 运行时的下载与安装（不依赖本机已装 Java）。</summary>
public interface IJavaRuntimeService
{
    /// <summary>在启动器数据目录里查找已下载好的指定主版本 Java；没有则返回 null。</summary>
    Task<JavaInfo?> FindInstalledAsync(int majorVersion, string dataDirectory, CancellationToken cancellationToken = default);

    /// <summary>
    /// 确保数据目录里存在可用的指定主版本 Java：已存在则直接返回，否则从下载源下载并解压。
    /// </summary>
    Task<JavaInfo> EnsureAsync(
        int majorVersion,
        string dataDirectory,
        IProgress<JavaRuntimeProgress>? progress = null,
        Action<string>? onStatus = null,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Adoptium（Temurin）官方 API 下载源：一次请求即可拿到最新 JRE 的直链、大小与 SHA256。
/// </summary>
public sealed class AdoptiumApiSource : IJavaRuntimeSource
{
    private readonly HttpClient _http;
    private readonly string _baseUrl;

    public AdoptiumApiSource(HttpClient http, string baseUrl = "https://api.adoptium.net")
    {
        _http = http;
        _baseUrl = baseUrl.TrimEnd('/');
    }

    public string DisplayName => "Adoptium 官方";

    /// <summary>构造某主版本 JRE（Windows x64）的查询地址。</summary>
    public string BuildQueryUrl(int majorVersion)
        => $"{_baseUrl}/v3/assets/latest/{majorVersion}/hotspot?architecture=x64&image_type=jre&os=windows&vendor=eclipse";

    public async Task<JavaRuntimePackage?> FindLatestAsync(int majorVersion, CancellationToken cancellationToken = default)
    {
        using HttpResponseMessage response = await _http.GetAsync(BuildQueryUrl(majorVersion), cancellationToken)
            .ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        string json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        JsonNode? root = JsonNode.Parse(json);
        if (root is not JsonArray assets || assets.Count == 0)
        {
            return null;
        }

        JsonNode? asset = assets[0]?["binary"]?["package"];
        string? link = asset?["link"]?.GetValue<string>();
        string? name = asset?["name"]?.GetValue<string>();
        if (string.IsNullOrWhiteSpace(link) || string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        string? checksum = asset?["checksum"]?.GetValue<string>();
        long size = asset?["size"]?.GetValue<long>() ?? 0;
        string version = assets[0]?["version"]?["semver"]?.GetValue<string>()
                         ?? assets[0]?["release_name"]?.GetValue<string>()
                         ?? majorVersion.ToString();
        return new JavaRuntimePackage(link, name, checksum, size, version);
    }
}

/// <summary>
/// 镜像站下载源（默认清华 TUNA 的 Adoptium 镜像）：解析目录页，取该主版本最新的 JRE 压缩包。
/// 镜像不提供校验文件，因此 <see cref="JavaRuntimePackage.Sha256"/> 为空，安装时改为「能解压且能运行」双重验证。
/// </summary>
public sealed class AdoptiumMirrorSource : IJavaRuntimeSource
{
    private readonly HttpClient _http;
    private readonly string _baseUrl;

    public AdoptiumMirrorSource(HttpClient http, string baseUrl = "https://mirrors.tuna.tsinghua.edu.cn/Adoptium")
    {
        _http = http;
        _baseUrl = baseUrl.TrimEnd('/');
    }

    public string DisplayName => "清华镜像";

    /// <summary>构造某主版本 JRE（Windows x64）的目录页地址。</summary>
    public string BuildIndexUrl(int majorVersion) => $"{_baseUrl}/{majorVersion}/jre/x64/windows/";

    public async Task<JavaRuntimePackage?> FindLatestAsync(int majorVersion, CancellationToken cancellationToken = default)
    {
        string indexUrl = BuildIndexUrl(majorVersion);
        using HttpResponseMessage response = await _http.GetAsync(indexUrl, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        string html = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        string? newest = FindNewestFileName(html, majorVersion);
        if (newest is null)
        {
            return null;
        }

        string version = ExtractVersion(newest) ?? majorVersion.ToString();
        return new JavaRuntimePackage(indexUrl + newest, newest, Sha256: null, Size: 0, version);
    }

    /// <summary>从目录页 HTML 中挑出该主版本最新的 JRE 压缩包文件名（公开以便测试）。</summary>
    public static string? FindNewestFileName(string html, int majorVersion)
    {
        var pattern = new Regex(
            $@"OpenJDK{majorVersion}U-jre_x64_windows_hotspot_(?<ver>[0-9]+(?:\.[0-9]+)*(?:_[0-9]+)?)\.zip",
            RegexOptions.IgnoreCase);
        return pattern.Matches(html)
            .Select(m => m.Value)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(name => VersionKey(ExtractVersion(name) ?? string.Empty))
            .ThenByDescending(name => name, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
    }

    private static string? ExtractVersion(string fileName)
    {
        Match match = Regex.Match(
            fileName,
            @"hotspot_(?<ver>[0-9]+(?:\.[0-9]+)*(?:_[0-9]+)?)\.zip",
            RegexOptions.IgnoreCase);
        return match.Success ? match.Groups["ver"].Value : null;
    }

    /// <summary>把 17.0.21_9 这类版本串转成可比较的整数键，避免字符串排序把 17.0.9 排在 17.0.21 之后。</summary>
    private static long VersionKey(string version)
    {
        long key = 0;
        foreach (string part in version.Split(['.', '_'], StringSplitOptions.RemoveEmptyEntries))
        {
            if (!int.TryParse(part, out int value))
            {
                continue;
            }
            key = (key * 1000) + Math.Min(value, 999);
        }
        return key;
    }
}

/// <summary>
/// 下载并安装 Java 运行时到 <c>{数据目录}/java/temurin-{主版本}</c>，供没有 Java 的机器直接玩。
/// 下载源按顺序尝试（官方 API → 镜像），全部失败时给出中文错误。
/// </summary>
public class JavaRuntimeService : IJavaRuntimeService
{
    private const int DownloadAttempts = 3;

    private readonly HttpClient _http;
    private readonly IReadOnlyList<IJavaRuntimeSource> _sources;
    private readonly Func<string, JavaInfo?> _probe;

    public JavaRuntimeService(
        HttpClient http,
        IReadOnlyList<IJavaRuntimeSource>? sources = null,
        Func<string, JavaInfo?>? probe = null)
    {
        _http = http;
        _sources = sources ?? [new AdoptiumApiSource(http), new AdoptiumMirrorSource(http)];
        _probe = probe ?? JavaDetector.Probe;
    }

    /// <summary>已配置的下载源（按尝试顺序）。</summary>
    public IReadOnlyList<IJavaRuntimeSource> Sources => _sources;

    /// <summary>某主版本 Java 的安装根目录（其下是解压出来的 jdk-…-jre 目录）。</summary>
    public static string GetInstallRoot(string dataDirectory, int majorVersion)
        => Path.Combine(dataDirectory, "java", $"temurin-{majorVersion}");

    public Task<JavaInfo?> FindInstalledAsync(int majorVersion, string dataDirectory, CancellationToken cancellationToken = default)
    {
        string root = GetInstallRoot(dataDirectory, majorVersion);
        return Task.Run(() => FindInstalled(root, majorVersion, cancellationToken), cancellationToken);
    }

    private JavaInfo? FindInstalled(string root, int majorVersion, CancellationToken cancellationToken)
    {
        foreach (string javaExe in EnumerateJavaExe(root))
        {
            cancellationToken.ThrowIfCancellationRequested();
            JavaInfo? info = TryProbe(javaExe);
            if (info is not null && info.MajorVersion == majorVersion)
            {
                return info;
            }
        }
        return null;
    }

    public async Task<JavaInfo> EnsureAsync(
        int majorVersion,
        string dataDirectory,
        IProgress<JavaRuntimeProgress>? progress = null,
        Action<string>? onStatus = null,
        CancellationToken cancellationToken = default)
    {
        if (majorVersion < 8)
        {
            throw new ArgumentOutOfRangeException(nameof(majorVersion), "只支持 Java 8 及以上的运行时。");
        }

        JavaInfo? installed = await FindInstalledAsync(majorVersion, dataDirectory, cancellationToken).ConfigureAwait(false);
        if (installed is not null)
        {
            progress?.Report(new JavaRuntimeProgress("已安装", 100, $"已检测到 Java {majorVersion}"));
            return installed;
        }

        var failures = new List<string>();
        foreach (IJavaRuntimeSource source in _sources)
        {
            cancellationToken.ThrowIfCancellationRequested();
            onStatus?.Invoke($"正在从{source.DisplayName}查找 Java {majorVersion}…");
            progress?.Report(new JavaRuntimeProgress("查找下载地址", 2, source.DisplayName));

            JavaRuntimePackage? package;
            try
            {
                package = await source.FindLatestAsync(majorVersion, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                failures.Add($"{source.DisplayName}：{ex.Message}");
                continue;
            }

            if (package is null)
            {
                failures.Add($"{source.DisplayName}：没有找到 Java {majorVersion} 的安装包");
                continue;
            }

            try
            {
                JavaInfo info = await InstallAsync(majorVersion, dataDirectory, package, progress, onStatus, cancellationToken)
                    .ConfigureAwait(false);
                progress?.Report(new JavaRuntimeProgress("完成", 100, $"Java {majorVersion} 已就绪"));
                return info;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                failures.Add($"{source.DisplayName}：{ex.Message}");
            }
        }

        throw new InvalidOperationException(
            $"未能在任何下载源获取 Java {majorVersion}，请检查网络后在“设置”页手动指定 java.exe。\n" +
            string.Join("\n", failures));
    }

    /// <summary>下载、校验、解压一个安装包并返回其中 java.exe 的信息。</summary>
    private async Task<JavaInfo> InstallAsync(
        int majorVersion,
        string dataDirectory,
        JavaRuntimePackage package,
        IProgress<JavaRuntimeProgress>? progress,
        Action<string>? onStatus,
        CancellationToken cancellationToken)
    {
        string root = GetInstallRoot(dataDirectory, majorVersion);
        string workingRoot = Path.Combine(dataDirectory, "java");
        Directory.CreateDirectory(workingRoot);

        string zipPath = Path.Combine(workingRoot, $"download-{Guid.NewGuid():N}.zip");
        string tempDir = Path.Combine(workingRoot, $"unpack-{Guid.NewGuid():N}");
        try
        {
            onStatus?.Invoke($"正在下载 Java {majorVersion}（{DescribeSize(package.Size)}）…");
            await DownloadAsync(package, zipPath, majorVersion, progress, cancellationToken).ConfigureAwait(false);

            if (!string.IsNullOrWhiteSpace(package.Sha256))
            {
                onStatus?.Invoke("正在校验安装包…");
                progress?.Report(new JavaRuntimeProgress("校验", 92, package.FileName));
                string actual = HashUtils.Sha256File(zipPath);
                if (!string.Equals(actual, package.Sha256, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException("安装包 SHA256 校验失败，文件可能已损坏。");
                }
            }

            onStatus?.Invoke("正在解压 Java…");
            progress?.Report(new JavaRuntimeProgress("解压", 95, package.FileName));
            ZipFile.ExtractToDirectory(zipPath, tempDir);

            string? javaExe = EnumerateJavaExe(tempDir).FirstOrDefault();
            if (javaExe is null)
            {
                throw new InvalidOperationException("安装包里没有找到 bin\\java.exe。");
            }

            // 先清掉旧目录，再把解压结果整体搬到位（同盘 Directory.Move 是原子重命名）。
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
            Directory.CreateDirectory(Path.GetDirectoryName(root)!);
            Directory.Move(tempDir, root);

            string relative = Path.GetRelativePath(tempDir, javaExe);
            string installedExe = Path.Combine(root, relative);
            JavaInfo? info = TryProbe(installedExe);
            if (info is null)
            {
                throw new InvalidOperationException($"已下载 Java {majorVersion}，但无法运行：{installedExe}");
            }
            return info;
        }
        finally
        {
            TryDeleteFile(zipPath);
            TryDeleteDirectory(tempDir);
        }
    }

    private async Task DownloadAsync(
        JavaRuntimePackage package,
        string destination,
        int majorVersion,
        IProgress<JavaRuntimeProgress>? progress,
        CancellationToken cancellationToken)
    {
        Exception? last = null;
        for (int attempt = 1; attempt <= DownloadAttempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                using HttpResponseMessage response = await _http
                    .GetAsync(package.Url, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                    .ConfigureAwait(false);
                response.EnsureSuccessStatusCode();

                long total = response.Content.Headers.ContentLength ?? package.Size;
                await using Stream input = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
                await using (var output = new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    byte[] buffer = new byte[81920];
                    long received = 0;
                    var lastReport = 0L;
                    int read;
                    while ((read = await input.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
                    {
                        await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                        received += read;
                        // 每 ~1% 或至少每 512 KB 上报一次，避免进度回调过于频繁。
                        if (total > 0 && (received - lastReport >= total / 100 || received - lastReport >= 512 * 1024))
                        {
                            lastReport = received;
                            double percent = Math.Clamp(received * 90.0 / total, 0, 90);
                            string text = $"正在下载 Java {majorVersion}：{received / 1024.0 / 1024.0:0.0} / {total / 1024.0 / 1024.0:0.0} MB";
                            progress?.Report(new JavaRuntimeProgress("下载", percent, text));
                        }
                    }
                }
                return;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                last = ex;
                TryDeleteFile(destination);
                if (attempt < DownloadAttempts)
                {
                    await Task.Delay(TimeSpan.FromSeconds(attempt), cancellationToken).ConfigureAwait(false);
                }
            }
        }

        throw new InvalidOperationException(
            $"下载 Java {majorVersion} 失败（已重试 {DownloadAttempts} 次）：{last?.Message}", last);
    }

    private JavaInfo? TryProbe(string javaExe)
    {
        try
        {
            return File.Exists(javaExe) ? _probe(javaExe) : null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>枚举目录下所有 bin\java.exe（公开以便测试与 JavaDetector 复用）。</summary>
    public static IEnumerable<string> EnumerateJavaExe(string? root)
    {
        if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
        {
            return [];
        }
        try
        {
            return Directory
                .EnumerateFiles(root, "java.exe", SearchOption.AllDirectories)
                .Where(path => string.Equals(Path.GetFileName(Path.GetDirectoryName(path)), "bin", StringComparison.OrdinalIgnoreCase))
                .ToList();
        }
        catch
        {
            return [];
        }
    }

    private static string DescribeSize(long size)
        => size > 0 ? $"约 {size / 1024.0 / 1024.0:0} MB" : "大小未知";

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // 临时文件清理失败不影响主流程。
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch
        {
            // 同上。
        }
    }
}
