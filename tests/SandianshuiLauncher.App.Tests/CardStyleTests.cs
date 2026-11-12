using System.Xml.Linq;
using Xunit;

namespace SandianshuiLauncher.App.Tests;

/// <summary>
/// 全局视觉规范测试：卡片阴影层级、hover 微抬升、按钮圆角。
/// 这些效果都依赖 App.xaml 里的共享样式与少量运行时代码，
/// 一旦有人改坏（例如把阴影删掉、把 hover 动画写错、把按钮圆角改回默认），本测试会立刻失败。
/// </summary>
public class CardStyleTests
{
    private static readonly XNamespace Ui = "http://schemas.lepo.co/wpfui/2022/xaml";

    [Fact]
    public void CardSurface_HasConsistentShadow()
    {
        var style = LoadAppStyle("CardSurface");

        var effect = style.Descendants(ViewXaml.Wpf + "DropShadowEffect").Single();
        Assert.Equal("16", (string?)effect.Attribute("BlurRadius"));
        Assert.Equal("2", (string?)effect.Attribute("ShadowDepth"));
        Assert.Equal("0.07", (string?)effect.Attribute("Opacity"));
    }

    [Fact]
    public void HoverCardSurface_IsBasedOnCardSurfaceAndLifts()
    {
        var style = LoadAppStyle("HoverCardSurface");

        Assert.Equal("{StaticResource CardSurface}", (string?)style.Attribute("BasedOn"));

        var trigger = style.Descendants(ViewXaml.Wpf + "Trigger")
            .Single(element => (string?)element.Attribute("Property") == "IsMouseOver"
                && (string?)element.Attribute("Value") == "True");

        var lift = trigger.Element(ViewXaml.Wpf + "Trigger.EnterActions")!
            .Descendants(ViewXaml.Wpf + "DoubleAnimation")
            .Single(animation => animation.ToString().Contains("TranslateTransform.Y", StringComparison.Ordinal));
        var reset = trigger.Element(ViewXaml.Wpf + "Trigger.ExitActions")!
            .Descendants(ViewXaml.Wpf + "DoubleAnimation")
            .Single(animation => animation.ToString().Contains("TranslateTransform.Y", StringComparison.Ordinal));

        // 抬升 2px，松手回到原位。
        Assert.Equal("-2", (string?)lift.Attribute("To"));
        Assert.Equal("0", (string?)reset.Attribute("To"));

        // 阴影同时加深一层，形成"抬高"的层次感。
        var hoverShadow = trigger.Descendants(ViewXaml.Wpf + "DropShadowEffect").Single();
        Assert.Equal("0.16", (string?)hoverShadow.Attribute("Opacity"));
    }

    [Fact]
    public void HoverCards_DeclareTheirOwnRenderTransform()
    {
        // 样式里的动画作用于 RenderTransform.TranslateTransform.Y，
        // 因此每个使用 HoverCardSurface 的卡片都必须内联声明变换实例，
        // 否则动画会作用到样式里被冻结的共享变换上（运行时报错）。
        var offenders = new List<string>();

        foreach (string file in Directory.GetFiles(Path.Combine(ViewXaml.ProjectRoot, "Views"), "*.xaml"))
        {
            XDocument document = XDocument.Load(file);
            foreach (var border in document.Descendants(ViewXaml.Wpf + "Border"))
            {
                if ((string?)border.Attribute("Style") != "{StaticResource HoverCardSurface}")
                {
                    continue;
                }

                bool hasTransform = border.Element(ViewXaml.Wpf + "Border.RenderTransform")
                    ?.Element(ViewXaml.Wpf + "TranslateTransform") is not null;
                if (!hasTransform)
                {
                    offenders.Add(Path.GetFileName(file));
                }
            }
        }

        Assert.True(offenders.Count == 0, $"这些卡片缺少内联 RenderTransform：{string.Join("、", offenders)}");
    }

    [Fact]
    public void InteractiveLists_DoNotPaintTheirOwnHover()
    {
        // 卡片自己负责 hover 效果；ListBoxItem 若保留默认模板，
        // 会在圆角卡片外面再套一个直角高亮方块。
        foreach (string page in new[] { "DownloadPage.xaml", "VersionsPage.xaml", "ModsPage.xaml" })
        {
            var itemStyle = ViewXaml.Load(page).Descendants(ViewXaml.Wpf + "Style")
                .FirstOrDefault(element => (string?)element.Attribute("TargetType") == "ListBoxItem");
            Assert.NotNull(itemStyle);

            bool transparent = itemStyle!.Descendants(ViewXaml.Wpf + "Setter")
                .Any(setter => (string?)setter.Attribute("Property") == "Background"
                    && (string?)setter.Attribute("Value") == "Transparent");
            bool flattened = itemStyle.Descendants(ViewXaml.Wpf + "ControlTemplate").Any();

            Assert.True(transparent && flattened, $"{page} 的列表容器仍会绘制自带的悬停高亮。");
        }
    }

    [Fact]
    public void Buttons_GetGlobalCornerRadiusAtStartup()
    {
        string appCode = File.ReadAllText(Path.Combine(ViewXaml.ProjectRoot, "App.xaml.cs"));

        // 必须在创建任何窗口之前改掉隐式样式，否则已创建的按钮拿不到新圆角。
        Assert.Contains("ApplyGlobalButtonCornerRadius();", appCode, StringComparison.Ordinal);
        Assert.Contains("new CornerRadius(6)", appCode, StringComparison.Ordinal);
        Assert.Contains("CornerRadiusProperty", appCode, StringComparison.Ordinal);
        Assert.Contains("TryFindResource(buttonType)", appCode, StringComparison.Ordinal);

        int startupIndex = appCode.IndexOf("ApplyGlobalButtonCornerRadius();", StringComparison.Ordinal);
        int windowIndex = appCode.IndexOf("new MainWindow", StringComparison.Ordinal);
        Assert.True(startupIndex < windowIndex, "按钮圆角必须在主窗口创建之前应用。");
    }

    private static XElement LoadAppStyle(string key)
    {
        string path = Path.Combine(ViewXaml.ProjectRoot, "App.xaml");
        XDocument document = XDocument.Load(path);
        var style = document.Descendants(ViewXaml.Wpf + "Style")
            .SingleOrDefault(element => (string?)element.Attribute(ViewXaml.Xaml + "Key") == key);
        Assert.NotNull(style);
        return style!;
    }
}
