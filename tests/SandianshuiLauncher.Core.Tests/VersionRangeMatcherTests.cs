using SandianshuiLauncher.Core.Services;
using Xunit;

namespace SandianshuiLauncher.Core.Tests;

/// <summary>
/// <see cref="VersionRangeMatcher"/> 测试：
/// 从日志 Expected range 挑选满足范围的最大版本，含边界与容错。
/// </summary>
public class VersionRangeMatcherTests
{
    private static readonly string[] ForgeCandidates = ["47.0.1", "47.1.0", "47.1.3", "47.3.12", "47.4.12"];

    [Fact]
    public void SelectBest_MinOnlyRange_PicksHighestSatisfying()
    {
        string? picked = VersionRangeMatcher.SelectBest(ForgeCandidates, "[47.1.3,)");

        Assert.Equal("47.4.12", picked);
    }

    [Fact]
    public void SelectBest_MinAndMaxRange_ExcludesOutOfRange()
    {
        string? picked = VersionRangeMatcher.SelectBest(ForgeCandidates, "[47.1,48.0)");

        Assert.Equal("47.4.12", picked);
    }

    [Fact]
    public void SelectBest_ExclusiveMin_ExcludesBoundary()
    {
        string? picked = VersionRangeMatcher.SelectBest(ForgeCandidates, "(47.1.3,)");

        // 47.1.3 本身被排除（开区间），最高满足者为 47.4.12。
        Assert.Equal("47.4.12", picked);
    }

    [Fact]
    public void SelectBest_TooHighRequirement_ReturnsNull()
    {
        string? picked = VersionRangeMatcher.SelectBest(ForgeCandidates, "[48.0,)");

        Assert.Null(picked);
    }

    [Fact]
    public void SelectBest_NoRange_PicksHighest()
    {
        string? picked = VersionRangeMatcher.SelectBest(ForgeCandidates, null);

        Assert.Equal("47.4.12", picked);
    }

    [Fact]
    public void SelectBest_SingleVersionNoBrackets_TreatedAsMinimum()
    {
        string? picked = VersionRangeMatcher.SelectBest(ForgeCandidates, "47.2");

        Assert.Equal("47.4.12", picked);
    }

    [Fact]
    public void ParseVersion_HandlesPlainAndVPrefix()
    {
        Assert.Equal(new Version(47, 4, 12), VersionRangeMatcher.ParseVersion("47.4.12"));
        Assert.Equal(new Version(0, 15, 11), VersionRangeMatcher.ParseVersion("v0.15.11"));
        Assert.Null(VersionRangeMatcher.ParseVersion("abc"));
    }

    [Fact]
    public void SelectBest_FabricLoaderStyleVersions()
    {
        string[] candidates = ["0.14.25", "0.15.10", "0.15.11"];

        string? picked = VersionRangeMatcher.SelectBest(candidates, "[0.15.0,)");

        Assert.Equal("0.15.11", picked);
    }
}
