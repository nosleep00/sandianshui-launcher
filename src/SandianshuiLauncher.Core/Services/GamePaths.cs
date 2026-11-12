namespace SandianshuiLauncher.Core.Services;

/// <summary>
/// 游戏目录与子目录的权威路径计算。游戏启动（LaunchService）与
/// Mod 安装（ModService）共用同一套规则，保证 mods 目录一致。
/// </summary>
public static class GamePaths
{
    /// <summary>
    /// 实际游戏目录（存档、mods、资源包、截图等所在）。
    /// 开启版本隔离时为 <c>.minecraft/versions/{versionId}</c>，
    /// 否则为游戏根目录 <c>.minecraft</c>。
    /// </summary>
    public static string GetActualGameDirectory(string gameDirectory, string versionId, bool versionIsolation)
    {
        return versionIsolation
            ? Path.Combine(gameDirectory, "versions", versionId)
            : gameDirectory;
    }

    /// <summary>
    /// 模组（mods）目录：实际游戏目录下的 <c>mods</c> 子目录。
    /// </summary>
    public static string GetModsDirectory(string gameDirectory, string versionId, bool versionIsolation)
    {
        return Path.Combine(GetActualGameDirectory(gameDirectory, versionId, versionIsolation), "mods");
    }

    /// <summary>资源包（resourcepacks）目录（隔离感知）。</summary>
    public static string GetResourcePacksDirectory(string gameDirectory, string versionId, bool versionIsolation)
    {
        return Path.Combine(GetActualGameDirectory(gameDirectory, versionId, versionIsolation), "resourcepacks");
    }

    /// <summary>光影包（shaderpacks）目录（隔离感知）。</summary>
    public static string GetShaderPacksDirectory(string gameDirectory, string versionId, bool versionIsolation)
    {
        return Path.Combine(GetActualGameDirectory(gameDirectory, versionId, versionIsolation), "shaderpacks");
    }
}
