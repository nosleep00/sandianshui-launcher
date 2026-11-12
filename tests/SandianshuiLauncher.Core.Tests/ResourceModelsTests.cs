using SandianshuiLauncher.Core.Models;
using Xunit;

namespace SandianshuiLauncher.Core.Tests;

/// <summary>统一资源模型（ResourceType / ResourceSortType / ResourceSearchRequest）测试。</summary>
public class ResourceModelsTests
{
    // ── ResourceType → Modrinth project_type ─────────────────

    [Theory]
    [InlineData(ResourceType.Mod, "mod")]
    [InlineData(ResourceType.Modpack, "modpack")]
    [InlineData(ResourceType.ResourcePack, "resourcepack")]
    [InlineData(ResourceType.Shader, "shader")]
    [InlineData(ResourceType.DataPack, "datapack")]
    [InlineData(ResourceType.Plugin, "plugin")]
    [InlineData(ResourceType.World, "world")]
    public void ResourceType_MapsToApiName(ResourceType type, string expected)
    {
        Assert.Equal(expected, type.ToApiName());
    }

    [Fact]
    public void ResourceType_AllValues_HaveUniqueApiName()
    {
        var names = Enum.GetValues<ResourceType>().Select(t => t.ToApiName()).ToList();
        Assert.Equal(names.Count, names.Distinct().Count());
    }

    // ── ResourceSortType → Modrinth index ────────────────────

    [Theory]
    [InlineData(ResourceSortType.Relevance, "relevance")]
    [InlineData(ResourceSortType.Downloads, "downloads")]
    [InlineData(ResourceSortType.Follows, "follows")]
    [InlineData(ResourceSortType.Newest, "newest")]
    [InlineData(ResourceSortType.Updated, "updated")]
    public void SortType_MapsToApiIndex(ResourceSortType sort, string expected)
    {
        Assert.Equal(expected, sort.ToApiIndex());
    }

    // ── ResourceSearchRequest 默认值 ─────────────────────────

    [Fact]
    public void Request_Defaults_AreModRelevance()
    {
        var request = new ResourceSearchRequest();
        Assert.Equal(ResourceType.Mod, request.Type);
        Assert.Equal(ResourceSortType.Relevance, request.Sort);
        Assert.Null(request.GameVersion);
        Assert.Null(request.Loader);
        Assert.Null(request.Category);
        Assert.Equal(30, request.Limit);
    }

    [Fact]
    public void Request_CanHoldWorldgenDownloadsQuery()
    {
        var request = new ResourceSearchRequest
        {
            Type = ResourceType.Mod,
            GameVersion = "1.20.4",
            Loader = "fabric",
            Category = "worldgen",
            Sort = ResourceSortType.Downloads,
            Limit = 20
        };
        Assert.Equal("worldgen", request.Category);
        Assert.Equal("downloads", request.Sort.ToApiIndex());
    }
}
