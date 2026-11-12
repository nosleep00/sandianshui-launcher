using System.IO;
using System.Linq;

namespace SandianshuiLauncher.App.Tests;

/// <summary>
/// 添加账号窗口的结构守卫：仿 PCL2 允许空用户名后，
/// 窗口要自适应内容高度（外置登录面板展开时才变高），并且要把"留空不能启动"讲清楚。
/// </summary>
public class AccountWindowLayoutTests
{
    [Fact]
    public void Window_HugsItsContentInsteadOfFixedHeight()
    {
        var window = ViewXaml.Load("AddAccountWindow.xaml").Root!;

        Assert.Equal("Height", (string?)window.Attribute("SizeToContent"));
        Assert.NotNull(window.Attribute("MinHeight"));
        // 固定 Height 会让离线模式下面板下方留一大片空白。
        Assert.Null(window.Attribute("Height"));
    }

    [Fact]
    public void UsernameHint_ExplainsThatEmptyNameCannotLaunch()
    {
        var document = ViewXaml.Load("AddAccountWindow.xaml");
        var hint = document.Descendants()
            .FirstOrDefault(e => (string?)e.Attribute(ViewXaml.Xaml + "Name") == "UsernameHint");

        Assert.NotNull(hint);
        Assert.Equal("TextBlock", hint!.Name.LocalName);
        string text = (string?)hint.Attribute("Text") ?? string.Empty;
        Assert.Contains("留空", text, StringComparison.Ordinal);
        Assert.Contains("不能启动游戏", text, StringComparison.Ordinal);
    }

    [Fact]
    public void RenameMode_UpdatesEveryTitleSurface()
    {
        string code = File.ReadAllText(
            Path.Combine(ViewXaml.ProjectRoot, "Views", "AddAccountWindow.xaml.cs"));

        // 补填用户名时窗口标题、标题栏、大标题、按钮文案都要跟着变，
        // 否则用户会以为自己在"再添加一个账号"。
        Assert.Contains("Title = \"设置用户名\";", code, StringComparison.Ordinal);
        Assert.Contains("WindowTitleBar.Title = \"设置用户名\";", code, StringComparison.Ordinal);
        Assert.Contains("HeadingText.Text = \"设置用户名\";", code, StringComparison.Ordinal);
        Assert.Contains("OkButton.Content = \"保存用户名\";", code, StringComparison.Ordinal);
    }
}
