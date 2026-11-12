using SandianshuiLauncher.Core;

namespace SandianshuiLauncher.App.Tests;

/// <summary>
/// 窗口底部版本号展示的回归测试：版本号统一来自 <see cref="LauncherInfo"/>，
/// 避免改版本时漏改界面，或把开发期的临时文案（如 v0.1）带进发布版本。
/// </summary>
public class LauncherVersionUiTests
{
    [Fact]
    public void SidebarVersionText_BindsLauncherInfoDisplayVersion()
    {
        var document = ViewXaml.Load("MainWindow.xaml");

        var versionText = document.Descendants(ViewXaml.Wpf + "TextBlock")
            .FirstOrDefault(element => (string?)element.Attribute(ViewXaml.Xaml + "Name") == "VersionText");
        Assert.NotNull(versionText);

        string text = (string?)versionText!.Attribute("Text") ?? string.Empty;
        Assert.Contains("core:LauncherInfo.DisplayVersion", text);
        Assert.Contains("开源版", text);
    }

    [Fact]
    public void MainWindow_NoLongerMentionsDevelopmentVersion()
    {
        string xaml = File.ReadAllText(Path.Combine(ViewXaml.ProjectRoot, "Views", "MainWindow.xaml"));

        Assert.DoesNotContain("v0.1", xaml);
        Assert.DoesNotContain("学习开发版", xaml);
    }

    [Fact]
    public void LauncherInfoStaticReference_IsDeclaredInXaml()
    {
        var document = ViewXaml.Load("MainWindow.xaml");
        var root = document.Root;
        Assert.NotNull(root);

        var coreNamespace = root!.Attributes()
            .FirstOrDefault(attribute => attribute.IsNamespaceDeclaration
                                         && attribute.Name.LocalName == "core");
        Assert.NotNull(coreNamespace);
        Assert.Contains("SandianshuiLauncher.Core", (string?)coreNamespace!.Value);
    }

    [Fact]
    public void LauncherInfo_VersionIsOnePointZero()
    {
        Assert.Equal("1.0.0", LauncherInfo.Version);
        Assert.Equal("v1.0.0", LauncherInfo.DisplayVersion);
    }
}
