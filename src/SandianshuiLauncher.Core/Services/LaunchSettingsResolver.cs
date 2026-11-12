using SandianshuiLauncher.Core.Models;

namespace SandianshuiLauncher.Core.Services;

/// <summary>解析后、启动实际使用的配置。</summary>
public sealed class LaunchSettings
{
    public required int MinMemoryMb { get; init; }

    public required int MaxMemoryMb { get; init; }

    public string? JavaPath { get; init; }

    public string? ExtraJvmArgs { get; init; }

    public int? WindowWidth { get; init; }

    public int? WindowHeight { get; init; }

    public required bool Fullscreen { get; init; }
}

/// <summary>
/// 每版本独立配置解析：<see cref="LauncherConfig.VersionSettings"/> 中与目标版本
/// 匹配的条目按字段级覆盖全局配置，未覆盖字段沿用全局值。
/// </summary>
public static class LaunchSettingsResolver
{
    /// <summary>解析某版本实际生效的启动配置。</summary>
    public static LaunchSettings Resolve(LauncherConfig config, string versionId)
    {
        VersionSetting? overrideSettings = config.VersionSettings?
            .FirstOrDefault(v => string.Equals(v.VersionId, versionId, StringComparison.OrdinalIgnoreCase));

        return new LaunchSettings
        {
            MinMemoryMb = overrideSettings?.MinMemoryMb ?? config.MinMemoryMb,
            MaxMemoryMb = overrideSettings?.MaxMemoryMb ?? config.MaxMemoryMb,
            JavaPath = overrideSettings?.JavaPath ?? config.JavaPath,
            ExtraJvmArgs = overrideSettings?.ExtraJvmArgs ?? config.ExtraJvmArgs,
            WindowWidth = overrideSettings?.WindowWidth ?? (config.WindowWidth > 0 ? config.WindowWidth : null),
            WindowHeight = overrideSettings?.WindowHeight ?? (config.WindowHeight > 0 ? config.WindowHeight : null),
            Fullscreen = overrideSettings?.Fullscreen ?? config.Fullscreen
        };
    }

    /// <summary>
    /// 保存/更新某版本的覆盖配置（同名覆盖）；全部字段为空时移除覆盖项。
    /// </summary>
    public static void SaveOverride(LauncherConfig config, VersionSetting setting)
    {
        config.VersionSettings ??= [];

        config.VersionSettings.RemoveAll(
            v => string.Equals(v.VersionId, setting.VersionId, StringComparison.OrdinalIgnoreCase));

        bool hasAny =
            setting.MinMemoryMb is not null
            || setting.MaxMemoryMb is not null
            || !string.IsNullOrWhiteSpace(setting.JavaPath)
            || !string.IsNullOrWhiteSpace(setting.ExtraJvmArgs)
            || setting.WindowWidth is not null
            || setting.WindowHeight is not null
            || setting.Fullscreen is not null;
        if (hasAny)
        {
            config.VersionSettings.Add(setting);
        }
    }

    /// <summary>查询某版本是否有独立配置。</summary>
    public static VersionSetting? Find(LauncherConfig config, string versionId)
        => config.VersionSettings?
            .FirstOrDefault(v => string.Equals(v.VersionId, versionId, StringComparison.OrdinalIgnoreCase));
}
