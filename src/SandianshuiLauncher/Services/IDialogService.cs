using System.Diagnostics;
using System.IO;
using System.Windows;
using SandianshuiLauncher.Views;

namespace SandianshuiLauncher.Services;

/// <summary>
/// 对话框抽象：ViewModels 通过该接口弹提示/警告/错误/确认对话框与打开文件夹，
/// 替代直接调用 MessageBox / Process.Start，便于单元测试注入假实现。
/// </summary>
public interface IDialogService
{
    /// <summary>提示信息对话框（仅“确定”）。</summary>
    void Info(string message, string title = "提示");

    /// <summary>警告通知对话框（仅“确定”）。</summary>
    void Warn(string message, string title = "提示");

    /// <summary>错误对话框（仅“确定”）。</summary>
    void Error(string message, string title = "错误");

    /// <summary>确认对话框（确定/取消），返回用户是否确认。</summary>
    bool Confirm(string message, string title, bool isWarning = false);

    /// <summary>选择单个文件，返回所选路径（取消返回 null）。</summary>
    string? PickFile(string filter, string title);

    /// <summary>选择保存路径，返回所选路径（取消返回 null）。</summary>
    string? PickSaveFile(string filter, string title, string defaultFileName);

    /// <summary>选择文件夹，返回所选路径（取消返回 null）。</summary>
    string? PickFolder(string title);

    /// <summary>单行文本输入，返回输入内容（取消或空输入返回 null）。</summary>
    string? PromptText(string message, string title, string initial = "");

    /// <summary>打开文件夹（不存在则先创建）。</summary>
    void OpenFolder(string path);
}

/// <summary>
/// <see cref="IDialogService"/> 的默认实现：
/// 全部消息/确认弹窗使用与主界面同风格的 <see cref="MessageDialogWindow"/>
/// （跟随浅色/深色主题），不再使用样式割裂的原生 MessageBox。
/// </summary>
public sealed class DialogService : IDialogService
{
    public void Info(string message, string title = "提示")
        => Show(message, title, MessageDialogKind.Info, showCancel: false);

    public void Warn(string message, string title = "提示")
        => Show(message, title, MessageDialogKind.Warning, showCancel: false);

    public void Error(string message, string title = "错误")
        => Show(message, title, MessageDialogKind.Error, showCancel: false);

    public bool Confirm(string message, string title, bool isWarning = false)
        => Show(message, title,
               isWarning ? MessageDialogKind.Warning : MessageDialogKind.Question,
               showCancel: true);

    /// <summary>统一入口：有主窗口时以其为 Owner 居中，否则独立显示。</summary>
    private static bool Show(
        string message, string title, MessageDialogKind kind, bool showCancel)
    {
        var dialog = new MessageDialogWindow(message, title, kind, showCancel)
        {
            Owner = Application.Current?.MainWindow
        };
        return dialog.ShowDialog() == true;
    }

    public string? PickFile(string filter, string title)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog { Filter = filter, Title = title };
        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    public string? PickSaveFile(string filter, string title, string defaultFileName)
    {
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Filter = filter,
            Title = title,
            FileName = defaultFileName
        };
        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    public string? PickFolder(string title)
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog
        {
            Title = title,
            Multiselect = false
        };
        return dialog.ShowDialog() == true ? dialog.FolderName : null;
    }

    public string? PromptText(string message, string title, string initial = "")
    {
        var window = new PromptWindow(message, title, initial)
        {
            Owner = Application.Current?.MainWindow
        };
        return window.ShowDialog() == true ? window.Input : null;
    }

    public void OpenFolder(string path)
    {
        Directory.CreateDirectory(path);
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true });
    }
}
