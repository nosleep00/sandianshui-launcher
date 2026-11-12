using SandianshuiLauncher.Core.Models;
using SandianshuiLauncher.Core.Services;
using Xunit;

namespace SandianshuiLauncher.Core.Tests;

public class ModLoaderTests
{
    private static ForgeProcessorContext MakeContext() => new()
    {
        GameDirectory = Path.Combine("E:", "MC"),
        LibrariesDir = Path.Combine("E:", "MC", "libraries"),
        McVersion = "1.20.4",
        InstallerPath = Path.Combine("C:", "tmp", "installer.jar"),
        InstallerExt = Path.Combine("C:", "tmp", "installer"),
        Data = new Dictionary<string, ForgeDataEntry>
        {
            ["MAPPINGS"] = new()
            {
                Client = "[de.oceanlabs.mcp:mcp_config:1.20.4-20231207.112700:mappings@tsrg]"
            },
            ["BINPATCH"] = new() { Client = "/data/client.lzma" },
            ["MAPPINGS_SHA"] = new() { Client = "'a9852bd9'" }
        }
    };

    [Fact]
    public void Bmcl_RewritesForgeMaven()
    {
        var source = new BmclCompatibleSource("https://bmclapi2.bangbang93.com");
        const string path = "net/minecraftforge/forge/1.20.4-49.2.13/forge-1.20.4-49.2.13-installer.jar";

        Assert.Equal(
            "https://bmclapi2.bangbang93.com/maven/" + path,
            source.Rewrite("https://maven.minecraftforge.net/" + path));
        Assert.Equal(
            "https://bmclapi2.bangbang93.com/maven/" + path,
            source.Rewrite("https://files.minecraftforge.net/maven/" + path));
    }

    [Fact]
    public void FabricMaven_UsesMirror()
    {
        var source = new BmclCompatibleSource("https://bsmirror.hkmc.online");
        Assert.Equal(
            "https://bsmirror.hkmc.online/maven/net/fabricmc/fabric-loader/0.20.1/fabric-loader-0.20.1.jar",
            source.Rewrite("https://maven.fabricmc.net/net/fabricmc/fabric-loader/0.20.1/fabric-loader-0.20.1.jar"));
    }

    [Fact]
    public void Processor_AppliesToClient()
    {
        Assert.True(ForgeProcessorResolver.AppliesToClient(new ForgeProcessor()));
        Assert.True(ForgeProcessorResolver.AppliesToClient(
            new ForgeProcessor { Sides = ["client"] }));
        Assert.True(ForgeProcessorResolver.AppliesToClient(
            new ForgeProcessor { Sides = ["client", "server"] }));
        Assert.False(ForgeProcessorResolver.AppliesToClient(
            new ForgeProcessor { Sides = ["server"] }));
    }

    [Fact]
    public void Resolve_BuiltInPlaceholders()
    {
        ForgeProcessorContext ctx = MakeContext();

        // SIDE 必须为小写 client，以匹配版本 JSON 中的 client_mappings 键。
        Assert.Equal("client", ForgeProcessorResolver.ResolveToken("{SIDE}", ctx));
        Assert.Equal("1.20.4", ForgeProcessorResolver.ResolveToken("{MINECRAFT_VERSION}", ctx));
        Assert.Equal(
            Path.Combine("E:", "MC", "libraries"),
            ForgeProcessorResolver.ResolveToken("{LIBRARY_DIR}", ctx));
        Assert.Equal(
            Path.Combine("E:", "MC", "versions", "1.20.4", "1.20.4.jar"),
            ForgeProcessorResolver.ResolveToken("{MINECRAFT_JAR}", ctx));
        Assert.Equal(
            Path.Combine("C:", "tmp", "installer.jar"),
            ForgeProcessorResolver.ResolveToken("{INSTALLER}", ctx));
    }

    [Fact]
    public void Resolve_DataPlaceholders()
    {
        ForgeProcessorContext ctx = MakeContext();

        // [Maven 坐标] → 本地 libraries 路径（含 classifier 与自定义扩展名）
        Assert.Equal(
            Path.Combine("E:", "MC", "libraries", "de", "oceanlabs", "mcp", "mcp_config",
                "1.20.4-20231207.112700", "mcp_config-1.20.4-20231207.112700-mappings.tsrg"),
            ForgeProcessorResolver.ResolveToken("{MAPPINGS}", ctx));

        // /开头 → 安装器解压目录内文件
        Assert.Equal(
            Path.Combine("C:", "tmp", "installer", "data", "client.lzma"),
            ForgeProcessorResolver.ResolveToken("{BINPATCH}", ctx));

        // '单引号字面量' → 去掉引号
        Assert.Equal("a9852bd9", ForgeProcessorResolver.ResolveToken("{MAPPINGS_SHA}", ctx));
    }

    [Fact]
    public void Resolve_BracketMavenToken()
    {
        ForgeProcessorContext ctx = MakeContext();

        Assert.Equal(
            Path.Combine("E:", "MC", "libraries", "de", "oceanlabs", "mcp", "mcp_config",
                "1.20.4-20231207.112700", "mcp_config-1.20.4-20231207.112700.zip"),
            ForgeProcessorResolver.ResolveToken(
                "[de.oceanlabs.mcp:mcp_config:1.20.4-20231207.112700@zip]", ctx));
    }

    [Fact]
    public void Resolve_PatchedClientPath()
    {
        ForgeProcessorContext ctx = MakeContext();

        Assert.Equal(
            Path.Combine("E:", "MC", "libraries", "net", "minecraftforge", "forge",
                "1.20.4-49.2.13", "forge-1.20.4-49.2.13-client.jar"),
            ForgeProcessorResolver.MavenLocalPath(
                "net.minecraftforge:forge:1.20.4-49.2.13:client", ctx.LibrariesDir));
    }

    [Fact]
    public void Resolve_PlainToken_Unchanged()
    {
        Assert.Equal("--output", ForgeProcessorResolver.ResolveToken("--output", MakeContext()));
    }

    [Fact]
    public void Resolve_UnknownPlaceholder_Throws()
    {
        Assert.Throws<InvalidDataException>(
            () => ForgeProcessorResolver.ResolveToken("{NOPE}", MakeContext()));
    }
}
