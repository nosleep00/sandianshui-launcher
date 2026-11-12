using SandianshuiLauncher.Core.Models;
using SandianshuiLauncher.Core.Utils;
using System.Diagnostics;
using System.IO.Compression;
using System.Text.Json;

namespace SandianshuiLauncher.Core.Services;

/// <summary>
/// 模组加载器安装服务：Fabric（profile JSON 方式）与 Forge（安装器 + 处理器方式）。
/// </summary>
public class ModLoaderService
{
    /// <summary>BMCLAPI 公益源：Forge 版本列表的可靠来源（光速镜像不提供该列表）。</summary>
    private const string BmclBaseUrl = "https://bmclapi2.bangbang93.com";

    /// <summary>Forge 官方 Maven 仓库基址。</summary>
    private const string ForgeMavenBase = "https://maven.minecraftforge.net/";

    /// <summary>NeoForge 官方 Maven 仓库基址（带 /releases 前缀）。</summary>
    private const string NeoForgeMavenBase = "https://maven.neoforged.net/releases/";

    private readonly HttpClient _http;
    private readonly DownloadSourceProvider _sourceProvider;
    private readonly DownloadService _downloadService;
    private readonly GameInstaller _gameInstaller;

    public ModLoaderService(
        HttpClient http,
        DownloadSourceProvider sourceProvider,
        DownloadService downloadService,
        GameInstaller gameInstaller)
    {
        _http = http;
        _sourceProvider = sourceProvider;
        _downloadService = downloadService;
        _gameInstaller = gameInstaller;
    }

    // ============================ Fabric / Quilt ============================

    /// <summary>获取指定原版版本可用的 Fabric loader 列表（按官方顺序，最新在前）。</summary>
    public Task<List<FabricLoaderListEntry>> GetFabricLoadersAsync(
        string mcVersion,
        CancellationToken cancellationToken = default)
        => GetFabricLikeLoadersAsync("https://meta.fabricmc.net/v2", mcVersion, cancellationToken);

    /// <summary>获取指定原版版本可用的 Quilt loader 列表。</summary>
    public Task<List<FabricLoaderListEntry>> GetQuiltLoadersAsync(
        string mcVersion,
        CancellationToken cancellationToken = default)
        => GetFabricLikeLoadersAsync("https://meta.quiltmc.org/v3", mcVersion, cancellationToken);

    private async Task<List<FabricLoaderListEntry>> GetFabricLikeLoadersAsync(
        string metaBase, string mcVersion, CancellationToken cancellationToken)
    {
        string officialUrl = $"{metaBase}/versions/loader/{mcVersion}";
        return await GetJsonFromMirrorsAsync<List<FabricLoaderListEntry>>(officialUrl, cancellationToken);
    }

    /// <summary>安装 Fabric：生成 profile JSON 并作为子版本写入，再下载其依赖库。返回生成的版本 Id。</summary>
    public Task<string> InstallFabricAsync(
        string mcVersion,
        string loaderVersion,
        string gameDirectory,
        int threads,
        IProgress<DownloadProgress>? progress = null,
        CancellationToken cancellationToken = default)
        => InstallFabricLikeAsync(
            "https://meta.fabricmc.net/v2", mcVersion, loaderVersion, gameDirectory, threads, progress, cancellationToken);

    /// <summary>安装 Quilt：与 Fabric 相同的 profile JSON 机制，仅元数据源不同。返回生成的版本 Id。</summary>
    public Task<string> InstallQuiltAsync(
        string mcVersion,
        string loaderVersion,
        string gameDirectory,
        int threads,
        IProgress<DownloadProgress>? progress = null,
        CancellationToken cancellationToken = default)
        => InstallFabricLikeAsync(
            "https://meta.quiltmc.org/v3", mcVersion, loaderVersion, gameDirectory, threads, progress, cancellationToken);

    private async Task<string> InstallFabricLikeAsync(
        string metaBase,
        string mcVersion,
        string loaderVersion,
        string gameDirectory,
        int threads,
        IProgress<DownloadProgress>? progress,
        CancellationToken cancellationToken)
    {
        string officialUrl =
            $"{metaBase}/versions/loader/{mcVersion}/{loaderVersion}/profile/json";
        byte[] bytes = await GetBytesFromMirrorsAsync(officialUrl, cancellationToken);

        VersionJson profile = JsonSerializer.Deserialize<VersionJson>(bytes, JsonOptions.Minecraft)
            ?? throw new InvalidDataException("Fabric profile JSON 解析失败。");
        string id = profile.Id ?? throw new InvalidDataException("Fabric profile JSON 缺少 id。");

        // 镜像返回的 profile 中，库地址往往已指向镜像自身；
        // 还原为官方形态后再落盘，避免磁盘 profile 锁定在镜像地址、无法回退。
        if (profile.Libraries is not null)
        {
            foreach (LibraryInfo lib in profile.Libraries)
            {
                if (lib.Url is not null)
                {
                    lib.Url = _sourceProvider.Current.ToOfficialUrl(lib.Url);
                }
            }
            bytes = JsonSerializer.SerializeToUtf8Bytes(profile, JsonOptions.Minecraft);
        }

        string versionDir = Path.Combine(gameDirectory, "versions", id);
        Directory.CreateDirectory(versionDir);
        await File.WriteAllBytesAsync(Path.Combine(versionDir, id + ".json"), bytes, cancellationToken);

        // 复用游戏安装器：合并继承链并下载全部依赖（已存在的文件自动跳过）。
        VersionJson merged = await _gameInstaller.LoadMergedAsync(id, gameDirectory, cancellationToken);
        List<DownloadTask> tasks = await _gameInstaller.BuildDownloadTasksAsync(merged, gameDirectory, cancellationToken);
        await _downloadService.DownloadAsync(tasks, threads, progress, cancellationToken);
        return id;
    }

    // ============================ Forge ============================

    /// <summary>
    /// 获取指定原版版本可用的 Forge 版本列表：
    /// 优先 BMCLAPI；失败时回退到 Forge 官方 promotions_slim.json（经下载源重写，可走镜像）。
    /// </summary>
    public async Task<List<ForgeVersionEntry>> GetForgeVersionsAsync(
        string mcVersion,
        CancellationToken cancellationToken = default)
    {
        try
        {
            string url = $"{BmclBaseUrl}/forge/minecraft/{mcVersion}";
            return await GetJsonWithTimeoutAsync<List<ForgeVersionEntry>>(url, 30, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            // BMCLAPI 不可用：回退官方 promotions。
        }

        const string officialUrl = "https://files.minecraftforge.net/net/minecraftforge/forge/promotions_slim.json";
        string promotionsUrl = _sourceProvider.Current.Rewrite(officialUrl);
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(TimeSpan.FromSeconds(30));
        using var response = await _http.GetAsync(promotionsUrl, cts.Token);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cts.Token);
        var promos = await JsonSerializer.DeserializeAsync<ForgePromotions>(stream, JsonOptions.Minecraft, cts.Token)
                     ?? throw new InvalidDataException("Forge 版本列表解析失败。");

        var versions = new List<ForgeVersionEntry>();
        foreach (string suffix in new[] { "latest", "recommended" })
        {
            if (promos.Promos.TryGetValue($"{mcVersion}-{suffix}", out string? version)
                && !string.IsNullOrWhiteSpace(version)
                && versions.All(e => !string.Equals(e.Version, version, StringComparison.OrdinalIgnoreCase)))
            {
                versions.Add(new ForgeVersionEntry { Version = version, McVersion = mcVersion });
            }
        }
        if (versions.Count == 0)
        {
            throw new InvalidDataException(
                $"官方源中未找到 {mcVersion} 对应的 Forge 版本。\n可尝试更换下载源或稍后重试。");
        }
        return versions;
    }

    /// <summary>
    /// 安装 Forge：下载安装器、解压、下载安装依赖、顺序运行客户端处理器，
    /// 最后写入版本模板并下载运行时依赖。
    /// </summary>
    public Task<string> InstallForgeAsync(
        string mcVersion,
        string forgeVersion,
        string gameDirectory,
        int threads,
        string javaPath,
        IProgress<DownloadProgress>? progress = null,
        Action<string>? log = null,
        CancellationToken cancellationToken = default)
    {
        string coord = $"{mcVersion}-{forgeVersion}";
        string installerRel = $"net/minecraftforge/forge/{coord}/forge-{coord}-installer.jar";
        return InstallForgeLikeAsync(
            ForgeMavenBase, installerRel, mcVersion, gameDirectory, threads, javaPath, progress, log, cancellationToken);
    }

    /// <summary>
    /// 安装 NeoForge：与 Forge 相同的安装器流程，仅 Maven 仓库与构件路径不同
    /// （NeoForge 版本号自包含游戏版本前缀，如 1.20.4 → 20.4.x）。
    /// </summary>
    public Task<string> InstallNeoForgeAsync(
        string mcVersion,
        string neoforgeVersion,
        string gameDirectory,
        int threads,
        string javaPath,
        IProgress<DownloadProgress>? progress = null,
        Action<string>? log = null,
        CancellationToken cancellationToken = default)
    {
        string installerRel =
            $"net/neoforged/neoforge/{neoforgeVersion}/neoforge-{neoforgeVersion}-installer.jar";
        return InstallForgeLikeAsync(
            NeoForgeMavenBase, installerRel, mcVersion, gameDirectory, threads, javaPath, progress, log, cancellationToken);
    }

    private async Task<string> InstallForgeLikeAsync(
        string mavenBase,
        string installerRel,
        string mcVersion,
        string gameDirectory,
        int threads,
        string javaPath,
        IProgress<DownloadProgress>? progress,
        Action<string>? log,
        CancellationToken cancellationToken)
    {
        string librariesDir = Path.Combine(gameDirectory, "libraries");
        string workDir = Path.Combine(Path.GetTempPath(), "sandianshui_forge_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workDir);
        string installedId;

        try
        {
            // 1. 下载安装器。
            string installerPath = Path.Combine(workDir, "installer.jar");
            byte[] installerBytes = await GetBytesFromMirrorsAsync(mavenBase + installerRel, cancellationToken);
            await File.WriteAllBytesAsync(installerPath, installerBytes, cancellationToken);
            log?.Invoke("已下载 Forge 安装器。");

            // 2. 解压安装器。
            string installerExt = Path.Combine(workDir, "installer");
            ZipFile.ExtractToDirectory(installerPath, installerExt);

            // 3. 解析 install_profile.json。
            string profileFile = Path.Combine(installerExt, "install_profile.json");
            await using (var fs = File.OpenRead(profileFile))
            {
                ForgeInstallProfile? profile = await JsonSerializer.DeserializeAsync<ForgeInstallProfile>(
                    fs, JsonOptions.Minecraft, cancellationToken);
                if (profile is null)
                {
                    throw new InvalidDataException("Forge install_profile.json 解析失败。");
                }

                // 4. 收集并下载安装依赖（含处理器引用的额外构件）。
                List<DownloadTask> installTasks = await BuildInstallTasksAsync(
                    profile, gameDirectory, cancellationToken);
                await _downloadService.DownloadAsync(installTasks, threads, progress, cancellationToken);
                log?.Invoke("安装依赖已就绪。");

                // 5. 顺序运行客户端处理器。
                var context = new ForgeProcessorContext
                {
                    GameDirectory = gameDirectory,
                    LibrariesDir = librariesDir,
                    // 以安装器 install_profile 声明的游戏版本为准（缺失时回退调用参数）。
                    McVersion = profile.Minecraft ?? mcVersion,
                    InstallerPath = installerPath,
                    InstallerExt = installerExt,
                    Data = profile.Data ?? new Dictionary<string, ForgeDataEntry>()
                };

                foreach (ForgeProcessor processor in profile.Processors ?? new List<ForgeProcessor>())
                {
                    if (!ForgeProcessorResolver.AppliesToClient(processor))
                    {
                        continue;
                    }
                    await RunProcessorAsync(processor, context, javaPath, log, cancellationToken);
                }
            }

            // 6. 读取版本模板并写入 versions。
            string templateSrc = Path.Combine(installerExt, "version.json");
            byte[] versionBytes = await File.ReadAllBytesAsync(templateSrc, cancellationToken);
            VersionJson versionJson = JsonSerializer.Deserialize<VersionJson>(versionBytes, JsonOptions.Minecraft)
                ?? throw new InvalidDataException("Forge version.json 解析失败。");
            string id = versionJson.Id ?? throw new InvalidDataException("Forge version.json 缺少 id。");

            string versionDir = Path.Combine(gameDirectory, "versions", id);
            Directory.CreateDirectory(versionDir);
            await File.WriteAllBytesAsync(Path.Combine(versionDir, id + ".json"), versionBytes, cancellationToken);
            installedId = id;

            // 7. 下载运行时依赖（处理器生成的补丁客户端已存在，自动跳过）。
            VersionJson merged = await _gameInstaller.LoadMergedAsync(id, gameDirectory, cancellationToken);
            List<DownloadTask> runTasks = await _gameInstaller.BuildDownloadTasksAsync(merged, gameDirectory, cancellationToken);
            await _downloadService.DownloadAsync(runTasks, threads, progress, cancellationToken);

            log?.Invoke("加载器安装完成。");
        }
        finally
        {
            try
            {
                Directory.Delete(workDir, recursive: true);
            }
            catch
            {
                // 临时目录清理失败不影响安装结果。
            }
        }
        return installedId;
    }

    /// <summary>收集安装依赖任务（install_profile.libraries + 处理器引用的额外构件）。</summary>
    private async Task<List<DownloadTask>> BuildInstallTasksAsync(
        ForgeInstallProfile profile,
        string gameDirectory,
        CancellationToken cancellationToken)
    {
        var installLibs = new List<LibraryInfo>(profile.Libraries ?? new List<LibraryInfo>());

        var known = new HashSet<string>(
            installLibs.Where(l => !string.IsNullOrEmpty(l.Name)).Select(l => l.Name!),
            StringComparer.OrdinalIgnoreCase);

        if (profile.Processors is not null)
        {
            foreach (ForgeProcessor processor in profile.Processors.Where(ForgeProcessorResolver.AppliesToClient))
            {
                IEnumerable<string?> refs = new[] { processor.Jar };
                if (processor.Classpath is not null)
                {
                    refs = refs.Concat(processor.Classpath.Cast<string?>());
                }
                foreach (string? coord in refs)
                {
                    if (!string.IsNullOrEmpty(coord) && !known.Contains(coord))
                    {
                        known.Add(coord);
                        installLibs.Add(new LibraryInfo { Name = coord, Url = ForgeMavenBase });
                    }
                }
            }
        }

        // 仅需下载安装依赖（库），给一个占位 Id 以通过客户端 jar 检查；
        // 没有 Downloads.client，不会生成 client jar 任务。
        var tempJson = new VersionJson { Id = "forge-installer", Libraries = installLibs };
        return await _gameInstaller.BuildDownloadTasksAsync(tempJson, gameDirectory, cancellationToken);
    }

    /// <summary>
    /// 获取指定原版版本可用的 NeoForge 版本列表：
    /// 解析官方 maven-metadata.xml（经下载源重写，可走镜像），
    /// 按 NeoForge 版本号方案过滤该游戏版本（1.20.4 → 20.4.x；1.21 → 21.x），最新在前。
    /// </summary>
    public async Task<List<ForgeVersionEntry>> GetNeoForgeVersionsAsync(
        string mcVersion,
        CancellationToken cancellationToken = default)
    {
        string officialUrl = $"{NeoForgeMavenBase}net/neoforged/neoforge/maven-metadata.xml";
        string url = _sourceProvider.Current.Rewrite(officialUrl);
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(TimeSpan.FromSeconds(30));
        using var response = await _http.GetAsync(url, cts.Token);
        response.EnsureSuccessStatusCode();
        string xml = await response.Content.ReadAsStringAsync(cts.Token);

        List<string> versions = ParseMavenMetadataVersions(xml);
        string prefix = NeoForgeVersionPrefix(mcVersion);
        return versions
            .Where(v => v.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(v => v, Comparer<string>.Create(CompareVersionParts))
            .Select(v => new ForgeVersionEntry { Version = v, McVersion = mcVersion })
            .ToList();
    }

    /// <summary>解析 maven-metadata.xml 中的全部 &lt;version&gt; 元素。</summary>
    internal static List<string> ParseMavenMetadataVersions(string xml)
    {
        var versions = new List<string>();
        using var reader = System.Xml.XmlReader.Create(new StringReader(xml));
        while (reader.Read())
        {
            if (reader.NodeType == System.Xml.XmlNodeType.Element && reader.LocalName == "version")
            {
                versions.Add(reader.ReadElementContentAsString());
            }
        }
        return versions;
    }

    /// <summary>NeoForge 版本号前缀映射：1.20.4 → 20.4.；1.21 → 21.；1.21.1 → 21.1.。</summary>
    internal static string NeoForgeVersionPrefix(string mcVersion)
    {
        string[] parts = mcVersion.Split('.');
        if (parts.Length >= 2 && parts[0] == "1")
        {
            return parts.Length >= 3 ? parts[1] + "." + parts[2] + "." : parts[1] + ".";
        }
        return mcVersion + ".";
    }

    /// <summary>按点分数字段比较版本号（支持不等长，如 20.4.9 与 20.4.10）。</summary>
    internal static int CompareVersionParts(string a, string b)
    {
        string[] partsA = a.Split('.');
        string[] partsB = b.Split('.');
        int count = Math.Max(partsA.Length, partsB.Length);
        for (int i = 0; i < count; i++)
        {
            int x = i < partsA.Length && int.TryParse(partsA[i], out int xi) ? xi : 0;
            int y = i < partsB.Length && int.TryParse(partsB[i], out int yi) ? yi : 0;
            if (x != y)
            {
                return x.CompareTo(y);
            }
        }
        return 0;
    }

    /// <summary>运行单个处理器并校验输出。</summary>
    private async Task RunProcessorAsync(
        ForgeProcessor processor,
        ForgeProcessorContext context,
        string javaPath,
        Action<string>? log,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(processor.Jar))
        {
            throw new InvalidDataException("处理器缺少 jar 坐标。");
        }

        // 输出已存在且校验通过：跳过（支持断点续装）。
        if (processor.Outputs is not null && OutputsMatch(processor, context))
        {
            log?.Invoke("处理器已完成，跳过。");
            return;
        }

        string mainJar = ForgeProcessorResolver.MavenLocalPath(processor.Jar, context.LibrariesDir);
        var classpath = new List<string> { mainJar };
        if (processor.Classpath is not null)
        {
            foreach (string cp in processor.Classpath)
            {
                classpath.Add(ForgeProcessorResolver.MavenLocalPath(cp, context.LibrariesDir));
            }
        }

        string mainClass = ReadMainClass(mainJar);

        var psi = new ProcessStartInfo
        {
            FileName = javaPath,
            WorkingDirectory = context.GameDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        psi.ArgumentList.Add("-cp");
        psi.ArgumentList.Add(string.Join(Path.PathSeparator, classpath));
        psi.ArgumentList.Add(mainClass);
        if (processor.Args is not null)
        {
            foreach (string arg in processor.Args)
            {
                psi.ArgumentList.Add(ForgeProcessorResolver.ResolveToken(arg, context));
            }
        }

        log?.Invoke("运行处理器：" + mainClass);

        using var process = Process.Start(psi)
            ?? throw new InvalidDataException("无法启动处理器进程。");
        string stdout = await process.StandardOutput.ReadToEndAsync(cancellationToken);
        string stderr = await process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);

        if (process.ExitCode != 0)
        {
            throw new InvalidDataException(
                $"Forge 处理器 {mainClass} 运行失败（退出码 {process.ExitCode}）。\n{stdout}\n{stderr}");
        }

        if (processor.Outputs is not null && !OutputsMatch(processor, context))
        {
            throw new InvalidDataException(
                $"Forge 处理器 {mainClass} 输出校验失败。\n{stdout}\n{stderr}");
        }
    }

    /// <summary>校验处理器全部输出文件的 SHA1。</summary>
    private static bool OutputsMatch(ForgeProcessor processor, ForgeProcessorContext context)
    {
        if (processor.Outputs is null)
        {
            return true;
        }
        foreach (var (outToken, shaToken) in processor.Outputs)
        {
            string outPath = ForgeProcessorResolver.ResolveToken(outToken, context);
            string expectedSha = ForgeProcessorResolver.ResolveToken(shaToken, context);
            if (!File.Exists(outPath))
            {
                return false;
            }
            if (!string.Equals(HashUtils.Sha1File(outPath), expectedSha, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }
        return true;
    }

    /// <summary>读取 jar 的 Main-Class。</summary>
    private static string ReadMainClass(string jarPath)
    {
        using ZipArchive zip = ZipFile.OpenRead(jarPath);
        ZipArchiveEntry? entry = zip.GetEntry("META-INF/MANIFEST.MF")
            ?? throw new InvalidDataException($"处理器 jar 缺少 MANIFEST.MF：{jarPath}");
        using var reader = new StreamReader(entry.Open());
        string manifest = reader.ReadToEnd();

        foreach (string raw in manifest.Split('\n'))
        {
            string line = raw.Trim();
            if (line.StartsWith("Main-Class:", StringComparison.OrdinalIgnoreCase))
            {
                return line["Main-Class:".Length..].Trim();
            }
        }
        throw new InvalidDataException($"处理器 jar 缺少 Main-Class：{jarPath}");
    }

    // ============================ 多源获取 ============================

    /// <summary>Fabric 元数据/文件的候选地址：当前下载源 → BMCLAPI → 官方。</summary>
    private List<string> BuildMirrorCandidates(string officialUrl)
    {
        var candidates = new List<string> { _sourceProvider.Current.Rewrite(officialUrl) };

        string bmcl;
        if (officialUrl.StartsWith("https://meta.fabricmc.net", StringComparison.OrdinalIgnoreCase))
        {
            bmcl = officialUrl.Replace("https://meta.fabricmc.net", BmclBaseUrl + "/fabric-meta");
        }
        else if (officialUrl.StartsWith("https://meta.quiltmc.org", StringComparison.OrdinalIgnoreCase))
        {
            bmcl = officialUrl.Replace("https://meta.quiltmc.org", BmclBaseUrl + "/quilt-meta");
        }
        else
        {
            bmcl = new BmclCompatibleSource(BmclBaseUrl).Rewrite(officialUrl);
        }
        if (!candidates.Contains(bmcl))
        {
            candidates.Add(bmcl);
        }
        if (!candidates.Contains(officialUrl))
        {
            candidates.Add(officialUrl);
        }
        return candidates;
    }

    private async Task<T> GetJsonFromMirrorsAsync<T>(string officialUrl, CancellationToken cancellationToken)
    {
        List<string> candidates = BuildMirrorCandidates(officialUrl);
        for (int i = 0; i < candidates.Count; i++)
        {
            string url = candidates[i];
            try
            {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                cts.CancelAfter(TimeSpan.FromSeconds(30));
                using var response = await _http.GetAsync(url, HttpCompletionOption.ResponseContentRead, cts.Token);
                response.EnsureSuccessStatusCode();
                await using Stream stream = await response.Content.ReadAsStreamAsync(cts.Token);
                T? result = await JsonSerializer.DeserializeAsync<T>(stream, JsonOptions.Minecraft, cts.Token);
                if (result is not null)
                {
                    return result;
                }
            }
            catch when (i < candidates.Count - 1)
            {
                // 当前源失败，尝试下一个候选。
            }
        }
        throw new InvalidDataException("全部下载源均获取失败：" + officialUrl);
    }

    private async Task<byte[]> GetBytesFromMirrorsAsync(string officialUrl, CancellationToken cancellationToken)
    {
        List<string> candidates = BuildMirrorCandidates(officialUrl);
        for (int i = 0; i < candidates.Count; i++)
        {
            string url = candidates[i];
            try
            {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                cts.CancelAfter(TimeSpan.FromSeconds(60));
                using var response = await _http.GetAsync(url, cts.Token);
                response.EnsureSuccessStatusCode();
                return await response.Content.ReadAsByteArrayAsync(cts.Token);
            }
            catch when (i < candidates.Count - 1)
            {
                // 当前源失败，尝试下一个候选。
            }
        }
        throw new InvalidDataException("全部下载源均获取失败：" + officialUrl);
    }

    private async Task<T> GetJsonWithTimeoutAsync<T>(string url, int timeoutSeconds, CancellationToken cancellationToken)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));
        using var response = await _http.GetAsync(url, cts.Token);
        response.EnsureSuccessStatusCode();
        await using Stream stream = await response.Content.ReadAsStreamAsync(cts.Token);
        T? result = await JsonSerializer.DeserializeAsync<T>(stream, JsonOptions.Minecraft, cts.Token);
        return result ?? throw new InvalidDataException("JSON 解析失败：" + url);
    }
}
