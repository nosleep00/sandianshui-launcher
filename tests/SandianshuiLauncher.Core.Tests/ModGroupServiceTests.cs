using SandianshuiLauncher.Core.Models;
using SandianshuiLauncher.Core.Services;
using Xunit;

namespace SandianshuiLauncher.Core.Tests;

/// <summary>
/// <see cref="ModGroupService"/> 测试：
/// 分组的保存/覆盖/删除/持久化恢复，以及应用分组时的启用禁用切换。
/// </summary>
public class ModGroupServiceTests : IDisposable
{
    private readonly string _root;
    private readonly string _gameDir;

    public ModGroupServiceTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "sdsl_group_" + Guid.NewGuid().ToString("N"));
        _gameDir = Path.Combine(_root, "mc");
        Directory.CreateDirectory(Path.Combine(_gameDir, "mods"));
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

    private List<InstalledMod> ScanMods()
    {
        return ModService.ScanInstalled(_gameDir, "1.20.4", versionIsolation: false);
    }

    private void CreateModFile(string fileName, bool enabled = true)
    {
        string actual = enabled ? fileName : fileName + ".disabled";
        File.WriteAllText(Path.Combine(_gameDir, "mods", actual), "fake");
    }

    [Fact]
    public void SaveGroup_PersistsAndRestores()
    {
        var service = new ModGroupService(_root);
        service.SaveGroup("生存整合", ["sodium.jar", "iris.jar"]);

        var restored = new ModGroupService(_root);

        ModGroup? group = restored.Find("生存整合");
        Assert.NotNull(group);
        Assert.Equal(["sodium.jar", "iris.jar"], group!.EnabledFiles);
    }

    [Fact]
    public void SaveGroup_SameName_Overwrites()
    {
        var service = new ModGroupService(_root);
        service.SaveGroup("方案A", ["a.jar"]);
        service.SaveGroup("方案A", ["b.jar", "c.jar"]);

        Assert.Single(service.Groups);
        Assert.Equal(["b.jar", "c.jar"], service.Find("方案A")!.EnabledFiles);
    }

    [Fact]
    public void SaveGroup_BlankName_Throws()
    {
        var service = new ModGroupService(_root);
        Assert.Throws<ArgumentException>(() => service.SaveGroup("   ", ["a.jar"]));
    }

    [Fact]
    public void RemoveGroup_RemovesAndPersists()
    {
        var service = new ModGroupService(_root);
        service.SaveGroup("方案A", ["a.jar"]);
        service.RemoveGroup("方案A");

        Assert.Empty(service.Groups);
        Assert.Empty(new ModGroupService(_root).Groups);
    }

    [Fact]
    public void ApplyGroup_EnablesListedAndDisablesOthers()
    {
        CreateModFile("sodium.jar", enabled: true);
        CreateModFile("iris.jar", enabled: true);
        CreateModFile("jei.jar", enabled: false);

        var service = new ModGroupService(_root);
        service.SaveGroup("光影组", ["sodium.jar"]); // 只启用 sodium

        (int enabled, int disabled) = service.ApplyGroup("光影组", ScanMods());

        Assert.Equal(0, enabled);   // sodium 本就启用
        Assert.Equal(1, disabled);  // iris 被禁用
        // jei 保持禁用不变（原为禁用且不在组内）。
        Assert.True(File.Exists(Path.Combine(_gameDir, "mods", "iris.jar.disabled")));
        Assert.True(File.Exists(Path.Combine(_gameDir, "mods", "sodium.jar")));
    }

    [Fact]
    public void ApplyGroup_EnablesPreviouslyDisabled()
    {
        CreateModFile("sodium.jar", enabled: false);
        CreateModFile("iris.jar", enabled: true);

        var service = new ModGroupService(_root);
        service.SaveGroup("全部", ["sodium.jar", "iris.jar"]);

        (int enabled, int disabled) = service.ApplyGroup("全部", ScanMods());

        Assert.Equal(1, enabled);
        Assert.Equal(0, disabled);
        Assert.True(File.Exists(Path.Combine(_gameDir, "mods", "sodium.jar")));
    }

    [Fact]
    public void ApplyGroup_MissingGroup_Throws()
    {
        var service = new ModGroupService(_root);
        Assert.Throws<InvalidDataException>(() => service.ApplyGroup("不存在", []));
    }

    [Fact]
    public void CorruptGroupFile_Tolerated()
    {
        File.WriteAllText(Path.Combine(_root, "mod-groups.json"), "{ broken");

        var service = new ModGroupService(_root);

        Assert.Empty(service.Groups);
    }
}
