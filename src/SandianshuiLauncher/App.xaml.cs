using System.IO;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using SandianshuiLauncher.Core.Services;
using SandianshuiLauncher.Infrastructure;
using SandianshuiLauncher.ViewModels;
using SandianshuiLauncher.Views;

namespace SandianshuiLauncher;

/// <summary>应用入口：构建服务容器（组合根）并打开主窗口。</summary>
public partial class App : Application
{
    /// <summary>应用级服务容器；ViewModels 全部通过构造函数注入获取依赖。</summary>
    public static ServiceProvider Services { get; private set; } = null!;

    private SingleInstanceGuard? _singleInstance;
    private CancellationTokenSource? _activationCts;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // 单实例保护：已有实例时通知其激活主窗口，本实例直接退出。
        _singleInstance = new SingleInstanceGuard();
        if (!_singleInstance.IsFirstInstance)
        {
            SingleInstanceGuard.NotifyExistingInstance();
            Shutdown();
            return;
        }

        _activationCts = new CancellationTokenSource();
        _singleInstance.ListenAsync(
            () => Dispatcher.BeginInvoke(() =>
            {
                if (MainWindow is { } window)
                {
                    if (window.WindowState == WindowState.Minimized)
                    {
                        window.WindowState = WindowState.Normal;
                    }
                    window.Activate();
                }
            }),
            _activationCts.Token);

        // 确保游戏目录与数据目录存在。
        var configService = new ConfigService();
        Directory.CreateDirectory(configService.Config.GameDirectory);

        // 统一按钮圆角（必须在任何窗口创建之前改掉隐式样式）。
        ApplyGlobalButtonCornerRadius();

        // 按配置应用主题（浅色/深色）。
        Wpf.Ui.Appearance.ApplicationThemeManager.Apply(
            string.Equals(configService.Config.Theme, "light", StringComparison.OrdinalIgnoreCase)
                ? Wpf.Ui.Appearance.ApplicationTheme.Light
                : Wpf.Ui.Appearance.ApplicationTheme.Dark);

        Services = CompositionRoot.Create(configService);

        Services.GetRequiredService<LauncherLogger>().Info("启动器启动。");

        var mainWindow = new MainWindow
        {
            DataContext = Services.GetRequiredService<MainViewModel>()
        };
        MainWindow = mainWindow;
        mainWindow.Show();
    }

    /// <summary>
    /// 把 WPF-UI 的默认按钮样式派生一份、统一圆角，并覆盖隐式样式键。
    /// 之所以放在运行时做：在 XAML 里写隐式样式
    /// <c>&lt;Style TargetType="ui:Button" BasedOn="{StaticResource {x:Type ui:Button}}"&gt;</c>
    /// 会解析到自己，导致按钮丢掉 WPF-UI 的模板、退化成裸 WPF 按钮（已实测踩坑）。
    /// </summary>
    private void ApplyGlobalButtonCornerRadius()
    {
        Type buttonType = typeof(Wpf.Ui.Controls.Button);
        if (TryFindResource(buttonType) is not Style defaultStyle)
        {
            return;
        }

        var derived = new Style(buttonType, defaultStyle);
        derived.Setters.Add(new Setter(Wpf.Ui.Controls.Button.CornerRadiusProperty, new CornerRadius(6)));
        Resources[buttonType] = derived;
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try
        {
            if (Services is not null)
            {
                Services.GetRequiredService<LauncherLogger>().Info("启动器退出。");
                Services.DisposeAsync().AsTask().GetAwaiter().GetResult();
            }
        }
        catch
        {
            // 退出清理失败不影响关闭。
        }
        _activationCts?.Cancel();
        _activationCts?.Dispose();
        _singleInstance?.Dispose();
        base.OnExit(e);
    }
}
