using SandianshuiLauncher.Core.Models;
using SandianshuiLauncher.Core.Services;

namespace SandianshuiLauncher.Services;

/// <summary>
/// 整合包导入编排：统一「自动新建实例」与「导入到现有版本」两条路径，
/// 供导入窗口与拖拽安装共同使用（含确认弹窗、结果反馈与配置更新）。
/// </summary>
public sealed class PackImportService
{
    private readonly ConfigService _config;
    private readonly ModpackService _modpackService;
    private readonly ModpackInstanceService _instanceService;
    private readonly IDialogService _dialogs;

    public PackImportService(
        ConfigService config,
        ModpackService modpackService,
        ModpackInstanceService instanceService,
        IDialogService dialogs)
    {
        _config = config;
        _modpackService = modpackService;
        _instanceService = instanceService;
        _dialogs = dialogs;
    }

    /// <summary>按扩展名/内部清单识别整合包类型（.mrpack 直接判定）。</summary>
    public static PackFileType DetectType(string path)
        => path.EndsWith(".mrpack", StringComparison.OrdinalIgnoreCase)
            ? PackFileType.ModrinthMrpack
            : ModpackService.DetectZipType(path);

    /// <summary>「自动新建实例」目前仅支持含 dependencies 的 Modrinth 整合包。</summary>
    public static bool SupportsNewInstance(PackFileType type) => type == PackFileType.ModrinthMrpack;

    /// <summary>整合包类型的中文显示名。</summary>
    public static string DescribeType(PackFileType type) => type switch
    {
        PackFileType.ModrinthMrpack => "Modrinth",
        PackFileType.CurseForge => "CurseForge",
        PackFileType.SdlauncherPack => "SDLauncherPack（本地备份/分享）",
        _ => "无法识别"
    };

    /// <summary>
    /// 自动新建实例导入：按清单安装原版与加载器，再导入 mods/overrides。
    /// 返回 null 表示用户取消或导入失败。
    /// </summary>
    public async Task<InstanceImportResult?> ImportAsNewInstanceAsync(
        string path,
        Action<string>? onStatus = null,
        IProgress<DownloadProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ModpackRequirements requirements;
        string packName;
        try
        {
            requirements = ModpackInstanceService.ReadRequirements(path);
            packName = ModpackInstanceService.ReadPackName(path);
        }
        catch (Exception ex)
        {
            _dialogs.Error("读取整合包清单失败：\n" + ex.Message, "导入失败");
            return null;
        }

        string loaderText = requirements.HasLoader
            ? $"加载器：{ModLoaderDetector.DisplayName(requirements.LoaderType)} {requirements.LoaderVersion}\n"
            : "加载器：无（原版整合包）\n";
        if (!_dialogs.Confirm(
                $"将自动新建独立实例：{packName}\n" +
                $"游戏版本：{requirements.Minecraft}\n" +
                loaderText +
                "\n会自动安装缺失的原版与加载器，再把整合包模组/配置导入新实例。\n继续？",
                "自动新建实例"))
        {
            return null;
        }

        LauncherConfig config = _config.Config;
        try
        {
            InstanceImportResult result = await _instanceService.ImportAsNewInstanceAsync(
                path, config.GameDirectory, config.DownloadThreads, progress, onStatus, cancellationToken);

            config.SelectedVersionId = result.VersionId;
            // 按整合包体量写入推荐内存（该版本未手动设置内存时才生效）。
            int recommendedMemory = ModpackInstanceService.RecommendedMemoryMb(result.Import.ImportedFiles);
            if (recommendedMemory > 0
                && LaunchSettingsResolver.Find(config, result.VersionId)?.MaxMemoryMb is null)
            {
                LaunchSettingsResolver.SaveOverride(config,
                    new VersionSetting { VersionId = result.VersionId, MaxMemoryMb = recommendedMemory });
            }
            EnsureVersionIsolation();
            _config.Save();

            string warnings = result.Import.Warnings.Count == 0
                ? string.Empty
                : "\n\n提示：\n · " + string.Join("\n · ", result.Import.Warnings);
            _dialogs.Info(
                $"整合包「{packName}」已导入为新实例：{result.VersionId}\n" +
                $"游戏 {result.MinecraftVersion}" +
                (result.LoaderType is null
                    ? "（原版）"
                    : $" / {ModLoaderDetector.DisplayName(result.LoaderType)} {result.LoaderVersion}") + "\n" +
                $"模组 {result.Import.ImportedFiles} 个，配置文件 {result.Import.OverrideFiles} 个。{warnings}\n\n" +
                "已自动选中该实例，可直接启动游戏。",
                "导入完成");
            return result;
        }
        catch (Exception ex)
        {
            _dialogs.Error(
                "自动新建实例失败：\n" + ex.Message +
                "\n\n可改用「导入到现有版本」模式，或先到下载页安装对应版本与加载器后重试。",
                "导入失败");
            return null;
        }
    }

    /// <summary>
    /// 导入到现有版本：把模组与覆盖文件写入该版本目录。
    /// 返回 null 表示用户取消或导入失败。
    /// </summary>
    public async Task<ModpackImportResult?> ImportIntoExistingVersionAsync(
        string path,
        string versionId,
        PackFileType packType,
        IProgress<DownloadProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        string packTypeLabel = DescribeType(packType);
        string extraNote = packType switch
        {
            PackFileType.CurseForge when string.IsNullOrWhiteSpace(_config.Config.CurseForgeApiKey)
                => "· 未配置 CurseForge API Key：仅应用配置，模组生成缺失清单\n",
            PackFileType.SdlauncherPack
                => "· 将从备份恢复 mods（含启用状态）、config 与实例配置\n",
            _ => string.Empty
        };
        if (!_dialogs.Confirm(
                $"将把{packTypeLabel}整合包导入到现有版本 {versionId}：\n" +
                "· 清单中的模组下载/恢复到该版本的 mods 目录\n" +
                "· overrides（配置、资源等）覆盖到该版本目录\n" +
                extraNote +
                "\n继续？",
                "导入整合包"))
        {
            return null;
        }

        LauncherConfig config = _config.Config;
        try
        {
            ModpackImportResult result = packType switch
            {
                PackFileType.ModrinthMrpack => await _modpackService.ImportMrpackAsync(
                    path, config.GameDirectory, versionId, config.VersionIsolation,
                    config.DownloadThreads, progress, cancellationToken),
                PackFileType.CurseForge => await _modpackService.ImportCurseForgeAsync(
                    path, config.GameDirectory, versionId, config.VersionIsolation,
                    config.CurseForgeApiKey, config.DownloadThreads, progress, cancellationToken),
                _ => await _modpackService.ImportSdlauncherPackAsync(
                    path, config.GameDirectory, versionId, config.VersionIsolation, cancellationToken)
            };

            string warnings = result.Warnings.Count == 0
                ? string.Empty
                : "\n\n提示：\n · " + string.Join("\n · ", result.Warnings);
            _dialogs.Info(
                $"整合包「{result.PackName}」已导入到版本 {versionId}：\n" +
                $"模组 {result.ImportedFiles} 个，配置文件 {result.OverrideFiles} 个。{warnings}",
                "导入完成");
            return result;
        }
        catch (Exception ex)
        {
            _dialogs.Error("整合包导入失败：\n" + ex.Message, "导入失败");
            return null;
        }
    }

    /// <summary>新实例的模组/配置在版本目录内：未开启隔离时征询并开启，否则启动读不到这些文件。</summary>
    private void EnsureVersionIsolation()
    {
        if (!_config.Config.VersionIsolation
            && _dialogs.Confirm(
                "新实例的模组与配置存放在它自己的版本目录中。\n是否开启「版本隔离」，让游戏读取该实例的独立目录？\n（不开启将无法加载新安装的模组）",
                "建议开启版本隔离"))
        {
            _config.Config.VersionIsolation = true;
        }
    }
}
