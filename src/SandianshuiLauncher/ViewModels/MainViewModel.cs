using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SandianshuiLauncher.Core.Models;
using SandianshuiLauncher.Core.Services;
using SandianshuiLauncher.Infrastructure;
using SandianshuiLauncher.Services;

namespace SandianshuiLauncher.ViewModels;

/// <summary>主窗口视图模型：负责四个页面之间的导航与拖拽安装路由。</summary>
public partial class MainViewModel : ObservableObject
{
    private readonly ConfigService _config;
    private readonly IDialogService _dialogs;

    [ObservableProperty]
    private ObservableObject? currentPage;

    public LaunchViewModel LaunchPage { get; }
    public DownloadViewModel DownloadPage { get; }
    public ModsViewModel ModsPage { get; }
    public VersionsViewModel VersionsPage { get; }
    public SettingsViewModel SettingsPage { get; }

    public MainViewModel(
        LaunchViewModel launchPage,
        DownloadViewModel downloadPage,
        ModsViewModel modsPage,
        VersionsViewModel versionsPage,
        SettingsViewModel settingsPage,
        ConfigService config,
        IDialogService dialogs)
    {
        LaunchPage = launchPage;
        DownloadPage = downloadPage;
        ModsPage = modsPage;
        VersionsPage = versionsPage;
        SettingsPage = settingsPage;
        _config = config;
        _dialogs = dialogs;
        CurrentPage = LaunchPage;

        // 版本页请求安装加载器时：预选目标版本并跳转下载页。
        VersionsPage.LoaderInstallRequested += OnLoaderInstallRequested;
    }

    private void OnLoaderInstallRequested(string versionId)
    {
        DownloadPage.Loader.Preselect(versionId);
        NavigateCommand.Execute(PageKey.Download);
    }

    [RelayCommand]
    private void Navigate(PageKey page)
    {
        CurrentPage = page switch
        {
            PageKey.Download => DownloadPage,
            PageKey.Mods => ModsPage,
            PageKey.Versions => VersionsPage,
            PageKey.Settings => SettingsPage,
            _ => LaunchPage
        };

        // 进入页面时刷新依赖本地文件的数据。
        switch (CurrentPage)
        {
            case LaunchViewModel launch:
                launch.Refresh();
                break;
            case VersionsViewModel versions:
                versions.Refresh();
                break;
            case DownloadViewModel download:
                download.RefreshInstalled();
                download.Loader.Refresh();
                break;
            case ModsViewModel mods:
                mods.RefreshVersions();
                break;
        }
    }

    /// <summary>
    /// 处理拖入主窗口的文件：.jar 装到当前版本 mods 目录；
    /// .mrpack/.zip 走整合包导入；其它类型给出提示。
    /// </summary>
    public async Task HandleDroppedFileAsync(string path)
    {
        switch (DragDropInstaller.Classify(path))
        {
            case DroppedFileKind.ModJar:
                await InstallDroppedModAsync(path);
                break;
            case DroppedFileKind.Modpack:
                await ModsPage.ImportPackAsync(path);
                NavigateCommand.Execute(PageKey.Mods);
                break;
            default:
                _dialogs.Warn(
                    "不支持的文件类型。\n可拖入 .jar 模组文件，或 .mrpack / .zip 整合包。",
                    "无法安装");
                break;
        }
    }

    private async Task InstallDroppedModAsync(string jarPath)
    {
        LocalVersion? version = ModsPage.SelectedVersion ?? LaunchPage.SelectedVersion;
        if (version is null)
        {
            _dialogs.Info("请先在“模组”或“启动”页选择一个版本，再拖入模组。", "提示");
            return;
        }

        var config = _config.Config;
        string modsDir = GamePaths.GetModsDirectory(
            config.GameDirectory, version.Id, config.VersionIsolation);
        try
        {
            string note = string.Empty;
            string destination = await Task.Run(
                () => DragDropInstaller.InstallMod(jarPath, modsDir, out note));
            _dialogs.Info(
                $"已安装模组：{Path.GetFileName(destination)}\n目标版本：{version.Id}" +
                (string.IsNullOrEmpty(note) ? string.Empty : $"\n\n{note}"),
                "拖拽安装完成");
            ModsPage.RefreshInstalled();
        }
        catch (Exception ex)
        {
            _dialogs.Error("拖拽安装失败：" + ex.Message, "错误");
        }
    }
}
