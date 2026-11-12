using SandianshuiLauncher.Core.Models;
using SandianshuiLauncher.Core.Utils;

namespace SandianshuiLauncher.Core.Services;

/// <summary>
/// Forge 安装处理器的纯逻辑：端过滤、占位符/数据值解析、Maven 本地路径计算。
/// 提取为静态类以便单元测试与服务复用。
/// </summary>
public static class ForgeProcessorResolver
{
    /// <summary>处理器是否适用于客户端。</summary>
    public static bool AppliesToClient(ForgeProcessor processor)
    {
        if (processor.Sides is null || processor.Sides.Count == 0)
        {
            return true;
        }
        return processor.Sides.Contains("client", StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>解析命令行元素：{占位符}、[Maven 坐标] 或原样字符串。</summary>
    public static string ResolveToken(string token, ForgeProcessorContext context)
    {
        if (token.Length >= 2 && token.StartsWith('{') && token.EndsWith('}'))
        {
            return ResolvePlaceholder(token[1..^1], context);
        }
        if (token.Length >= 2 && token.StartsWith('[') && token.EndsWith(']'))
        {
            return MavenLocalPath(token[1..^1], context.LibrariesDir);
        }
        return token;
    }

    /// <summary>解析 {占位符}：内置变量或 data 条目。</summary>
    public static string ResolvePlaceholder(string name, ForgeProcessorContext context)
    {
        switch (name)
        {
            case "SIDE":
                // 官方安装器使用小写 side；DownloadMojmaps 会拼接 side + "_mappings"
                // （须为小写 client_mappings），因此这里必须返回小写 client。
                return "client";
            case "ROOT":
                return context.GameDirectory;
            case "MINECRAFT_VERSION":
                return context.McVersion;
            case "LIBRARY_DIR":
                return context.LibrariesDir;
            case "INSTALLER":
                return context.InstallerPath;
            case "MINECRAFT_JAR":
                return Path.Combine(
                    context.GameDirectory, "versions", context.McVersion, context.McVersion + ".jar");
            default:
                if (context.Data.TryGetValue(name, out ForgeDataEntry? entry))
                {
                    return ResolveDataValue(entry.Client ?? entry.Server ?? string.Empty, context);
                }
                throw new InvalidDataException($"未知的安装占位符：{name}");
        }
    }

    /// <summary>解析 data 取值：[Maven 坐标]、安装器内文件（/开头）、'字面量' 或原样。</summary>
    public static string ResolveDataValue(string value, ForgeProcessorContext context)
    {
        value = value.Trim();
        if (value.Length >= 2 && value.StartsWith('[') && value.EndsWith(']'))
        {
            return MavenLocalPath(value[1..^1], context.LibrariesDir);
        }
        if (value.StartsWith('/'))
        {
            return Path.Combine(
                context.InstallerExt,
                value.TrimStart('/').Replace('/', Path.DirectorySeparatorChar));
        }
        if (value.Length >= 2 && value.StartsWith('\'') && value.EndsWith('\''))
        {
            return value[1..^1];
        }
        return value;
    }

    /// <summary>把 Maven 坐标解析为本地 libraries 目录中的文件路径。</summary>
    public static string MavenLocalPath(string coordinate, string librariesDir)
    {
        string coord = coordinate;
        string? extension = null;
        int at = coordinate.IndexOf('@');
        if (at >= 0)
        {
            extension = coordinate[(at + 1)..];
            coord = coordinate[..at];
        }

        MavenCoordinate maven = MavenName.Parse(coord);
        if (extension is not null)
        {
            maven = maven with { Extension = extension };
        }
        string relative = maven.ToRepositoryPath().Replace('/', Path.DirectorySeparatorChar);
        return Path.Combine(librariesDir, relative);
    }
}
