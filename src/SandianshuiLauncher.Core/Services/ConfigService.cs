using SandianshuiLauncher.Core.Models;
using System.IO;
using System.Text.Json;

namespace SandianshuiLauncher.Core.Services;

/// <summary>
/// 启动器配置的加载与保存（%AppData%/SandianshuiLauncher/launcher_config.json）。
/// </summary>
public class ConfigService
{
    public const string AppFolderName = "SandianshuiLauncher";

    private readonly string _dataDirectory;
    private readonly string _configPath;

    public ConfigService(string? dataDirectory = null)
    {
        DataDirectory = dataDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            AppFolderName);
        _dataDirectory = DataDirectory;
        _configPath = Path.Combine(_dataDirectory, "launcher_config.json");
        Config = Load();
    }

    /// <summary>启动器数据目录。</summary>
    public string DataDirectory { get; }

    /// <summary>当前配置。</summary>
    public LauncherConfig Config { get; private set; }

    /// <summary>读取配置；文件不存在或损坏时生成默认配置。</summary>
    public LauncherConfig Load()
    {
        try
        {
            if (File.Exists(_configPath))
            {
                string json = File.ReadAllText(_configPath);
                var config = JsonSerializer.Deserialize<LauncherConfig>(json, JsonOptions.Config);
                if (config is not null)
                {
                    EnsureDefaults(config);
                    MigrateAccountTokens(config);
                    Config = config;
                    return Config;
                }
            }
        }
        catch (JsonException)
        {
            // 配置损坏时保留备份并重建，避免启动器无法打开。
            TryBackupCorruptConfig();
        }

        Config = CreateDefault();
        Save();
        return Config;
    }

    /// <summary>保存配置（先写临时文件再原子替换）；保存前先迁移遗留明文令牌。</summary>
    public void Save()
    {
        MigrateAccountTokens(Config);
        Directory.CreateDirectory(_dataDirectory);
        string json = JsonSerializer.Serialize(Config, JsonOptions.Config);
        string tempPath = _configPath + ".tmp";
        File.WriteAllText(tempPath, json);
        if (File.Exists(_configPath))
        {
            File.Replace(tempPath, _configPath, destinationBackupFileName: null);
        }
        else
        {
            File.Move(tempPath, _configPath);
        }
    }

    /// <summary>生成默认配置。</summary>
    public static LauncherConfig CreateDefault()
    {
        return new LauncherConfig
        {
            GameDirectory = GetDefaultGameDirectory(),
            MaxMemoryMb = RecommendMaxMemoryMb(),
            MinMemoryMb = 512,
            DownloadSourceId = "bmclapi",
            DownloadThreads = 32
        };
    }

    /// <summary>
    /// 默认游戏根目录：<c>%AppData%\.minecraft</c>。
    /// 与官方启动器、其它第三方启动器的默认位置一致，用户不需要额外配置；
    /// 想要放到别的盘（例如游戏盘）可以在「设置 → 游戏目录」里改。
    /// </summary>
    public static string GetDefaultGameDirectory()
        => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            ".minecraft");

    /// <summary>按物理内存推荐最大内存：约 1/4，限制在 1024–8192 MB。</summary>
    public static int RecommendMaxMemoryMb()
    {
        double totalGb = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes / 1024.0 / 1024.0 / 1024.0;
        int recommended = (int)Math.Round(totalGb * 1024 / 4.0 / 256.0) * 256;
        return Math.Clamp(recommended, 1024, 8192);
    }

    /// <summary>
    /// 把旧版配置中遗留的明文令牌迁移为 DPAPI 密文：
    /// 密文为空且存在旧明文字段时，走 AccessToken 写入器自动加密，并清空旧字段。
    /// </summary>
    private static void MigrateAccountTokens(LauncherConfig config)
    {
        foreach (GameAccount account in config.Accounts)
        {
            if (string.IsNullOrEmpty(account.EncryptedToken)
                && !string.IsNullOrEmpty(account.LegacyAccessToken))
            {
                account.AccessToken = account.LegacyAccessToken;
                account.LegacyAccessToken = null;
            }
        }
    }

    private static void EnsureDefaults(LauncherConfig config)
    {        if (string.IsNullOrWhiteSpace(config.GameDirectory))
        {
            config.GameDirectory = GetDefaultGameDirectory();
        }
        if (config.MaxMemoryMb <= 0)
        {
            config.MaxMemoryMb = RecommendMaxMemoryMb();
        }
        if (config.DownloadThreads <= 0)
        {
            config.DownloadThreads = 32;
        }
        if (string.IsNullOrWhiteSpace(config.DownloadSourceId))
        {
            config.DownloadSourceId = "bmclapi";
        }
        if (string.IsNullOrWhiteSpace(config.Theme))
        {
            config.Theme = "dark";
        }
    }

    private void TryBackupCorruptConfig()
    {
        try
        {
            if (File.Exists(_configPath))
            {
                File.Copy(_configPath, _configPath + ".corrupt-" + DateTime.Now.ToString("yyyyMMddHHmmss"), overwrite: true);
            }
        }
        catch
        {
            // 备份失败不影响重建配置。
        }
    }
}
