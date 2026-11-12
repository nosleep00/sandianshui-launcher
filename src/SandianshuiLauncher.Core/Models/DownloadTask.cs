namespace SandianshuiLauncher.Core.Models;

/// <summary>
/// 一个待下载文件。
/// </summary>
public class DownloadTask
{
    public DownloadTask(
        string url,
        string destinationPath,
        string? sha1 = null,
        long size = 0,
        string? displayName = null,
        IReadOnlyList<string>? fallbackUrls = null)
    {
        Url = url;
        DestinationPath = destinationPath;
        Sha1 = sha1;
        Size = size;
        DisplayName = displayName ?? Path.GetFileName(destinationPath);
        FallbackUrls = fallbackUrls ?? [];
    }

    /// <summary>主下载地址（已经过下载源重写）。</summary>
    public string Url { get; }

    /// <summary>备用下载地址（主地址失败时按顺序回退）。</summary>
    public IReadOnlyList<string> FallbackUrls { get; }

    /// <summary>全部候选地址（主地址在前，备用地址依次在后）。</summary>
    public IReadOnlyList<string> AllUrls
    {
        get
        {
            if (FallbackUrls.Count == 0)
            {
                return [Url];
            }
            var all = new List<string>(FallbackUrls.Count + 1) { Url };
            all.AddRange(FallbackUrls);
            return all;
        }
    }

    /// <summary>本地目标绝对路径。</summary>
    public string DestinationPath { get; }

    /// <summary>期望的 SHA1（为空则不校验）。</summary>
    public string? Sha1 { get; }

    /// <summary>文件大小（字节，0 表示未知）。</summary>
    public long Size { get; }

    /// <summary>进度展示用的文件名。</summary>
    public string DisplayName { get; }
}

/// <summary>批量下载进度。</summary>
public class DownloadProgress
{
    public int TotalFiles { get; init; }

    public int CompletedFiles { get; init; }

    public long TotalBytes { get; init; }

    public long DownloadedBytes { get; init; }

    /// <summary>当前正在下载的文件展示名。</summary>
    public string CurrentFile { get; init; } = string.Empty;

    /// <summary>百分比（0-100），总大小未知时按文件数估算。</summary>
    public double Percent =>
        TotalBytes > 0
            ? Math.Clamp(Math.Round(DownloadedBytes * 100.0 / TotalBytes, 1), 0, 100)
            : TotalFiles > 0
                ? Math.Clamp(Math.Round(CompletedFiles * 100.0 / TotalFiles, 1), 0, 100)
                : 0;
}
