namespace SandianshuiLauncher.Core.Services;

/// <summary>
/// 版本删除前的用户数据保护：识别版本目录内残留的存档、截图等用户数据，
/// 并在删除版本前迁移到游戏根目录，避免误删玩家数据。
/// </summary>
public static class VersionDataService
{
    /// <summary>版本目录内可能存在的用户数据条目（相对版本目录的路径）。</summary>
    private static readonly string[] UserDataEntries =
    [
        "saves",        // 存档目录
        "screenshots",  // 截图目录
        "config",       // 模组配置目录
        "options.txt",  // 游戏选项
        "servers.dat"   // 服务器列表
    ];

    /// <summary>
    /// 返回版本目录内存在且有效的用户数据条目相对路径。
    /// 目录仅在非空时计入；文件存在即计入。
    /// </summary>
    public static List<string> GetUserDataEntries(string versionDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(versionDirectory);

        var result = new List<string>();
        foreach (string entry in UserDataEntries)
        {
            string path = Path.Combine(versionDirectory, entry);
            if (Directory.Exists(path))
            {
                if (Directory.EnumerateFileSystemEntries(path).Any())
                {
                    result.Add(entry);
                }
            }
            else if (File.Exists(path))
            {
                result.Add(entry);
            }
        }
        return result;
    }

    /// <summary>
    /// 把版本目录内的用户数据条目迁移到游戏根目录（目标重名时自动加“(备份-N)”后缀），
    /// 返回迁移后的目标路径清单。任一条目迁移失败时抛出异常，调用方应中止删除并保留版本目录。
    /// </summary>
    public static List<string> MigrateToGameRoot(
        string versionDirectory,
        string gameDirectory,
        IReadOnlyList<string> entries)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(versionDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(gameDirectory);
        ArgumentNullException.ThrowIfNull(entries);

        var migrated = new List<string>(entries.Count);
        foreach (string entry in entries)
        {
            string source = Path.Combine(versionDirectory, entry);
            string destination = FindAvailableDestination(gameDirectory, entry);

            Directory.CreateDirectory(gameDirectory);
            if (Directory.Exists(source))
            {
                Directory.Move(source, destination);
            }
            else
            {
                File.Move(source, destination);
            }
            migrated.Add(destination);
        }
        return migrated;
    }

    /// <summary>在游戏根目录中找到一个不冲突的目标路径；冲突时插入“(备份-N)”后缀。</summary>
    private static string FindAvailableDestination(string gameDirectory, string entry)
    {
        string candidate = Path.Combine(gameDirectory, entry);
        if (!Directory.Exists(candidate) && !File.Exists(candidate))
        {
            return candidate;
        }

        string fileName = Path.GetFileNameWithoutExtension(entry);
        string extension = Path.GetExtension(entry);
        for (int i = 1; ; i++)
        {
            string suffixed = Path.Combine(gameDirectory, $"{fileName}(备份-{i}){extension}");
            if (!Directory.Exists(suffixed) && !File.Exists(suffixed))
            {
                return suffixed;
            }
        }
    }
}
