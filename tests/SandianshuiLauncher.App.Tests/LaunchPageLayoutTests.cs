namespace SandianshuiLauncher.App.Tests;

/// <summary>
/// 启动页（首页）布局回归测试。
/// 背景：首页原先是「账号卡片 + 版本卡片 + 运行日志」三段式，
/// 日志区被换成「循环动画 + 启动进度条」。这里用 XAML 结构守护这次改版，
/// 避免以后有人把日志列表加回来、或把动画/进度条删掉。
/// </summary>
public class LaunchPageLayoutTests
{
    [Fact]
    public void Page_NoLongerShowsRuntimeLog()
    {
        var document = ViewXaml.Load("LaunchPage.xaml");

        Assert.DoesNotContain(
            document.Descendants(),
            element => (string?)element.Attribute("Text") == "运行日志");

        Assert.DoesNotContain(
            document.Descendants(ViewXaml.Wpf + "ListBox"),
            element => ((string?)element.Attribute("ItemsSource"))?.Contains("LogLines", StringComparison.Ordinal) == true);
    }

    [Fact]
    public void Page_HasLoopingAnimationStartedOnLoad()
    {
        var document = ViewXaml.Load("LaunchPage.xaml");

        // 环形动画由多条独立重复的动画组成：扫光弧、反向弧、光点绕行、光晕呼吸。
        var looping = document.Descendants(ViewXaml.Wpf + "DoubleAnimation")
            .Where(animation => (string?)animation.Attribute("RepeatBehavior") == "Forever")
            .ToList();
        Assert.True(looping.Count >= 4, $"循环动画至少应有 4 条（实际 {looping.Count} 条），否则动画会停在某一帧。");

        // 这些动画必须挂在 Loaded 事件上启动，否则永远不动。
        var loaded = document.Descendants(ViewXaml.Wpf + "EventTrigger")
            .FirstOrDefault(trigger => (string?)trigger.Attribute("RoutedEvent") == "Loaded");
        Assert.NotNull(loaded);
        Assert.NotNull(loaded!.Elements(ViewXaml.Wpf + "BeginStoryboard").FirstOrDefault());

        // TargetName 必须都能对上页面里命名的元素，否则动画静默失败。
        var names = document.Descendants()
            .Select(element => element.Attributes()
                .FirstOrDefault(attribute => attribute.Name.LocalName == "Name" && attribute.Name.Namespace == ViewXaml.Xaml))
            .Where(attribute => attribute is not null)
            .Select(attribute => attribute!.Value)
            .ToHashSet(StringComparer.Ordinal);

        foreach (var animation in looping)
        {
            string? target = (string?)animation.Attribute("TargetName");
            if (target is not null)
            {
                Assert.Contains(target, names);
            }
        }
    }

    [Fact]
    public void Page_HasProgressBarBoundToLaunchProgress()
    {
        var document = ViewXaml.Load("LaunchPage.xaml");

        var bar = Assert.Single(document.Descendants(ViewXaml.Wpf + "ProgressBar"));
        Assert.Contains("LaunchProgress", (string?)bar.Attribute("Value") ?? string.Empty, StringComparison.Ordinal);

        // 进度条右侧的百分比文本，以及进度条上方的阶段文案。
        var texts = document.Descendants(ViewXaml.Wpf + "TextBlock")
            .Select(element => (string?)element.Attribute("Text") ?? string.Empty)
            .ToList();
        Assert.Contains(texts, text => text.Contains("LaunchProgress", StringComparison.Ordinal)
                                       && text.Contains("{0:0}%", StringComparison.Ordinal));
        Assert.Contains(texts, text => text.Contains("StatusText", StringComparison.Ordinal));
    }
}
