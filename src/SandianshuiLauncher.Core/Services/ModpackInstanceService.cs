using System.IO.Compression;
using System.Text.Json;
using SandianshuiLauncher.Core.Models;

namespace SandianshuiLauncher.Core.Services;

/// <summary>整合包声明的运行环境要求（来自 modrinth.index.json 的 dependencies）。</summary>
public sealed class ModpackRequirements
{
    /// <summary>所需的 Minecraft 原版版本，如 1.20.1。</summary>
    public required string Minecraft { get; init; }

    /// <summary>加载器标识（forge/neoforge/fabric/quilt），原版包为 null。</summary>
    public string? LoaderType { get; init; }

    /// <summary>加载器版本，如 47.3.12 / 0.15.11。未在清单中指明具体版本时为 null。</summary>
    public string? LoaderVersion { get; init; }

    public bool HasLoader => LoaderType is not null;
}

/// <summary>「自动新建实例」导入的最终结果。</summary>
public sealed class InstanceImportResult
{
    public required string VersionId { get; init; }

    public required string MinecraftVersion { get; init; }

    public string? LoaderType { get; init; }

    public string? LoaderVersion { get; init; }

    public required ModpackImportResult Import { get; init; }

    public List<string> Warnings { get; } = [];
}

/// <summary>
/// 整合包 → 独立实例：按 modrinth.index.json 的 dependencies 自动安装
/// 原版版本与精确版本的模组加载器，生成独立实例目录，再把 mods 与 overrides 导入该实例。
/// </summary>
public sealed class ModpackInstanceService
{
    private readonly VersionService _versionService;
    private readonly GameInstaller _gameInstaller;
    private readonly ModLoaderService _modLoaderService;
    private readonly JavaDetector _javaDetector;
    private readonly ModpackService _modpackService;

    /// <summary>清单中可能出现的加载器键（按优先级）。</summary>
    private static readonly string[] LoaderKeys = ["neoforge", "forge", "fabric", "quilt"];

    public ModpackInstanceService(
        VersionService versionService,
        GameInstaller gameInstaller,
        ModLoaderService modLoaderService,
        JavaDetector javaDetector,
        ModpackService modpackService)
    {
        _versionService = versionService;
        _gameInstaller = gameInstaller;
        _modLoaderService = modLoaderService;
        _javaDetector = javaDetector;
        _modpackService = modpackService;
    }

    /// <summary>读取 .mrpack 的运行环境要求（缺 minecraft 时报中文错误）。</summary>
    public static ModpackRequirements ReadRequirements(string mrpackPath)
    {
        using ZipArchive archive = ZipFile.OpenRead(mrpackPath);
        ModrinthIndex index = ReadIndex(archive);
        if (!index.Dependencies.TryGetValue("minecraft", out string? mc) || string.IsNullOrWhiteSpace(mc))
        {
            throw new InvalidDataException("整合包清单缺少 dependencies.minecraft，无法确定游戏版本。");
        }

        foreach (string key in LoaderKeys)
        {
            if (index.Dependencies.TryGetValue(key, out string? loaderVersion)
                && !string.IsNullOrWhiteSpace(loaderVersion))
            {
                return new ModpackRequirements
                {
                    Minecraft = mc.Trim(),
                    LoaderType = key,
                    LoaderVersion = loaderVersion.Trim()
                };
            }
        }
        return new ModpackRequirements { Minecraft = mc.Trim() };
    }

    /// <summary>读取整合包显示名（缺失时退回文件名）。</summary>
    public static string ReadPackName(string mrpackPath)
    {
        try
        {
            using ZipArchive archive = ZipFile.OpenRead(mrpackPath);
            ModrinthIndex index = ReadIndex(archive);
            return string.IsNullOrWhiteSpace(index.Name)
                ? Path.GetFileNameWithoutExtension(mrpackPath)
                : index.Name!.Trim();
        }
        catch
        {
            return Path.GetFileNameWithoutExtension(mrpackPath);
        }
    }

    /// <summary>按模组数量推荐最大内存（MB）；0 表示无需特殊建议。</summary>
    public static int RecommendedMemoryMb(int modCount) => modCount switch
    {
        >= 150 => 8192,
        >= 100 => 6144,
        >= 50 => 4096,
        _ => 0
    };

    /// <summary>
    /// 生成实例版本 Id：优先「整合包名」，去掉非法字符；重名自动追加序号。
    /// </summary>
    public static string BuildInstanceId(string packName, string gameDirectory)
    {
        string baseId = Sanitize(packName);
        if (string.IsNullOrWhiteSpace(baseId))
        {
            baseId = "整合包实例";
        }
        // 过长名字截断，避免 Windows 路径超限。
        if (baseId.Length > 48)
        {
            baseId = baseId[..48].Trim();
        }

        string candidate = baseId;
        int suffix = 2;
        while (Directory.Exists(Path.Combine(gameDirectory, "versions", candidate)))
        {
            candidate = $"{baseId}-{suffix}";
            suffix++;
        }
        return candidate;
    }

    /// <summary>去掉文件名非法字符与首尾空白，把连续空白压成短横线。</summary>
    public static string Sanitize(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return string.Empty;
        }
        char[] invalid = Path.GetInvalidFileNameChars();
        var builder = new System.Text.StringBuilder(name.Length);
        foreach (char ch in name.Trim())
        {
            builder.Append(Array.IndexOf(invalid, ch) >= 0 ? '-' : ch);
        }
        string cleaned = builder.ToString();
        while (cleaned.Contains("--", StringComparison.Ordinal))
        {
            cleaned = cleaned.Replace("--", "-", StringComparison.Ordinal);
        }
        return cleaned.Trim(' ', '-', '.');
    }

    /// <summary>
    /// 自动新建实例并导入整合包：
    /// 安装原版 → 安装指定版本加载器 → 生成独立实例 → 导入 mods/overrides。
    /// 导入内容始终写入实例自己的版本目录（配合版本隔离使用）。
    /// </summary>
    public async Task<InstanceImportResult> ImportAsNewInstanceAsync(
        string mrpackPath,
        string gameDirectory,
        int threads,
        IProgress<DownloadProgress>? progress = null,
        Action<string>? onStatus = null,
        CancellationToken cancellationToken = default)
    {
        ModpackRequirements requirements = ReadRequirements(mrpackPath);
        string packName = ReadPackName(mrpackPath);

        // 1. 确保原版版本就绪（缺失时安装；已存在则补齐文件）。
        onStatus?.Invoke($"正在准备原版 {requirements.Minecraft} …");
        await EnsureVanillaInstalledAsync(requirements.Minecraft, gameDirectory, threads, progress, cancellationToken);

        // 2. 安装加载器（若清单指定）。
        string? loaderVersionId = null;
        if (requirements.HasLoader)
        {
            onStatus?.Invoke($"正在安装 {requirements.LoaderType} {requirements.LoaderVersion} …");
            loaderVersionId = await InstallLoaderAsync(
                requirements, gameDirectory, threads, progress, onStatus, cancellationToken);
        }

        // 3. 生成独立实例：以加载器版本（或原版）为模板复制一份专属实例，
        //    避免与其他实例共用同一版本目录导致 mods 互相污染。
        string sourceVersionId = loaderVersionId ?? requirements.Minecraft;
        string instanceId = BuildInstanceId(packName, gameDirectory);
        if (!Directory.Exists(Path.Combine(gameDirectory, "versions", sourceVersionId)))
        {
            throw new InvalidDataException(
                $"加载器版本 {sourceVersionId} 未生成，无法创建实例。请检查网络后重试。");
        }
        VersionService.Copy(gameDirectory, sourceVersionId, instanceId);

        // 4. 导入 mods 与 overrides 到实例目录（强制隔离目录语义）。
        onStatus?.Invoke("正在下载并导入整合包文件 …");
        ModpackImportResult import = await _modpackService.ImportMrpackAsync(
            mrpackPath, gameDirectory, instanceId, versionIsolation: true, threads, progress, cancellationToken);

        return new InstanceImportResult
        {
            VersionId = instanceId,
            MinecraftVersion = requirements.Minecraft,
            LoaderType = requirements.LoaderType,
            LoaderVersion = requirements.LoaderVersion,
            Import = import
        };
    }

    /// <summary>确保原版版本已安装（缺失版本 JSON 时从官方清单安装）。</summary>
    private async Task EnsureVanillaInstalledAsync(
        string mcVersion,
        string gameDirectory,
        int threads,
        IProgress<DownloadProgress>? progress,
        CancellationToken cancellationToken)
    {
        string jsonPath = Path.Combine(gameDirectory, "versions", mcVersion, mcVersion + ".json");
        if (File.Exists(jsonPath))
        {
            return;
        }

        VersionManifest manifest = await _versionService.FetchManifestAsync(cancellationToken);
        ManifestEntry? entry = manifest.Versions
            .FirstOrDefault(v => string.Equals(v.Id, mcVersion, StringComparison.OrdinalIgnoreCase));
        if (entry is null)
        {
            throw new InvalidDataException(
                $"官方版本清单中没有 Minecraft {mcVersion}，无法自动安装。\n请确认整合包声明的游戏版本是否正确。");
        }
        await _gameInstaller.InstallAsync(entry, manifest, gameDirectory, threads, progress, cancellationToken);
    }

    /// <summary>按清单要求安装加载器，返回生成的版本 Id。</summary>
    private async Task<string> InstallLoaderAsync(
        ModpackRequirements requirements,
        string gameDirectory,
        int threads,
        IProgress<DownloadProgress>? progress,
        Action<string>? onStatus,
        CancellationToken cancellationToken)
    {
        string mc = requirements.Minecraft;
        string loader = requirements.LoaderType!;
        string? requested = requirements.LoaderVersion;

        if (loader == "fabric")
        {
            string version = requested
                ?? await PickLatestFabricLikeAsync(true, mc, cancellationToken);
            return await _modLoaderService.InstallFabricAsync(
                mc, version, gameDirectory, threads, progress, cancellationToken);
        }
        if (loader == "quilt")
        {
            string version = requested
                ?? await PickLatestFabricLikeAsync(false, mc, cancellationToken);
            return await _modLoaderService.InstallQuiltAsync(
                mc, version, gameDirectory, threads, progress, cancellationToken);
        }

        // Forge / NeoForge 需要本机 Java 运行安装器处理器。
        string javaPath;
        try
        {
            JavaInfo java = await Task.Run(
                () => _javaDetector.Resolve(null, 17, gameDirectory), cancellationToken);
            javaPath = java.Path;
        }
        catch (Exception ex)
        {
            throw new InvalidDataException(
                $"安装 {loader} 需要 Java 17 或更高版本，但未在本机找到可用的 Java。\n" +
                $"请先在设置页指定 Java 路径。\n原始错误：{ex.Message}");
        }

        if (loader == "neoforge")
        {
            string version = requested ?? await PickLatestForgeVersionAsync(loader, mc, cancellationToken);
            return await _modLoaderService.InstallNeoForgeAsync(
                mc, version, gameDirectory, threads, javaPath, progress, onStatus, cancellationToken);
        }

        string forgeVersion = requested ?? await PickLatestForgeVersionAsync(loader, mc, cancellationToken);
        return await _modLoaderService.InstallForgeAsync(
            mc, forgeVersion, gameDirectory, threads, javaPath, progress, onStatus, cancellationToken);
    }

    private async Task<string> PickLatestFabricLikeAsync(
        bool fabric, string mcVersion, CancellationToken cancellationToken)
    {
        List<FabricLoaderListEntry> list = fabric
            ? await _modLoaderService.GetFabricLoadersAsync(mcVersion, cancellationToken)
            : await _modLoaderService.GetQuiltLoadersAsync(mcVersion, cancellationToken);
        string? version = list
            .Select(entry => entry.Loader?.Version)
            .FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));
        return version ?? throw new InvalidDataException(
            $"未获取到 {mcVersion} 可用的加载器版本，请检查网络后重试。");
    }

    private async Task<string> PickLatestForgeVersionAsync(
        string loader, string mcVersion, CancellationToken cancellationToken)
    {
        List<ForgeVersionEntry> list = loader == "neoforge"
            ? await _modLoaderService.GetNeoForgeVersionsAsync(mcVersion, cancellationToken)
            : await _modLoaderService.GetForgeVersionsAsync(mcVersion, cancellationToken);
        string? version = list
            .Select(entry => entry.Version)
            .FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));
        return version ?? throw new InvalidDataException(
            $"未获取到 {mcVersion} 可用的 {loader} 版本，请检查网络后重试。");
    }

    private static ModrinthIndex ReadIndex(ZipArchive archive)
    {
        ZipArchiveEntry? entry = archive.GetEntry("modrinth.index.json");
        if (entry is null)
        {
            throw new InvalidDataException("压缩包中找不到 modrinth.index.json，不是有效的 Modrinth 整合包。");
        }
        using Stream stream = entry.Open();
        return JsonSerializer.Deserialize<ModrinthIndex>(stream, JsonOptions.Minecraft)
               ?? throw new InvalidDataException("modrinth.index.json 解析失败。");
    }
}
