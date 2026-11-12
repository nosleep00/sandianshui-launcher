namespace SandianshuiLauncher.Core.Models;

/// <summary>
/// 检测到的一个 Java 运行环境。
/// </summary>
public class JavaInfo
{
    /// <summary>java.exe 完整路径。</summary>
    public string Path { get; set; } = string.Empty;

    /// <summary>原始版本字符串，如 "17.0.10"、"1.8.0_392"。</summary>
    public string Version { get; set; } = string.Empty;

    /// <summary>主版本号：8 / 17 / 21 等。</summary>
    public int MajorVersion { get; set; }

    /// <summary>是否为 64 位。</summary>
    public bool Is64Bit { get; set; }

    /// <summary>发行商（解析到的目录名）。</summary>
    public string Vendor { get; set; } = string.Empty;

    public override string ToString() => $"Java {MajorVersion} ({Version}) - {Path}";
}
