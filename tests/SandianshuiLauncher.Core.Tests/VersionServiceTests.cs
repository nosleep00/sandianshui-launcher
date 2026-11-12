using SandianshuiLauncher.Core.Services;
using Xunit;

namespace SandianshuiLauncher.Core.Tests;

/// <summary>
/// <see cref="LocalVersion.GameVersion"/> 测试：
/// 验证资源接口过滤所用的基础游戏版本号解析正确。
/// </summary>
public class VersionServiceTests
{
    [Fact]
    public void GameVersion_ForgeProfile_ReturnsInheritsFrom()
    {
        var version = new LocalVersion
        {
            Id = "1.20.4-forge-49.2.13",
            InheritsFrom = "1.20.4"
        };

        Assert.Equal("1.20.4", version.GameVersion);
    }

    [Fact]
    public void GameVersion_FabricProfile_ReturnsInheritsFrom()
    {
        var version = new LocalVersion
        {
            Id = "fabric-loader-0.20.1-1.20.4",
            InheritsFrom = "1.20.4"
        };

        Assert.Equal("1.20.4", version.GameVersion);
    }

    [Fact]
    public void GameVersion_VanillaProfile_ReturnsOwnId()
    {
        var version = new LocalVersion
        {
            Id = "1.20.4",
            InheritsFrom = null
        };

        Assert.Equal("1.20.4", version.GameVersion);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void GameVersion_BlankInheritsFrom_FallsBackToId(string inheritsFrom)
    {
        var version = new LocalVersion
        {
            Id = "1.20.4",
            InheritsFrom = inheritsFrom
        };

        Assert.Equal("1.20.4", version.GameVersion);
    }

    // ───────── 重命名与复制 ─────────

    private sealed class TempVersions : IDisposable
    {
        public string GameDir { get; }

        public TempVersions()
        {
            GameDir = Path.Combine(Path.GetTempPath(), "sdsl_versions_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(GameDir);
        }

        public void AddVersion(string id, string json, string? jarContent = null)
        {
            string dir = Path.Combine(GameDir, "versions", id);
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, id + ".json"), json);
            if (jarContent is not null)
            {
                File.WriteAllText(Path.Combine(dir, id + ".jar"), jarContent);
            }
        }

        public string ReadJson(string id) => File.ReadAllText(Path.Combine(GameDir, "versions", id, id + ".json"));

        public void Dispose()
        {
            try { Directory.Delete(GameDir, recursive: true); } catch { }
        }
    }

    [Fact]
    public void Rename_MovesDir_UpdatesIdAndJarFields_AndFixesReferences()
    {
        using var temp = new TempVersions();
        temp.AddVersion("a", """{ "id": "a", "jar": "a", "libraries": [] }""", "jar-a");
        temp.AddVersion("b", """{ "id": "b", "inheritsFrom": "a" }""");

        VersionService.Rename(temp.GameDir, "a", "c");

        Assert.False(Directory.Exists(Path.Combine(temp.GameDir, "versions", "a")));
        Assert.True(File.Exists(Path.Combine(temp.GameDir, "versions", "c", "c.json")));
        Assert.True(File.Exists(Path.Combine(temp.GameDir, "versions", "c", "c.jar")));
        Assert.Contains("\"id\": \"c\"", temp.ReadJson("c"));
        Assert.Contains("\"jar\": \"c\"", temp.ReadJson("c"));
        Assert.Contains("\"inheritsFrom\": \"c\"", temp.ReadJson("b"));
    }

    [Fact]
    public void Rename_TargetExists_Throws()
    {
        using var temp = new TempVersions();
        temp.AddVersion("a", """{ "id": "a" }""");
        temp.AddVersion("c", """{ "id": "c" }""");

        Assert.Throws<InvalidDataException>(() => VersionService.Rename(temp.GameDir, "a", "c"));
    }

    [Fact]
    public void Rename_MissingSource_Throws()
    {
        using var temp = new TempVersions();

        Assert.Throws<InvalidDataException>(() => VersionService.Rename(temp.GameDir, "nope", "c"));
    }

    [Fact]
    public void Copy_ClonesFilesAndUpdatesId()
    {
        using var temp = new TempVersions();
        temp.AddVersion("a", """{ "id": "a", "jar": "a", "unknownField": "keep-me" }""", "jar-a");

        VersionService.Copy(temp.GameDir, "a", "c");

        Assert.True(File.Exists(Path.Combine(temp.GameDir, "versions", "c", "c.json")));
        Assert.True(File.Exists(Path.Combine(temp.GameDir, "versions", "c", "c.jar")));
        Assert.Contains("\"id\": \"c\"", temp.ReadJson("c"));
        // 原版本不受影响，且未知字段被保留（JsonNode 原位修改）。
        Assert.Contains("\"id\": \"a\"", temp.ReadJson("a"));
        Assert.Contains("keep-me", temp.ReadJson("c"));
    }

    [Fact]
    public void Copy_TargetExists_Throws()
    {
        using var temp = new TempVersions();
        temp.AddVersion("a", """{ "id": "a" }""");
        temp.AddVersion("c", """{ "id": "c" }""");

        Assert.Throws<InvalidDataException>(() => VersionService.Copy(temp.GameDir, "a", "c"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("bad/name")]
    [InlineData("bad\\name")]
    public void ValidateVersionId_Invalid_Throws(string id)
    {
        Assert.Throws<ArgumentException>(() => VersionService.ValidateVersionId(id));
    }

    [Fact]
    public void ValidateVersionId_Valid_DoesNotThrow()
    {
        VersionService.ValidateVersionId("1.20.4-fabric-0.20.1");
    }

    [Fact]
    public void ScanLocal_FillsLoaderName()
    {
        using var temp = new TempVersions();
        temp.AddVersion("1.20.4", """{ "id": "1.20.4", "mainClass": "net.minecraft.client.main.Main" }""");
        temp.AddVersion("1.20.4-forge", """{ "id": "1.20.4-forge", "inheritsFrom": "1.20.4", "libraries": [ { "name": "net.minecraftforge:forge:1.20.4-49.2.13" } ] }""");
        temp.AddVersion("1.20.4-fabric", """{ "id": "1.20.4-fabric", "inheritsFrom": "1.20.4", "mainClass": "net.fabricmc.loader.impl.launch.knot.KnotClient" }""");

        List<LocalVersion> versions = VersionService.ScanLocal(temp.GameDir);

        Assert.Equal("原版", versions.Single(v => v.Id == "1.20.4").LoaderName);
        Assert.Equal("Forge", versions.Single(v => v.Id == "1.20.4-forge").LoaderName);
        Assert.Equal("Fabric", versions.Single(v => v.Id == "1.20.4-fabric").LoaderName);
    }

    [Fact]
    public void ScanLocal_BrokenJson_LoaderNameEmpty()
    {
        using var temp = new TempVersions();
        temp.AddVersion("broken", "{ not valid json");

        List<LocalVersion> versions = VersionService.ScanLocal(temp.GameDir);

        Assert.Equal(string.Empty, versions.Single(v => v.Id == "broken").LoaderName);
    }
}
