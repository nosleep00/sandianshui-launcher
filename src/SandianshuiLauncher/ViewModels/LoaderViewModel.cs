using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SandianshuiLauncher.Core.Models;
using SandianshuiLauncher.Core.Services;
using SandianshuiLauncher.Services;

namespace SandianshuiLauncher.ViewModels;

/// <summary>下载页“安装模组加载器”卡片：选择目标版本、加载器类型与版本并安装。</summary>
public partial class LoaderViewModel : ObservableObject
{
    private readonly ConfigService _config;
    private readonly ModLoaderService _modLoaderService;
    private readonly JavaDetector _javaDetector;
    private readonly IDialogService _dialogs;

    /// <summary>可作为安装目标的原版版本（无继承链）。</summary>
    [ObservableProperty]
    private ObservableCollection<string> _targetVersions = [];

    [ObservableProperty]
    private string? _selectedTargetVersion;

    /// <summary>加载器类型。</summary>
    [ObservableProperty]
    private ObservableCollection<string> _loaderTypes = ["Fabric", "Forge", "NeoForge", "Quilt"];

    [ObservableProperty]
    private string? _selectedLoaderType = "Fabric";

    /// <summary>当前目标版本 + 类型下可用的加载器版本。</summary>
    [ObservableProperty]
    private ObservableCollection<string> _loaderVersions = [];

    [ObservableProperty]
    private string? _selectedLoaderVersion;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private double _progress;

    [ObservableProperty]
    private string _statusText = "请选择目标版本与加载器类型";

    public LoaderViewModel(ConfigService config, ModLoaderService modLoaderService, JavaDetector javaDetector, IDialogService dialogs)
    {
        _config = config;
        _modLoaderService = modLoaderService;
        _javaDetector = javaDetector;
        _dialogs = dialogs;
    }

    /// <summary>刷新可安装目标版本列表。</summary>
    public void Refresh()
    {
        var local = VersionService.ScanLocal(_config.Config.GameDirectory);
        // 仅列出原版（无继承），避免在已有加载器版本上重复嵌套。
        List<string> baseVersions = local
            .Where(v => string.IsNullOrEmpty(v.InheritsFrom))
            .Select(v => v.Id)
            .OrderByDescending(x => x)
            .ToList();

        TargetVersions = new ObservableCollection<string>(baseVersions);
        if (SelectedTargetVersion is null || !baseVersions.Contains(SelectedTargetVersion))
        {
            SelectedTargetVersion = baseVersions.FirstOrDefault();
        }
    }

    /// <summary>外部入口（版本页）预选目标版本：刷新列表后选中该版本并加载其加载器列表。</summary>
    public void Preselect(string versionId)
    {
        Refresh();
        if (TargetVersions.Contains(versionId))
        {
            SelectedTargetVersion = versionId;
        }
    }

    partial void OnSelectedTargetVersionChanged(string? value) => _ = LoadLoaderVersionsAsync();
    partial void OnSelectedLoaderTypeChanged(string? value) => _ = LoadLoaderVersionsAsync();

    /// <summary>获取当前目标版本与加载器类型下的加载器版本列表。</summary>
    [RelayCommand]
    private async Task LoadLoaderVersionsAsync()
    {
        if (string.IsNullOrEmpty(SelectedTargetVersion) || string.IsNullOrEmpty(SelectedLoaderType))
        {
            LoaderVersions = [];
            return;
        }

        IsBusy = true;
        StatusText = "正在获取加载器版本列表…";
        try
        {
            List<string> versions;
            if (SelectedLoaderType is "Fabric" or "Quilt")
            {
                List<FabricLoaderListEntry> list = SelectedLoaderType == "Fabric"
                    ? await _modLoaderService.GetFabricLoadersAsync(SelectedTargetVersion)
                    : await _modLoaderService.GetQuiltLoadersAsync(SelectedTargetVersion);
                versions = list
                    .Where(e => e.Loader?.Version is not null)
                    .Select(e => e.Loader!.Version!)
                    .ToList();
            }
            else if (SelectedLoaderType == "NeoForge")
            {
                List<ForgeVersionEntry> list =
                    await _modLoaderService.GetNeoForgeVersionsAsync(SelectedTargetVersion);
                versions = list
                    .Where(e => e.Version is not null)
                    .Select(e => e.Version!)
                    .ToList();
            }
            else
            {
                List<ForgeVersionEntry> list =
                    await _modLoaderService.GetForgeVersionsAsync(SelectedTargetVersion);
                versions = list
                    .Where(e => e.Version is not null)
                    .Select(e => e.Version!)
                    .ToList();
            }

            LoaderVersions = new ObservableCollection<string>(versions);
            SelectedLoaderVersion = versions.FirstOrDefault();
            StatusText = $"共 {versions.Count} 个加载器版本";
        }
        catch (Exception ex)
        {
            LoaderVersions = [];
            SelectedLoaderVersion = null;
            StatusText = "获取加载器版本失败：" + ex.Message;
            _dialogs.Warn("获取加载器版本失败：\n" + ex.Message, "网络错误");
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>安装所选加载器。</summary>
    [RelayCommand]
    private async Task InstallAsync()
    {
        if (IsBusy)
        {
            return;
        }
        if (string.IsNullOrEmpty(SelectedTargetVersion)
            || string.IsNullOrEmpty(SelectedLoaderType)
            || string.IsNullOrEmpty(SelectedLoaderVersion))
        {
            _dialogs.Info(
                "请先选择目标版本、加载器类型与加载器版本。",
                "提示");
            return;
        }

        LauncherConfig config = _config.Config;

        // Forge / NeoForge 需要本机 Java 运行处理器；Fabric / Quilt 安装无需 Java。
        string javaPath = string.Empty;
        if (SelectedLoaderType is "Forge" or "NeoForge")
        {
            try
            {
                JavaInfo java = _javaDetector.Resolve(config.JavaPath, 17, config.GameDirectory);
                javaPath = java.Path;
            }
            catch (Exception ex)
            {
                _dialogs.Warn(ex.Message, "未找到 Java");
                return;
            }
        }

        IsBusy = true;
        Progress = 0;
        StatusText = "正在安装…";
        try
        {
            var progress = new Progress<DownloadProgress>(p =>
            {
                Progress = p.Percent;
                StatusText = $"正在下载 {p.CurrentFile}（{p.CompletedFiles}/{p.TotalFiles}，{p.Percent:0.0}%）";
            });

            if (SelectedLoaderType == "Fabric")
            {
                await _modLoaderService.InstallFabricAsync(
                    SelectedTargetVersion,
                    SelectedLoaderVersion,
                    config.GameDirectory,
                    config.DownloadThreads,
                    progress);
            }
            else if (SelectedLoaderType == "Quilt")
            {
                await _modLoaderService.InstallQuiltAsync(
                    SelectedTargetVersion,
                    SelectedLoaderVersion,
                    config.GameDirectory,
                    config.DownloadThreads,
                    progress);
            }
            else if (SelectedLoaderType == "NeoForge")
            {
                await _modLoaderService.InstallNeoForgeAsync(
                    SelectedTargetVersion,
                    SelectedLoaderVersion,
                    config.GameDirectory,
                    config.DownloadThreads,
                    javaPath,
                    progress,
                    msg => StatusText = msg);
            }
            else
            {
                await _modLoaderService.InstallForgeAsync(
                    SelectedTargetVersion,
                    SelectedLoaderVersion,
                    config.GameDirectory,
                    config.DownloadThreads,
                    javaPath,
                    progress,
                    msg => StatusText = msg);
            }

            StatusText = $"{SelectedLoaderType} 加载器安装完成";
            _dialogs.Info($"{SelectedLoaderType} 加载器安装完成！", "完成");
            Refresh();
        }
        catch (Exception ex)
        {
            StatusText = "安装失败：" + ex.Message;
            _dialogs.Error("安装失败：\n" + ex.Message, "安装失败");
        }
        finally
        {
            IsBusy = false;
            Progress = 0;
        }
    }
}
