using SandianshuiLauncher.Core.Models;
using SandianshuiLauncher.ViewModels;
using Xunit;

namespace SandianshuiLauncher.App.Tests;

/// <summary>
/// 下载页空区域的状态测试：骨架屏、空状态、无匹配结果三种占位互斥显示，
/// 以及清单实际来源的提示文案。
/// </summary>
public class DownloadViewModelPlaceholderTests
{
    private static RemoteVersionItem Item(string id, string type = "release")
        => new() { Entry = new ManifestEntry { Id = id, Type = type } };

    [Fact]
    public void NewViewModel_ShowsEmptyStateOnly()
    {
        using var services = new TestServices();
        DownloadViewModel viewModel = services.Download;

        Assert.True(viewModel.ShowEmptyState);
        Assert.False(viewModel.ShowSkeleton);
        Assert.False(viewModel.ShowNoMatch);
    }

    [Fact]
    public void BusyBeforeAnyData_ShowsSkeleton()
    {
        using var services = new TestServices();
        DownloadViewModel viewModel = services.Download;

        viewModel.IsBusy = true;

        Assert.True(viewModel.ShowSkeleton);
        Assert.False(viewModel.ShowEmptyState);
        Assert.False(viewModel.ShowNoMatch);
    }

    [Fact]
    public void HasVersions_HidesEveryPlaceholder()
    {
        using var services = new TestServices();
        DownloadViewModel viewModel = services.Download;

        viewModel.AllVersions = [Item("1.21.4")];
        viewModel.VisibleVersions = [Item("1.21.4")];

        Assert.False(viewModel.ShowSkeleton);
        Assert.False(viewModel.ShowEmptyState);
        Assert.False(viewModel.ShowNoMatch);
    }

    [Fact]
    public void FilteredOutEverything_ShowsNoMatch()
    {
        using var services = new TestServices();
        DownloadViewModel viewModel = services.Download;

        viewModel.AllVersions = [Item("25w01a", "snapshot")];
        viewModel.VisibleVersions = [];

        Assert.True(viewModel.ShowNoMatch);
        Assert.False(viewModel.ShowEmptyState);
        Assert.False(viewModel.ShowSkeleton);
    }

    [Fact]
    public void FilteredOutEverything_ButBusy_ShowsNothing()
    {
        using var services = new TestServices();
        DownloadViewModel viewModel = services.Download;

        viewModel.AllVersions = [Item("25w01a", "snapshot")];
        viewModel.VisibleVersions = [];
        viewModel.IsBusy = true;

        Assert.False(viewModel.ShowNoMatch);
        Assert.False(viewModel.ShowSkeleton);
        Assert.False(viewModel.ShowEmptyState);
    }

    [Theory]
    [InlineData("https://bsmirror.hkmc.online/mc/game/version_manifest_v2.json", "MC 光速镜像（推荐·主线路）", "MC 光速镜像（推荐·主线路）")]
    [InlineData("https://bmclapi2.bangbang93.com/mc/game/version_manifest_v2.json", "MC 光速镜像（推荐·主线路）", "BMCLAPI 公益源（备用）")]
    [InlineData("https://piston-meta.mojang.com/mc/game/version_manifest_v2.json", "MC 光速镜像（推荐·主线路）", "Mojang 官方源（备用）")]
    [InlineData(null, "BMCLAPI 公益源", "BMCLAPI 公益源")]
    [InlineData("不是网址", "BMCLAPI 公益源", "BMCLAPI 公益源")]
    public void DescribeManifestSource_ReflectsActualSource(string? url, string configured, string expected)
    {
        Assert.Equal(expected, DownloadViewModel.DescribeManifestSource(url, configured));
    }
}
