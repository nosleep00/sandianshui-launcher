using SandianshuiLauncher.Core.Utils;
using Xunit;

namespace SandianshuiLauncher.Core.Tests;

public class MavenNameTests
{
    [Fact]
    public void Parse_BasicCoordinate()
    {
        var c = MavenName.Parse("org.lwjgl:lwjgl:3.3.1");
        Assert.Equal("org.lwjgl", c.Group);
        Assert.Equal("lwjgl", c.Artifact);
        Assert.Equal("3.3.1", c.Version);
        Assert.Null(c.Classifier);
        Assert.Equal("jar", c.Extension);
    }

    [Fact]
    public void Parse_WithClassifier()
    {
        var c = MavenName.Parse("org.lwjgl:lwjgl:3.3.1:natives-windows");
        Assert.Equal("natives-windows", c.Classifier);
        Assert.Equal(
            "org/lwjgl/lwjgl/3.3.1/lwjgl-3.3.1-natives-windows.jar",
            c.ToRepositoryPath());
    }

    [Fact]
    public void Parse_WithPackExtension()
    {
        var c = MavenName.Parse("com.example:mod:1.0@pom");
        Assert.Equal("pom", c.Extension);
        Assert.Equal("com/example/mod/1.0/mod-1.0.pom", c.ToRepositoryPath());
    }

    [Fact]
    public void Parse_Invalid_Throws()
    {
        Assert.Throws<FormatException>(() => MavenName.Parse("only:one"));
    }

    [Theory]
    [InlineData("natives-windows", true)]
    [InlineData("natives-windows-x86_64", true)]
    [InlineData("sources", false)]
    [InlineData(null, false)]
    public void IsNatives(string? classifier, bool expected)
    {
        Assert.Equal(expected, MavenName.IsNatives(classifier));
    }
}
