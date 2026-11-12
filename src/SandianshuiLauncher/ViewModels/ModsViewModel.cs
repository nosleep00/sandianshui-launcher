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

/// <summary>模组页：搜索、安装模组（MCIM/Modrinth），查看并管理已安装模组。</summary>
public partial class ModsViewModel : ObservableObject
{
    private readonly ConfigService _config;
    private readonly ModService _modService;
    private readonly CurseForgeService _curseForgeService;
    private readonly ModGroupService _groupService;
    private readonly DirectoryPackService _directoryPack;
    private readonly PackImportService _packImport;
    private readonly IDialogService _dialogs;

    [ObservableProperty]
    private ObservableCollection<LocalVersion> _localVersions = [];

    [ObservableProperty]
    private LocalVersion? _selectedVersion;

    /// <summary>加载器过滤值：fabric/forge/neoforge/quilt 或空字符串（任意）。</summary>
    [ObservableProperty]
    private string _selectedLoader = "fabric";

    /// <summary>
    /// 排序方式：relevance（相关性）/downloads（下载量）/follows（关注量）/newest（最新发布）/updated（最近更新）。
    /// 找“下载量最多”的资源时选择 downloads。
    /// </summary>
    [ObservableProperty]
    private string _selectedSort = "relevance";

    /// <summary>资源类型：mod / resourcepack / shader（复用统一资源模型搜索）。</summary>
    [ObservableProperty]
    private string _selectedResourceType = "mod";

    /// <summary>模组平台：modrinth / curseforge。</summary>
    [ObservableProperty]
    private string _selectedPlatform = "modrinth";

    /// <summary>是否已完成过一次浏览/搜索（用于“切换筛选时自动重浏览”的去抖判断）。</summary>
    private bool _hasBrowsed;

    partial void OnSelectedResourceTypeChanged(string value) => ReBrowseIfIdle();
    partial void OnSelectedPlatformChanged(string value) => ReBrowseIfIdle();
    partial void OnSelectedSortChanged(string value) => ReBrowseIfIdle();
    partial void OnSelectedLoaderChanged(string value) => ReBrowseIfIdle();

    /// <summary>浏览状态下切换资源类型/平台/排序时自动刷新推荐列表。</summary>
    private void ReBrowseIfIdle()
    {
        if (_hasBrowsed && string.IsNullOrWhiteSpace(SearchText) && SelectedVersion is not null && !IsBusy)
        {
            _ = SearchCoreAsync(string.Empty);
        }
    }

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private ObservableCollection<ModSearchResult> _results = [];

    [ObservableProperty]
    private ObservableCollection<InstalledModItem> _installedMods = [];

    [ObservableProperty]
    private ObservableCollection<string> _groupNames = [];

    [ObservableProperty]
    private string? _selectedGroupName;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private double _progress;

    [ObservableProperty]
    private string _statusText = "选择版本与加载器，输入关键词后点击“搜索模组”。";

    public ModsViewModel(
        ConfigService config,
        ModService modService,
        CurseForgeService curseForgeService,
        ModGroupService groupService,
        DirectoryPackService directoryPack,
        PackImportService packImport,
        IDialogService dialogs)
    {
        _config = config;
        _modService = modService;
        _curseForgeService = curseForgeService;
        _groupService = groupService;
        _directoryPack = directoryPack;
        _packImport = packImport;
        _dialogs = dialogs;
        RefreshGroupNames();
        RefreshVersions();
    }

    /// <summary>重新扫描本地版本（进入页面时调用）。</summary>
    public void RefreshVersions()
    {
        var versions = VersionService.ScanLocal(_config.Config.GameDirectory);
        LocalVersions = new ObservableCollection<LocalVersion>(versions);

        string? selectedId = _config.Config.SelectedVersionId;
        SelectedVersion = versions.FirstOrDefault(v => v.Id == selectedId)
                          ?? versions.FirstOrDefault();
        RefreshInstalled();
        // 搜索框为空时自动浏览热门/最新模组。
        _ = LoadPopularIfNeededAsync();
    }

    /// <summary>扫描当前所选版本 mods 目录下的已安装模组（保留既有勾选状态）。</summary>
    public void RefreshInstalled()
    {
        if (SelectedVersion is null)
        {
            InstalledMods = [];
            return;
        }

        var config = _config.Config;
        List<InstalledMod> mods = ModService.ScanInstalled(
            config.GameDirectory, SelectedVersion.Id, config.VersionIsolation);

        var previousSelection = InstalledMods?
            .Where(i => i.IsSelected)
            .Select(i => i.FileName)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        InstalledMods = new ObservableCollection<InstalledModItem>(mods.Select(m => new InstalledModItem
        {
            Mod = m,
            IsSelected = previousSelection is not null && previousSelection.Contains(m.FileName)
        }));
    }

    /// <summary>刷新分组下拉（保持当前选择）。</summary>
    private void RefreshGroupNames()
    {
        string? current = SelectedGroupName;
        GroupNames = new ObservableCollection<string>(_groupService.Names);
        SelectedGroupName = GroupNames.Contains(current ?? string.Empty) ? current : GroupNames.FirstOrDefault();
    }

    partial void OnSelectedVersionChanged(LocalVersion? value) => RefreshInstalled();

    /// <summary>把界面排序值解析为 <see cref="ResourceSortType"/>，非法值回退为相关性。</summary>
    private static ResourceSortType ParseSort(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return ResourceSortType.Relevance;
        }
        return Enum.TryParse(value.Trim(), ignoreCase: true, out ResourceSortType sort)
            ? sort
            : ResourceSortType.Relevance;
    }

    /// <summary>把界面资源类型解析为 <see cref="ResourceType"/>，非法值回退为模组。</summary>
    private static ResourceType ParseResourceType(string? value)
        => Enum.TryParse<ResourceType>(value, ignoreCase: true, out ResourceType type) ? type : ResourceType.Mod;

    /// <summary>加载器标识的中文显示名（未识别时为原版说明）。</summary>
    private static string LoaderDisplayName(string? loader) => loader?.ToLowerInvariant() switch
    {
        "fabric" => "Fabric",
        "forge" => "Forge",
        "neoforge" => "NeoForge",
        "quilt" => "Quilt",
        _ => "原版（未检测到模组加载器）"
    };

    [RelayCommand]
    private async Task SearchAsync()
    {
        if (IsBusy)
        {
            return;
        }
        // 空关键词 = 浏览模式（按当前排序展示热门/最新模组），不再弹提示。
        await SearchCoreAsync(SearchText ?? string.Empty);
    }

    /// <summary>进入页面时若搜索框为空且已选择版本，自动按当前排序浏览热门/最新模组。</summary>
    private async Task LoadPopularIfNeededAsync()
    {
        if (IsBusy || !string.IsNullOrWhiteSpace(SearchText) || SelectedVersion is null)
        {
            return;
        }
        await SearchCoreAsync(string.Empty);
    }

    /// <summary>公开入口：供导航/测试在进入页面时触发浏览（等价 LoadPopularIfNeededAsync）。</summary>
    public Task BrowseIfIdleAsync() => LoadPopularIfNeededAsync();

    /// <summary>统一搜索/浏览核心（关键词可为空）。</summary>
    private async Task SearchCoreAsync(string query)
    {
        query = query.Trim();
        string? gameVersion = SelectedVersion?.GameVersion;
        string? loader = string.IsNullOrEmpty(SelectedLoader) ? null : SelectedLoader;
        ResourceSortType sort = ParseSort(SelectedSort);
        ResourceType resourceType = ParseResourceType(SelectedResourceType);

        if (SelectedPlatform == "curseforge" && resourceType != ResourceType.Mod)
        {
            _dialogs.Info("CurseForge 平台仅支持模组搜索；资源包/光影请切换到 Modrinth 平台。", "提示");
            return;
        }

        IsBusy = true;
        Progress = 0;
        try
        {
            StatusText = string.IsNullOrEmpty(query) ? "正在浏览模组…" : "正在搜索…";

            // 浏览模式多展示一些推荐（Modrinth 上限 100，CurseForge 上限 50）。
            int limit = string.IsNullOrWhiteSpace(query) ? 100 : 40;

            List<ModSearchResult> list;
            string platformLabel;
            if (SelectedPlatform == "curseforge")
            {
                list = await SearchCurseForgeAsync(query, gameVersion, loader, pageSize: string.IsNullOrWhiteSpace(query) ? 50 : 30);
                platformLabel = "CurseForge";
            }
            else
            {
                list = await SearchModrinthAsync(query, resourceType, gameVersion, loader, sort, limit);
                platformLabel = "Modrinth";
            }

            Results = new ObservableCollection<ModSearchResult>(list);
            StatusText = BuildStatusText(query, list.Count, platformLabel, sort, gameVersion, loader);
            _hasBrowsed = true;
        }
        catch (Exception ex)
        {
            StatusText = "搜索失败：" + ex.Message;
            _dialogs.Warn(
                "搜索模组失败：\n" + ex.Message + "\n\n请检查网络、平台与 API Key 设置后重试。",
                "网络错误");
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>拼接状态栏文案：空关键词显示浏览模式（热门/最新等）。</summary>
    private static string BuildStatusText(
        string query, int count, string platform, ResourceSortType sort,
        string? gameVersion, string? loader)
    {
        if (count == 0)
        {
            return $"在 {platform} 上没有找到匹配的结果，可尝试切换加载器、平台或更换英文关键词。";
        }
        string mode = !string.IsNullOrWhiteSpace(query)
            ? $"搜索“{query}”"
            : "浏览" + (sort switch
            {
                ResourceSortType.Newest => "最新发布",
                ResourceSortType.Updated => "最近更新",
                ResourceSortType.Follows => "关注最多",
                _ => "热门（按下载量）"
            });
        return $"在 {platform} {mode}，共 {count} 个结果（{gameVersion ?? "未选版本"} / {loader ?? "任意加载器"}）。";
    }

    /// <summary>Modrinth 搜索/浏览：中文关键词先映射为英文候选逐个尝试，原词兜底；空关键词直接浏览。</summary>
    private async Task<List<ModSearchResult>> SearchModrinthAsync(
        string query, ResourceType resourceType, string? gameVersion, string? loader, ResourceSortType sort, int limit)
    {
        // 空关键词浏览时“相关性”无意义，回退到下载量（热门）。
        ResourceSortType effectiveSort = string.IsNullOrWhiteSpace(query) && sort == ResourceSortType.Relevance
            ? ResourceSortType.Downloads
            : sort;

        var candidates = string.IsNullOrWhiteSpace(query)
            ? new List<string> { string.Empty }
            : ChineseKeywordMapper.Expand(query);
        foreach (string candidate in candidates)
        {
            var request = new ResourceSearchRequest
            {
                Type = resourceType,
                Query = candidate,
                GameVersion = gameVersion,
                Loader = loader,
                Category = null,
                Sort = effectiveSort,
                Limit = limit
            };
            List<ModSearchResult> hits = await _modService.SearchResourcesAsync(request);
            if (hits.Count > 0)
            {
                if (!string.IsNullOrWhiteSpace(query)
                    && !string.Equals(candidate, query, StringComparison.OrdinalIgnoreCase))
                {
                    StatusText = $"“{query}”按英文关键词“{candidate}”搜索…";
                }
                return hits;
            }
        }
        return [];
    }

    /// <summary>CurseForge 搜索/浏览：中文关键词先映射为英文候选；空关键词直接按人气浏览。</summary>
    private async Task<List<ModSearchResult>> SearchCurseForgeAsync(
        string query, string? gameVersion, string? loader, int pageSize = 30)
    {
        var candidates = string.IsNullOrWhiteSpace(query)
            ? new List<string> { string.Empty }
            : ChineseKeywordMapper.Expand(query);
        foreach (string candidate in candidates)
        {
            List<CurseForgeMod> hits = await _curseForgeService.SearchAsync(candidate, gameVersion, loader, pageSize);
            if (hits.Count > 0)
            {
                if (!string.IsNullOrWhiteSpace(query)
                    && !string.Equals(candidate, query, StringComparison.OrdinalIgnoreCase))
                {
                    StatusText = $"“{query}”按英文关键词“{candidate}”搜索…";
                }
                return hits.Select(m => new ModSearchResult
                {
                    ProjectId = m.Id.ToString(),
                    Slug = m.Slug,
                    Title = m.Name,
                    TitleZh = ModNameLocalizer.Localize(m.Name),
                    Description = m.Summary,
                    Downloads = m.DownloadCount,
                    IconUrl = m.IconUrl,
                    Categories = [],
                    SourcePlatform = "curseforge"
                }).ToList();
            }
        }
        return [];
    }

    [RelayCommand]
    private async Task InstallAsync(ModSearchResult? mod)
    {
        if (mod is null || IsBusy)
        {
            return;
        }
        if (SelectedVersion is null)
        {
            _dialogs.Info("请先选择一个本地版本。", "提示");
            return;
        }

        var config = _config.Config;
        string versionId = SelectedVersion.Id;
        // 接口过滤用基础游戏版本号（加载器 profile 需取其父版本），目录仍用 profile id。
        string gameVersion = SelectedVersion.GameVersion;
        ResourceType resourceType = ParseResourceType(SelectedResourceType);

        // 目标版本的真实加载器优先于界面筛选（界面里的“加载器”只是搜索过滤器）。
        string? uiLoader = string.IsNullOrEmpty(SelectedLoader) ? null : SelectedLoader;
        string? targetLoader = resourceType == ResourceType.Mod
            ? VersionService.DetectLoader(config.GameDirectory, versionId) ?? uiLoader
            : null;

        string targetDescription = resourceType switch
        {
            ResourceType.ResourcePack => "资源包 zip 将被放入该版本的 resourcepacks 目录。",
            ResourceType.Shader => "光影包 zip 将被放入该版本的 shaderpacks 目录。",
            _ => "模组 jar 将被放入该版本的 mods 目录。"
        };
        string loaderLine = resourceType == ResourceType.Mod
            ? $"目标加载器：{LoaderDisplayName(targetLoader)}（按版本自动识别）\n\n"
            : string.Empty;

        if (!_dialogs.Confirm(
                $"确定安装“{mod.Title}”吗？\n\n" +
                $"目标版本：{versionId}\n" +
                loaderLine +
                targetDescription,
                "确认安装"))
        {
            return;
        }

        IsBusy = true;
        Progress = 0;
        try
        {
            StatusText = $"正在解析 {mod.Title} 的依赖…";

            string targetDirectory = resourceType switch
            {
                ResourceType.ResourcePack =>
                    GamePaths.GetResourcePacksDirectory(config.GameDirectory, versionId, config.VersionIsolation),
                ResourceType.Shader =>
                    GamePaths.GetShaderPacksDirectory(config.GameDirectory, versionId, config.VersionIsolation),
                _ => GamePaths.GetModsDirectory(config.GameDirectory, versionId, config.VersionIsolation)
            };

            // 先解析完整安装计划（含必需依赖的传递闭包），再决定是否下载。
            ModInstallPlan plan = mod.SourcePlatform == "curseforge"
                ? await _curseForgeService.BuildInstallPlanAsync(
                    long.Parse(mod.ProjectId), gameVersion, targetLoader, targetDirectory)
                : await _modService.BuildInstallPlanAsync(
                    mod, gameVersion, targetLoader, targetDirectory);

            if (plan.Dependencies.Count > 0)
            {
                string depList = string.Join("\n", plan.Dependencies.Select(d =>
                    $" · {d.Title}（{d.File.FileName}，{d.File.SizeDisplay}）"));
                if (!_dialogs.Confirm(
                        $"“{mod.Title}”还需要以下 {plan.Dependencies.Count} 个前置模组（缺失会导致游戏无法启动）：\n\n{depList}\n\n是否一并安装？",
                        "依赖确认"))
                {
                    StatusText = "已取消：缺少前置依赖，未安装。";
                    return;
                }
            }

            StatusText = $"正在下载 {mod.Title}…";
            var progress = new Progress<double>(p =>
            {
                Progress = p;
                StatusText = $"正在下载 {mod.Title}：{p:0.0}%";
            });

            InstalledMod installed = mod.SourcePlatform == "curseforge"
                ? await _curseForgeService.ExecuteInstallPlanAsync(plan, targetDirectory, progress)
                : await _modService.ExecuteInstallPlanAsync(plan, targetDirectory, progress);

            Progress = 100;
            if (resourceType == ResourceType.Mod)
            {
                RefreshInstalled();
            }
            StatusText = $"安装完成：{installed.FileName}";
            string dependencyNote = plan.Dependencies.Count > 0
                ? $"\n\n已一并安装 {plan.Dependencies.Count} 个前置依赖。"
                : string.Empty;
            string skippedNote = plan.SkippedNotes.Count > 0
                ? "\n\n提示：\n · " + string.Join("\n · ", plan.SkippedNotes)
                : string.Empty;
            _dialogs.Info(
                $"已成功安装：\n{installed.FileName}\n（{installed.SizeDisplay}）\n" +
                (resourceType == ResourceType.Mod ? "已放入 mods 目录。" : targetDescription) +
                dependencyNote + skippedNote,
                "安装完成");
        }
        catch (Exception ex)
        {
            StatusText = "安装失败：" + ex.Message;
            _dialogs.Error("安装模组失败：\n" + ex.Message, "安装失败");
        }
        finally
        {
            IsBusy = false;
            Progress = 0;
        }
    }

    [RelayCommand]
    private void DeleteMod(InstalledModItem? item)
    {
        if (item is null)
        {
            return;
        }
        InstalledMod mod = item.Mod;

        if (!_dialogs.Confirm(
                $"确定删除模组 {mod.FileName} 吗？\n该文件将从 mods 目录移除。",
                "确认删除"))
        {
            return;
        }

        try
        {
            if (File.Exists(mod.FullPath))
            {
                File.Delete(mod.FullPath);
            }
            RefreshInstalled();
            StatusText = $"已删除 {mod.FileName}";
        }
        catch (Exception ex)
        {
            _dialogs.Error("删除失败：" + ex.Message, "错误");
        }
    }

    [RelayCommand]
    private void ToggleMod(InstalledModItem? item)
    {
        if (item is null)
        {
            return;
        }
        InstalledMod mod = item.Mod;

        try
        {
            ModService.SetEnabled(mod, !mod.IsEnabled);
            RefreshInstalled();
            StatusText = mod.IsEnabled ? $"已禁用 {mod.FileName}" : $"已启用 {mod.FileName}";
        }
        catch (Exception ex)
        {
            _dialogs.Error("操作失败：" + ex.Message, "错误");
        }
    }

    // ───────── 批量运维 ─────────

    /// <summary>选中的条目（无勾选时按全部处理）。</summary>
    private List<InstalledModItem> SelectedItems()
    {
        var selected = InstalledMods.Where(i => i.IsSelected).ToList();
        return selected.Count > 0 ? selected : InstalledMods.ToList();
    }

    [RelayCommand]
    private void ToggleSelectAll()
    {
        bool allSelected = InstalledMods.Count > 0 && InstalledMods.All(i => i.IsSelected);
        foreach (InstalledModItem item in InstalledMods)
        {
            item.IsSelected = !allSelected;
        }
    }

    [RelayCommand]
    private void BatchEnable()
    {
        ApplyBatch(enable: true);
    }

    [RelayCommand]
    private void BatchDisable()
    {
        ApplyBatch(enable: false);
    }

    private void ApplyBatch(bool enable)
    {
        List<InstalledMod> targets = SelectedItems().Select(i => i.Mod).Where(m => m.IsEnabled != enable).ToList();
        if (targets.Count == 0)
        {
            StatusText = enable ? "没有需要启用的模组（所选模组均已启用）。" : "没有需要禁用的模组（所选模组均已禁用）。";
            return;
        }
        try
        {
            foreach (InstalledMod mod in targets)
            {
                ModService.SetEnabled(mod, enable);
            }
            RefreshInstalled();
            StatusText = enable
                ? $"已批量启用 {targets.Count} 个模组。"
                : $"已批量禁用 {targets.Count} 个模组。";
        }
        catch (Exception ex)
        {
            _dialogs.Error("批量操作失败：" + ex.Message, "错误");
        }
    }

    [RelayCommand]
    private void BatchDelete()
    {
        List<InstalledModItem> targets = SelectedItems();
        if (targets.Count == 0)
        {
            return;
        }
        if (!_dialogs.Confirm(
                $"确定批量删除 {targets.Count} 个模组吗？\n该操作不可恢复。",
                "批量删除", isWarning: true))
        {
            return;
        }
        try
        {
            foreach (InstalledMod mod in targets.Select(i => i.Mod))
            {
                if (File.Exists(mod.FullPath))
                {
                    File.Delete(mod.FullPath);
                }
            }
            RefreshInstalled();
            StatusText = $"已批量删除 {targets.Count} 个模组。";
        }
        catch (Exception ex)
        {
            _dialogs.Error("批量删除失败：" + ex.Message, "错误");
        }
    }

    // ───────── 分组管理 ─────────

    /// <summary>把当前启用状态保存为命名分组（快速切换方案）。</summary>
    [RelayCommand]
    private void SaveGroup()
    {
        string? name = _dialogs.PromptText("请输入分组名称（将保存当前所有模组的启用状态）：", "保存分组", "我的方案");
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }
        var enabled = InstalledMods.Where(i => i.IsEnabled).Select(i => i.FileName);
        _groupService.SaveGroup(name, enabled);
        RefreshGroupNames();
        StatusText = $"已保存分组「{name}」。";
    }

    /// <summary>应用所选分组：组内启用、组外禁用。</summary>
    [RelayCommand]
    private void ApplyGroup()
    {
        if (string.IsNullOrWhiteSpace(SelectedGroupName))
        {
            _dialogs.Info("请先保存一个分组再应用。", "提示");
            return;
        }
        try
        {
            (int enabled, int disabled) = _groupService.ApplyGroup(
                SelectedGroupName, InstalledMods.Select(i => i.Mod).ToList());
            RefreshInstalled();
            StatusText = $"已应用分组「{SelectedGroupName}」：启用 {enabled} 个、禁用 {disabled} 个。";
        }
        catch (Exception ex)
        {
            _dialogs.Error("应用分组失败：" + ex.Message, "错误");
        }
    }

    [RelayCommand]
    private void DeleteGroup()
    {
        if (string.IsNullOrWhiteSpace(SelectedGroupName))
        {
            return;
        }
        if (!_dialogs.Confirm($"确定删除分组「{SelectedGroupName}」吗？", "删除分组", isWarning: true))
        {
            return;
        }
        _groupService.RemoveGroup(SelectedGroupName);
        RefreshGroupNames();
        StatusText = "分组已删除。";
    }

    /// <summary>打开统一的「导入整合包」窗口（压缩包内容 或 整合包实例）。</summary>
    [RelayCommand]
    private void OpenImportPack()
    {
        var window = new ImportPackWindow(_config, _directoryPack, _packImport, _dialogs)
        {
            Owner = Application.Current.Windows.OfType<MainWindow>().FirstOrDefault()
        };
        window.ShowDialog();
        RefreshVersions();
    }

    /// <summary>
    /// 拖拽/外部调用的整合包导入：.mrpack 自动新建独立实例（按清单装原版+加载器），
    /// 其它格式导入到当前所选版本。与「导入整合包」窗口行为保持一致。
    /// </summary>
    public async Task ImportPackAsync(string? path)
    {
        if (IsBusy)
        {
            return;
        }

        path ??= _dialogs.PickFile("整合包|*.mrpack;*.zip", "选择整合包（.mrpack 或 zip）");
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

        // Modrinth 整合包：自动新建实例（与 PCL2 行为一致，不复用当前版本目录）。
        if (PackImportService.SupportsNewInstance(packType))
        {
            IsBusy = true;
            Progress = 0;
            StatusText = "正在创建新实例并导入整合包…";
            try
            {
                var progress = new Progress<DownloadProgress>(p =>
                {
                    Progress = p.Percent;
                    StatusText = $"正在下载 {p.CurrentFile}（{p.CompletedFiles}/{p.TotalFiles}，{p.Percent:0.0}%）";
                });
                InstanceImportResult? result = await _packImport.ImportAsNewInstanceAsync(
                    path, text => StatusText = text, progress);
                if (result is not null)
                {
                    Progress = 100;
                    StatusText = $"已创建实例：{result.VersionId}";
                    RefreshVersions();
                }
                else
                {
                    StatusText = "导入已取消或失败。";
                }
            }
            finally
            {
                IsBusy = false;
                Progress = 0;
            }
            return;
        }

        // 其它格式（CurseForge / SDLauncherPack 备份）：导入到当前所选版本。
        if (SelectedVersion is null)
        {
            _dialogs.Info("请先选择一个本地版本作为导入目标。", "提示");
            return;
        }
        IsBusy = true;
        Progress = 0;
        StatusText = "正在导入整合包…";
        try
        {
            var progress = new Progress<DownloadProgress>(p =>
            {
                Progress = p.Percent;
                StatusText = $"正在下载 {p.CurrentFile}（{p.CompletedFiles}/{p.TotalFiles}，{p.Percent:0.0}%）";
            });
            ModpackImportResult? result = await _packImport.ImportIntoExistingVersionAsync(
                path, SelectedVersion.Id, packType, progress);
            if (result is not null)
            {
                Progress = 100;
                StatusText = $"导入完成：{result.PackName}";
                RefreshInstalled();
            }
            else
            {
                StatusText = "导入已取消或失败。";
            }
        }
        finally
        {
            IsBusy = false;
            Progress = 0;
        }
    }

    [RelayCommand]
    private void OpenModsFolder()
    {
        if (SelectedVersion is null)
        {
            return;
        }

        var config = _config.Config;
        string dir = GamePaths.GetModsDirectory(
            config.GameDirectory, SelectedVersion.Id, config.VersionIsolation);
        _dialogs.OpenFolder(dir);
    }
}
