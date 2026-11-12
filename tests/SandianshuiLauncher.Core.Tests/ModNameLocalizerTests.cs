using SandianshuiLauncher.Core.Services;
using Xunit;

namespace SandianshuiLauncher.Core.Tests;

/// <summary>
/// <see cref="ModNameLocalizer"/> 测试：精确匹配、包含匹配、未命中回退原名。
/// </summary>
public class ModNameLocalizerTests
{
    [Theory]
    [InlineData("Just Enough Items", "JEI 物品管理器")]
    [InlineData("Sodium", "钠（Sodium）")]
    [InlineData("Create", "机械动力")]
    [InlineData("Xaero's Minimap", "Xaero 小地图")]
    public void Localize_ExactMatch_ReturnsChineseName(string title, string expected)
    {
        Assert.Equal(expected, ModNameLocalizer.Localize(title));
    }

    [Theory]
    [InlineData("Just Enough Items (JEI)", "JEI 物品管理器")]
    [InlineData("Xaero's Minimap (Fair-Play Edition)", "Xaero 小地图")]
    [InlineData("YUNG's Better Dungeons (Forge)", "YUNG 的更好地牢")]
    public void Localize_ContainsMatch_ReturnsChineseName(string title, string expected)
    {
        Assert.Equal(expected, ModNameLocalizer.Localize(title));
    }

    [Fact]
    public void Localize_LongestKeyWins()
    {
        // “Sodium Extra” 含 “Sodium”，但应命中更长键。
        Assert.Equal("钠扩展（Sodium Extra）", ModNameLocalizer.Localize("Sodium Extra"));
    }

    [Fact]
    public void Localize_UnknownTitle_ReturnsOriginal()
    {
        Assert.Equal("Some Unknown Mod", ModNameLocalizer.Localize("Some Unknown Mod"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Localize_Blank_ReturnsEmpty(string? title)
    {
        Assert.Equal(string.Empty, ModNameLocalizer.Localize(title));
    }

    [Fact]
    public void Localize_CaseInsensitive()
    {
        Assert.Equal("钠（Sodium）", ModNameLocalizer.Localize("SODIUM"));
    }
}
