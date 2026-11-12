using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SandianshuiLauncher.Core.Models;
using SandianshuiLauncher.Core.Services;
using SandianshuiLauncher.Services;
using SandianshuiLauncher.Views;

namespace SandianshuiLauncher.ViewModels;

/// <summary>版本管理页：查看、删除本地版本，打开版本文件夹。</summary>
public partial class VersionsViewModel : ObservableObject
{
    private readonly ConfigService _config;
    private readonly ModpackService _modpackService;
    private readonly DirectoryPackService _directoryPack;
    private readonly PackImportService _packImport;
    private readonly IDialogService _dialogs;

    /// <summary>请求跳转到下载页并为该原版版本安装模组加载器。</summary>
    public event Action<string>? LoaderInstallRequested;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private ObservableCollection<LocalVersion> _localVersions = [];

    [ObservableProperty]
    private string _statusText = string.Empty;

    public VersionsViewModel(
        ConfigService config,
        ModpackService modpackService,
        DirectoryPackService directoryPack,
        PackImportService packImport,
        IDialogService dialogs)
    {
        _config = config;
        _modpackService = modpackService;
        _directoryPack = directoryPack;
        _packImport = packImport;
        _dialogs = dialogs;
        Refresh();
    }

    public void Refresh()
    {
        var versions = VersionService.ScanLocal(_config.Config.GameDirectory);
        LocalVersions = new ObservableCollection<LocalVersion>(versions);
        StatusText = $"共安装 {versions.Count} 个版本";
    }

    [RelayCommand]
    private void OpenFolder(LocalVersion? version)
    {
        if (version is null)
        {
            return;
        }
        if (Directory.Exists(version.Directory))
        {
            _dialogs.OpenFolder(version.Directory);
        }
    }

    [RelayCommand]
    private void Rename(LocalVersion? version)
    {
        if (version is null)
        {
            return;
        }
        string? newId = _dialogs.PromptText($"请输入版本 {version.Id} 的新名称：", "重命名版本", version.Id);
        if (string.IsNullOrWhiteSpace(newId) || newId == version.Id)
        {
            return;
        }

        try
        {
            VersionService.Rename(_config.Config.GameDirectory, version.Id, newId);
            StatusText = $"已重命名：{version.Id} → {newId}";
            Refresh();
        }
        catch (Exception ex)
        {
            _dialogs.Error(ex.Message, "重命名失败");
        }
    }

    [RelayCommand]
    private void Copy(LocalVersion? version)
    {
        if (version is null)
        {
            return;
        }
        string? newId = _dialogs.PromptText($"请输入复制版本 {version.Id} 的新名称：", "复制版本", version.Id + "-副本");
        if (string.IsNullOrWhiteSpace(newId) || newId == version.Id)
        {
            return;
        }

        try
        {
            VersionService.Copy(_config.Config.GameDirectory, version.Id, newId);
            StatusText = $"已复制：{version.Id} → {newId}";
            Refresh();
        }
        catch (Exception ex)
        {
            _dialogs.Error(ex.Message, "复制失败");
        }
    }

    /// <summary>为该原版版本安装模组加载器：跳转下载页并预选目标版本。</summary>
    [RelayCommand]
    private void InstallLoader(LocalVersion? version)
    {
        if (version is null)
        {
            return;
        }
        if (!string.IsNullOrEmpty(version.InheritsFrom))
        {
            _dialogs.Info(
                $"版本 {version.Id} 已包含加载器（{version.LoaderName}），无需重复安装。\n如需更换加载器，请先下载对应原版版本。",
                "提示");
            return;
        }
        LoaderInstallRequested?.Invoke(version.Id);
    }

    /// <summary>打开统一的「导入整合包」窗口（压缩包内容 或 整合包实例），关闭后刷新版本列表。</summary>
    [RelayCommand]
    private void OpenImportPack()
    {
        var window = new ImportPackWindow(_config, _directoryPack, _packImport, _dialogs)
        {
            Owner = Application.Current.Windows.OfType<MainWindow>().FirstOrDefault()
        };
        window.ShowDialog();
        Refresh();
    }

    /// <summary>把当前实例打包为 SDLauncherPack 整合包（环境备份/分享）。</summary>
    [RelayCommand]
    private async Task ExportPack(LocalVersion? version)
    {
        if (version is null)
        {
            return;
        }
        string? path = _dialogs.PickSaveFile(
            "SDLauncherPack 整合包|*.zip", "导出整合包", version.Id + "-整合包.zip");
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }
        if (!path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
        {
            path += ".zip";
        }

        try
        {
            StatusText = "正在导出整合包…";
            ModpackImportResult result = await _modpackService.ExportInstanceAsync(
                _config.Config.GameDirectory, version.Id, _config.Config.VersionIsolation, path);            string notes = result.Warnings.Count == 0
                ? string.Empty
                : "\n\n提示：\n · " + string.Join("\n · ", result.Warnings);
            StatusText = $"已导出：{version.Id}";
            _dialogs.Info(
                $"整合包已导出到：\n{path}\n\n包含模组 {result.ImportedFiles} 个、配置文件 {result.OverrideFiles} 个。" + notes,
                "导出完成");
        }
        catch (Exception ex)
        {
            StatusText = "导出失败：" + ex.Message;
            _dialogs.Error("整合包导出失败：\n" + ex.Message, "导出失败");
        }
    }

    [RelayCommand]
    private void Delete(LocalVersion? version)
    {
        if (version is null)
        {
            return;
        }

        string gameDirectory = _config.Config.GameDirectory;
        List<string> userData = VersionDataService.GetUserDataEntries(version.Directory);

        string userDataNotice = userData.Count == 0
            ? string.Empty
            : "\n\n⚠ 检测到该版本目录内还有以下用户数据：\n   · "
              + string.Join("\n   · ", userData)
              + "\n删除前会自动迁移到游戏根目录（重名自动改名），请确认：";

        if (!_dialogs.Confirm(
                $"确定删除版本 {version.Id} 吗？\n将删除文件夹：\n{version.Directory}\n\n（该操作不可恢复。）"
                + userDataNotice,
                "删除版本",
                isWarning: true))
        {
            return;
        }

        try
        {
            // 先迁移用户数据，再删除版本目录；迁移失败则中止删除，避免误删玩家数据。
            if (userData.Count > 0)
            {
                VersionDataService.MigrateToGameRoot(version.Directory, gameDirectory, userData);
            }
            Directory.Delete(version.Directory, recursive: true);
            StatusText = userData.Count > 0
                ? $"已删除版本：{version.Id}（用户数据已迁移到游戏根目录）"
                : $"已删除版本：{version.Id}";
            Refresh();
        }
        catch (Exception ex)
        {
            _dialogs.Error(
                "删除失败：" + ex.Message + "\n\n版本目录已保留，用户数据可能未完成迁移，请解决占用问题后重试。",
                "错误");
        }
    }
}
