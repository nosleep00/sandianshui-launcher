using System.Windows;
using Microsoft.Win32;
using SandianshuiLauncher.Core.Models;
using SandianshuiLauncher.Core.Services;
using Wpf.Ui.Controls;

namespace SandianshuiLauncher.Views;

/// <summary>
/// 版本独立设置窗口：为单个版本覆盖内存 / Java / JVM 参数 / 窗口配置，
/// 留空表示沿用全局设置。
/// </summary>
public partial class VersionSettingsWindow : FluentWindow
{
    private readonly ConfigService _config;
    private readonly string _versionId;

    public VersionSettingsWindow(ConfigService config, string versionId)
    {
        _config = config;
        _versionId = versionId;
        InitializeComponent();

        WindowTitle.Title = $"版本设置 - {versionId}";
        LauncherConfig global = _config.Config;
        HintText.Text =
            $"版本：{versionId}\n以下留空的项将沿用全局设置。" +
            $"当前全局：内存 {global.MinMemoryMb}–{global.MaxMemoryMb} MB" +
            (string.IsNullOrWhiteSpace(global.JavaPath) ? "，Java 自动" : $"，Java {global.JavaPath}") +
            "。可在「设置」页修改全局值。";

        VersionSetting? existing = LaunchSettingsResolver.Find(global, versionId);
        if (existing is null)
        {
            return;
        }
        MinMemoryBox.Text = existing.MinMemoryMb?.ToString() ?? string.Empty;
        MaxMemoryBox.Text = existing.MaxMemoryMb?.ToString() ?? string.Empty;
        JavaPathBox.Text = existing.JavaPath ?? string.Empty;
        JvmArgsBox.Text = existing.ExtraJvmArgs ?? string.Empty;
        WidthBox.Text = existing.WindowWidth?.ToString() ?? string.Empty;
        HeightBox.Text = existing.WindowHeight?.ToString() ?? string.Empty;
        FullscreenSwitch.IsChecked = existing.Fullscreen ?? false;
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (!TryParseOptional(MinMemoryBox.Text, out int? min) ||
            !TryParseOptional(MaxMemoryBox.Text, out int? max) ||
            !TryParseOptional(WidthBox.Text, out int? width) ||
            !TryParseOptional(HeightBox.Text, out int? height))
        {
            new MessageDialogWindow("内存与窗口宽高必须为数字（留空 = 使用全局设置）。", "输入有误", MessageDialogKind.Warning) { Owner = this }.ShowDialog();
            return;
        }
        if (min is not null && max is not null && min > max)
        {
            new MessageDialogWindow("最大内存不能小于最小内存。", "输入有误", MessageDialogKind.Warning) { Owner = this }.ShowDialog();
            return;
        }

        LaunchSettingsResolver.SaveOverride(_config.Config, new VersionSetting
        {
            VersionId = _versionId,
            MinMemoryMb = min,
            MaxMemoryMb = max,
            JavaPath = string.IsNullOrWhiteSpace(JavaPathBox.Text) ? null : JavaPathBox.Text.Trim(),
            ExtraJvmArgs = string.IsNullOrWhiteSpace(JvmArgsBox.Text) ? null : JvmArgsBox.Text,
            WindowWidth = width,
            WindowHeight = height,
            Fullscreen = FullscreenSwitch.IsChecked == true ? true : null
        });
        _config.Save();
        DialogResult = true;
        Close();
    }

    private void Reset_Click(object sender, RoutedEventArgs e)
    {
        LaunchSettingsResolver.SaveOverride(_config.Config, new VersionSetting { VersionId = _versionId });
        _config.Save();
        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void BrowseJava_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "选择 java.exe",
            Filter = "Java 可执行文件|java.exe|可执行文件|*.exe"
        };
        if (dialog.ShowDialog() == true)
        {
            JavaPathBox.Text = dialog.FileName;
        }
    }

    private static bool TryParseOptional(string text, out int? value)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            value = null;
            return true;
        }
        if (int.TryParse(text.Trim(), out int parsed))
        {
            value = parsed;
            return true;
        }
        value = null;
        return false;
    }
}
