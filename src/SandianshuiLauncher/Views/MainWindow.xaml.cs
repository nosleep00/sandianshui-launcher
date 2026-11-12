using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;
using SandianshuiLauncher.Infrastructure;
using SandianshuiLauncher.ViewModels;
using Wpf.Ui.Controls;

namespace SandianshuiLauncher.Views;

/// <summary>主窗口：侧边栏导航与拖拽安装。</summary>
public partial class MainWindow : FluentWindow
{
    public MainWindow()
    {
        InitializeComponent();
        NavList.SelectedIndex = 0;
    }

    private void NavList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (NavList.SelectedItem is not ListBoxItem item)
        {
            return;
        }
        if (DataContext is MainViewModel viewModel
            && Enum.TryParse(item.Tag?.ToString(), ignoreCase: true, out PageKey page))
        {
            viewModel.NavigateCommand.Execute(page);
            PlayPageTransition();
        }
    }

    /// <summary>内容页切换：整页淡入 + 轻微上移，动画定义见 XAML 资源 <c>PageTransition</c>。</summary>
    private void PlayPageTransition()
    {
        if (TryFindResource("PageTransition") is Storyboard storyboard)
        {
            storyboard.Begin(this, isControllable: true);
        }
    }

    private void Window_DragOver(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            e.Effects = DragDropEffects.Copy;
            DropOverlay.Visibility = Visibility.Visible;
            e.Handled = true;
        }
        else
        {
            e.Effects = DragDropEffects.None;
        }
    }

    private void Window_DragLeave(object sender, DragEventArgs e)
    {
        DropOverlay.Visibility = Visibility.Collapsed;
    }

    private async void Window_Drop(object sender, DragEventArgs e)
    {
        DropOverlay.Visibility = Visibility.Collapsed;
        if (!e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            return;
        }
        if (e.Data.GetData(DataFormats.FileDrop) is not string[] files || files.Length == 0)
        {
            return;
        }
        if (DataContext is MainViewModel viewModel)
        {
            await viewModel.HandleDroppedFileAsync(files[0]);
        }
    }
}
