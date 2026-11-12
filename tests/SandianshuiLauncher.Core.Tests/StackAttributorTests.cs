using System.IO.Compression;
using System.Text;
using SandianshuiLauncher.Core.Models;
using SandianshuiLauncher.Core.Services;
using Xunit;

namespace SandianshuiLauncher.Core.Tests;

/// <summary>
/// 堆栈归因与模组元数据读取测试：
/// 验证类名提取、最长前缀匹配，以及 Fabric / Forge 两种 jar 元数据解析。
/// </summary>
public class StackAttributorTests : IDisposable
{
    private readonly string _root;

    public StackAttributorTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "sdsl_stack_" + Guid.NewGuid().ToString("N"));
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

    private static byte[] BuildJar(IReadOnlyDictionary<string, byte[]> entries)
    {
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach ((string name, byte[] data) in entries)
            {
                ZipArchiveEntry entry = zip.CreateEntry(name);
                using var stream = entry.Open();
                stream.Write(data, 0, data.Length);
            }
        }
        return ms.ToArray();
    }

    [Fact]
    public void ExtractClasses_FindsAtLines()
    {
        const string crashLog = """
        java.lang.RuntimeException: boom
            at net.minecraft.client.main.Main.main(Main.java:42)
            at com.example.coolmod.hooks.RenderHook.onRender(RenderHook.java:10)
            at net.minecraft.client.renderer.GameRenderer.render(GameRenderer.java:777)
        """;

        List<string> classes = StackAttributor.ExtractClasses(crashLog);

        Assert.Contains("net.minecraft.client.main.Main", classes);
        Assert.Contains("com.example.coolmod.hooks.RenderHook", classes);
        Assert.Equal(3, classes.Count);
    }

    [Fact]
    public void Attribute_LongestPackagePrefixWins()
    {
        var index = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["com.example"] = "mod-a",
            ["com.example.coolmod"] = "mod-b"
        };

        string? mod = StackAttributor.Attribute(
            ["com.example.coolmod.hooks.RenderHook"], index);

        Assert.Equal("mod-b", mod);
    }

    [Fact]
    public void Attribute_NoMatch_ReturnsNull()
    {
        var index = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["com.example"] = "mod-a"
        };

        Assert.Null(StackAttributor.Attribute(["org.other.lib.Thing"], index));
    }

    [Fact]
    public void ModMetadataReader_FabricJar_ReadsIdAndEntrypointPackages()
    {
        const string fabricJson = """
        {
          "schemaVersion": 1,
          "id": "coolmod",
          "version": "1.0.0",
          "entrypoints": {
            "main": [ "com.example.coolmod.CoolMod" ],
            "client": { "value": "com.example.coolmod.client.ClientInit" }
          }
        }
        """;
        string jarPath = Path.Combine(_root, "coolmod.jar");
        File.WriteAllBytes(jarPath, BuildJar(new Dictionary<string, byte[]>
        {
            ["fabric.mod.json"] = Encoding.UTF8.GetBytes(fabricJson),
            ["com/example/coolmod/CoolMod.class"] = [1, 2, 3]
        }));

        ModIdentity? identity = ModMetadataReader.Read(jarPath);

        Assert.NotNull(identity);
        Assert.Equal("coolmod", identity!.ModId);
        Assert.Contains("com.example.coolmod", identity.Packages);
    }

    [Fact]
    public void ModMetadataReader_ForgeJar_ReadsModIdAndTopPackage()
    {
        const string modsToml = """
        modLoader = "javafml"
        loaderVersion = "[49,)"
        [[mods]]
        modId = "forgemod"
        version = "1.0.0"
        """;
        string jarPath = Path.Combine(_root, "forgemod.jar");
        File.WriteAllBytes(jarPath, BuildJar(new Dictionary<string, byte[]>
        {
            ["META-INF/mods.toml"] = Encoding.UTF8.GetBytes(modsToml),
            ["top/forgemod/ForgeMod.class"] = [1, 2, 3],
            ["top/forgemod/hooks/Hook.class"] = [4, 5, 6],
            ["org/apache/commons/Helper.class"] = [7, 8, 9]
        }));

        ModIdentity? identity = ModMetadataReader.Read(jarPath);

        Assert.NotNull(identity);
        Assert.Equal("forgemod", identity!.ModId);
        Assert.Contains("top", identity.Packages);
    }

    [Fact]
    public void CrashAnalyzer_WithIndex_AttributesModFromStack()
    {
        const string crashLog = """
        [main/ERROR]: Mixin apply failed coolmod: client.mixins.json
        java.lang.RuntimeException: Mixin apply failed
            at com.example.coolmod.mixin.RenderMixin.apply(RenderMixin.java:1)
            at net.minecraft.client.renderer.GameRenderer.render(GameRenderer.java:777)
        """;
        var index = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["com.example.coolmod"] = "coolmod"
        };

        CrashDiagnosis diagnosis = CrashAnalyzer.Analyze(crashLog, index);

        Assert.Equal("coolmod", diagnosis.RelatedMod);
    }
}
