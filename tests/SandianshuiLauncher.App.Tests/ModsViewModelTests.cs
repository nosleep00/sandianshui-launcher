using SandianshuiLauncher.Core.Models;
using SandianshuiLauncher.Core.Services;
using SandianshuiLauncher.ViewModels;

namespace SandianshuiLauncher.App.Tests;

/// <summary>
/// 模组删除/启停的对话框交互测试：用户取消时文件保留，确认时文件被删除。
/// </summary>
public class ModsViewModelTests : IDisposable
{
    private readonly TestServices _services = new();

    public void Dispose() => _services.Dispose();

    private (string ModsDir, InstalledModItem Item) SetupInstalledMod()
    {
        string modsDir = Path.Combine(_services.Config.Config.GameDirectory, "mods");
        Directory.CreateDirectory(modsDir);
        string jarPath = Path.Combine(modsDir, "test-mod.jar");
        File.WriteAllText(jarPath, "fake jar");

        _services.Mods.SelectedVersion = new LocalVersion { Id = "1.20.4" };
        Assert.Single(_services.Mods.InstalledMods);
        return (modsDir, _services.Mods.InstalledMods[0]);
    }

    [Fact]
    public void DeleteMod_UserCancels_FileRemains()
    {
        (string _, InstalledModItem item) = SetupInstalledMod();
        _services.Dialogs.ConfirmResult = false;

        _services.Mods.DeleteModCommand.Execute(item);

        Assert.True(File.Exists(item.Mod.FullPath));
        Assert.Equal(1, _services.Dialogs.ConfirmCalls);
    }

    [Fact]
    public void DeleteMod_UserConfirms_FileDeletedAndListRefreshed()
    {
        (string _, InstalledModItem item) = SetupInstalledMod();
        _services.Dialogs.ConfirmResult = true;

        _services.Mods.DeleteModCommand.Execute(item);

        Assert.False(File.Exists(item.Mod.FullPath));
        Assert.Empty(_services.Mods.InstalledMods);
    }

    [Fact]
    public void OpenModsFolder_RecordsFolderPath()
    {
        _services.Mods.SelectedVersion = new LocalVersion { Id = "1.20.4" };

        _services.Mods.OpenModsFolderCommand.Execute(null);

        string expected = Path.Combine(_services.Config.Config.GameDirectory, "mods");
        Assert.Contains(expected, _services.Dialogs.OpenedFolders);
    }

    [Fact]
    public void ToggleMod_DisablesThenEnables()
    {
        (string _, InstalledModItem item) = SetupInstalledMod();
        Assert.True(item.IsEnabled);

        _services.Mods.ToggleModCommand.Execute(item);

        Assert.False(File.Exists(Path.Combine(_services.Config.Config.GameDirectory, "mods", "test-mod.jar")));
        Assert.True(File.Exists(Path.Combine(_services.Config.Config.GameDirectory, "mods", "test-mod.jar.disabled")));
        InstalledModItem disabled = _services.Mods.InstalledMods.Single();
        Assert.False(disabled.IsEnabled);

        _services.Mods.ToggleModCommand.Execute(disabled);

        Assert.True(File.Exists(Path.Combine(_services.Config.Config.GameDirectory, "mods", "test-mod.jar")));
        Assert.True(_services.Mods.InstalledMods.Single().IsEnabled);
    }
}
