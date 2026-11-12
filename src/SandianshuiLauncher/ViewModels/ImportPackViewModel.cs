using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SandianshuiLauncher.Core.Models;
using SandianshuiLauncher.Core.Services;
using SandianshuiLauncher.Services;

namespace SandianshuiLauncher.ViewModels;

/// <summary>
/// 「导入整合包」统一窗口视图模型：
/// ① 压缩包模式——.mrpack / .zip：自动新建实例 或 导入到选定的现有版本；
/// ② 实例模式——「解压即玩」文件夹整体导入（复制版本目录、合并 assets/libraries、检测自带 Java）。
/// 具体导入逻辑复用 <see cref="PackImportService"/>，与拖拽安装保持一致。
/// </summary>
public partial class ImportPackViewModel : ObservableObject
{
    private readonly ConfigService _config;
    private readonly DirectoryPackService _directoryPack;
    private readonly PackImportService _packImport;
    private readonly IDialogService _dialogs;

    [ObservableProperty]
    private ObservableCollection<LocalVersion> _localVersions = [];

    [ObservableProperty]
    private LocalVersion? _selectedTargetVersion;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string _statusText = string.Empty;

    /// <summary>
    /// true = 自动新建实例（默认，按清单安装原版+加载器）；
    /// false = 导入到选定的现有版本。
    /// </summary>
    [ObservableProperty]
    private bool _createNewInstance = true;

    /// <summary>任一模式导入成功后触发（宿主页面借此刷新列表）。</summary>
    public event Action? Imported;

    public ImportPackViewModel(
        ConfigService config,
        DirectoryPackService directoryPack,
        PackImportService packImport,
        IDialogService dialogs)
    {
        _config = config;
        _directoryPack = directoryPack;
        _packImport = packImport;
        _dialogs = dialogs;
        RefreshVersions();
    }

    /// <summary>刷新可选目标版本（zip 模式目标）。</summary>
    public void RefreshVersions()
    {
        List<LocalVersion> versions = VersionService.ScanLocal(_config.Config.GameDirectory);
        LocalVersions = new ObservableCollection<LocalVersion>(versions);
        string? selectedId = _config.Config.SelectedVersionId;
        SelectedTargetVersion = versions.FirstOrDefault(v => v.Id == selectedId)
                                ?? versions.FirstOrDefault();
    }

    /// <summary>压缩包模式：选 .mrpack/.zip → 自动识别类型 → 新建实例 或 导入到目标版本。</summary>
    [RelayCommand]
    private async Task ImportZipAsync()
    {
        if (IsBusy)
        {
            return;
        }
        if (!CreateNewInstance && SelectedTargetVersion is null)
        {
            _dialogs.Info("请先选择一个本地版本作为导入目标（可到下载页安装）。", "提示");
            return;
        }

        string? path = _dialogs.PickFile("整合包|*.mrpack;*.zip", "选择整合包压缩包（.mrpack 或 .zip）");
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        PackFileType packType = PackImportService.DetectType(path);
        if (packType == PackFileType.Unknown)
        {
            _dialogs.Error(
                "无法识别该整合包：压缩包内既没有 modrinth.index.json、manifest.json，也没有 sdlauncher-pack.json。\n" +
                "请确认文件是 Modrinth / CurseForge / SDLauncherPack 格式的整合包。",
                "导入失败");
            return;
        }

        if (CreateNewInstance && !PackImportService.SupportsNewInstance(packType))
        {
            _dialogs.Info(
                "「自动新建实例」目前支持 Modrinth 整合包（.mrpack，含 dependencies 版本信息）。\n" +
                "CurseForge / 备份包请切换为「导入到现有版本」模式。",
                "提示");
            return;
        }

        IsBusy = true;
        StatusText = "正在导入整合包…";
        try
        {
            var progress = new Progress<DownloadProgress>(p =>
            {
                StatusText = $"正在下载 {p.CurrentFile}（{p.CompletedFiles}/{p.TotalFiles}，{p.Percent:0.0}%）";
            });

            if (CreateNewInstance)
            {
                InstanceImportResult? result = await _packImport.ImportAsNewInstanceAsync(
                    path, text => StatusText = text, progress);
                if (result is not null)
                {
                    StatusText = $"已创建实例：{result.VersionId}";
                    RefreshVersions();
                    Imported?.Invoke();
                }
                else
                {
                    StatusText = "导入已取消或失败。";
                }
            }
            else
            {
                ModpackImportResult? result = await _packImport.ImportIntoExistingVersionAsync(
                    path, SelectedTargetVersion!.Id, packType, progress);
                if (result is not null)
                {
                    StatusText = "整合包导入完成。";
                    Imported?.Invoke();
                }
                else
                {
                    StatusText = "导入已取消或失败。";
                }
            }
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>实例模式：选「解压即玩」文件夹 → 复制版本目录、合并 assets/libraries、检测自带 Java。</summary>
    [RelayCommand]
    private async Task ImportFolderAsync()
    {
        if (IsBusy)
        {
            return;
        }

        string? selected = _dialogs.PickFolder("选择整合包目录（包含 .minecraft 的文件夹，或 .minecraft 本身）");
        if (string.IsNullOrWhiteSpace(selected))
        {
            return;
        }
        string? minecraftRoot = DirectoryPackService.FindMinecraftRoot(selected);
        if (minecraftRoot is null)
        {
            _dialogs.Warn(
                "所选目录不是有效的整合包目录。\n需要包含 .minecraft\\versions 与 .minecraft\\assets。",
                "无法导入");
            return;
        }
        List<string> versions = DirectoryPackService.DetectImportableVersions(minecraftRoot);
        if (versions.Count == 0)
        {
            _dialogs.Info(
                "该目录中没有检测到可导入的整合包版本。\n整合包版本目录（versions 下）应包含 mods 或 config 文件夹。",
                "提示");
            return;
        }
        if (!_dialogs.Confirm(
                $"检测到 {versions.Count} 个整合包版本：\n · {string.Join("\n · ", versions)}\n\n" +
                "将把版本目录复制到当前游戏目录，并合并 assets 与 libraries（已存在文件自动跳过）。\n继续？",
                "导入整合包"))
        {
            return;
        }

        IsBusy = true;
        StatusText = "正在导入整合包…";
        try
        {
            var result = await _directoryPack.ImportAsync(
                minecraftRoot, _config.Config.GameDirectory, versions,
                onProgress: text => StatusText = text);

            LauncherConfig config = _config.Config;
            foreach ((string versionId, string javaPath) in result.BundledJavaByVersion)
            {
                LaunchSettingsResolver.SaveOverride(config, new VersionSetting { VersionId = versionId, JavaPath = javaPath });
            }
            if (result.ImportedVersions.Count > 0)
            {
                config.SelectedVersionId = result.ImportedVersions[^1];
                if (!config.VersionIsolation
                    && _dialogs.Confirm(
                        "该整合包版本自带独立目录（mods/配置/存档都在版本文件夹内）。\n是否开启“版本隔离”，把它作为独立实例使用？",
                        "建议开启版本隔离"))
                {
                    config.VersionIsolation = true;
                }
            }
            _config.Save();
            RefreshVersions();

            string notes = result.Warnings.Count == 0
                ? string.Empty
                : "\n\n提示：\n · " + string.Join("\n · ", result.Warnings);
            string importedText = result.ImportedVersions.Count == 0 ? "无" : string.Join("、", result.ImportedVersions);
            string skippedText = result.SkippedVersions.Count == 0 ? "无" : string.Join("、", result.SkippedVersions);
            StatusText = "整合包导入完成。";
            _dialogs.Info(
                $"整合包导入完成：\n已导入版本：{importedText}\n复制文件 {result.CopiedFiles} 个\n跳过版本：{skippedText}{notes}\n\n" +
                (result.ImportedVersions.Count > 0 ? "已自动选中新版本，可到启动页直接启动。" : string.Empty),
                "导入完成");
            Imported?.Invoke();
        }
        catch (Exception ex)
        {
            StatusText = "导入失败：" + ex.Message;
            _dialogs.Error("整合包导入失败：\n" + ex.Message, "导入失败");
        }
        finally
        {
            IsBusy = false;
        }
    }
}
