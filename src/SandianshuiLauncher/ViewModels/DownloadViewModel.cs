using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SandianshuiLauncher.Core.Models;
using SandianshuiLauncher.Core.Services;
using SandianshuiLauncher.Services;

namespace SandianshuiLauncher.ViewModels;

/// <summary>远程版本列表中的一行。</summary>
public partial class RemoteVersionItem : ObservableObject
{
    [ObservableProperty]
    private bool _isInstalled;

    public ManifestEntry Entry { get; init; } = null!;

    public string TypeDisplay => Entry.Type switch
    {
        "release" => "正式版",
        "snapshot" => "快照版",
        "old_beta" => "远古 Beta",
        "old_alpha" => "远古 Alpha",
        _ => Entry.Type
    };

    public string TimeDisplay => Entry.ReleaseTime?.ToLocalTime().ToString("yyyy-MM-dd") ?? string.Empty;
}

/// <summary>下载页：获取在线版本清单、安装游戏。</summary>
public partial class DownloadViewModel : ObservableObject
{
    private readonly ConfigService _config;
    private readonly VersionService _versionService;
    private readonly GameInstaller _gameInstaller;
    private readonly DownloadSourceProvider _sourceProvider;
    private readonly DownloadQueue _queue;
    private readonly IDialogService _dialogs;

    private VersionManifest? _manifest;

    [ObservableProperty]
    private ObservableCollection<RemoteVersionItem> _allVersions = [];

    [ObservableProperty]
    private ObservableCollection<RemoteVersionItem> _visibleVersions = [];

    /// <summary>all / release / snapshot / old</summary>
    [ObservableProperty]
    private string _filterType = "release";

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private double _progress;

    [ObservableProperty]
    private string _statusText = "就绪";

    [ObservableProperty]
    private string _downloadSourceName = string.Empty;

    /// <summary>“安装模组加载器”卡片。</summary>
    public LoaderViewModel Loader { get; }

    public DownloadViewModel(
        ConfigService config,
        VersionService versionService,
        GameInstaller gameInstaller,
        DownloadSourceProvider sourceProvider,
        DownloadQueue queue,
        LoaderViewModel loader,
        IDialogService dialogs)
    {
        _config = config;
        _versionService = versionService;
        _gameInstaller = gameInstaller;
        _sourceProvider = sourceProvider;
        _queue = queue;
        _dialogs = dialogs;
        Loader = loader;
        DownloadSourceName = _sourceProvider.Current.DisplayName;
        if (_queue.Count > 0)
        {
            StatusText = $"有 {_queue.Count} 个未完成下载任务；重新安装对应版本或整合包可自动续传。";
        }
    }

    /// <summary>刷新已安装标记。</summary>
    public void RefreshInstalled()
    {
        var local = VersionService.ScanLocal(_config.Config.GameDirectory);
        var localIds = new HashSet<string>(local.Select(v => v.Id));
        foreach (var item in AllVersions)
        {
            item.IsInstalled = localIds.Contains(item.Entry.Id);
        }
    }

    [RelayCommand]
    private async Task LoadManifestAsync()
    {
        IsBusy = true;
        StatusText = "正在获取版本清单…";
        try
        {
            DownloadSourceName = _sourceProvider.Current.DisplayName;
            _manifest = await _versionService.FetchManifestAsync();
            var local = VersionService.ScanLocal(_config.Config.GameDirectory);
            var localIds = new HashSet<string>(local.Select(v => v.Id));

            var items = _manifest.Versions
                .OrderByDescending(v => v.ReleaseTime)
                .Select(v => new RemoteVersionItem
                {
                    Entry = v,
                    IsInstalled = localIds.Contains(v.Id)
                })
                .ToList();

            AllVersions = new ObservableCollection<RemoteVersionItem>(items);
            ApplyFilter();
            StatusText = $"已从 {DescribeManifestSource(_versionService.LastManifestSourceUrl, _sourceProvider.Current.DisplayName)} 获取 {items.Count} 个版本";
        }
        catch (Exception ex)
        {
            StatusText = "版本清单获取失败：" + ex.Message;
            _dialogs.Warn(
                "获取版本清单失败：\n" + ex.Message + "\n\n程序已自动尝试镜像、BMCLAPI 与官方源。仍失败时请检查网络，或在设置中切换下载源。",
                "网络错误");
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task InstallAsync(RemoteVersionItem? item)
    {
        if (item is null || _manifest is null || IsBusy)
        {
            return;
        }
        if (item.IsInstalled)
        {
            return;
        }

        if (!_dialogs.Confirm(
                $"确定要安装 Minecraft {item.Entry.Id}（{item.TypeDisplay}）吗？",
                "确认安装"))
        {
            return;
        }

        IsBusy = true;
        Progress = 0;
        var config = _config.Config;
        try
        {
            var progress = new Progress<DownloadProgress>(p =>
            {
                Progress = p.Percent;
                StatusText = $"正在下载 {p.CurrentFile}（{p.CompletedFiles}/{p.TotalFiles}，{p.Percent:0.0}%）";
            });

            await _gameInstaller.InstallAsync(
                item.Entry,
                _manifest,
                config.GameDirectory,
                config.DownloadThreads,
                progress);

            item.IsInstalled = true;
            StatusText = $"安装完成：{item.Entry.Id}";
            _dialogs.Info($"Minecraft {item.Entry.Id} 安装完成！", "完成");
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

    partial void OnFilterTypeChanged(string value) => ApplyFilter();

    partial void OnSearchTextChanged(string value) => ApplyFilter();

    /// <summary>首次获取版本清单中：列表区显示骨架屏。</summary>
    public bool ShowSkeleton => IsBusy && AllVersions.Count == 0;

    /// <summary>还没获取过版本清单：列表区显示空状态引导。</summary>
    public bool ShowEmptyState => !IsBusy && AllVersions.Count == 0;

    /// <summary>清单里有数据、但被筛选或搜索过滤光了。</summary>
    public bool ShowNoMatch => !IsBusy && AllVersions.Count > 0 && VisibleVersions.Count == 0;

    partial void OnIsBusyChanged(bool value) => RaisePlaceholderChanged();

    partial void OnAllVersionsChanged(ObservableCollection<RemoteVersionItem> value) => RaisePlaceholderChanged();

    partial void OnVisibleVersionsChanged(ObservableCollection<RemoteVersionItem> value) => RaisePlaceholderChanged();

    private void RaisePlaceholderChanged()
    {
        OnPropertyChanged(nameof(ShowSkeleton));
        OnPropertyChanged(nameof(ShowEmptyState));
        OnPropertyChanged(nameof(ShowNoMatch));
    }

    /// <summary>把筛选逻辑抽为纯函数（便于单元测试与复用）。</summary>
    public static IEnumerable<RemoteVersionItem> FilterVersions(
        IEnumerable<RemoteVersionItem> all, string filterType, string searchText)
    {
        IEnumerable<RemoteVersionItem> query = filterType switch
        {
            "release" => all.Where(i => i.Entry.Type == "release"),
            "snapshot" => all.Where(i => i.Entry.Type == "snapshot"),
            "old" => all.Where(i => i.Entry.Type is "old_beta" or "old_alpha"),
            _ => all
        };

        if (!string.IsNullOrWhiteSpace(searchText))
        {
            query = query.Where(i => i.Entry.Id.Contains(searchText.Trim(), StringComparison.OrdinalIgnoreCase));
        }
        return query;
    }

    private void ApplyFilter()
    {
        VisibleVersions = new ObservableCollection<RemoteVersionItem>(
            FilterVersions(AllVersions, FilterType, SearchText).Take(500));
    }

    /// <summary>
    /// 把清单的实际来源翻译成用户可读的名字：镜像证书失效时会自动回退到
    /// BMCLAPI 或官方源，此时如实显示备用源，避免让用户误以为主镜像可用。
    /// </summary>
    public static string DescribeManifestSource(string? manifestUrl, string configuredName)
    {
        if (string.IsNullOrWhiteSpace(manifestUrl) || !Uri.TryCreate(manifestUrl, UriKind.Absolute, out var uri))
        {
            return configuredName;
        }

        if (uri.Host.Contains("bangbang93", StringComparison.OrdinalIgnoreCase))
        {
            return "BMCLAPI 公益源（备用）";
        }
        if (uri.Host.Contains("mojang", StringComparison.OrdinalIgnoreCase))
        {
            return "Mojang 官方源（备用）";
        }
        return configuredName;
    }
}
