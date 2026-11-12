using System.Xml.Linq;

namespace SandianshuiLauncher.App.Tests;

/// <summary>
/// 读取 <c>src/SandianshuiLauncher</c> 下的视图 XAML。
/// 本仓库无法在 xUnit 里真正渲染 WPF 界面（需要 Application 资源与 UI 线程），
/// 因此界面回归测试统一用 XAML 结构断言来守护布局约定。
/// </summary>
internal static class ViewXaml
{
    public static readonly XNamespace Wpf = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

    public static readonly XNamespace Xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

    /// <summary>WPF 项目根目录（<c>src/SandianshuiLauncher</c>）的绝对路径。</summary>
    public static string ProjectRoot { get; } = FindProjectRoot();

    /// <summary>按文件名（如 <c>LaunchPage.xaml</c>）加载视图 XAML。</summary>
    public static XDocument Load(string fileName)
    {
        var path = Path.Combine(ProjectRoot, "Views", fileName);
        Assert.True(File.Exists(path), $"未找到视图 XAML：{path}");
        return XDocument.Load(path);
    }

    private static string FindProjectRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "SandianshuiLauncher.slnx")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        return Path.Combine(directory!.FullName, "src", "SandianshuiLauncher");
    }
}
