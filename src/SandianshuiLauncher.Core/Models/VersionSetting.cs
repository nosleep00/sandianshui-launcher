namespace SandianshuiLauncher.Core.Models;

/// <summary>
/// 单个版本的独立配置覆盖：字段为 null 表示沿用全局设置。
/// </summary>
public sealed class VersionSetting
{
    public string VersionId { get; set; } = string.Empty;

    public int? MaxMemoryMb { get; set; }

    public int? MinMemoryMb { get; set; }

    public string? JavaPath { get; set; }

    public string? ExtraJvmArgs { get; set; }

    public int? WindowWidth { get; set; }

    public int? WindowHeight { get; set; }

    public bool? Fullscreen { get; set; }
}
