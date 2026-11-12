using SandianshuiLauncher.Core;

namespace SandianshuiLauncher.Core.Tests;

public class LauncherInfoTests
{
    [Fact]
    public void Version_IsOnePointOne()
    {
        Assert.Equal("1.1.0", LauncherInfo.Version);
    }

    [Fact]
    public void DisplayVersion_MatchesVersionWithPrefix()
    {
        Assert.Equal("v" + LauncherInfo.Version, LauncherInfo.DisplayVersion);
    }

    [Fact]
    public void UserAgent_ContainsSlugAndVersion()
    {
        Assert.StartsWith(LauncherInfo.Slug + "/", LauncherInfo.UserAgent);
        Assert.Contains(LauncherInfo.Version, LauncherInfo.UserAgent);
        Assert.DoesNotContain(" ", LauncherInfo.UserAgent);
    }

    [Fact]
    public void Names_AreNotEmpty()
    {
        Assert.False(string.IsNullOrWhiteSpace(LauncherInfo.Name));
        Assert.False(string.IsNullOrWhiteSpace(LauncherInfo.EnglishName));
    }
}
