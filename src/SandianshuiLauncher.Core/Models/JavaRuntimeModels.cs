namespace SandianshuiLauncher.Core.Models;

/// <summary>
/// 一个可供下载的 Java 运行时安装包（目前来自 Adoptium / Temurin 的 JRE 分发）。
/// </summary>
/// <param name="Url">安装包的下载地址。</param>
/// <param name="FileName">安装包文件名（用于日志与临时文件命名）。</param>
/// <param name="Sha256">官方给出的 SHA256；镜像站不提供校验文件时为空，此时只校验压缩包可解压。</param>
/// <param name="Size">预计字节数；未知时为 0。</param>
/// <param name="Version">运行时版本（如 <c>17.0.21+9</c>），仅用于界面展示与日志。</param>
public sealed record JavaRuntimePackage(string Url, string FileName, string? Sha256, long Size, string Version);

/// <summary>Java 运行时下载/安装进度：<see cref="Stage"/> 为阶段说明，<see cref="Percent"/> 为 0–100 的总体百分比。</summary>
public sealed record JavaRuntimeProgress(string Stage, double Percent, string Message)
{
    /// <summary>便于界面直接绑定的百分比文本（如 "42%"）。</summary>
    public string PercentText => $"{Math.Clamp(Percent, 0, 100):0}%";
}
