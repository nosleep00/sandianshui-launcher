namespace SandianshuiLauncher.App.Tests;

/// <summary>
/// 搜索框占位文案回归测试：输入框为空时只显示一条灰色提示，
/// 否则界面上就是一个不知道能搜什么的空条（README 截图里尤其明显）。
/// </summary>
public class SearchPlaceholderTests
{
    [Fact]
    public void DownloadPage_VersionSearchHasPlaceholder()
    {
        AssertHasSearchPlaceholder("DownloadPage.xaml", "搜索版本号");
    }

    [Fact]
    public void ModsPage_ModSearchHasPlaceholder()
    {
        AssertHasSearchPlaceholder("ModsPage.xaml", "搜索模组");
    }

    private static void AssertHasSearchPlaceholder(string fileName, string expectedHint)
    {
        var document = ViewXaml.Load(fileName);

        // 搜索框是 WPF-UI 的 ui:TextBox（命名空间与 WPF 原生不同），按本地名匹配。
        var boxes = document.Descendants()
            .Where(element => element.Name.LocalName == "TextBox")
            .Where(element => ((string?)element.Attribute("Text") ?? string.Empty).Contains("{Binding SearchText"))
            .ToList();
        Assert.NotEmpty(boxes);

        string? placeholder = (string?)boxes[0].Attribute("PlaceholderText");
        Assert.False(string.IsNullOrWhiteSpace(placeholder));
        Assert.Contains(expectedHint, placeholder);
    }
}
