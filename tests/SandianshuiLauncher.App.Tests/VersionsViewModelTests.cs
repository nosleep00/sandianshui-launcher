using SandianshuiLauncher.Core.Models;
using SandianshuiLauncher.Core.Services;

namespace SandianshuiLauncher.App.Tests;

/// <summary>版本管理 ViewModel 的重命名交互测试。</summary>
public class VersionsViewModelTests : IDisposable
{
    private readonly TestServices _services = new();

    public void Dispose() => _services.Dispose();

    [Fact]
    public void Rename_WithConfirmedName_RenamesVersion()
    {
        string versionDir = Path.Combine(_services.Config.Config.GameDirectory, "versions", "a");
        Directory.CreateDirectory(versionDir);
        File.WriteAllText(Path.Combine(versionDir, "a.json"), """{ "id": "a" }""");
        _services.Versions.Refresh();
        LocalVersion version = _services.Versions.LocalVersions.Single(v => v.Id == "a");
        _services.Dialogs.PromptTextResult = "c";

        _services.Versions.RenameCommand.Execute(version);

        Assert.True(Directory.Exists(Path.Combine(_services.Config.Config.GameDirectory, "versions", "c")));
        Assert.False(Directory.Exists(versionDir));
        // 重命名后已刷新列表（状态文案为统计信息）。
        Assert.Contains("共安装", _services.Versions.StatusText);
    }

    [Fact]
    public void Rename_UserCancels_NoChange()
    {
        string versionDir = Path.Combine(_services.Config.Config.GameDirectory, "versions", "a");
        Directory.CreateDirectory(versionDir);
        File.WriteAllText(Path.Combine(versionDir, "a.json"), """{ "id": "a" }""");
        _services.Versions.Refresh();
        LocalVersion version = _services.Versions.LocalVersions.Single(v => v.Id == "a");
        _services.Dialogs.PromptTextResult = "a"; // 输入与原名相同 → 取消操作。

        _services.Versions.RenameCommand.Execute(version);

        Assert.True(Directory.Exists(versionDir));
    }

    [Fact]
    public void InstallLoader_VanillaVersion_NavigatesToDownloadAndPreselects()
    {
        string versionDir = Path.Combine(_services.Config.Config.GameDirectory, "versions", "1.20.4");
        Directory.CreateDirectory(versionDir);
        File.WriteAllText(Path.Combine(versionDir, "1.20.4.json"), """{ "id": "1.20.4" }""");
        _services.Versions.Refresh();
        LocalVersion version = _services.Versions.LocalVersions.Single(v => v.Id == "1.20.4");

        _services.Versions.InstallLoaderCommand.Execute(version);

        Assert.Same(_services.Download, _services.Main.CurrentPage);
        Assert.Equal("1.20.4", _services.Download.Loader.SelectedTargetVersion);
    }

    [Fact]
    public void InstallLoader_ModdedVersion_ShowsInfoAndStays()
    {
        var version = new LocalVersion { Id = "1.20.4-forge", InheritsFrom = "1.20.4", LoaderName = "Forge" };

        _services.Versions.InstallLoaderCommand.Execute(version);

        Assert.Contains(_services.Dialogs.Infos, m => m.Contains("无需重复安装"));
        Assert.Same(_services.Launch, _services.Main.CurrentPage); // 未跳转
    }
}
