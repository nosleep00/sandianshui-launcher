using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SandianshuiLauncher.Core.Models;
using SandianshuiLauncher.Core.Services;
using SandianshuiLauncher.Services;
using SandianshuiLauncher.Views;

namespace SandianshuiLauncher.ViewModels;

/// <summary>启动页：账号、版本选择、启动游戏、游戏日志。</summary>
public partial class LaunchViewModel : ObservableObject
{
    /// <summary>界面最多保留的日志行数，超出后丢弃最旧的，防止长时间运行内存膨胀。</summary>
    private const int MaxLogLines = 1000;

    private readonly AccountService _accounts;
    private readonly ConfigService _config;
    private readonly GameInstaller _gameInstaller;
    private readonly JavaDetector _javaDetector;
    private readonly LaunchService _launchService;
    private readonly AuthlibInjectorService _authlib;
    private readonly LauncherLogger _logger;
    private readonly CrashFixService _crashFixer;
    private readonly IDialogService _dialogs;

    [ObservableProperty]
    private string _accountName = "未登录";

    /// <summary>账号类型显示文本（命名避开与 AccountType 枚举的遮蔽冲突）。</summary>
    [ObservableProperty]
    private string _accountTypeText = "点击下方按钮添加账号";

    [ObservableProperty]
    private ObservableCollection<LocalVersion> _localVersions = [];

    [ObservableProperty]
    private LocalVersion? _selectedVersion;

    /// <summary>结构化日志条目（替代纯文本，支持按级别着色）。</summary>
    public ObservableCollection<LogLineItem> LogLines { get; } = [];

    /// <summary>
    /// 后台线程产出的游戏日志先进入该线程安全队列，再由 UI 线程的定时器批量搬运，
    /// 避免多线程直接修改界面集合导致“项源与生成器不一致”。
    /// </summary>
    private readonly ConcurrentQueue<LogLineItem> _pendingLogs = new();

    /// <summary>定时把队列中的日志批量刷新到界面（始终在 UI 线程触发）。</summary>
    private readonly DispatcherTimer _logFlushTimer;

    /// <summary>本次启动时刻，用于判断进程是否「瞬间退出」（未产生日志）。</summary>
    private DateTime? _gameStartTimestamp;

    /// <summary>本次启动的游戏输出日志写入器与路径（崩溃排查证据）。</summary>
    private StreamWriter? _gameLogWriter;

    private string? _gameLogPath;

    [ObservableProperty]
    private bool _isLaunching;

    [ObservableProperty]
    private string _statusText = "就绪";

    /// <summary>启动进度（0-100），首页进度条与百分比文本绑定它。</summary>
    [ObservableProperty]
    private double _launchProgress;

    public LaunchViewModel(
        AccountService accounts,
        ConfigService config,
        GameInstaller gameInstaller,
        JavaDetector javaDetector,
        LaunchService launchService,
        AuthlibInjectorService authlib,
        LauncherLogger logger,
        CrashFixService crashFixer,
        IDialogService dialogs)
    {
        _accounts = accounts;
        _config = config;
        _gameInstaller = gameInstaller;
        _javaDetector = javaDetector;
        _launchService = launchService;
        _authlib = authlib;
        _logger = logger;
        _crashFixer = crashFixer;
        _dialogs = dialogs;

        Refresh();
        LogLines.Add(new LogLineItem { Text = "欢迎使用三点水启动器。", Level = GameLogLevel.Info });

        // 每 100ms 批量搬运一次日志：既保证接近实时，又把高频零散写入合并成少量界面更新。
        _logFlushTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(100)
        };
        _logFlushTimer.Tick += (_, _) => FlushPendingLogs();
        _logFlushTimer.Start();
    }

    /// <summary>重新读取账号与本地版本。</summary>
    public void Refresh()
    {
        var account = _accounts.Current;
        if (account is not null)
        {
            AccountName = account.DisplayName;
            AccountTypeText = account.DisplayType;
        }
        else
        {
            AccountName = "未登录";
            AccountTypeText = "点击下方按钮添加账号";
        }

        var versions = VersionService.ScanLocal(_config.Config.GameDirectory);
        LocalVersions = new ObservableCollection<LocalVersion>(versions);

        string? selectedId = _config.Config.SelectedVersionId;
        SelectedVersion = versions.FirstOrDefault(v => v.Id == selectedId)
                          ?? versions.FirstOrDefault();
    }

    partial void OnSelectedVersionChanged(LocalVersion? value)
    {
        if (value is not null)
        {
            _config.Config.SelectedVersionId = value.Id;
            _config.Save();
        }
    }

    [RelayCommand]
    private void AddAccount()
    {
        var dialog = new AddAccountWindow(_accounts, _authlib)
        {
            Owner = Application.Current.Windows.OfType<MainWindow>().FirstOrDefault()
        };
        if (dialog.ShowDialog() == true)
        {
            Refresh();
        }
    }

    /// <summary>打开当前版本的独立设置（内存/Java/JVM 参数/窗口覆盖）。</summary>
    [RelayCommand]
    private void OpenVersionSettings()
    {
        if (SelectedVersion is null)
        {
            _dialogs.Info("请先选择一个版本。", "提示");
            return;
        }
        var window = new VersionSettingsWindow(_config, SelectedVersion.Id)
        {
            Owner = Application.Current.Windows.OfType<MainWindow>().FirstOrDefault()
        };
        window.ShowDialog();
    }

    [RelayCommand]
    private async Task LaunchAsync()
    {
        if (IsLaunching)
        {
            return;
        }

        var account = _accounts.Current;
        if (account is null)
        {
            _dialogs.Info("请先添加一个离线账号。", "提示");
            return;
        }
        if (account.IsUsernameMissing)
        {
            // 仿 PCL2：允许创建空用户名的账号，但空用户名不能启动游戏。
            _dialogs.Info("这个账号还没有设置用户名，请先填写用户名再启动游戏。", "提示");
            return;
        }
        if (SelectedVersion is null)
        {
            _dialogs.Info("没有可启动的版本，请先到“下载”页安装游戏。", "提示");
            return;
        }

        var config = _config.Config;
        IsLaunching = true;
        StatusText = "正在读取版本配置…";
        LaunchProgress = 10;
        try
        {
            // 每版本独立配置优先于全局设置（内存/Java/JVM 参数/窗口）。
            LaunchSettings settings = LaunchSettingsResolver.Resolve(config, SelectedVersion.Id);

            VersionJson merged = await Task.Run(
                () => _gameInstaller.LoadMergedAsync(SelectedVersion.Id, config.GameDirectory, CancellationToken.None));

            StatusText = "正在选择 Java 运行时…";
            LaunchProgress = 35;

            int requiredJava = merged.JavaVersion?.MajorVersion ?? 8;
            // 无论是否手动指定，都结合本机已安装的 Java 选出最匹配该版本的运行时。
            JavaInfo javaInfo = await Task.Run(
                () => _javaDetector.Resolve(settings.JavaPath, requiredJava, config.GameDirectory));
            string javaPath = javaInfo.Path;

            // 外置登录需要 authlib-injector javaagent：本地缺失时自动下载到启动器数据目录。
            string? authlibInjectorPath = null;
            if (account.Type == AccountType.AuthlibInjector)
            {
                StatusText = "正在准备外置登录…";
                LaunchProgress = 55;
                authlibInjectorPath = await Task.Run(
                    () => _authlib.EnsureInjectorAsync(_config.DataDirectory, CancellationToken.None));
            }

            StatusText = "正在启动游戏进程…";
            LaunchProgress = 75;

            var options = new LaunchOptions
            {
                Account = account,
                VersionId = SelectedVersion.Id,
                Version = merged,
                GameDirectory = config.GameDirectory,
                JavaPath = javaPath,
                MinMemoryMb = settings.MinMemoryMb,
                MaxMemoryMb = settings.MaxMemoryMb,
                WindowWidth = settings.WindowWidth,
                WindowHeight = settings.WindowHeight,
                Fullscreen = settings.Fullscreen,
                ExtraJvmArgs = settings.ExtraJvmArgs,
                VersionIsolation = config.VersionIsolation,
                AuthlibInjectorPath = authlibInjectorPath
            };

            AppendLog($"正在启动 {SelectedVersion.Id}（{account.Username}）…");
            _gameStartTimestamp = DateTime.Now;
            OpenGameLogFile();

            // 把完整启动命令写入日志文件开头，便于复现与排查（游戏自身日志缺失时尤其重要）。
            if (_gameLogWriter is not null)
            {
                try
                {
                    _gameLogWriter.WriteLine($"# Java: {javaPath} ({javaInfo.Version})");
                    _gameLogWriter.WriteLine($"# Version: {SelectedVersion.Id}");
                    _gameLogWriter.WriteLine($"# Command: {javaPath} {string.Join(' ', _launchService.BuildArguments(options, javaInfo.MajorVersion))}");
                    _gameLogWriter.WriteLine();
                }
                catch (Exception ex)
                {
                    AppendLog("写入启动命令日志失败：" + ex.Message, GameLogLevel.Warn);
                }
            }

            var process = _launchService.Launch(
                options,
                onLog: EnqueueGameLog,
                onExited: code => Application.Current.Dispatcher.BeginInvoke(() => HandleGameExited(code)));

            AppendLog($"游戏进程已启动（PID {process.Id}）。使用 Java {javaInfo.Version}：{javaPath}");
            if (_gameLogPath is not null)
            {
                AppendLog($"本次游戏输出已同步保存：{_gameLogPath}");
            }
            _logger.Info($"启动游戏：版本 {SelectedVersion.Id}，账号 {account.Username}，Java {javaInfo.Version}");
            StatusText = "游戏运行中";
            LaunchProgress = 100;

            // 后台监视游戏窗口：出现后按设置决定是否自动最小化启动器。
            _ = WatchGameWindowAsync(process);
        }
        catch (Exception ex)
        {
            AppendLog("启动失败：" + ex.Message, GameLogLevel.Error);
            _logger.Error($"启动游戏失败：版本 {SelectedVersion.Id}", ex);
            StatusText = "启动失败";
            LaunchProgress = 0;
            _dialogs.Error(ex.Message, "启动失败");
        }
        finally
        {
            IsLaunching = false;
        }
    }

    /// <summary>追加启动器自身的提示行（带启动器时间戳，仅在 UI 线程调用）。</summary>
    private void AppendLog(string line, GameLogLevel level = GameLogLevel.Info)
    {
        string time = DateTime.Now.ToString("HH:mm:ss");
        LogLines.Add(new LogLineItem { Text = $"[{time}] {line}", Level = level });
        TrimLogLines();
    }

    /// <summary>
    /// 游戏进程回调（后台线程）：解析级别后放入待处理队列，并同步落盘到本次运行的日志文件
    /// （崩溃时游戏自身的日志可能写不出来，这份文件是唯一完整证据）。
    /// 不直接触碰界面集合，保证线程安全且不阻塞游戏输出线程。
    /// </summary>
    private void EnqueueGameLog(string rawLine)
    {
        GameLogEntry entry = GameLogParser.ParseLine(rawLine);
        _pendingLogs.Enqueue(new LogLineItem { Text = rawLine, Level = entry.Level });

        if (_gameLogWriter is not null)
        {
            lock (_gameLogWriter)
            {
                try
                {
                    _gameLogWriter.WriteLine(rawLine);
                    _gameLogWriter.Flush();
                }
                catch (IOException)
                {
                    // 落盘失败不影响游戏运行。
                }
                catch (ObjectDisposedException)
                {
                    // 进程退出时已关闭写入器。
                }
            }
        }
    }

    /// <summary>为本次启动创建游戏输出日志文件（记录路径以便告知用户）。</summary>
    private void OpenGameLogFile()
    {
        CloseGameLogFile();
        try
        {
            string dir = Path.Combine(_config.DataDirectory, "logs");
            Directory.CreateDirectory(dir);
            string path = Path.Combine(dir, $"game-{DateTime.Now:yyyyMMdd-HHmmss}.log");
            _gameLogWriter = new StreamWriter(path, append: false, Encoding.UTF8) { AutoFlush = true };
            _gameLogPath = path;
        }
        catch (Exception ex)
        {
            AppendLog("无法创建游戏日志文件：" + ex.Message, GameLogLevel.Warn);
        }
    }

    /// <summary>关闭本次启动的游戏输出日志文件。</summary>
    private void CloseGameLogFile()
    {
        if (_gameLogWriter is null)
        {
            return;
        }
        lock (_gameLogWriter)
        {
            try
            {
                _gameLogWriter.Flush();
                _gameLogWriter.Dispose();
            }
            catch (IOException)
            {
                // 关闭失败可忽略。
            }
            finally
            {
                _gameLogWriter = null;
            }
        }
    }

    /// <summary>UI 线程：把队列中积压的日志一次性搬到界面，并裁剪到上限。</summary>
    private void FlushPendingLogs()
    {
        if (_pendingLogs.IsEmpty)
        {
            return;
        }

        while (_pendingLogs.TryDequeue(out LogLineItem? item))
        {
            LogLines.Add(item);
        }
        TrimLogLines();
    }

    /// <summary>游戏进程退出回调（已 marshal 到 UI 线程）：正常退出记日志，异常退出则弹出诊断。</summary>
    private void HandleGameExited(int code)
    {
        // 先把队列里残留的最后一批日志刷新出来，确保崩溃前的输出不丢失。
        FlushPendingLogs();
        CloseGameLogFile();

        if (code == 0)
        {
            AppendLog("游戏已正常退出。");
            _logger.Info("游戏正常退出（退出码 0）。");
        }
        else
        {
            AppendLog($"游戏进程异常退出，退出码：{code}", GameLogLevel.Error);
            _logger.Warn($"游戏异常退出，退出码：{code}");
            if (_gameLogPath is not null)
            {
                AppendLog($"游戏输出日志：{_gameLogPath}");
            }
            ShowCrashDiagnosis(swiftExit: _gameStartTimestamp is { } started
                && DateTime.Now - started < TimeSpan.FromSeconds(3));
        }
        StatusText = "就绪";
        LaunchProgress = 0;
    }

    /// <summary>把界面日志裁剪到上限，丢弃最旧的行。</summary>
    private void TrimLogLines()
    {
        while (LogLines.Count > MaxLogLines)
        {
            LogLines.RemoveAt(0);
        }
    }

    /// <summary>监视游戏窗口出现；按“启动后自动最小化”设置处理启动器窗口。</summary>
    private async Task WatchGameWindowAsync(Process process)
    {
        bool appeared = await ProcessWindowWatcher.WaitForWindowAsync(
            () =>
            {
                process.Refresh(); // MainWindowHandle 会缓存，先刷新。
                return process.MainWindowHandle;
            },
            () => process.HasExited,
            TimeSpan.FromMilliseconds(500),
            TimeSpan.FromSeconds(60),
            CancellationToken.None);

        if (!appeared)
        {
            return;
        }
        _ = Application.Current.Dispatcher.BeginInvoke(() =>
        {
            AppendLog("游戏窗口已打开。");
            if (_config.Config.MinimizeOnLaunch && Application.Current.MainWindow is { } window)
            {
                window.WindowState = WindowState.Minimized;
            }
        });
    }

    /// <summary>读取本次运行日志并弹出崩溃诊断窗口；诊断自身失败时退化为日志提示。</summary>
    private void ShowCrashDiagnosis(bool swiftExit = false)
    {
        try
        {
            var config = _config.Config;
            // 隔离模式下优先读实例目录日志，避免根目录遗留旧日志导致误诊。
            string logText = CrashAnalyzer.ReadLatestLogFor(
                config.GameDirectory, SelectedVersion?.Id, config.VersionIsolation);

            // 瞬间退出且没有本次日志：说明 JVM 未启动游戏（参数/路径/内存问题），
            // 此时绝不能拿根目录的历史日志下结论（曾导致“缺少前置依赖”误报）。
            CrashDiagnosis diagnosis = swiftExit && string.IsNullOrWhiteSpace(logText)
                ? CrashAnalyzer.CreateNoLogDiagnosis()
                : CrashAnalyzer.Analyze(logText, BuildModAttributionIndex());

            var window = new CrashDiagnosisWindow(
                diagnosis,
                Path.Combine(_config.DataDirectory, "logs"),
                _crashFixer,
                SelectedVersion?.Id)
            {
                Owner = Application.Current.Windows.OfType<MainWindow>().FirstOrDefault()
            };
            window.ShowDialog();
        }
        catch (Exception ex)
        {
            AppendLog("崩溃诊断失败：" + ex.Message, GameLogLevel.Warn);
        }
    }

    /// <summary>从当前版本已启用的 mod jar 构建「包名/标识 → 模组」归因索引（供堆栈二级归因）。</summary>
    private Dictionary<string, string> BuildModAttributionIndex()
    {
        var index = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            if (SelectedVersion is null)
            {
                return index;
            }
            var config = _config.Config;
            List<InstalledMod> mods = ModService.ScanInstalled(
                config.GameDirectory, SelectedVersion.Id, config.VersionIsolation);
            foreach (InstalledMod mod in mods.Where(m => m.IsEnabled))
            {
                ModIdentity? identity = ModMetadataReader.Read(mod.FullPath);
                if (identity is null)
                {
                    continue;
                }
                foreach (string package in identity.Packages)
                {
                    index.TryAdd(package, identity.ModId);
                }
            }
        }
        catch
        {
            // 归因索引构建失败不影响诊断主流程。
        }
        return index;
    }
}
