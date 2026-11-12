using System.Text.Json;
using SandianshuiLauncher.Core.Models;
using SandianshuiLauncher.Core.Services;
using Xunit;

namespace SandianshuiLauncher.Core.Tests;

/// <summary>
/// 加载器识别与按加载器挑选文件测试：
/// 从版本 JSON 识别 forge / neoforge / fabric / quilt；
/// 从多加载器文件列表里挑对构建（forge 不得误选 neoforge）。
/// </summary>
public class ModLoaderDetectorTests : IDisposable
{
    private readonly string _root;

    public ModLoaderDetectorTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "sdsl_loader_det_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch
        {
            // 清理失败不影响测试结果。
        }
    }

    private static VersionJson Version(params string[] libraryNames) => new()
    {
        Id = "test",
        Libraries = libraryNames
            .Select(n => new LibraryInfo { Name = n })
            .ToList()
    };

    [Theory]
    [InlineData("net.minecraftforge:forge:1.20.4-49.2.13", "forge")]
    [InlineData("net.neoforged:neoforge:20.4.237", "neoforge")]
    [InlineData("net.fabricmc:fabric-loader:0.15.11", "fabric")]
    [InlineData("org.quiltmc:quilt-loader:0.26.3", "quilt")]
    public void Detect_ByLibrary_ReturnsLoader(string library, string expected)
    {
        Assert.Equal(expected, ModLoaderDetector.Detect(Version(library)));
    }

    [Fact]
    public void Detect_ByFabricMainClass_ReturnsFabric()
    {
        var version = new VersionJson { Id = "x", MainClass = "net.fabricmc.loader.impl.launch.knot.KnotClient" };
        Assert.Equal("fabric", ModLoaderDetector.Detect(version));
    }

    [Fact]
    public void Detect_Vanilla_ReturnsNull()
    {
        var version = new VersionJson { Id = "1.20.4", MainClass = "net.minecraft.client.main.Main", Libraries = [] };
        Assert.Null(ModLoaderDetector.Detect(version));
    }

    [Fact]
    public void DetectLoader_MergesInheritsChain()
    {
        // 原版 + Forge profile（libraries 在子版本里）。
        string vanillaDir = Path.Combine(_root, "versions", "1.20.4");
        Directory.CreateDirectory(vanillaDir);
        File.WriteAllText(Path.Combine(vanillaDir, "1.20.4.json"),
            """{ "id": "1.20.4", "mainClass": "net.minecraft.client.main.Main", "libraries": [] }""");

        string forgeId = "1.20.4-forge-49.2.13";
        string forgeDir = Path.Combine(_root, "versions", forgeId);
        Directory.CreateDirectory(forgeDir);
        File.WriteAllText(Path.Combine(forgeDir, forgeId + ".json"),
            """{ "id": "1.20.4-forge-49.2.13", "inheritsFrom": "1.20.4", "libraries": [ { "name": "net.minecraftforge:forge:1.20.4-49.2.13" } ] }""");

        Assert.Equal("forge", VersionService.DetectLoader(_root, forgeId));
    }

    [Fact]
    public void DetectLoader_MissingVersion_ReturnsNull()
    {
        Assert.Null(VersionService.DetectLoader(_root, "not-exist"));
    }

    [Theory]
    [InlineData("fabric", "Fabric")]
    [InlineData("forge", "Forge")]
    [InlineData("neoforge", "NeoForge")]
    [InlineData("quilt", "Quilt")]
    [InlineData(null, "原版")]
    [InlineData("", "原版")]
    [InlineData("unknown", "原版")]
    public void DisplayName_Mapping(string? loader, string expected)
    {
        Assert.Equal(expected, ModLoaderDetector.DisplayName(loader));
    }

    // ── 文件挑选 ──────────────────────────────────────────────

    private static ModFileInfo MakeFile(string name, bool primary = false) => new()
    {
        FileName = name,
        Url = "https://cdn.modrinth.com/data/x/versions/v/" + name,
        Primary = primary
    };

    private static ModVersionInfo VersionWithFiles(params (string Name, bool Primary)[] files) => new()
    {
        VersionId = "v1",
        Name = "v1",
        Files = files.Select(f => MakeFile(f.Name, f.Primary)).ToList()
    };

    [Fact]
    public void SelectFileForLoader_Forge_PicksForgeBuildNotNeoForge()
    {
        var version = VersionWithFiles(
            ("mod-1.0.0-fabric.jar", true),
            ("mod-1.0.0-forge.jar", false),
            ("mod-1.0.0-neoforge.jar", false));

        ModFileInfo? file = ModService.SelectFileForLoader(version, "forge");

        Assert.Equal("mod-1.0.0-forge.jar", file!.FileName);
    }

    [Fact]
    public void SelectFileForLoader_NeoForge_PicksNeoForgeBuild()
    {
        var version = VersionWithFiles(
            ("mod-1.0.0-fabric.jar", true),
            ("mod-1.0.0-forge.jar", false),
            ("mod-1.0.0-neoforge.jar", false));

        ModFileInfo? file = ModService.SelectFileForLoader(version, "neoforge");

        Assert.Equal("mod-1.0.0-neoforge.jar", file!.FileName);
    }

    [Fact]
    public void SelectFileForLoader_NoLoader_ReturnsPrimary()
    {
        var version = VersionWithFiles(
            ("mod-1.0.0-fabric.jar", false),
            ("mod-1.0.0-forge.jar", true));

        ModFileInfo? file = ModService.SelectFileForLoader(version, null);

        Assert.Equal("mod-1.0.0-forge.jar", file!.FileName);
    }

    [Theory]
    [InlineData("cloth-config-13.3.1-neoforge.jar", "neoforge", true)]
    [InlineData("cloth-config-13.3.1-neoforge.jar", "forge", false)]
    [InlineData("cloth-config-13.3.1-forge.jar", "forge", true)]
    [InlineData("cloth-config-13.3.1-fabric.jar", "fabric", true)]
    [InlineData("jei-1.20.4-forge-17.0.0.9.jar", "forge", true)]
    public void IsFileForLoader_Matching(string fileName, string loader, bool expected)
    {
        Assert.Equal(expected, ModService.IsFileForLoader(fileName, loader));
    }
}
