using System.Windows.Controls;

namespace SandianshuiLauncher.Views;

/// <summary>
/// 启动页。
/// 首页的运行日志已被「循环动画 + 启动进度条」取代：动画由 XAML 里的 Storyboard 驱动，
/// 进度由 ViewModel 的 <c>LaunchProgress</c> 提供，因此这里不再需要滚动日志的代码。
/// </summary>
public partial class LaunchPage : UserControl
{
    public LaunchPage() => InitializeComponent();
}
