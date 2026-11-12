using SandianshuiLauncher.Core.Models;
using SandianshuiLauncher.Core.Services;
using Xunit;

namespace SandianshuiLauncher.Core.Tests;

/// <summary>
/// <see cref="LaunchSettingsResolver"/> 测试：
/// 每版本覆盖配置的字段级解析、保存覆盖/移除、查找。
/// </summary>
public class LaunchSettingsResolverTests
{
    private static LauncherConfig GlobalConfig() => new()
    {
        MinMemoryMb = 512,
        MaxMemoryMb = 2048,
        JavaPath = @"C:\jdk17\bin\java.exe",
        ExtraJvmArgs = "-Dfile.encoding=UTF-8",
        WindowWidth = 854,
        WindowHeight = 480,
        Fullscreen = false
    };

    [Fact]
    public void Resolve_NoOverride_UsesGlobal()
    {
        LaunchSettings settings = LaunchSettingsResolver.Resolve(GlobalConfig(), "1.20.4");

        Assert.Equal(2048, settings.MaxMemoryMb);
        Assert.Equal(512, settings.MinMemoryMb);
        Assert.Equal(@"C:\jdk17\bin\java.exe", settings.JavaPath);
        Assert.Equal("-Dfile.encoding=UTF-8", settings.ExtraJvmArgs);
        Assert.Equal(854, settings.WindowWidth);
        Assert.Equal(480, settings.WindowHeight);
        Assert.False(settings.Fullscreen);
    }

    [Fact]
    public void Resolve_WithOverride_FieldLevelOverrides()
    {
        var config = GlobalConfig();
        config.VersionSettings.Add(new VersionSetting
        {
            VersionId = "1.20.4",
            MaxMemoryMb = 8192,
            JavaPath = @"C:\jdk21\bin\java.exe"
        });

        LaunchSettings settings = LaunchSettingsResolver.Resolve(config, "1.20.4");

        Assert.Equal(8192, settings.MaxMemoryMb);                 // 覆盖
        Assert.Equal(@"C:\jdk21\bin\java.exe", settings.JavaPath); // 覆盖
        Assert.Equal(512, settings.MinMemoryMb);                  // 未覆盖沿用全局
        Assert.Equal("-Dfile.encoding=UTF-8", settings.ExtraJvmArgs);
        Assert.Equal(854, settings.WindowWidth);
    }

    [Fact]
    public void Resolve_MatchIsCaseInsensitive()
    {
        var config = GlobalConfig();
        config.VersionSettings.Add(new VersionSetting { VersionId = "Fabric-1.20.4", MaxMemoryMb = 4096 });

        LaunchSettings settings = LaunchSettingsResolver.Resolve(config, "fabric-1.20.4");

        Assert.Equal(4096, settings.MaxMemoryMb);
    }

    [Fact]
    public void SaveOverride_StoresAndReplaces()
    {
        var config = GlobalConfig();
        LaunchSettingsResolver.SaveOverride(config, new VersionSetting { VersionId = "1.20.4", MaxMemoryMb = 4096 });
        LaunchSettingsResolver.SaveOverride(config, new VersionSetting { VersionId = "1.20.4", MaxMemoryMb = 6144 });

        VersionSetting? found = LaunchSettingsResolver.Find(config, "1.20.4");
        Assert.NotNull(found);
        Assert.Equal(6144, found!.MaxMemoryMb);
        Assert.Single(config.VersionSettings);
    }

    [Fact]
    public void SaveOverride_AllEmpty_RemovesOverride()
    {
        var config = GlobalConfig();
        config.VersionSettings.Add(new VersionSetting { VersionId = "1.20.4", MaxMemoryMb = 4096 });

        LaunchSettingsResolver.SaveOverride(config, new VersionSetting { VersionId = "1.20.4" });

        Assert.Null(LaunchSettingsResolver.Find(config, "1.20.4"));
        Assert.Empty(config.VersionSettings);
    }

    [Fact]
    public void Resolve_OverrideFullscreenTrue_OverridesGlobal()
    {
        var config = GlobalConfig();
        config.VersionSettings.Add(new VersionSetting { VersionId = "1.20.4", Fullscreen = true });

        LaunchSettings settings = LaunchSettingsResolver.Resolve(config, "1.20.4");

        Assert.True(settings.Fullscreen);
    }
}
