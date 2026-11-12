using SandianshuiLauncher.Core.Models;
using SandianshuiLauncher.Core.Services;
using Xunit;

namespace SandianshuiLauncher.Core.Tests;

public class DownloadSourceTests
{
    [Fact]
    public void Mojang_DoesNotRewrite()
    {
        var source = new MojangSource();
        const string url = "https://piston-meta.mojang.com/mc/game/version_manifest_v2.json";
        Assert.Equal(url, source.Rewrite(url));
        Assert.Equal(url, source.ManifestUrl);
    }

    [Fact]
    public void Bmcl_RewritesAssetResources()
    {
        var source = new BmclCompatibleSource("https://bmclapi2.bangbang93.com");
        const string assetUrl = "https://resources.download.minecraft.net/ab/abcdef1234567890";
        Assert.Equal(
            "https://bmclapi2.bangbang93.com/assets/ab/abcdef1234567890",
            source.Rewrite(assetUrl));
    }

    [Fact]
    public void Bmcl_RewritesLibraries()
    {
        var source = new BmclCompatibleSource("https://bmclapi2.bangbang93.com");
        Assert.Equal(
            "https://bmclapi2.bangbang93.com/maven/com/example/lib/1.0/lib-1.0.jar",
            source.Rewrite("https://libraries.minecraft.net/com/example/lib/1.0/lib-1.0.jar"));
        Assert.Equal(
            "https://bmclapi2.bangbang93.com/maven/net/fabricmc/fabric-loader/1.0/fabric-loader-1.0.jar",
            source.Rewrite("https://maven.fabricmc.net/net/fabricmc/fabric-loader/1.0/fabric-loader-1.0.jar"));
    }

    [Fact]
    public void Bmcl_RewritesMetadataHosts()
    {
        var source = new BmclCompatibleSource("https://bmclapi2.bangbang93.com");
        Assert.Equal(
            "https://bmclapi2.bangbang93.com/mc/game/version_manifest_v2.json",
            source.ManifestUrl);
        Assert.Equal(
            "https://bmclapi2.bangbang93.com/v1/objects/ab/x.jar",
            source.Rewrite("https://piston-data.mojang.com/v1/objects/ab/x.jar"));
        Assert.Equal(
            "https://bmclapi2.bangbang93.com/fabric-meta/v2/versions/game",
            source.Rewrite("https://meta.fabricmc.net/v2/versions/game"));
    }

    [Fact]
    public void Bmcl_UnknownHost_LeftUnchanged()
    {
        var source = new BmclCompatibleSource("https://mirror.example.com");
        const string url = "https://other.example.com/file.zip";
        Assert.Equal(url, source.Rewrite(url));
    }

    [Fact]
    public void Bmcl_TrailingSlashOnBaseUrl_Trimmed()
    {
        var source = new BmclCompatibleSource("https://mirror.example.com///");
        Assert.Equal(
            "https://mirror.example.com/assets/ab/abc",
            source.Rewrite("https://resources.download.minecraft.net/ab/abc"));
    }

    [Fact]
    public void Create_SelectsSourceByConfig()
    {
        Assert.IsType<MojangSource>(DownloadSource.Create(new LauncherConfig { DownloadSourceId = "mojang" }));
        Assert.IsType<BmclCompatibleSource>(DownloadSource.Create(new LauncherConfig { DownloadSourceId = "bmclapi" }));
        Assert.IsType<BmclCompatibleSource>(DownloadSource.Create(new LauncherConfig { DownloadSourceId = "guangsu" }));
        var custom = DownloadSource.Create(new LauncherConfig
        {
            DownloadSourceId = "custom",
            CustomMirrorBaseUrl = "https://my.mirror.com"
        });
        Assert.IsType<BmclCompatibleSource>(custom);
        Assert.Equal(
            "https://my.mirror.com/assets/ab/abc",
            custom.Rewrite("https://resources.download.minecraft.net/ab/abc"));
    }

    [Fact]
    public void Guangsu_Manifest_UsesMainLine()
    {
        var source = DownloadSource.Create(new LauncherConfig { DownloadSourceId = "guangsu" });
        Assert.Equal("guangsu", source.Id);
        Assert.Equal(
            "https://bsmirror.hkmc.online/mc/game/version_manifest_v2.json",
            source.ManifestUrl);
    }

    [Fact]
    public void Guangsu_RewritesOfficialHosts()
    {
        var source = DownloadSource.Create(new LauncherConfig { DownloadSourceId = "guangsu" });
        Assert.Equal(
            "https://bsmirror.hkmc.online/assets/ab/abc",
            source.Rewrite("https://resources.download.minecraft.net/ab/abc"));
        Assert.Equal(
            "https://bsmirror.hkmc.online/maven/com/example/lib-1.0.jar",
            source.Rewrite("https://libraries.minecraft.net/com/example/lib-1.0.jar"));
        Assert.Equal(
            "https://bsmirror.hkmc.online/v1/objects/ab/x.jar",
            source.Rewrite("https://piston-data.mojang.com/v1/objects/ab/x.jar"));
    }

    [Fact]
    public void Guangsu_NormalizesBackupHostToMainLine()
    {
        // 清单内嵌的文件可能指向备用线路 mirror.hkmc.online，
        // 必须统一归一化到主线路 bsmirror.hkmc.online，保证全部流量走主线路。
        var source = DownloadSource.Create(new LauncherConfig { DownloadSourceId = "guangsu" });

        Assert.Equal(
            "https://bsmirror.hkmc.online/maven/ca/weblite/java-objc-bridge/1.1/java-objc-bridge-1.1.jar",
            source.Rewrite("https://mirror.hkmc.online/maven/ca/weblite/java-objc-bridge/1.1/java-objc-bridge-1.1.jar"));
        Assert.Equal(
            "https://bsmirror.hkmc.online/v1/objects/ab/x.jar",
            source.Rewrite("https://mirror.hkmc.online/v1/objects/ab/x.jar"));
        // 主线路自身域名保持不变。
        Assert.Equal(
            "https://bsmirror.hkmc.online/assets/ab/abc",
            source.Rewrite("https://bsmirror.hkmc.online/assets/ab/abc"));
    }

    [Fact]
    public void Bmcl_DoesNotNormalizeUnknownHost()
    {
        // BMCLAPI 源不应把无关域名误改写到自己。
        var source = DownloadSource.Create(new LauncherConfig { DownloadSourceId = "bmclapi" });
        Assert.Equal(
            "https://mirror.hkmc.online/maven/x.jar",
            source.Rewrite("https://mirror.hkmc.online/maven/x.jar"));
    }

    [Fact]
    public void Guangsu_BuildCandidates_FallsBackToBmclThenOfficial()
    {
        var source = DownloadSource.Create(new LauncherConfig { DownloadSourceId = "guangsu" });
        const string official = "https://maven.fabricmc.net/net/fabricmc/fabric-loader/0.20.1/fabric-loader-0.20.1.jar";

        IReadOnlyList<string> candidates = source.BuildCandidates(official);

        Assert.Equal(
            [
                "https://bsmirror.hkmc.online/maven/net/fabricmc/fabric-loader/0.20.1/fabric-loader-0.20.1.jar",
                "https://bmclapi2.bangbang93.com/maven/net/fabricmc/fabric-loader/0.20.1/fabric-loader-0.20.1.jar",
                official
            ],
            candidates);
    }

    [Fact]
    public void Bmcl_BuildCandidates_NoDuplicateBmcl()
    {
        var source = DownloadSource.Create(new LauncherConfig { DownloadSourceId = "bmclapi" });
        const string official = "https://maven.fabricmc.net/net/fabricmc/fabric-loader/0.20.1/fabric-loader-0.20.1.jar";

        IReadOnlyList<string> candidates = source.BuildCandidates(official);

        // BMCL 自身作为主源，不应再重复添加 BMCL，最后回退官方。
        Assert.Equal(
            [
                "https://bmclapi2.bangbang93.com/maven/net/fabricmc/fabric-loader/0.20.1/fabric-loader-0.20.1.jar",
                official
            ],
            candidates);
    }

    [Fact]
    public void Mojang_BuildCandidates_OnlyOfficial()
    {
        var source = new MojangSource();
        const string official = "https://libraries.minecraft.net/com/example/lib-1.0.jar";

        IReadOnlyList<string> candidates = source.BuildCandidates(official);

        Assert.Equal([official], candidates);
    }

    [Fact]
    public void Guangsu_ManifestCandidates_FallBackToBmclThenOfficial()
    {
        var source = DownloadSource.Create(new LauncherConfig { DownloadSourceId = "guangsu" });

        Assert.Equal(
            [
                "https://bsmirror.hkmc.online/mc/game/version_manifest_v2.json",
                "https://bmclapi2.bangbang93.com/mc/game/version_manifest_v2.json",
                DownloadSource.OfficialManifestUrl
            ],
            source.BuildManifestCandidates());
    }

    [Fact]
    public void Bmcl_ManifestCandidates_NoDuplicateBmcl()
    {
        var source = DownloadSource.Create(new LauncherConfig { DownloadSourceId = "bmclapi" });

        Assert.Equal(
            [
                "https://bmclapi2.bangbang93.com/mc/game/version_manifest_v2.json",
                DownloadSource.OfficialManifestUrl
            ],
            source.BuildManifestCandidates());
    }

    [Fact]
    public void Mojang_ManifestCandidates_OnlyOfficial()
    {
        var source = new MojangSource();

        Assert.Equal([DownloadSource.OfficialManifestUrl], source.BuildManifestCandidates());
    }

    [Fact]
    public void DownloadTask_AllUrls_CombinesPrimaryAndFallbacks()
    {
        var task = new DownloadTask(
            "https://primary/file.jar",
            "dest.jar",
            fallbackUrls: ["https://backup1/file.jar", "https://backup2/file.jar"]);

        Assert.Equal(
            ["https://primary/file.jar", "https://backup1/file.jar", "https://backup2/file.jar"],
            task.AllUrls);
    }

    [Fact]
    public void DownloadTask_AllUrls_NoFallbacks_ReturnsPrimary()
    {
        var task = new DownloadTask("https://primary/file.jar", "dest.jar");

        Assert.Equal(["https://primary/file.jar"], task.AllUrls);
    }
}
