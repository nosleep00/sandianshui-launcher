using System.Diagnostics;
using System.IO;
using System.Windows;
using SandianshuiLauncher.Core.Models;
using SandianshuiLauncher.Services;
using Wpf.Ui.Controls;

namespace SandianshuiLauncher.Views;

/// <summary>崩溃诊断结果窗口：展示结构化的原因、建议与证据，并提供一键自动修复。</summary>
public partial class CrashDiagnosisWindow : FluentWindow
{
    private readonly string? _logDirectory;
    private readonly CrashFixService? _fixer;
    private readonly string? _versionId;

    public CrashDiagnosisWindow(
        CrashDiagnosis diagnosis,
        string? logDirectory = null,
        CrashFixService? fixer = null,
        string? versionId = null)
    {
        InitializeComponent();
        DataContext = diagnosis;
        _logDirectory = logDirectory;
        _fixer = fixer;
        _versionId = versionId;

        // 有可自动执行的修复动作时显示「一键修复」按钮。
        FixButton.Visibility = diagnosis.FixAction == CrashFixAction.None
            ? Visibility.Collapsed
            : Visibility.Visible;
    }

    private void Ok_Click(object sender, RoutedEventArgs e) => Close();

    private async void Fix_Click(object sender, RoutedEventArgs e)
    {
        if (_fixer is null || string.IsNullOrWhiteSpace(_versionId) || DataContext is not CrashDiagnosis diagnosis)
        {
            return;
        }

        FixButton.IsEnabled = false;
        FixButton.Content = "正在修复…";
        try
        {
            string message = await _fixer.ApplyAsync(diagnosis, _versionId);
            new MessageDialogWindow("修复完成\n\n" + message, "一键修复", MessageDialogKind.Info) { Owner = this }.ShowDialog();
        }
        catch (Exception ex)
        {
            new MessageDialogWindow("自动修复失败：\n" + ex.Message, "一键修复", MessageDialogKind.Error) { Owner = this }.ShowDialog();
        }
        finally
        {
            FixButton.IsEnabled = true;
            FixButton.Content = "一键修复";
        }
    }

    private void OpenLogs_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_logDirectory))
        {
            return;
        }
        Directory.CreateDirectory(_logDirectory);
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{_logDirectory}\"") { UseShellExecute = true });
    }
}
