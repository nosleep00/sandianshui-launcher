using SandianshuiLauncher.Core.Models;

namespace SandianshuiLauncher.Core.Services;

/// <summary>
/// 下载源的可变持有者：设置页切换下载源后通过 <see cref="Refresh"/> 重建，
/// 依赖下载源的服务（版本清单、游戏安装、加载器安装）引用本 Provider 而非具体实例，
/// 从而避免"改设置后服务实例失效、需手动重建"的问题。
/// </summary>
public class DownloadSourceProvider
{
    public DownloadSourceProvider(LauncherConfig config)
    {
        Current = DownloadSource.Create(config);
    }

    /// <summary>当前生效的下载源。</summary>
    public DownloadSource Current { get; private set; }

    /// <summary>按最新配置重建下载源实例。</summary>
    public void Refresh(LauncherConfig config)
    {
        Current = DownloadSource.Create(config);
    }
}
