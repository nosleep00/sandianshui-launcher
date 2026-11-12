using SandianshuiLauncher.Core.Models;
using SandianshuiLauncher.Core.Services;
using Xunit;

namespace SandianshuiLauncher.Core.Tests;

/// <summary>
/// Mod 启用/禁用与扫描测试：
/// 验证 .jar ↔ .jar.disabled 重命名、扫描同时列出两种状态、目标冲突时拒绝覆盖。
/// </summary>
public class ModManagementTests : IDisposable
{
    private readonly string _gameDir;

    public ModManagementTests()
    {
        _gameDir = Path.Combine(Path.GetTempPath(), "sdsl_mods_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(_gameDir, "mods"));
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_gameDir, recursive: true);
        }
        catch
        {
            // 清理失败不影响测试结果。
        }
    }

    private InstalledMod CreateMod(string fileName, bool disabled = false)
    {
        string actualName = disabled ? fileName + ".disabled" : fileName;
        string path = Path.Combine(_gameDir, "mods", actualName);
        File.WriteAllText(path, "fake jar");
        return new InstalledMod
        {
            FileName = fileName,
            FullPath = path,
            SizeBytes = 8,
            IsEnabled = !disabled
        };
    }

    [Fact]
    public void SetEnabled_Disable_RenamesToDisabled()
    {
        InstalledMod mod = CreateMod("sodium.jar");

        ModService.SetEnabled(mod, enabled: false);

        Assert.False(File.Exists(mod.FullPath));
        Assert.True(File.Exists(mod.FullPath + ".disabled"));
    }

    [Fact]
    public void SetEnabled_Enable_RestoresJarName()
    {
        InstalledMod mod = CreateMod("sodium.jar", disabled: true);

        ModService.SetEnabled(mod, enabled: true);

        Assert.True(File.Exists(Path.Combine(_gameDir, "mods", "sodium.jar")));
        Assert.False(File.Exists(mod.FullPath));
    }

    [Fact]
    public void SetEnabled_NoStateChange_IsNoOp()
    {
        InstalledMod mod = CreateMod("sodium.jar");

        ModService.SetEnabled(mod, enabled: true);

        Assert.True(File.Exists(mod.FullPath));
    }

    [Fact]
    public void SetEnabled_TargetExists_ThrowsAndKeepsOriginal()
    {
        InstalledMod mod = CreateMod("sodium.jar");
        File.WriteAllText(Path.Combine(_gameDir, "mods", "sodium.jar.disabled"), "existing");

        Assert.Throws<IOException>(() => ModService.SetEnabled(mod, enabled: false));
        Assert.True(File.Exists(mod.FullPath));
    }

    [Fact]
    public void ScanInstalled_ListsEnabledAndDisabledWithState()
    {
        CreateMod("sodium.jar");
        CreateMod("iris.jar", disabled: true);

        List<InstalledMod> mods = ModService.ScanInstalled(_gameDir, "1.20.4", versionIsolation: false);

        Assert.Equal(2, mods.Count);
        InstalledMod sodium = mods.Single(m => m.FileName == "sodium.jar");
        InstalledMod iris = mods.Single(m => m.FileName == "iris.jar");
        Assert.True(sodium.IsEnabled);
        Assert.False(iris.IsEnabled);
        // 展示名统一为 .jar 名称，不带 .disabled 后缀。
        Assert.DoesNotContain(mods, m => m.FileName.Contains(".disabled"));
    }

    [Fact]
    public void ScanInstalled_IgnoresNonJarFiles()
    {
        File.WriteAllText(Path.Combine(_gameDir, "mods", "notes.txt"), "not a mod");
        CreateMod("sodium.jar");

        List<InstalledMod> mods = ModService.ScanInstalled(_gameDir, "1.20.4", versionIsolation: false);

        Assert.Single(mods);
    }
}
