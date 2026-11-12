namespace SandianshuiLauncher.App.Tests;

/// <summary>
/// 主窗口动效回归测试：侧边导航的过渡高亮、内容页切换的淡入动画。
/// 动画定义散落在 ControlTemplate 与窗口资源里，容易被后续改样式时静默删掉，
/// 这里用 XAML 结构断言把它们钉住。
/// </summary>
public class MainWindowAnimationTests
{
    [Fact]
    public void NavItem_HasAnimatedSelectionHighlight()
    {
        var document = ViewXaml.Load("MainWindow.xaml");

        var selected = document.Descendants(ViewXaml.Wpf + "Trigger")
            .Where(trigger => (string?)trigger.Attribute("Property") == "IsSelected")
            .ToList();
        Assert.NotEmpty(selected);

        var animations = selected.SelectMany(trigger => trigger.Descendants(ViewXaml.Wpf + "DoubleAnimation")).ToList();
        Assert.Contains(animations, animation => (string?)animation.Attribute("Storyboard.TargetName") == "SelectLayer"
                                                 && (string?)animation.Attribute("Storyboard.TargetProperty") == "Opacity");
        Assert.Contains(animations, animation => (string?)animation.Attribute("Storyboard.TargetName") == "ContentSlide"
                                                 && (string?)animation.Attribute("Storyboard.TargetProperty") == "X");

        // 取消选中时也要有淡出，否则切换页面会残留两块高亮。
        Assert.Contains(selected, trigger => trigger.Element(ViewXaml.Wpf + "Trigger.ExitActions") is not null);
    }

    [Fact]
    public void NavItem_HasAnimatedHoverHighlight()
    {
        var document = ViewXaml.Load("MainWindow.xaml");

        var hover = document.Descendants(ViewXaml.Wpf + "Trigger")
            .FirstOrDefault(trigger => (string?)trigger.Attribute("Property") == "IsMouseOver");
        Assert.NotNull(hover);

        var layers = hover!.Descendants(ViewXaml.Wpf + "DoubleAnimation")
            .Select(animation => (string?)animation.Attribute("Storyboard.TargetName"))
            .ToList();
        Assert.Contains("HoverLayer", layers);
    }

    [Fact]
    public void PageHost_FadesAndSlidesOnSwitch()
    {
        var document = ViewXaml.Load("MainWindow.xaml");

        var host = document.Descendants(ViewXaml.Wpf + "ContentControl")
            .FirstOrDefault(element => (string?)element.Attribute(ViewXaml.Xaml + "Name") == "PageHost");
        Assert.NotNull(host);
        Assert.Contains("CurrentPage", (string?)host!.Attribute("Content") ?? string.Empty, StringComparison.Ordinal);

        var storyboard = document.Descendants(ViewXaml.Wpf + "Storyboard")
            .FirstOrDefault(element => (string?)element.Attribute(ViewXaml.Xaml + "Key") == "PageTransition");
        Assert.NotNull(storyboard);

        var animations = storyboard!.Descendants(ViewXaml.Wpf + "DoubleAnimation").ToList();
        Assert.Contains(animations, animation => (string?)animation.Attribute("Storyboard.TargetProperty") == "Opacity"
                                                 && (string?)animation.Attribute("From") == "0.35");
        Assert.Contains(animations, animation => ((string?)animation.Attribute("Storyboard.TargetProperty"))
            ?.Contains("TranslateTransform.Y", StringComparison.Ordinal) == true);
    }

    [Fact]
    public void PageTransition_IsTriggeredOnNavigation()
    {
        // 结构上再完整，没被调用也不会动：这里直接守代码后置的接线。
        var code = File.ReadAllText(Path.Combine(ViewXaml.ProjectRoot, "Views", "MainWindow.xaml.cs"));

        Assert.Contains("PlayPageTransition()", code, StringComparison.Ordinal);
        Assert.Contains("\"PageTransition\"", code, StringComparison.Ordinal);
        Assert.Contains("storyboard.Begin(", code, StringComparison.Ordinal);
    }
}
