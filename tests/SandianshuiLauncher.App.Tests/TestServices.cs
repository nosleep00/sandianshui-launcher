using SandianshuiLauncher.Core.Services;
using SandianshuiLauncher.Services;
using SandianshuiLauncher.ViewModels;

namespace SandianshuiLauncher.App.Tests;

/// <summary>测试环境默认禁用真实网络的 404 应答器。</summary>
internal sealed class Stub404Handler : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
        => Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.NotFound));
}

/// <summary>
/// 手工装配全部服务与 ViewModel 的测试环境（与 CompositionRoot 相同的依赖图，
/// 但使用临时目录与假对话框，便于在无 WPF Application 的测试进程中验证装配与导航）。
/// </summary>
internal sealed class TestServices : IDisposable
{
    public string Root { get; }
    public ConfigService Config { get; }
    public AccountService Accounts { get; }
    public FakeDialogService Dialogs { get; }
    public MainViewModel Main { get; }
    public LaunchViewModel Launch { get; }
    public DownloadViewModel Download { get; }
    public LoaderViewModel Loader { get; }
    public ModsViewModel Mods { get; }
    public VersionsViewModel Versions { get; }
    public SettingsViewModel Settings { get; }

    /// <summary>为独立测试构造实例服务（共享同一 HttpClient 与配置）。</summary>
    public static ModpackInstanceService CreateInstanceService(
        HttpClient http, ConfigService config, DownloadService downloads)
    {
        var provider = new DownloadSourceProvider(config.Config);
        var installer = new GameInstaller(http, provider, downloads);
        return new ModpackInstanceService(
            new VersionService(http, provider),
            installer,
            new ModLoaderService(http, provider, downloads, installer),
            new JavaDetector(),
            new ModpackService(http, downloads));
    }

    /// <summary>为独立测试构造整合包导入编排服务。</summary>
    public static PackImportService CreatePackImportService(
        HttpClient http, ConfigService config, DownloadService downloads, IDialogService dialogs)
        => new(config, new ModpackService(http, downloads), CreateInstanceService(http, config, downloads), dialogs);

    public TestServices(JavaDetector? javaDetectorOverride = null, IJavaRuntimeService? javaRuntimeOverride = null)
    {
        Root = Path.Combine(Path.GetTempPath(), "sdsl_app_tests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Root);

        Config = new ConfigService(Path.Combine(Root, "data"));
        Config.Config.GameDirectory = Path.Combine(Root, "mc");

        // 测试环境不发起真实网络：全部 HTTP 请求一律 404（各专项测试自建 Stub 覆盖）。
        var http = new HttpClient(new Stub404Handler());
        var sourceProvider = new DownloadSourceProvider(Config.Config);
        var downloads = new DownloadService(http);
        var downloadQueue = new DownloadQueue(Path.Combine(Root, "data"));
        var gameInstaller = new GameInstaller(http, sourceProvider, downloads, downloadQueue);
        var versionService = new VersionService(http, sourceProvider);
        var modLoaderService = new ModLoaderService(http, sourceProvider, downloads, gameInstaller);
        var javaDetector = javaDetectorOverride ?? new JavaDetector();
        var javaRuntime = javaRuntimeOverride ?? new JavaRuntimeService(http);
        var launchService = new LaunchService();
        var accountService = new AccountService(Config);
        Accounts = accountService;
        var modService = new ModService(http, downloads, Config);
        var authlibService = new AuthlibInjectorService(http, downloads);
        var modpackService = new ModpackService(http, downloads, downloadQueue);
        var curseForgeService = new CurseForgeService(http, downloads, Config);
        var groupService = new ModGroupService(Path.Combine(Root, "data"));
        var crashFixer = new CrashFixService(Config, modService, gameInstaller, javaDetector, versionService, modLoaderService);
        var directoryPack = new DirectoryPackService(versionService);
        var instanceService = new ModpackInstanceService(versionService, gameInstaller, modLoaderService, javaDetector, modpackService);
        var logger = new LauncherLogger(Path.Combine(Root, "data"));
        Dialogs = new FakeDialogService();
        var packImport = new PackImportService(Config, modpackService, instanceService, Dialogs);

        Launch = new LaunchViewModel(accountService, Config, gameInstaller, javaDetector, javaRuntime, launchService, authlibService, logger, crashFixer, Dialogs);
        Loader = new LoaderViewModel(Config, modLoaderService, javaDetector, Dialogs);
        Download = new DownloadViewModel(Config, versionService, gameInstaller, sourceProvider, downloadQueue, Loader, Dialogs);
        Mods = new ModsViewModel(Config, modService, curseForgeService, groupService, directoryPack, packImport, Dialogs);
        Versions = new VersionsViewModel(Config, modpackService, directoryPack, packImport, Dialogs);
        Settings = new SettingsViewModel(Config, javaDetector, javaRuntime, sourceProvider, Dialogs);
        Main = new MainViewModel(Launch, Download, Mods, Versions, Settings, Config, Dialogs);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(Root, recursive: true);
        }
        catch
        {
            // 清理失败不影响测试结果。
        }
    }
}
