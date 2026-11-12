using SandianshuiLauncher.Core.Models;
using SandianshuiLauncher.Core.Services;
using Xunit;

namespace SandianshuiLauncher.Core.Tests;

public class ModServiceTests
{
    // ── GamePaths：mods 目录 ──────────────────────────────────

    [Fact]
    public void ModsDirectory_NonIsolated_UsesRoot()
    {
        string mods = GamePaths.GetModsDirectory(@"E:\MC", "1.20.4", versionIsolation: false);
        Assert.Equal(@"E:\MC\mods", mods);
    }

    [Fact]
    public void ModsDirectory_Isolated_UsesVersionFolder()
    {
        string mods = GamePaths.GetModsDirectory(@"E:\MC", "1.20.4", versionIsolation: true);
        Assert.Equal(@"E:\MC\versions\1.20.4\mods", mods);
    }

    // ── BuildFacets ───────────────────────────────────────────

    [Fact]
    public void Facets_NoVersionNoLoader_OnlyProjectType()
    {
        Assert.Equal("[[\"project_type:mod\"]]", ModService.BuildFacets(null, null));
    }

    [Fact]
    public void Facets_VersionAndLoader_AddsBoth()
    {
        Assert.Equal(
            "[[\"project_type:mod\"],[\"versions:1.20.4\"],[\"categories:fabric\"]]",
            ModService.BuildFacets("1.20.4", "fabric"));
    }

    [Fact]
    public void Facets_LoaderIsLowercased()
    {
        Assert.Contains("categories:neoforge", ModService.BuildFacets("1.21", "NeoForge"));
    }

    // ── BuildSearchUrl ────────────────────────────────────────

    [Fact]
    public void SearchUrl_UsesMirrorBaseAndEncodesQuery()
    {
        string url = ModService.BuildSearchUrl(
            ModService.McimApiBase, "vein miner", "1.20.4", "fabric", 30);

        Assert.StartsWith("https://mod.mcimirror.top/modrinth/v2/search?", url);
        Assert.Contains("query=vein%20miner", url);
        Assert.Contains("limit=30", url);
        Assert.Contains("facets=", url);
    }

    [Fact]
    public void SearchUrl_UsesOfficialBaseWhenNotMirror()
    {
        string url = ModService.BuildSearchUrl(
            ModService.ModrinthApiBase, "sodium", null, null, 20);
        Assert.StartsWith("https://api.modrinth.com/v2/search?", url);
    }

    // ── BuildVersionsUrl ──────────────────────────────────────

    [Fact]
    public void VersionsUrl_EncodesLoaderAndGameVersion()
    {
        string url = ModService.BuildVersionsUrl(
            ModService.McimApiBase, "veinminer", "1.20.4", "fabric");

        Assert.Equal(
            "https://mod.mcimirror.top/modrinth/v2/project/veinminer/version"
            + "?loaders=%5B%22fabric%22%5D&game_versions=%5B%221.20.4%22%5D",
            url);
    }

    [Fact]
    public void VersionsUrl_NoFilters_HasNoQueryString()
    {
        string url = ModService.BuildVersionsUrl(
            ModService.McimApiBase, "veinminer", null, null);
        Assert.Equal("https://mod.mcimirror.top/modrinth/v2/project/veinminer/version", url);
    }

    // ── RewriteFileUrl ────────────────────────────────────────

    [Fact]
    public void RewriteFileUrl_MirrorOn_RewritesCdnHostKeepsPath()
    {
        const string official = "https://cdn.modrinth.com/data/OhduvhIc/versions/KTrObKTw/Veinminer-2.0.4.jar";
        const string expected = "https://mod.mcimirror.top/data/OhduvhIc/versions/KTrObKTw/Veinminer-2.0.4.jar";
        Assert.Equal(expected, ModService.RewriteFileUrl(official, useMirror: true));
    }

    [Fact]
    public void RewriteFileUrl_MirrorOff_LeavesOfficial()
    {
        const string official = "https://cdn.modrinth.com/data/OhduvhIc/x.jar";
        Assert.Equal(official, ModService.RewriteFileUrl(official, useMirror: false));
    }

    [Fact]
    public void RewriteFileUrl_OtherHost_LeftUnchanged()
    {
        const string other = "https://example.com/files/a.jar";
        Assert.Equal(other, ModService.RewriteFileUrl(other, useMirror: true));
    }

    // ── SelectBestVersion：任意加载器排除服务端插件 ───────────

    private static ModVersionInfo MakeVersion(string id, string[] loaders, params (string name, bool primary)[] files)
    {
        return new ModVersionInfo
        {
            VersionId = id,
            Name = id,
            Loaders = loaders,
            Files = files.Select(f => new ModFileInfo
            {
                FileName = f.name,
                Url = "https://cdn.modrinth.com/" + f.name,
                Primary = f.primary
            }).ToList()
        };
    }

    [Fact]
    public void SelectBestVersion_AnyLoader_SkipsServerPlugin()
    {
        // 列表里最新的是 Paper/Purpur 服务端插件，不应被误选为模组。
        var plugin = MakeVersion("plugin", new[] { "paper", "purpur" }, ("Veinminer-2.1.0.jar", true));
        var fabric = MakeVersion("fabric", new[] { "fabric", "quilt" }, ("Veinminer-2.0.4.jar", true));
        var versions = new List<ModVersionInfo> { plugin, fabric };

        ModVersionInfo? picked = ModService.SelectBestVersion(versions, loader: null);

        Assert.NotNull(picked);
        Assert.Equal("fabric", picked!.VersionId);
        Assert.Equal("Veinminer-2.0.4.jar", picked.PrimaryFile!.FileName);
    }

    [Fact]
    public void SelectBestVersion_SpecificLoader_PicksMatching()
    {
        var plugin = MakeVersion("plugin", new[] { "paper" }, ("p.jar", true));
        var fabric = MakeVersion("fabric", new[] { "fabric" }, ("f.jar", true));
        var picked = ModService.SelectBestVersion(new List<ModVersionInfo> { plugin, fabric }, "fabric");
        Assert.Equal("fabric", picked!.VersionId);
    }

    [Fact]
    public void SelectBestVersion_OnlyPlugins_ReturnsNull()
    {
        var plugin = MakeVersion("plugin", new[] { "paper", "spigot" }, ("p.jar", true));
        Assert.Null(ModService.SelectBestVersion(new List<ModVersionInfo> { plugin }, null));
    }

    [Fact]
    public void SelectBestVersion_NoMatchingLoader_ReturnsNull()
    {
        var fabric = MakeVersion("fabric", new[] { "fabric" }, ("f.jar", true));
        Assert.Null(ModService.SelectBestVersion(new List<ModVersionInfo> { fabric }, "neoforge"));
    }

    // ── ModVersionInfo.PrimaryFile ────────────────────────────

    [Fact]
    public void PrimaryFile_PrefersPrimary()
    {
        var version = MakeVersion("v", new[] { "fabric" },
            ("a.jar", false), ("b.jar", true));
        Assert.Equal("b.jar", version.PrimaryFile!.FileName);
    }

    [Fact]
    public void PrimaryFile_NoPrimary_FallsBackToFirst()
    {
        var version = MakeVersion("v", new[] { "fabric" },
            ("a.jar", false), ("b.jar", false));
        Assert.Equal("a.jar", version.PrimaryFile!.FileName);
    }

    // ── 业务模型显示 ──────────────────────────────────────────

    [Fact]
    public void FileSizeDisplay_FormatsKbAndMb()
    {
        var kb = new ModFileInfo { FileName = "a.jar", Url = "https://x/a.jar", Size = 110 * 1024 };
        var mb = new ModFileInfo { FileName = "b.jar", Url = "https://x/b.jar", Size = 2 * 1024 * 1024 };
        Assert.Contains("KB", kb.SizeDisplay);
        Assert.Equal("2.0 MB", mb.SizeDisplay);
    }

    // ── 通用 BuildFacets：资源类型与功能分类 ─────────────────

    [Fact]
    public void Facets_WithCategory_AddsCategoryFacet()
    {
        string facets = ModService.BuildFacets(
            ResourceType.Mod, "1.20.4", "fabric", "worldgen");
        Assert.Equal(
            "[[\"project_type:mod\"],[\"versions:1.20.4\"]," +
            "[\"categories:fabric\"],[\"categories:worldgen\"]]",
            facets);
    }

    [Fact]
    public void Facets_OtherResourceType_UsesItsApiName()
    {
        string facets = ModService.BuildFacets(
            ResourceType.ResourcePack, null, null, null);
        Assert.Equal("[[\"project_type:resourcepack\"]]", facets);
    }

    [Fact]
    public void Facets_CategoryIsLowercased()
    {
        string facets = ModService.BuildFacets(
            ResourceType.Mod, null, null, "WorldGen");
        Assert.Contains("categories:worldgen", facets);
    }

    // ── 通用 BuildSearchUrl：排序与分类 ──────────────────────

    [Fact]
    public void SearchUrl_DownloadsSort_UsesDownloadsIndex()
    {
        var request = new ResourceSearchRequest
        {
            Type = ResourceType.Mod,
            GameVersion = "1.20.4",
            Category = "worldgen",
            Sort = ResourceSortType.Downloads,
            Limit = 10
        };
        string url = ModService.BuildSearchUrl(ModService.McimApiBase, request);

        Assert.Contains("index=downloads", url);
        Assert.Contains("limit=10", url);
        Assert.Contains("categories%3Aworldgen", url);
        Assert.DoesNotContain("query=", url);
    }

    [Fact]
    public void SearchUrl_FollowsSort_UsesFollowsIndex()
    {
        var request = new ResourceSearchRequest { Sort = ResourceSortType.Follows };
        string url = ModService.BuildSearchUrl(ModService.McimApiBase, request);
        Assert.Contains("index=follows", url);
    }

    // ── ModSearchResult.IsLibrary ────────────────────────────

    private static ModSearchResult MakeResult(string title, params string[] categories)
    {
        return new ModSearchResult
        {
            ProjectId = title,
            Slug = title.ToLowerInvariant(),
            Title = title,
            Categories = categories
        };
    }

    [Fact]
    public void IsLibrary_TrueWhenCategoryContainsLibrary()
    {
        Assert.True(MakeResult("TerraBlender", "fabric", "library", "worldgen").IsLibrary);
        Assert.False(MakeResult("BiomesOPlenty", "fabric", "worldgen").IsLibrary);
    }

    // ── PickTopContent：跳过纯依赖库 ─────────────────────────

    [Fact]
    public void PickTopContent_SkipsLibraryAndReturnsFirstContent()
    {
        var results = new List<ModSearchResult>
        {
            MakeResult("TerraBlender", "fabric", "library", "worldgen"),
            MakeResult("BiomesOPlenty", "fabric", "worldgen"),
            MakeResult("Other", "forge", "worldgen")
        };

        ModSearchResult? picked = ModService.PickTopContent(results);

        Assert.NotNull(picked);
        Assert.Equal("BiomesOPlenty", picked!.Title);
    }

    [Fact]
    public void PickTopContent_AllLibraries_ReturnsNull()
    {
        var results = new List<ModSearchResult>
        {
            MakeResult("LibA", "forge", "library"),
            MakeResult("LibB", "fabric", "library")
        };
        Assert.Null(ModService.PickTopContent(results));
    }

    [Fact]
    public void PickTopContent_Empty_ReturnsNull()
    {
        Assert.Null(ModService.PickTopContent([]));
    }
}
