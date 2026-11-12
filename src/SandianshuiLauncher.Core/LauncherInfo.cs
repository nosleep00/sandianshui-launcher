namespace SandianshuiLauncher.Core;

/// <summary>
/// 启动器的公开身份信息（版本号、名称、网络请求 User-Agent）。
/// 版本号只在这里维护一份，程序集属性、界面显示与 HTTP 请求全部引用它，
/// 避免各处版本号不一致。
/// </summary>
public static class LauncherInfo
{
    /// <summary>语义化版本号。</summary>
    public const string Version = "1.0.0";

    /// <summary>界面上显示的版本号。</summary>
    public const string DisplayVersion = "v1.0.0";

    /// <summary>启动器名称。</summary>
    public const string Name = "三点水启动器";

    /// <summary>英文名称。</summary>
    public const string EnglishName = "Sandianshui Launcher";

    /// <summary>用作网络标识的无空格代号（可用于 User-Agent、日志前缀等）。</summary>
    public const string Slug = "SandianshuiLauncher";

    /// <summary>
    /// 访问 Modrinth / CurseForge 等服务时使用的 User-Agent。
    /// 不包含任何个人联系方式：各平台只要求能识别请求来自哪个启动器版本。
    /// </summary>
    public const string UserAgent = Slug + "/" + Version;
}
