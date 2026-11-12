using System.Windows;
using System.Windows.Input;
using Wpf.Ui.Controls;

namespace SandianshuiLauncher.Views;

/// <summary>单行文本输入对话框（用于重命名等场景）。</summary>
public partial class PromptWindow : FluentWindow
{
    /// <summary>用户输入的文本（去除首尾空白）。</summary>
    public string Input => InputBox.Text.Trim();

    public PromptWindow(string message, string title, string initial)
    {
        InitializeComponent();
        MessageText.Text = message;
        WindowTitle.Title = title;
        InputBox.Text = initial;
        Loaded += (_, _) =>
        {
            InputBox.Focus();
            InputBox.SelectAll();
        };
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(InputBox.Text))
        {
            new MessageDialogWindow("输入不能为空。", "提示", MessageDialogKind.Warning) { Owner = this }.ShowDialog();
            return;
        }
        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void InputBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            Ok_Click(sender, e);
        }
    }
}
