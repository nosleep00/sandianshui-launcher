using SandianshuiLauncher.Core.Models;

namespace SandianshuiLauncher.ViewModels;

/// <summary>
/// 日志展示项：一行待显示文本及其解析出的级别，供界面按级别着色。
/// </summary>
public sealed class LogLineItem
{
    /// <summary>显示文本（游戏行为原始整行，启动器行为带时间戳的提示）。</summary>
    public required string Text { get; init; }

    /// <summary>日志级别，决定前景色。</summary>
    public GameLogLevel Level { get; init; }
}
