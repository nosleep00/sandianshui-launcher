using SandianshuiLauncher.Core.Models;
using SandianshuiLauncher.Core.Services;
using Xunit;

namespace SandianshuiLauncher.Core.Tests;

/// <summary>
/// <see cref="DownloadSourceProvider"/> 测试：
/// 验证按配置创建下载源、切换配置后重建实例、无效自定义镜像回退官方源。
/// </summary>
public class DownloadSourceProviderTests
{
    [Fact]
    public void Create_MojangConfig_ReturnsMojangSource()
    {
        var provider = new DownloadSourceProvider(new LauncherConfig { DownloadSourceId = "mojang" });

        Assert.IsType<MojangSource>(provider.Current);
        Assert.Equal("mojang", provider.Current.Id);
    }

    [Fact]
    public void Create_GuangsuConfig_ReturnsBmclCompatibleSource()
    {
        var provider = new DownloadSourceProvider(new LauncherConfig { DownloadSourceId = "guangsu" });

        Assert.IsType<BmclCompatibleSource>(provider.Current);
        Assert.Equal("guangsu", provider.Current.Id);
    }

    [Fact]
    public void Refresh_SwapsToNewSourceInstance()
    {
        var provider = new DownloadSourceProvider(new LauncherConfig { DownloadSourceId = "guangsu" });
        DownloadSource before = provider.Current;

        provider.Refresh(new LauncherConfig { DownloadSourceId = "mojang" });

        Assert.NotSame(before, provider.Current);
        Assert.IsType<MojangSource>(provider.Current);
    }

    [Fact]
    public void Create_CustomWithoutBaseUrl_FallsBackToMojangSource()
    {
        var provider = new DownloadSourceProvider(new LauncherConfig
        {
            DownloadSourceId = "custom",
            CustomMirrorBaseUrl = null
        });

        Assert.IsType<MojangSource>(provider.Current);
    }

    [Fact]
    public void Create_CustomWithBaseUrl_ReturnsBmclCompatibleSource()
    {
        var provider = new DownloadSourceProvider(new LauncherConfig
        {
            DownloadSourceId = "custom",
            CustomMirrorBaseUrl = "https://mirror.example.com/"
        });

        Assert.IsType<BmclCompatibleSource>(provider.Current);
        Assert.Equal("custom", provider.Current.Id);
        Assert.Equal(
            "https://mirror.example.com",
            ((BmclCompatibleSource)provider.Current).ManifestUrl
                .Replace("/mc/game/version_manifest_v2.json", string.Empty));
    }
}
