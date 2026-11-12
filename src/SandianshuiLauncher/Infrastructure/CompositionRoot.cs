using System.Net.Http;
using Microsoft.Extensions.DependencyInjection;
using SandianshuiLauncher.Core.Services;
using SandianshuiLauncher.Services;
using SandianshuiLauncher.ViewModels;

namespace SandianshuiLauncher.Infrastructure;

/// <summary>
/// 组合根：集中注册全部服务与 ViewModel（单例）。
/// ViewModels 一律通过构造函数注入获取依赖，禁止再通过静态属性定位服务。
/// </summary>
public static class CompositionRoot
{
    /// <summary>浏览器风格 UA：下载游戏文件与 Forge 安装器等需通过镜像站反爬校验。</summary>
    public const string BrowserUserAgent =
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/130.0.0.0 Safari/537.36";

    public static ServiceProvider Create(ConfigService configService)
    {
        var services = new ServiceCollection();

        // —— 基础服务 ——
        services.AddSingleton(configService);

        // 通用下载客户端：统一带浏览器 UA，避免 BMCLAPI 高频请求 403、官方源限流。
        services.AddSingleton<HttpClient>(_ =>
        {
            var http = HttpFactory.Create();
            http.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", BrowserUserAgent);
            return http;
        });

        // 下载源可变持有者：设置切换下载源时 Refresh 即可，下游服务无需重建。
        services.AddSingleton(sp =>
            new DownloadSourceProvider(sp.GetRequiredService<ConfigService>().Config));

        services.AddSingleton<AccountService>();
        services.AddSingleton(sp => new DownloadService(
            sp.GetRequiredService<HttpClient>(),
            sp.GetRequiredService<ConfigService>().Config.DownloadRetries));
        // 下载队列持久化：<dataDir>/downloads.queue.json。
        services.AddSingleton(sp => new DownloadQueue(sp.GetRequiredService<ConfigService>().DataDirectory));
        services.AddSingleton<VersionService>();
        services.AddSingleton<GameInstaller>();
        // Java 检测：注入注册表扫描器（WPF 层）作为额外候选来源，
        // 并把启动器自己下载的 Java（<dataDir>/java）加入扫描范围。
        services.AddSingleton(sp =>
        {
            var configService = sp.GetRequiredService<ConfigService>();
            var detector = new JavaDetector(JavaRegistryScanner.CollectCandidates())
            {
                ManagedRuntimeRoots = [System.IO.Path.Combine(configService.DataDirectory, "java")]
            };
            return detector;
        });
        // Java 运行时下载：官方 Adoptium → 清华镜像，装到 <dataDir>/java 下。
        services.AddSingleton<IJavaRuntimeService>(sp => new JavaRuntimeService(sp.GetRequiredService<HttpClient>()));
        services.AddSingleton<LaunchService>();
        // 模组服务：独立客户端（UA 由自身按配置逐请求设置），镜像开关与 UA 实时读取配置。
        services.AddSingleton(sp => new ModService(
            HttpFactory.Create(),
            sp.GetRequiredService<DownloadService>(),
            sp.GetRequiredService<ConfigService>()));
        services.AddSingleton<ModLoaderService>();
        services.AddSingleton<AuthlibInjectorService>();
        services.AddSingleton<ModpackService>();
        services.AddSingleton<CurseForgeService>();
        services.AddSingleton<CrashFixService>();
        services.AddSingleton<DirectoryPackService>();
        services.AddSingleton<ModpackInstanceService>();
        services.AddSingleton<PackImportService>();
        // 模组分组：<dataDir>/mod-groups.json。
        services.AddSingleton(sp => new ModGroupService(sp.GetRequiredService<ConfigService>().DataDirectory));
        // 启动器持久日志：<dataDir>/logs/launcher.log（滚动 7 天 + 令牌脱敏）。
        services.AddSingleton(sp => new LauncherLogger(sp.GetRequiredService<ConfigService>().DataDirectory));

        // —— 对话框 ——
        services.AddSingleton<IDialogService, DialogService>();

        // —— ViewModels ——
        services.AddSingleton<LaunchViewModel>();
        services.AddSingleton<DownloadViewModel>();
        services.AddSingleton<LoaderViewModel>();
        services.AddSingleton<ModsViewModel>();
        services.AddSingleton<VersionsViewModel>();
        services.AddSingleton<SettingsViewModel>();
        services.AddSingleton<MainViewModel>();

        return services.BuildServiceProvider();
    }
}
