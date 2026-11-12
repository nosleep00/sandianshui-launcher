using SandianshuiLauncher.Core.Models;
using SandianshuiLauncher.Core.Services;

namespace SandianshuiLauncher.App.Tests;

/// <summary>
/// 验证「本机没有 Java 时是否自动下载」的交互：确认弹窗、下载调用、
/// 拒绝下载时的中文提示，以及设置页一键下载按钮的回填。
/// </summary>
public sealed class JavaAutoDownloadTests
{
    [Fact]
    public async Task MissingJava_UserDeclines_DoesNotDownloadAndPointsToSettings()
    {
        var runtime = new FakeJavaRuntime(_ => throw new InvalidOperationException("用户拒绝后不应下载。"));
        using var services = new TestServices(new NoJavaDetector(), runtime);
        InstallFakeVersion(services);
        services.Accounts.AddOfflineAccount("Steve");
        services.Launch.Refresh();
        services.Dialogs.ConfirmResults.Enqueue(false);

        await services.Launch.LaunchCommand.ExecuteAsync(null);

        Assert.Equal(1, services.Dialogs.ConfirmCalls);
        Assert.Contains("自动下载", services.Dialogs.ConfirmMessages[0], StringComparison.Ordinal);
        Assert.Equal(0, runtime.Calls);
        Assert.Contains(services.Dialogs.Errors, e => e.Contains("手动指定 java.exe 路径", StringComparison.Ordinal));
        Assert.False(services.Launch.IsLaunching);
    }

    [Fact]
    public async Task MissingJava_UserAccepts_DownloadsRequiredMajorVersion()
    {
        var runtime = new FakeJavaRuntime(_ => throw new InvalidOperationException("网络下载失败（测试）。"));
        using var services = new TestServices(new NoJavaDetector(), runtime);
        InstallFakeVersion(services);
        services.Accounts.AddOfflineAccount("Steve");
        services.Launch.Refresh();
        services.Dialogs.ConfirmResults.Enqueue(true);

        await services.Launch.LaunchCommand.ExecuteAsync(null);

        Assert.Equal(1, runtime.Calls);
        Assert.Equal(17, runtime.LastMajorVersion);
        Assert.Contains(services.Dialogs.Errors, e => e.Contains("网络下载失败", StringComparison.Ordinal));
        Assert.DoesNotContain(services.Dialogs.Errors, e => e.Contains("未找到任何可用的 Java", StringComparison.Ordinal));
    }

    [Fact]
    public async Task SettingsDownloadJava_FillsPathAndReportsSuccess()
    {
        var info = new JavaInfo
        {
            Path = Path.Combine(Path.GetTempPath(), "sds-fake-java", "bin", "java.exe"),
            MajorVersion = 21,
            Version = "21.0.5",
            Is64Bit = true
        };
        var runtime = new FakeJavaRuntime(_ => info);
        using var services = new TestServices(javaRuntimeOverride: runtime);
        services.Settings.SelectedJavaDownloadVersion = 21;

        await services.Settings.DownloadJavaCommand.ExecuteAsync(null);

        Assert.Equal(1, runtime.Calls);
        Assert.Equal(21, runtime.LastMajorVersion);
        Assert.Equal(info.Path, services.Settings.JavaPath);
        Assert.Contains("21", services.Settings.DetectedJavaText, StringComparison.Ordinal);
        Assert.Contains("已就绪", services.Settings.JavaDownloadStatus, StringComparison.Ordinal);
        Assert.Contains(services.Dialogs.Infos, i => i.Contains("下载完成", StringComparison.Ordinal));
        Assert.False(services.Settings.IsDownloadingJava);
    }

    [Fact]
    public async Task SettingsDownloadJava_WhenDownloadFails_ShowsErrorAndResetsBusy()
    {
        var runtime = new FakeJavaRuntime(_ => throw new InvalidOperationException("未能在任何下载源获取 Java 17"));
        using var services = new TestServices(javaRuntimeOverride: runtime);
        services.Settings.SelectedJavaDownloadVersion = 17;

        await services.Settings.DownloadJavaCommand.ExecuteAsync(null);

        Assert.Contains(services.Dialogs.Errors, e => e.Contains("未能在任何下载源", StringComparison.Ordinal));
        Assert.Equal("下载失败", services.Settings.JavaDownloadStatus);
        Assert.False(services.Settings.IsDownloadingJava);
    }

    [Fact]
    public void SettingsPage_OffersJavaDownloadEntry()
    {
        System.Xml.Linq.XDocument doc = ViewXaml.Load("SettingsPage.xaml");

        // 按钮是 WPF-UI 的 ui:Button（自定义命名空间），只能按 LocalName 匹配。
        var button = doc.Descendants()
            .FirstOrDefault(e => e.Name.LocalName == "Button" && (string?)e.Attribute("Content") == "下载 Java");
        Assert.NotNull(button);
        Assert.Equal("{Binding DownloadJavaCommand}", (string?)button.Attribute("Command"));

        var combo = doc.Descendants()
            .FirstOrDefault(e => e.Name.LocalName == "ComboBox"
                && (string?)e.Attribute("ItemsSource") == "{Binding JavaDownloadVersions}");
        Assert.NotNull(combo);
        Assert.Contains("JavaDownloadStatus", doc.ToString(), StringComparison.Ordinal);
    }

    private static void InstallFakeVersion(TestServices services, string id = "1.20.1")
    {
        string dir = Path.Combine(services.Config.Config.GameDirectory, "versions", id);
        Directory.CreateDirectory(dir);
        File.WriteAllText(
            Path.Combine(dir, id + ".json"),
            "{ \"id\": \"" + id + "\", \"mainClass\": \"net.minecraft.client.main.Main\", "
            + "\"javaVersion\": { \"majorVersion\": 17 } }");
        services.Launch.Refresh();
    }

    /// <summary>模拟「本机一个 Java 都没有」的检测器。</summary>
    private sealed class NoJavaDetector : JavaDetector
    {
        public override JavaInfo Resolve(string? configuredPath, int requiredMajorVersion, string? gameDirectory)
            => throw new InvalidOperationException(
                $"未找到任何可用的 Java（该版本需要 Java {requiredMajorVersion}）。");
    }

    /// <summary>记录调用次数的假下载服务；ensure 抛异常即模拟下载失败。</summary>
    private sealed class FakeJavaRuntime(Func<int, JavaInfo> ensure) : IJavaRuntimeService
    {
        public int Calls { get; private set; }

        public int LastMajorVersion { get; private set; }

        public Task<JavaInfo?> FindInstalledAsync(
            int majorVersion, string dataDirectory, CancellationToken cancellationToken = default)
            => Task.FromResult<JavaInfo?>(null);

        public Task<JavaInfo> EnsureAsync(
            int majorVersion,
            string dataDirectory,
            IProgress<JavaRuntimeProgress>? progress = null,
            Action<string>? onStatus = null,
            CancellationToken cancellationToken = default)
        {
            Calls++;
            LastMajorVersion = majorVersion;
            onStatus?.Invoke($"正在下载 Java {majorVersion}…（测试）");
            progress?.Report(new JavaRuntimeProgress("下载", 50, $"正在下载 Java {majorVersion}…（测试）"));
            return Task.FromResult(ensure(majorVersion));
        }
    }
}
