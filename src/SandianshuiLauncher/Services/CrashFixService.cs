using SandianshuiLauncher.Core.Models;
using SandianshuiLauncher.Core.Services;

namespace SandianshuiLauncher.Services;

/// <summary>
/// 崩溃一键修复执行器：把诊断附带的 <see cref="CrashFixAction"/> 落实为实际修改——
/// 调内存、选 Java、重装版本、自动安装缺失模组（含依赖闭包）。
/// 返回给用户的中文说明。
/// </summary>
public sealed class CrashFixService
{
    private readonly ConfigService _config;
    private readonly ModService _modService;
    private readonly GameInstaller _gameInstaller;
    private readonly JavaDetector _javaDetector;
    private readonly VersionService _versionService;
    private readonly ModLoaderService _modLoaderService;

    public CrashFixService(
        ConfigService config,
        ModService modService,
        GameInstaller gameInstaller,
        JavaDetector javaDetector,
        VersionService versionService,
        ModLoaderService modLoaderService)
    {
        _config = config;
        _modService = modService;
        _gameInstaller = gameInstaller;
        _javaDetector = javaDetector;
        _versionService = versionService;
        _modLoaderService = modLoaderService;
    }

    /// <summary>执行诊断附带的修复动作，返回修复结果说明。</summary>
    public Task<string> ApplyAsync(CrashDiagnosis diagnosis, string versionId, CancellationToken cancellationToken = default)
        => diagnosis.FixAction switch
        {
            CrashFixAction.IncreaseMemory => Task.FromResult(IncreaseMemory(versionId)),
            CrashFixAction.DecreaseMemory => Task.FromResult(DecreaseMemory(versionId)),
            CrashFixAction.AutoSelectJava => AutoSelectJavaAsync(versionId, cancellationToken),
            CrashFixAction.ReinstallVersion => ReinstallVersionAsync(versionId, cancellationToken),
            CrashFixAction.InstallMissingMod => InstallMissingModAsync(diagnosis.FixTarget, versionId, cancellationToken),
            CrashFixAction.UpgradeLoader => UpgradeLoaderAsync(diagnosis, versionId, cancellationToken),
            _ => throw new InvalidOperationException("该崩溃原因暂不支持自动修复，请按建议手动处理。")
        };

    /// <summary>调大该版本最大内存：当前值 +2048 MB，至少 4096，不超物理内存推荐值。</summary>
    private string IncreaseMemory(string versionId)
    {
        LauncherConfig config = _config.Config;
        LaunchSettings current = LaunchSettingsResolver.Resolve(config, versionId);
        int newMax = Math.Max(current.MaxMemoryMb + 2048, 4096);
        newMax = Math.Min(newMax, ConfigService.RecommendMaxMemoryMb());
        if (newMax <= current.MaxMemoryMb)
        {
            return "当前内存设置已达到本机推荐上限，无需调整。";
        }
        LaunchSettingsResolver.SaveOverride(config, new VersionSetting { VersionId = versionId, MaxMemoryMb = newMax });
        _config.Save();
        return $"已将该版本最大内存从 {current.MaxMemoryMb} MB 提高到 {newMax} MB。\n请重新启动游戏。";
    }

    /// <summary>调小该版本最大内存：减半，至少 1024 MB。</summary>
    private string DecreaseMemory(string versionId)
    {
        LauncherConfig config = _config.Config;
        LaunchSettings current = LaunchSettingsResolver.Resolve(config, versionId);
        int newMax = Math.Max(1024, current.MaxMemoryMb / 2);
        if (newMax >= current.MaxMemoryMb)
        {
            return "当前内存设置已较低，无需再调小。";
        }
        LaunchSettingsResolver.SaveOverride(config, new VersionSetting { VersionId = versionId, MaxMemoryMb = newMax });
        _config.Save();
        return $"已将该版本最大内存从 {current.MaxMemoryMb} MB 调整为 {newMax} MB。\n请重新启动游戏。";
    }

    /// <summary>自动选择匹配该版本要求的 Java，并写入该版本覆盖配置。</summary>
    private async Task<string> AutoSelectJavaAsync(string versionId, CancellationToken cancellationToken)
    {
        LauncherConfig config = _config.Config;
        int requiredMajor = 8;
        try
        {
            requiredMajor = VersionService.ReadMergedLocal(config.GameDirectory, versionId).JavaVersion?.MajorVersion ?? 8;
        }
        catch
        {
            // 版本 JSON 读取失败按 Java 8 处理。
        }

        JavaInfo java = await Task.Run(
            () => _javaDetector.Resolve(null, requiredMajor, config.GameDirectory), cancellationToken);
        LaunchSettingsResolver.SaveOverride(config, new VersionSetting { VersionId = versionId, JavaPath = java.Path });
        _config.Save();
        return $"已为该版本选用 Java {java.MajorVersion}（{java.Version}）：\n{java.Path}\n请重新启动游戏。";
    }

    /// <summary>重新下载并校验该版本全部文件（修复缺失/损坏的原生库与客户端）。</summary>
    private async Task<string> ReinstallVersionAsync(string versionId, CancellationToken cancellationToken)
    {
        LauncherConfig config = _config.Config;
        VersionManifest manifest = await _versionService.FetchManifestAsync(cancellationToken);
        ManifestEntry? entry = manifest.Versions
            .FirstOrDefault(v => string.Equals(v.Id, versionId, StringComparison.OrdinalIgnoreCase));
        if (entry is null)
        {
            return "该版本不在官方版本清单中（可能是加载器版本）。\n请到“下载”页重新安装对应的模组加载器。";
        }
        await _gameInstaller.InstallAsync(
            entry, manifest, config.GameDirectory, config.DownloadThreads, progress: null, cancellationToken);
        return "已重新下载并校验该版本的全部游戏文件（含原生库）。\n请重新启动游戏。";
    }

    /// <summary>按日志提取的 modid 自动搜索并安装缺失模组（含依赖闭包）。</summary>
    private async Task<string> InstallMissingModAsync(
        string modId, string versionId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(modId))
        {
            throw new InvalidOperationException("无法从日志中识别缺失的模组标识，请在模组页手动搜索。");
        }

        LauncherConfig config = _config.Config;
        LocalVersion? local = null;
        try
        {
            local = VersionService.ScanLocal(config.GameDirectory)
                .FirstOrDefault(v => string.Equals(v.Id, versionId, StringComparison.OrdinalIgnoreCase));
        }
        catch
        {
            // 扫描失败不阻断修复。
        }
        string gameVersion = local?.GameVersion ?? versionId;
        string? loader = VersionService.DetectLoader(config.GameDirectory, versionId);

        var request = new ResourceSearchRequest
        {
            Type = ResourceType.Mod,
            Query = modId,
            GameVersion = gameVersion,
            Loader = loader,
            Sort = ResourceSortType.Relevance,
            Limit = 10
        };
        List<ModSearchResult> results = await _modService.SearchResourcesAsync(request, cancellationToken);
        ModSearchResult? hit =
            results.FirstOrDefault(r => string.Equals(r.Slug, modId, StringComparison.OrdinalIgnoreCase))
            ?? results.FirstOrDefault();
        if (hit is null)
        {
            return $"已自动搜索但未匹配到缺失模组“{modId}”。\n请在模组页搜索“{modId}”并手动安装。";
        }

        string modsDir = GamePaths.GetModsDirectory(config.GameDirectory, versionId, config.VersionIsolation);
        ModInstallPlan plan = await _modService.BuildInstallPlanAsync(
            hit, gameVersion, loader, modsDir, cancellationToken);
        await _modService.ExecuteInstallPlanAsync(plan, modsDir, progress: null, cancellationToken);

        string dependencyNote = plan.Dependencies.Count > 0
            ? $"\n已一并安装 {plan.Dependencies.Count} 个前置依赖。"
            : string.Empty;
        string skippedNote = plan.SkippedNotes.Count > 0
            ? "\n提示：\n · " + string.Join("\n · ", plan.SkippedNotes)
            : string.Empty;
        return $"已自动安装缺失模组「{hit.DisplayTitle}」（{plan.PrimaryFile.FileName}）{dependencyNote}{skippedNote}\n请重新启动游戏。";
    }

    /// <summary>
    /// 缺失/过旧的依赖是模组加载器自身：自动安装满足日志要求范围的最新版加载器，
    /// 并把新版本设为选中版本。
    /// </summary>
    private async Task<string> UpgradeLoaderAsync(
        CrashDiagnosis diagnosis, string versionId, CancellationToken cancellationToken)
    {
        LauncherConfig config = _config.Config;
        string loaderId = diagnosis.FixTarget;
        string expectedRange = diagnosis.FixVersionRange;

        // 定位基础游戏版本：当前版本自身是加载器版本时取其父版本。
        string baseGameVersion = versionId;
        try
        {
            LocalVersion? local = VersionService.ScanLocal(config.GameDirectory)
                .FirstOrDefault(v => string.Equals(v.Id, versionId, StringComparison.OrdinalIgnoreCase));
            if (local is not null)
            {
                baseGameVersion = local.GameVersion;
            }
        }
        catch
        {
            // 扫描失败时按当前版本 id 处理。
        }

        string normalized = loaderId.Trim().ToLowerInvariant() switch
        {
            "fabric" or "fabricloader" or "fabric_loader" => "fabric",
            "quilt" or "quilt_loader" => "quilt",
            "neoforge" => "neoforge",
            _ => "forge"
        };

        string loaderDisplay = ModLoaderDetector.DisplayName(normalized);
        string loaderVersion;
        string? picked;

        if (normalized == "fabric")
        {
            List<FabricLoaderListEntry> list = await _modLoaderService.GetFabricLoadersAsync(baseGameVersion, cancellationToken);
            var candidates = list.Where(e => !string.IsNullOrEmpty(e.Loader?.Version)).Select(e => e.Loader!.Version!);
            picked = VersionRangeMatcher.SelectBest(candidates, expectedRange);
            loaderVersion = picked ?? throw new InvalidOperationException($"未获取到满足要求 {expectedRange} 的 {baseGameVersion} Fabric 加载器版本。");
            await _modLoaderService.InstallFabricAsync(
                baseGameVersion, loaderVersion, config.GameDirectory, config.DownloadThreads,
                progress: null, cancellationToken);
        }
        else if (normalized == "quilt")
        {
            List<FabricLoaderListEntry> list = await _modLoaderService.GetQuiltLoadersAsync(baseGameVersion, cancellationToken);
            var candidates = list.Where(e => !string.IsNullOrEmpty(e.Loader?.Version)).Select(e => e.Loader!.Version!);
            picked = VersionRangeMatcher.SelectBest(candidates, expectedRange);
            loaderVersion = picked ?? throw new InvalidOperationException($"未获取到满足要求 {expectedRange} 的 {baseGameVersion} Quilt 加载器版本。");
            await _modLoaderService.InstallQuiltAsync(
                baseGameVersion, loaderVersion, config.GameDirectory, config.DownloadThreads,
                progress: null, cancellationToken);
        }
        else if (normalized == "neoforge")
        {
            List<ForgeVersionEntry> list = await _modLoaderService.GetNeoForgeVersionsAsync(baseGameVersion, cancellationToken);
            var candidates = list.Where(e => !string.IsNullOrEmpty(e.Version)).Select(e => e.Version!);
            picked = VersionRangeMatcher.SelectBest(candidates, expectedRange);
            loaderVersion = picked ?? throw new InvalidOperationException($"未获取到满足要求 {expectedRange} 的 {baseGameVersion} NeoForge 版本。");
            string javaPath = (await Task.Run(
                () => _javaDetector.Resolve(null, 17, config.GameDirectory), cancellationToken)).Path;
            await _modLoaderService.InstallNeoForgeAsync(
                baseGameVersion, loaderVersion, config.GameDirectory, config.DownloadThreads,
                javaPath, progress: null, log: null, cancellationToken);
        }
        else
        {
            List<ForgeVersionEntry> list = await _modLoaderService.GetForgeVersionsAsync(baseGameVersion, cancellationToken);
            var candidates = list.Where(e => !string.IsNullOrEmpty(e.Version)).Select(e => e.Version!);
            picked = VersionRangeMatcher.SelectBest(candidates, expectedRange);
            loaderVersion = picked ?? throw new InvalidOperationException($"未获取到满足要求 {expectedRange} 的 {baseGameVersion} Forge 版本。");
            string javaPath = (await Task.Run(
                () => _javaDetector.Resolve(null, 17, config.GameDirectory), cancellationToken)).Path;
            await _modLoaderService.InstallForgeAsync(
                baseGameVersion, loaderVersion, config.GameDirectory, config.DownloadThreads,
                javaPath, progress: null, log: null, cancellationToken);
        }

        // 自动选中新安装的加载器版本。
        string? newVersionId = null;
        try
        {
            newVersionId = VersionService.ScanLocal(config.GameDirectory)
                .Where(v => string.Equals(v.InheritsFrom, baseGameVersion, StringComparison.OrdinalIgnoreCase)
                            && !string.Equals(v.Id, versionId, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(v => v.Id)
                .Select(v => v.Id)
                .FirstOrDefault();
        }
        catch
        {
            // 自动选中失败不阻断修复结果。
        }
        if (!string.IsNullOrEmpty(newVersionId))
        {
            config.SelectedVersionId = newVersionId;
            _config.Save();
        }

        string selectedNote = string.IsNullOrEmpty(newVersionId)
            ? "\n请在启动页手动选择新安装的加载器版本。"
            : $"\n已自动切换到新版本「{newVersionId}」。";
        string rangeNote = string.IsNullOrWhiteSpace(expectedRange)
            ? string.Empty
            : $"（满足日志要求范围 {expectedRange}）";
        return $"已安装 {loaderDisplay} 加载器 {loaderVersion}{rangeNote}（基于 {baseGameVersion}）。{selectedNote}\n请启动新版本游戏。";
    }
}
