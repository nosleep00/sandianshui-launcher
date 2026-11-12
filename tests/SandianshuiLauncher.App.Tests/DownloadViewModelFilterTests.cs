using SandianshuiLauncher.Core.Models;
using SandianshuiLauncher.ViewModels;

namespace SandianshuiLauncher.App.Tests;

/// <summary>
/// 下载页版本筛选纯函数测试：验证类型过滤、搜索过滤与全量展示。
/// </summary>
public class DownloadViewModelFilterTests
{
    private static RemoteVersionItem Item(string id, string type)
        => new() { Entry = new ManifestEntry { Id = id, Type = type } };

    private static List<RemoteVersionItem> All()
        => [Item("1.21.4", "release"), Item("1.21.3", "release"), Item("25w01a", "snapshot"), Item("b1.7.3", "old_beta")];

    [Fact]
    public void Filter_Release_OnlyRelease()
    {
        List<RemoteVersionItem> result = DownloadViewModel.FilterVersions(All(), "release", "").ToList();

        Assert.Equal(2, result.Count);
        Assert.All(result, i => Assert.Equal("release", i.Entry.Type));
    }

    [Fact]
    public void Filter_Snapshot_OnlySnapshot()
    {
        List<RemoteVersionItem> result = DownloadViewModel.FilterVersions(All(), "snapshot", "").ToList();

        Assert.Single(result);
        Assert.Equal("25w01a", result[0].Entry.Id);
    }

    [Fact]
    public void Filter_Old_IncludesBetaAndAlpha()
    {
        var items = All();
        items.Add(Item("a1.2.6", "old_alpha"));
        List<RemoteVersionItem> result = DownloadViewModel.FilterVersions(items, "old", "").ToList();

        Assert.Equal(2, result.Count);
        Assert.All(result, i => Assert.Contains(i.Entry.Type, new[] { "old_beta", "old_alpha" }));
    }

    [Fact]
    public void Filter_Search_MatchesIdCaseInsensitive()
    {
        List<RemoteVersionItem> result = DownloadViewModel.FilterVersions(All(), "all", "1.21").ToList();

        Assert.Equal(2, result.Count);
    }

    [Fact]
    public void Filter_All_ReturnsAll()
    {
        List<RemoteVersionItem> result = DownloadViewModel.FilterVersions(All(), "all", "").ToList();

        Assert.Equal(4, result.Count);
    }

    [Fact]
    public void Filter_BlankSearch_DoesNotFilter()
    {
        List<RemoteVersionItem> result = DownloadViewModel.FilterVersions(All(), "all", "   ").ToList();

        Assert.Equal(4, result.Count);
    }
}
