using System.Windows;
using System.Windows.Media;
using Wpf.Ui.Controls;

namespace SandianshuiLauncher.Views;

/// <summary>消息弹窗类型（决定图标与配色）。</summary>
public enum MessageDialogKind
{
    Info,
    Warning,
    Error,
    Question
}

/// <summary>
/// 与主界面同风格的主题化消息弹窗（FluentWindow + 主题资源），
/// 替代原生 MessageBox，保证浅色/深色主题下观感一致。
/// </summary>
public partial class MessageDialogWindow : FluentWindow
{
    /// <summary>用户是否点击了主按钮（确定）。</summary>
    public bool Confirmed { get; private set; }

    public MessageDialogWindow(
        string message,
        string title,
        MessageDialogKind kind = MessageDialogKind.Info,
        bool showCancel = false,
        string primaryText = "确定",
        string secondaryText = "取消")
    {
        InitializeComponent();
        WindowTitle.Title = title;
        MessageText.Text = message;
        PrimaryButton.Content = primaryText;
        CancelButton.Content = secondaryText;
        CancelButton.Visibility = showCancel ? Visibility.Visible : Visibility.Collapsed;

        (IconText.Text, IconBadge.Background) = kind switch
        {
            MessageDialogKind.Warning => ("!", (Brush)FindResource("SystemFillColorCautionBrush")),
            MessageDialogKind.Error => ("✕", (Brush)FindResource("SystemFillColorCriticalBrush")),
            MessageDialogKind.Question => ("?", (Brush)FindResource("AccentFillColorDefaultBrush")),
            _ => ("i", (Brush)FindResource("AccentFillColorDefaultBrush"))
        };

        Loaded += (_, _) => PrimaryButton.Focus();
    }

    private void Primary_Click(object sender, RoutedEventArgs e)
    {
        Confirmed = true;
        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        Confirmed = false;
        DialogResult = false;
        Close();
    }
}
