using SandianshuiLauncher.Core.Models;

namespace SandianshuiLauncher.Core.Services;

/// <summary>
/// 本地版本加载器识别：从版本 JSON 的 mainClass 与依赖库坐标判断
/// 该版本使用的模组加载器（fabric / quilt / forge / neoforge），原版返回 null。
/// </summary>
public static class ModLoaderDetector
{
    /// <summary>从合并后的版本 JSON 识别加载器；无法识别（原版）返回 null。</summary>
    public static string? Detect(VersionJson version)
    {
        if (version.MainClass is { } mainClass)
        {
            if (mainClass.Contains("fabricmc", StringComparison.OrdinalIgnoreCase))
            {
                return "fabric";
            }
            if (mainClass.Contains("quiltmc", StringComparison.OrdinalIgnoreCase))
            {
                return "quilt";
            }
        }

        foreach (var library in version.Libraries ?? [])
        {
            string name = library.Name ?? string.Empty;
            if (name.StartsWith("net.fabricmc:", StringComparison.OrdinalIgnoreCase))
            {
                return "fabric";
            }
            if (name.StartsWith("org.quiltmc:", StringComparison.OrdinalIgnoreCase))
            {
                return "quilt";
            }
            if (name.StartsWith("net.neoforged:", StringComparison.OrdinalIgnoreCase))
            {
                return "neoforge";
            }
            if (name.StartsWith("net.minecraftforge:", StringComparison.OrdinalIgnoreCase))
            {
                return "forge";
            }
        }
        return null;
    }

    /// <summary>加载器标识的中文显示名（未识别时显示“原版”）。</summary>
    public static string DisplayName(string? loader) => loader?.ToLowerInvariant() switch
    {
        "fabric" => "Fabric",
        "forge" => "Forge",
        "neoforge" => "NeoForge",
        "quilt" => "Quilt",
        _ => "原版"
    };

    /// <summary>判断日志中的 mod id 是否为模组加载器自身（而非普通模组）。</summary>
    public static bool IsLoaderId(string? modId) => modId?.Trim().ToLowerInvariant() switch
    {
        "forge" => true,
        "neoforge" => true,
        "fabric" => true,
        "fabricloader" => true,
        "fabric_loader" => true,
        "quilt" => true,
        "quilt_loader" => true,
        _ => false
    };
}
