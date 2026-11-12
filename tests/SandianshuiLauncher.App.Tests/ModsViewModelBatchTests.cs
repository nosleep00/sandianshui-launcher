using SandianshuiLauncher.Core.Models;
using SandianshuiLauncher.Core.Services;
using SandianshuiLauncher.ViewModels;

namespace SandianshuiLauncher.App.Tests;

/// <summary>
/// 批量模组运维与分组管理测试：批量启用/禁用/删除、保存分组与应用分组切换方案。
/// </summary>
public class ModsViewModelBatchTests : IDisposable
{
    private readonly TestServices _services = new();

    public void Dispose() => _services.Dispose();

    private string ModsDir => Path.Combine(_services.Config.Config.GameDirectory, "mods");

    private void SetupMods()
    {
        Directory.CreateDirectory(ModsDir);
        File.WriteAllText(Path.Combine(ModsDir, "a.jar"), "a");
        File.WriteAllText(Path.Combine(ModsDir, "b.jar"), "b");
        File.WriteAllText(Path.Combine(ModsDir, "c.jar.disabled"), "c");
        _services.Mods.SelectedVersion = new LocalVersion { Id = "1.20.4" };
    }

    private InstalledModItem Item(string fileName)
        => _services.Mods.InstalledMods.Single(i => i.FileName == fileName);

    [Fact]
    public void BatchDisable_SelectedItems_Disabled()
    {
        SetupMods();
        Item("a.jar").IsSelected = true;
        Item("b.jar").IsSelected = true;

        _services.Mods.BatchDisableCommand.Execute(null);

        Assert.True(File.Exists(Path.Combine(ModsDir, "a.jar.disabled")));
        Assert.True(File.Exists(Path.Combine(ModsDir, "b.jar.disabled")));
        // 未选中的 c 保持禁用不变。
        Assert.True(File.Exists(Path.Combine(ModsDir, "c.jar.disabled")));
    }

    [Fact]
    public void BatchEnable_SelectedDisabled_Enabled()
    {
        SetupMods();
        Item("c.jar").IsSelected = true;

        _services.Mods.BatchEnableCommand.Execute(null);

        Assert.True(File.Exists(Path.Combine(ModsDir, "c.jar")));
        Assert.False(File.Exists(Path.Combine(ModsDir, "c.jar.disabled")));
    }

    [Fact]
    public void BatchDelete_Confirmed_RemovesSelectedFiles()
    {
        SetupMods();
        Item("a.jar").IsSelected = true;
        Item("b.jar").IsSelected = true;
        _services.Dialogs.ConfirmResult = true;

        _services.Mods.BatchDeleteCommand.Execute(null);

        Assert.False(File.Exists(Path.Combine(ModsDir, "a.jar")));
        Assert.False(File.Exists(Path.Combine(ModsDir, "b.jar")));
        Assert.True(File.Exists(Path.Combine(ModsDir, "c.jar.disabled")));
    }

    [Fact]
    public void SaveGroup_ThenApplyGroup_SwitchesCombination()
    {
        SetupMods();
        // 当前状态：a、b 启用，c 禁用 → 保存为分组「轻量」。
        _services.Dialogs.PromptTextResult = "轻量";
        _services.Mods.SaveGroupCommand.Execute(null);

        // 手动改状态：启用 c、禁用 b。
        _services.Mods.ToggleModCommand.Execute(Item("b.jar"));
        _services.Mods.ToggleModCommand.Execute(Item("c.jar"));
        Assert.True(File.Exists(Path.Combine(ModsDir, "b.jar.disabled")));
        Assert.True(File.Exists(Path.Combine(ModsDir, "c.jar")));

        // 应用分组「轻量」→ 回到 a、b 启用，c 禁用。
        _services.Mods.SelectedGroupName = "轻量";
        _services.Mods.ApplyGroupCommand.Execute(null);

        Assert.True(File.Exists(Path.Combine(ModsDir, "a.jar")));
        Assert.True(File.Exists(Path.Combine(ModsDir, "b.jar")));
        Assert.True(File.Exists(Path.Combine(ModsDir, "c.jar.disabled")));
        Assert.Contains("轻量", _services.Mods.GroupNames);
    }
}
