using SandianshuiLauncher.Core.Models;
using SandianshuiLauncher.Core.Services;
using Xunit;

namespace SandianshuiLauncher.Core.Tests;

public class VersionJsonMergerTests
{
    [Fact]
    public void ChildScalarOverridesParent()
    {
        var parent = new VersionJson { Id = "1.20.1", MainClass = "net.minecraft.client.main.Main", Assets = "1" };
        var child = new VersionJson { Id = "fabric-loader-1.20.1", MainClass = "net.fabricmc.loader.impl.launch.knot.KnotClient" };

        var merged = VersionJsonMerger.Merge(child, parent);

        Assert.Equal("net.fabricmc.loader.impl.launch.knot.KnotClient", merged.MainClass);
        // 子未提供的字段回退到父。
        Assert.Equal("1", merged.Assets);
        Assert.Equal("fabric-loader-1.20.1", merged.Id);
    }

    [Fact]
    public void Libraries_SameBaseKey_ChildReplacesParent()
    {
        var parent = new VersionJson
        {
            Libraries =
            [
                new LibraryInfo { Name = "com.example:same:1.0", Url = "http://parent" },
                new LibraryInfo { Name = "com.example:parentonly:1.0" }
            ]
        };
        var child = new VersionJson
        {
            Libraries =
            [
                new LibraryInfo { Name = "com.example:same:1.0", Url = "http://child" },
                new LibraryInfo { Name = "com.example:childonly:1.0" }
            ]
        };

        var merged = VersionJsonMerger.Merge(child, parent);

        Assert.NotNull(merged.Libraries);
        Assert.Equal(3, merged.Libraries.Count);
        Assert.Equal("http://child", merged.Libraries.Single(l => l.Name == "com.example:same:1.0").Url);
        Assert.Contains(merged.Libraries, l => l.Name == "com.example:parentonly:1.0");
        Assert.Contains(merged.Libraries, l => l.Name == "com.example:childonly:1.0");
    }

    [Fact]
    public void Libraries_NativesWithDifferentClassifier_KeptSeparately()
    {
        var parent = new VersionJson
        {
            Libraries = [new LibraryInfo { Name = "org.lwjgl:lwjgl:3.3.1" }]
        };
        var child = new VersionJson
        {
            Libraries = [new LibraryInfo { Name = "org.lwjgl:lwjgl:3.3.1:natives-windows" }]
        };

        var merged = VersionJsonMerger.Merge(child, parent);

        Assert.Equal(2, merged.Libraries!.Count);
    }

    [Fact]
    public void Downloads_MergeByKey()
    {
        var parent = new VersionJson
        {
            Downloads = new Dictionary<string, DownloadInfo>
            {
                ["client"] = new() { Url = "http://parent/client" },
                ["server"] = new() { Url = "http://parent/server" }
            }
        };
        var child = new VersionJson
        {
            Downloads = new Dictionary<string, DownloadInfo>
            {
                ["client"] = new() { Url = "http://child/client" }
            }
        };

        var merged = VersionJsonMerger.Merge(child, parent);

        Assert.Equal("http://child/client", merged.Downloads!["client"].Url);
        Assert.Equal("http://parent/server", merged.Downloads!["server"].Url);
    }

    [Fact]
    public void Jar_ChildWithoutClient_ReusesParentJar()
    {
        // Fabric 场景：子版本无自己的 client，应复用父版本 jar，避免重复下载。
        var parent = new VersionJson
        {
            Id = "1.20.4",
            Downloads = new Dictionary<string, DownloadInfo>
            {
                ["client"] = new() { Url = "http://parent/client" }
            }
        };
        var child = new VersionJson
        {
            Id = "fabric-loader-0.20.1-1.20.4",
            InheritsFrom = "1.20.4"
        };

        var merged = VersionJsonMerger.Merge(child, parent);

        Assert.Equal("1.20.4", merged.Jar);
    }

    [Fact]
    public void Jar_ChildWithClient_UsesChildItself()
    {
        // 子版本自带 client 时，jar 留空，默认回退到子版本 id（merged.Id）。
        var parent = new VersionJson
        {
            Id = "1.20.4",
            Downloads = new Dictionary<string, DownloadInfo>
            {
                ["client"] = new() { Url = "http://parent/client" }
            }
        };
        var child = new VersionJson
        {
            Id = "custom-1.20.4",
            Downloads = new Dictionary<string, DownloadInfo>
            {
                ["client"] = new() { Url = "http://child/client" }
            }
        };

        var merged = VersionJsonMerger.Merge(child, parent);

        Assert.Null(merged.Jar);
    }

    [Fact]
    public void Jar_ExplicitChildJarField_TakesPrecedence()
    {
        // 子版本显式声明 jar 字段时，优先级最高。
        var parent = new VersionJson
        {
            Id = "1.20.4",
            Downloads = new Dictionary<string, DownloadInfo>
            {
                ["client"] = new() { Url = "http://parent/client" }
            }
        };
        var child = new VersionJson
        {
            Id = "some-loader-1.20.4",
            InheritsFrom = "1.20.4",
            Jar = "explicit-jar-version"
        };

        var merged = VersionJsonMerger.Merge(child, parent);

        Assert.Equal("explicit-jar-version", merged.Jar);
    }
}
