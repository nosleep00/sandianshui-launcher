namespace SandianshuiLauncher.Core.Models;

/// <summary>
/// 启动器全局配置，对应 %AppData%/SandianshuiLauncher/launcher_config.json。
/// </summary>
public class LauncherConfig
{
    /// <summary>配置文件格式版本，用于以后升级迁移。</summary>
    public int ConfigVersion { get; set; } = 1;

    /// <summary>游戏根目录（.minecraft）。</summary>
    public string GameDirectory { get; set; } = string.Empty;

    /// <summary>手动指定的 java.exe 路径；为空时自动检测。</summary>
    public string? JavaPath { get; set; }

    /// <summary>最大内存（MB）。</summary>
    public int MaxMemoryMb { get; set; } = 2048;

    /// <summary>最小内存（MB）。</summary>
    public int MinMemoryMb { get; set; } = 512;

    /// <summary>下载源标识：bmclapi（BMCLAPI，默认）/ guangsu（MC光速镜像）/ mojang（官方）/ custom（自定义镜像）。</summary>
    public string DownloadSourceId { get; set; } = "bmclapi";

    /// <summary>自定义镜像基础地址（DownloadSourceId 为 custom 时使用）。</summary>
    public string? CustomMirrorBaseUrl { get; set; }

    /// <summary>下载并发线程数。</summary>
    public int DownloadThreads { get; set; } = 32;

    /// <summary>下载失败重试次数。</summary>
    public int DownloadRetries { get; set; } = 3;

    /// <summary>
    /// 模组下载使用的 User-Agent。Modrinth 官方要求填写启动器标识，
    /// MCIM 镜像可能启用 UA 白名单（需向 MCIM 项目提交本 UA 报备）。
    /// </summary>
    public string ModUserAgent { get; set; } = LauncherInfo.UserAgent;

    /// <summary>模组搜索/下载是否优先走 MCIM 国内镜像（关闭则直连 Modrinth 官方）。</summary>
    public bool ModUseMirror { get; set; } = true;

    /// <summary>CurseForge API Key（选填）：配置后整合包中的 CurseForge 模组可自动下载。</summary>
    public string? CurseForgeApiKey { get; set; }

    /// <summary>是否启用版本隔离（每个版本独立 saves/mods 等目录）。</summary>
    public bool VersionIsolation { get; set; }

    /// <summary>游戏窗口宽度（0 表示不指定）。</summary>
    public int WindowWidth { get; set; }

    /// <summary>游戏窗口高度（0 表示不指定）。</summary>
    public int WindowHeight { get; set; }

    /// <summary>是否全屏。</summary>
    public bool Fullscreen { get; set; }

    /// <summary>启动游戏并检测到游戏窗口后，自动最小化启动器窗口。</summary>
    public bool MinimizeOnLaunch { get; set; }

    /// <summary>界面主题：dark（默认）/ light。</summary>
    public string Theme { get; set; } = "dark";

    /// <summary>额外 JVM 参数。</summary>
    public string? ExtraJvmArgs { get; set; }

    /// <summary>网络代理地址（预留）。</summary>
    public string? ProxyUrl { get; set; }

    /// <summary>已保存的账号列表。</summary>
    public List<GameAccount> Accounts { get; set; } = [];

    /// <summary>当前选中的账号 Id。</summary>
    public string? SelectedAccountId { get; set; }

    /// <summary>当前选中的版本 Id。</summary>
    public string? SelectedVersionId { get; set; }

    /// <summary>每版本独立配置覆盖（内存/Java/JVM 参数/窗口，字段级覆盖全局）。</summary>
    public List<VersionSetting> VersionSettings { get; set; } = [];
}
