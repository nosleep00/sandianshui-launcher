namespace SandianshuiLauncher.Core.Models;

/// <summary>启动游戏所需的全部参数。</summary>
public class LaunchOptions
{
    /// <summary>使用的账号。</summary>
    public required GameAccount Account { get; init; }

    /// <summary>启动的版本文件夹 Id。</summary>
    public required string VersionId { get; init; }

    /// <summary>已合并继承链的版本 JSON。</summary>
    public required VersionJson Version { get; init; }

    /// <summary>游戏根目录（.minecraft）。</summary>
    public required string GameDirectory { get; init; }

    /// <summary>java.exe 完整路径。</summary>
    public required string JavaPath { get; init; }

    /// <summary>最小内存（MB）。</summary>
    public int MinMemoryMb { get; init; } = 512;

    /// <summary>最大内存（MB）。</summary>
    public int MaxMemoryMb { get; init; } = 2048;

    /// <summary>窗口宽度（可空）。</summary>
    public int? WindowWidth { get; init; }

    /// <summary>窗口高度（可空）。</summary>
    public int? WindowHeight { get; init; }

    /// <summary>是否全屏。</summary>
    public bool Fullscreen { get; init; }

    /// <summary>额外 JVM 参数（原样追加）。</summary>
    public string? ExtraJvmArgs { get; init; }

    /// <summary>是否版本隔离。</summary>
    public bool VersionIsolation { get; init; }

    /// <summary>authlib-injector 的 jar 路径（外置登录，阶段 4）。</summary>
    public string? AuthlibInjectorPath { get; init; }
}
