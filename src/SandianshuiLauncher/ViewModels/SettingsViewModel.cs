using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using SandianshuiLauncher.Core.Models;
using SandianshuiLauncher.Core.Services;
using SandianshuiLauncher.Services;

namespace SandianshuiLauncher.ViewModels;

/// <summary>设置页：游戏目录、Java、内存、下载源、窗口等。</summary>
public partial class SettingsViewModel : ObservableObject
{
    private readonly ConfigService _config;
    private readonly JavaDetector _javaDetector;
    private readonly IJavaRuntimeService _javaRuntime;
    private readonly DownloadSourceProvider _sourceProvider;
    private readonly IDialogService _dialogs;

    [ObservableProperty] private string _gameDirectory = string.Empty;
    [ObservableProperty] private string? _javaPath;
    [ObservableProperty] private int _maxMemoryMb;
    [ObservableProperty] private int _minMemoryMb;
    [ObservableProperty] private string _downloadSourceId = "bmclapi";
    [ObservableProperty] private string? _customMirrorBaseUrl;
    [ObservableProperty] private int _downloadThreads;
    [ObservableProperty] private bool _versionIsolation;
    [ObservableProperty] private int _windowWidth;
    [ObservableProperty] private int _windowHeight;
    [ObservableProperty] private bool _fullscreen;
    [ObservableProperty] private bool _minimizeOnLaunch;
    [ObservableProperty] private bool _isDarkTheme = true;
    [ObservableProperty] private string? _extraJvmArgs;
    [ObservableProperty] private string _modUserAgent = string.Empty;
    [ObservableProperty] private bool _modUseMirror = true;
    [ObservableProperty] private string? _curseForgeApiKey;
    [ObservableProperty] private string _detectedJavaText = "尚未检测";
    [ObservableProperty] private string _statusText = "修改后请点击“保存设置”";

    public int RecommendedMemory => ConfigService.RecommendMaxMemoryMb();

    public SettingsViewModel(
        ConfigService config,
        JavaDetector javaDetector,
        IJavaRuntimeService javaRuntime,
        DownloadSourceProvider sourceProvider,
        IDialogService dialogs)
    {
        _config = config;
        _javaDetector = javaDetector;
        _javaRuntime = javaRuntime;
        _sourceProvider = sourceProvider;
        _dialogs = dialogs;
        LoadFromConfig();
    }

    private void LoadFromConfig()
    {
        var config = _config.Config;
        GameDirectory = config.GameDirectory;
        JavaPath = config.JavaPath;
        MaxMemoryMb = config.MaxMemoryMb;
        MinMemoryMb = config.MinMemoryMb;
        DownloadSourceId = config.DownloadSourceId;
        CustomMirrorBaseUrl = config.CustomMirrorBaseUrl;
        DownloadThreads = config.DownloadThreads;
        VersionIsolation = config.VersionIsolation;
        WindowWidth = config.WindowWidth;
        WindowHeight = config.WindowHeight;
        Fullscreen = config.Fullscreen;
        MinimizeOnLaunch = config.MinimizeOnLaunch;
        IsDarkTheme = !string.Equals(config.Theme, "light", StringComparison.OrdinalIgnoreCase);
        ExtraJvmArgs = config.ExtraJvmArgs;
        ModUserAgent = config.ModUserAgent;
        ModUseMirror = config.ModUseMirror;
        CurseForgeApiKey = config.CurseForgeApiKey;
    }

    [RelayCommand]
    private void BrowseGameDirectory()
    {
        var dialog = new OpenFolderDialog
        {
            Title = "选择游戏目录（.minecraft）",
            InitialDirectory = Directory.Exists(GameDirectory) ? GameDirectory : null
        };
        if (dialog.ShowDialog() == true)
        {
            GameDirectory = dialog.FolderName;
        }
    }

    [RelayCommand]
    private void BrowseJava()
    {
        var dialog = new OpenFileDialog
        {
            Title = "选择 java.exe",
            Filter = "Java 可执行文件|java.exe|可执行文件|*.exe"
        };
        if (dialog.ShowDialog() == true)
        {
            JavaPath = dialog.FileName;
        }
    }

    [ObservableProperty] private bool _isDetectingJava;

    /// <summary>
    /// 在后台线程扫描 Java 并更新结果，避免枚举目录 + 逐个运行 java -version 阻塞界面。
    /// </summary>
    [RelayCommand]
    private async Task DetectJavaAsync()
    {
        if (IsDetectingJava)
        {
            return;
        }

        IsDetectingJava = true;
        DetectedJavaText = "检测中…";
        try
        {
            var javas = await Task.Run(() => _javaDetector.Detect(GameDirectory));
            if (javas.Count == 0)
            {
                DetectedJavaText = "未检测到 Java。可在上方手动指定 java.exe 路径。";
                return;
            }
            DetectedJavaText = string.Join("\n", javas.Select(j =>
                $"Java {j.MajorVersion}（{j.Version}）{(j.Is64Bit ? "64位" : "32位")}\n  {j.Path}"));
            if (string.IsNullOrWhiteSpace(JavaPath) || !File.Exists(JavaPath))
            {
                JavaPath = javas[0].Path;
            }
        }
        finally
        {
            IsDetectingJava = false;
        }
    }

    [ObservableProperty] private bool _isDownloadingJava;

    [ObservableProperty] private string _javaDownloadStatus = string.Empty;

    /// <summary>可一键下载的 Java 大版本（Adoptium Temurin JRE）。</summary>
    public IReadOnlyList<int> JavaDownloadVersions { get; } = [17, 21];

    [ObservableProperty] private int _selectedJavaDownloadVersion = 17;

    /// <summary>
    /// 下载并安装一个 JRE 到启动器数据目录，装好后直接选用它（免去用户自己去装 Java）。
    /// </summary>
    [RelayCommand]
    private async Task DownloadJavaAsync()
    {
        if (IsDownloadingJava)
        {
            return;
        }

        IsDownloadingJava = true;
        int major = SelectedJavaDownloadVersion;
        JavaDownloadStatus = $"正在准备下载 Java {major}…";
        try
        {
            var progress = new Progress<JavaRuntimeProgress>(p =>
                JavaDownloadStatus = $"{p.Message}（{p.PercentText}）");
            JavaInfo info = await _javaRuntime.EnsureAsync(
                major,
                _config.DataDirectory,
                progress);

            JavaPath = info.Path;
            DetectedJavaText =
                $"Java {info.MajorVersion}（{info.Version}）{(info.Is64Bit ? "64位" : "32位")}\n  {info.Path}";
            JavaDownloadStatus = $"Java {info.MajorVersion} 已就绪，已填入上方路径";
            _dialogs.Info(
                $"Java {info.MajorVersion} 已下载完成，并已选为启动时使用的运行时。\n{info.Path}",
                "下载完成");
        }
        catch (Exception ex)
        {
            JavaDownloadStatus = "下载失败";
            _dialogs.Error($"下载 Java 失败：{ex.Message}", "下载失败");
        }
        finally
        {
            IsDownloadingJava = false;
        }
    }

    [RelayCommand]
    private void Save()
    {
        if (string.IsNullOrWhiteSpace(GameDirectory))
        {
            _dialogs.Info("请选择游戏目录。", "提示");
            return;
        }
        if (DownloadSourceId == "custom" && string.IsNullOrWhiteSpace(CustomMirrorBaseUrl))
        {
            _dialogs.Info("已选择自定义镜像，请填写镜像基础地址。", "提示");
            return;
        }
        if (DownloadThreads < 1 || DownloadThreads > 256)
        {
            _dialogs.Info("下载线程数应在 1–256 之间。", "提示");
            return;
        }
        if (MaxMemoryMb < MinMemoryMb)
        {
            _dialogs.Info("最大内存不能小于最小内存。", "提示");
            return;
        }

        var config = _config.Config;
        config.GameDirectory = GameDirectory;
        config.JavaPath = string.IsNullOrWhiteSpace(JavaPath) ? null : JavaPath;
        config.MaxMemoryMb = MaxMemoryMb;
        config.MinMemoryMb = MinMemoryMb;
        config.DownloadSourceId = DownloadSourceId;
        config.CustomMirrorBaseUrl = DownloadSourceId == "custom" ? CustomMirrorBaseUrl?.TrimEnd('/') : null;
        config.DownloadThreads = DownloadThreads;
        config.VersionIsolation = VersionIsolation;
        config.WindowWidth = WindowWidth;
        config.WindowHeight = WindowHeight;
        config.Fullscreen = Fullscreen;
        config.MinimizeOnLaunch = MinimizeOnLaunch;
        config.Theme = IsDarkTheme ? "dark" : "light";
        config.ExtraJvmArgs = string.IsNullOrWhiteSpace(ExtraJvmArgs) ? null : ExtraJvmArgs;
        config.ModUserAgent = string.IsNullOrWhiteSpace(ModUserAgent) ? ModService.DefaultUserAgent : ModUserAgent.Trim();
        config.ModUseMirror = ModUseMirror;
        config.CurseForgeApiKey = string.IsNullOrWhiteSpace(CurseForgeApiKey) ? null : CurseForgeApiKey.Trim();
        _config.Save();

        // 下载源重建；模组服务的镜像开关与 UA 每次请求实时读取配置，无需重建。
        _sourceProvider.Refresh(config);
        // 主题立即生效。
        Wpf.Ui.Appearance.ApplicationThemeManager.Apply(
            IsDarkTheme ? Wpf.Ui.Appearance.ApplicationTheme.Dark : Wpf.Ui.Appearance.ApplicationTheme.Light);
        StatusText = $"设置已保存（{DateTime.Now:HH:mm:ss}）";
    }

    [RelayCommand]
    private void OpenDataDirectory()
    {
        _dialogs.OpenFolder(_config.DataDirectory);
    }
}
