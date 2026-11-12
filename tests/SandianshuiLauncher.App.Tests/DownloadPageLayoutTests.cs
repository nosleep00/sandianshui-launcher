using System.Xml.Linq;

namespace SandianshuiLauncher.App.Tests;

/// <summary>
/// 下载页布局回归测试。
/// 背景：模组加载器卡片原先用固定像素列宽（Auto,170,Auto,120,Auto,200,*,Auto），
/// 在 125% 缩放下总宽超过卡片可用宽度，最后一列被压扁到几十像素，
/// 「安装加载器」按钮的文字因此被截断。此后改为星号列 + 共享列宽，
/// 并让按钮与页头「刷新版本列表」右对齐。这里直接用 XAML 结构守护这两条约定。
/// </summary>
public class DownloadPageLayoutTests
{
    private static readonly XNamespace Wpf = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
    private static readonly XNamespace Xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

    [Fact]
    public void LoaderRow_UsesFluidColumnWidths()
    {
        var grid = LoaderRowGrid();

        var widths = grid.Elements(Wpf + "Grid.ColumnDefinitions")
            .Elements(Wpf + "ColumnDefinition")
            .Select(column => (string?)column.Attribute("Width") ?? "*")
            .ToList();

        Assert.NotEmpty(widths);
        Assert.All(widths, width => Assert.True(
            IsFluid(width),
            $"模组加载器这一行不允许使用固定像素列宽（发现 \"{width}\"），否则窄窗口下按钮会被挤扁。"));
    }

    [Fact]
    public void LoaderRow_InstallButtonSharesWidthWithHeaderAction()
    {
        var document = LoadDocument();

        var shared = document.Descendants()
            .Where(element => (string?)element.Attribute("SharedSizeGroup") == "PageAction")
            .ToList();

        // 页头「刷新版本列表」与卡片内「安装加载器」各一列，共享宽度才能等宽且右缘对齐。
        Assert.Equal(2, shared.Count);

        var scope = document.Root!
            .Elements(Wpf + "Grid")
            .FirstOrDefault(grid => (string?)grid.Attribute("Grid.IsSharedSizeScope") == "True");
        Assert.NotNull(scope);
    }

    [Fact]
    public void LoaderRow_InstallButtonStretchesToSharedColumn()
    {
        var button = LoadDocument().Descendants()
            .Single(element => element.Name.LocalName == "Button"
                && (string?)element.Attribute("Content") == "安装加载器");

        // 共享列宽只统一了「列」，按钮必须 Stretch 才会填满该列；
        // 否则按钮按自身文字宽度收缩，右缘会比页头按钮短一截（实测差 4 DIP）。
        Assert.Equal("Stretch", (string?)button.Attribute("HorizontalAlignment"));

        // 同排的下拉框都是 32 DIP 高，按钮高度需一致，否则视觉上被压扁。
        Assert.Equal("32", (string?)button.Attribute("Height"));
    }

    [Fact]
    public void VersionList_HasSkeletonRowsWithLoopingPulse()
    {
        var document = LoadDocument();

        var skeletonRows = document.Descendants()
            .Where(element => ((string?)element.Attribute(ViewXaml.Xaml + "Name"))?.StartsWith("Skel", StringComparison.Ordinal) == true)
            .ToList();
        Assert.True(skeletonRows.Count >= 5, $"骨架屏应至少 5 行占位，实际 {skeletonRows.Count} 行。");

        var animations = document.Descendants(ViewXaml.Wpf + "DoubleAnimation")
            .Where(animation => ((string?)animation.Attribute("Storyboard.TargetName"))?.StartsWith("Skel", StringComparison.Ordinal) == true)
            .ToList();
        Assert.Equal(skeletonRows.Count, animations.Count);
        Assert.All(animations, animation => Assert.Equal("Forever", (string?)animation.Attribute("RepeatBehavior")));

        // 每行错开起播时间，避免整片同时闪烁。
        Assert.Contains(animations, animation => animation.Attribute("BeginTime") is not null);

        var trigger = document.Root!.Element(ViewXaml.Wpf + "UserControl.Triggers");
        Assert.NotNull(trigger);
        Assert.Contains("SkeletonPulse", trigger!.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void VersionList_PlaceholdersAreMutuallyExclusiveStates()
    {
        var document = LoadDocument();

        var visibilityBindings = document.Descendants()
            .Select(element => (string?)element.Attribute("Visibility"))
            .Where(value => value is not null)
            .ToList();

        Assert.Contains(visibilityBindings, value => value!.Contains("ShowSkeleton", StringComparison.Ordinal));
        Assert.Contains(visibilityBindings, value => value!.Contains("ShowEmptyState", StringComparison.Ordinal));
        Assert.Contains(visibilityBindings, value => value!.Contains("ShowNoMatch", StringComparison.Ordinal));

        // 空状态里要有能直接触发加载的按钮，否则用户只能去右上角找。
        var emptyStateButton = document.Descendants()
            .Single(element => element.Name.LocalName == "Button"
                && (string?)element.Attribute("Content") == "立即获取版本列表");
        Assert.Contains("LoadManifestCommand", (string?)emptyStateButton.Attribute("Command") ?? string.Empty, StringComparison.Ordinal);
    }

    private static XElement LoaderRowGrid()
    {
        // 按钮用的是 WPF-UI 的 ui:Button（http://schemas.lepo.co/wpfui/2022/xaml），
        // 因此按 LocalName 匹配，不限定命名空间。
        var button = LoadDocument().Descendants()
            .Single(element => element.Name.LocalName == "Button"
                && (string?)element.Attribute("Content") == "安装加载器");

        var grid = button.Parent;
        Assert.NotNull(grid);
        Assert.Equal("Grid", grid!.Name.LocalName);
        return grid;
    }

    /// <summary>Auto 或星号（含 1.6* 这类比例）都算自适应宽度。</summary>
    private static bool IsFluid(string width) =>
        string.Equals(width, "Auto", StringComparison.OrdinalIgnoreCase)
        || (width.EndsWith('*') && double.TryParse(
            width[..^1],
            System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture,
            out _))
        || string.Equals(width, "*", StringComparison.Ordinal);

    private static XDocument LoadDocument() => ViewXaml.Load("DownloadPage.xaml");
}
