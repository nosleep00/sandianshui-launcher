using System.Net.Http;
using System.Text.Json;
using SandianshuiLauncher.Core.Models;
using SandianshuiLauncher.Core.Utils;

namespace SandianshuiLauncher.Core.Services;

/// <summary>
/// 模组搜索与安装：基于 Modrinth API，默认通过 MCIM 国内镜像加速。
/// MCIM 100% 兼容 Modrinth 官方 API，仅替换域名即可。
/// </summary>
public class ModService
{
    /// <summary>MCIM 镜像的 Modrinth API 基址（替换 api.modrinth.com）。</summary>
    public const string McimApiBase = "https://mod.mcimirror.top/modrinth";

    /// <summary>MCIM 镜像的文件 CDN 主机（替换 cdn.modrinth.com，路径不变）。</summary>
    public const string McimCdnHost = "mod.mcimirror.top";

    /// <summary>Modrinth 官方 API 基址。</summary>
    public const string ModrinthApiBase = "https://api.modrinth.com";

    /// <summary>Modrinth 官方文件 CDN 主机。</summary>
    public const string ModrinthCdnHost = "cdn.modrinth.com";

    /// <summary>默认 User-Agent（配置为空时兜底）。</summary>
    public const string DefaultUserAgent = LauncherInfo.UserAgent;

    /// <summary>
    /// 会被放进 mods 目录的客户端模组加载器。
    /// 排除 paper/spigot/purpur/folia/bukkit 等服务端插件加载器，
    /// 避免在“任意加载器”时把服务端插件误装为模组。
    /// </summary>
    public static readonly IReadOnlySet<string> ModLoaders =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "fabric", "forge", "neoforge", "quilt", "liteloader" };

    private readonly HttpClient _http;
    private readonly DownloadService _downloads;
    private readonly ConfigService _configService;

    /// <summary>
    /// 构造模组服务：镜像开关与 User-Agent 每次请求时从 <see cref="ConfigService"/> 实时读取，
    /// 因此设置页修改后无需重建本服务实例。
    /// </summary>
    public ModService(HttpClient http, DownloadService downloads, ConfigService configService)
    {
        _http = http;
        _downloads = downloads;
        _configService = configService;
    }

    private string ApiBase => _configService.Config.ModUseMirror ? McimApiBase : ModrinthApiBase;

    private string CurrentUserAgent => string.IsNullOrWhiteSpace(_configService.Config.ModUserAgent)
        ? DefaultUserAgent
        : _configService.Config.ModUserAgent.Trim();

    // ─────────────────────────────────────────────────────────
    // URL 构造（纯函数，便于单元测试）
    // ─────────────────────────────────────────────────────────

    /// <summary>
    /// 构造 Modrinth 搜索 facets（JSON）——通用版本，支持资源类型与功能分类。
    /// 形如 [["project_type:mod"],["versions:1.20.4"],["categories:fabric"],["categories:worldgen"]]。
    /// </summary>
    public static string BuildFacets(
        ResourceType type, string? gameVersion, string? loader, string? category)
    {
        var facets = new List<List<string>> { new() { $"project_type:{type.ToApiName()}" } };

        if (!string.IsNullOrWhiteSpace(gameVersion))
        {
            facets.Add(new List<string> { $"versions:{gameVersion.Trim()}" });
        }

        if (!string.IsNullOrWhiteSpace(loader))
        {
            facets.Add(new List<string> { $"categories:{loader.Trim().ToLowerInvariant()}" });
        }

        if (!string.IsNullOrWhiteSpace(category))
        {
            facets.Add(new List<string> { $"categories:{category.Trim().ToLowerInvariant()}" });
        }

        return JsonSerializer.Serialize(facets);
    }

    /// <summary>
    /// 构造 Modrinth 搜索 facets（JSON）——模组专用重载（向后兼容）。
    /// 形如 [["project_type:mod"],["versions:1.20.4"],["categories:fabric"]]。
    /// </summary>
    public static string BuildFacets(string? gameVersion, string? loader)
        => BuildFacets(ResourceType.Mod, gameVersion, loader, category: null);

    /// <summary>构造搜索请求 URL——通用版本，支持完整请求与排序。</summary>
    public static string BuildSearchUrl(string apiBase, ResourceSearchRequest request)
    {
        var parameters = new List<string>();
        if (!string.IsNullOrWhiteSpace(request.Query))
        {
            parameters.Add("query=" + Uri.EscapeDataString(request.Query.Trim()));
        }
        parameters.Add("limit=" + Math.Clamp(request.Limit, 1, 100));
        parameters.Add("facets=" + Uri.EscapeDataString(
            BuildFacets(request.Type, request.GameVersion, request.Loader, request.Category)));
        parameters.Add("index=" + request.Sort.ToApiIndex());

        return $"{apiBase.TrimEnd('/')}/v2/search?{string.Join("&", parameters)}";
    }

    /// <summary>构造搜索请求 URL——模组专用重载（向后兼容，固定相关性排序）。</summary>
    public static string BuildSearchUrl(
        string apiBase, string query, string? gameVersion, string? loader, int limit = 30)
    {
        var request = new ResourceSearchRequest
        {
            Type = ResourceType.Mod,
            Query = query,
            GameVersion = gameVersion,
            Loader = loader,
            Category = null,
            Sort = ResourceSortType.Relevance,
            Limit = limit
        };
        return BuildSearchUrl(apiBase, request);
    }

    /// <summary>构造模组版本列表请求 URL（可按游戏版本与加载器过滤）。</summary>
    public static string BuildVersionsUrl(
        string apiBase, string projectId, string? gameVersion, string? loader)
    {
        var parameters = new List<string>();
        if (!string.IsNullOrWhiteSpace(loader))
        {
            string json = JsonSerializer.Serialize(new[] { loader.Trim().ToLowerInvariant() });
            parameters.Add("loaders=" + Uri.EscapeDataString(json));
        }
        if (!string.IsNullOrWhiteSpace(gameVersion))
        {
            string json = JsonSerializer.Serialize(new[] { gameVersion.Trim() });
            parameters.Add("game_versions=" + Uri.EscapeDataString(json));
        }

        string suffix = parameters.Count > 0 ? "?" + string.Join("&", parameters) : string.Empty;
        return $"{apiBase.TrimEnd('/')}/v2/project/{Uri.EscapeDataString(projectId)}/version{suffix}";
    }

    /// <summary>把 Modrinth 文件 CDN 地址重写为镜像地址（实例方法，遵循当前设置）。</summary>
    public string RewriteFileUrl(string url) => RewriteFileUrl(url, _configService.Config.ModUseMirror);

    /// <summary>把 Modrinth 文件 CDN 地址重写为镜像地址；关闭镜像或非 CDN 域名时原样返回。</summary>
    public static string RewriteFileUrl(string url, bool useMirror)
    {
        if (!useMirror || string.IsNullOrWhiteSpace(url))
        {
            return url;
        }

        var uri = new Uri(url);
        if (uri.Host.Equals(ModrinthCdnHost, StringComparison.OrdinalIgnoreCase))
        {
            var builder = new UriBuilder(uri) { Host = McimCdnHost, Port = -1 };
            return builder.Uri.ToString();
        }

        return url;
    }

    // ─────────────────────────────────────────────────────────
    // 网络请求
    // ─────────────────────────────────────────────────────────

    /// <summary>
    /// 通用资源搜索：按 <see cref="ResourceSearchRequest"/> 调用搜索接口，
    /// 支持任意资源类型、功能分类与排序方式。当前返回模型沿用 ModSearchResult，
    /// 后续接入其他资源类型时再扩展对应模型。
    /// </summary>
    public async Task<List<ModSearchResult>> SearchResourcesAsync(
        ResourceSearchRequest request,
        CancellationToken cancellationToken = default)
    {
        string url = BuildSearchUrl(ApiBase, request);
        using var httpRequest = CreateRequest(HttpMethod.Get, url);
        using var response = await _http.SendAsync(httpRequest, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(DescribeHttpFailure(response, isVersionQuery: false));
        }

        await using Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var data = await JsonSerializer.DeserializeAsync<ModrinthSearchResponse>(
            stream, JsonOptions.Minecraft, cancellationToken)
            ?? throw new InvalidDataException("资源搜索响应解析失败。");

        return data.Hits.Select(MapSearchHit).ToList();
    }

    /// <summary>按关键词搜索模组（可按游戏版本与加载器过滤，固定相关性排序）。</summary>
    public Task<List<ModSearchResult>> SearchModsAsync(
        string query,
        string? gameVersion,
        string? loader,
        int limit = 30,
        CancellationToken cancellationToken = default)
    {
        var request = new ResourceSearchRequest
        {
            Type = ResourceType.Mod,
            Query = query,
            GameVersion = gameVersion,
            Loader = loader,
            Category = null,
            Sort = ResourceSortType.Relevance,
            Limit = limit
        };
        return SearchResourcesAsync(request, cancellationToken);
    }

    /// <summary>
    /// 搜索某功能分类下的模组，按下载量从高到低排序。
    /// 例如 category=worldgen 即查找世界生成（地图）类模组。
    /// </summary>
    public Task<List<ModSearchResult>> SearchByCategoryAsync(
        string category,
        string? gameVersion,
        string? loader,
        int limit = 30,
        CancellationToken cancellationToken = default)
    {
        var request = new ResourceSearchRequest
        {
            Type = ResourceType.Mod,
            Query = string.Empty,
            GameVersion = gameVersion,
            Loader = loader,
            Category = category,
            Sort = ResourceSortType.Downloads,
            Limit = limit
        };
        return SearchResourcesAsync(request, cancellationToken);
    }

    /// <summary>
    /// 从按下载量排序的结果中，跳过纯依赖库（<see cref="ModSearchResult.IsLibrary"/>），
    /// 选出第一个真正提供游戏内容的资源；全部为库或列表为空时返回 null。
    /// </summary>
    public static ModSearchResult? PickTopContent(IEnumerable<ModSearchResult> results)
        => results.FirstOrDefault(r => !r.IsLibrary);

    /// <summary>
    /// 按优先级给出可用于请求版本接口的标识：
    /// <b>project_id 优先</b>，slug 作为兜底。
    /// 注意：MCIM 镜像的 /v2/project/{id}/version 只认 project_id，
    /// 用 slug 请求会返回 404（"Modrinth Version not found"），因此必须优先用 ID。
    /// </summary>
    public static List<string> BuildProjectReferences(ModSearchResult mod)
    {
        ArgumentNullException.ThrowIfNull(mod);

        var references = new List<string>();
        void Add(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return;
            }
            string trimmed = value.Trim();
            if (!references.Contains(trimmed, StringComparer.OrdinalIgnoreCase))
            {
                references.Add(trimmed);
            }
        }

        Add(mod.ProjectId);
        Add(mod.Slug);
        return references;
    }

    /// <summary>查询模组可用版本（已按游戏版本与加载器过滤）。</summary>
    public Task<List<ModVersionInfo>> GetVersionsAsync(
        string projectId,
        string? gameVersion,
        string? loader,
        CancellationToken cancellationToken = default)
        => GetVersionsAsync(projectId, gameVersion, loader, slug: null, cancellationToken);

    /// <summary>
    /// 查询模组可用版本：先按 <paramref name="projectId"/>，返回 404 时改用
    /// <paramref name="slug"/> 重试一次（兼容个别只认 slug 的服务端）。
    /// 全部失败时抛出中文可操作错误。
    /// </summary>
    public async Task<List<ModVersionInfo>> GetVersionsAsync(
        string projectId,
        string? gameVersion,
        string? loader,
        string? slug,
        CancellationToken cancellationToken = default)
    {
        var references = new List<string>();
        foreach (string? candidate in new[] { projectId, slug })
        {
            if (!string.IsNullOrWhiteSpace(candidate)
                && !references.Contains(candidate!.Trim(), StringComparer.OrdinalIgnoreCase))
            {
                references.Add(candidate.Trim());
            }
        }
        if (references.Count == 0)
        {
            throw new ArgumentException("缺少模组标识，无法查询版本。", nameof(projectId));
        }

        for (int i = 0; i < references.Count; i++)
        {
            string reference = references[i];
            bool hasNext = i < references.Count - 1;
            string url = BuildVersionsUrl(ApiBase, reference, gameVersion, loader);
            using var request = CreateRequest(HttpMethod.Get, url);
            using var response = await _http.SendAsync(request, cancellationToken);

            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                if (hasNext)
                {
                    // 标识不被该服务端识别（如镜像不支持 slug）：换下一个标识重试。
                    continue;
                }
                // 所有标识都 404：跳出统一给出“已尝试标识”的汇总提示。
                break;
            }
            if (!response.IsSuccessStatusCode)
            {
                throw new InvalidOperationException(DescribeHttpFailure(response, isVersionQuery: true));
            }

            await using Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            var data = await JsonSerializer.DeserializeAsync<List<ModrinthVersion>>(
                stream, JsonOptions.Minecraft, cancellationToken)
                ?? throw new InvalidDataException("模组版本响应解析失败。");
            return data.Select(MapVersion).ToList();
        }

        throw new InvalidOperationException(BuildNoVersionReferenceError(references));
    }

    /// <summary>把 HTTP 失败转换成带状态码与操作建议的中文说明。</summary>
    public static string DescribeHttpFailure(HttpResponseMessage response, bool isVersionQuery)
    {
        int status = (int)response.StatusCode;
        string host = response.RequestMessage?.RequestUri?.Host ?? "服务端";

        if (status == 404)
        {
            return isVersionQuery
                ? $"服务端未收录该模组的版本文件（HTTP 404，{host}）。\n"
                  + "可在“设置”中关闭“使用 MCIM 镜像”后重试，或稍后再试。"
                : $"请求的资源不存在（HTTP 404，{host}）。\n可能是该模组已下架，可返回搜索结果重新选择。";
        }
        if (status == 403 || status == 401)
        {
            return $"访问被拒绝（HTTP {status}，{host}）。\n"
                   + "可能是镜像启用了 User-Agent 白名单，可在“设置”中修改模组 User-Agent 或关闭镜像。";
        }
        if (status == 429)
        {
            return $"请求过于频繁（HTTP 429，{host}）。\n请稍等片刻后重试。";
        }
        if (status >= 500)
        {
            return $"服务端暂时不可用（HTTP {status}，{host}）。\n请稍后重试，或在“设置”中切换镜像/官方源。";
        }
        return $"请求失败（HTTP {status}，{host}）。\n请检查网络后重试。";
    }

    private static string BuildNoVersionReferenceError(IReadOnlyList<string> references)
        => $"无法获取该模组的版本列表（已尝试标识：{string.Join("、", references)}）。\n"
           + "可能是 MCIM 镜像未收录该模组，可在“设置”中关闭“使用 MCIM 镜像”后重试。";

    /// <summary>
    /// 安装模组：查询匹配版本 → 取主文件 → 下载到对应版本的 mods 目录。
    /// 返回安装完成的模组文件信息。
    /// </summary>
    public Task<InstalledMod> InstallAsync(
        ModSearchResult mod,
        string gameVersion,
        string? loader,
        string gameDirectory,
        string versionId,
        bool versionIsolation,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
        => InstallToAsync(
            mod,
            gameVersion,
            loader,
            GamePaths.GetModsDirectory(gameDirectory, versionId, versionIsolation),
            progress,
            cancellationToken);

    /// <summary>
    /// 安装资源包/光影：下载主文件到对应版本的 resourcepacks / shaderpacks 目录
    /// （资源包与光影不按加载器过滤）。
    /// </summary>
    public Task<InstalledMod> InstallResourceAsync(
        ResourceType type,
        ModSearchResult mod,
        string gameVersion,
        string gameDirectory,
        string versionId,
        bool versionIsolation,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        string targetDirectory = type switch
        {
            ResourceType.ResourcePack =>
                GamePaths.GetResourcePacksDirectory(gameDirectory, versionId, versionIsolation),
            ResourceType.Shader =>
                GamePaths.GetShaderPacksDirectory(gameDirectory, versionId, versionIsolation),
            _ => throw new ArgumentOutOfRangeException(nameof(type), type, "仅支持资源包与光影。")
        };
        return InstallToAsync(mod, gameVersion, loader: null, targetDirectory, progress, cancellationToken);
    }

    private async Task<InstalledMod> InstallToAsync(
        ModSearchResult mod,
        string gameVersion,
        string? loader,
        string targetDirectory,
        IProgress<double>? progress,
        CancellationToken cancellationToken)
    {
        ModInstallPlan plan = await BuildInstallPlanAsync(mod, gameVersion, loader, targetDirectory, cancellationToken);
        return await ExecuteInstallPlanAsync(plan, targetDirectory, progress, cancellationToken);
    }

    /// <summary>
    /// 解析一次安装的完整计划：主文件 + 必需依赖的传递闭包。
    /// 依赖按「游戏版本 + 加载器」选择最佳版本；已安装（SHA1 校验通过）的跳过；
    /// 解析失败的依赖记入 <see cref="ModInstallPlan.SkippedNotes"/> 而不中断主流程。
    /// </summary>
    public async Task<ModInstallPlan> BuildInstallPlanAsync(
        ModSearchResult mod,
        string gameVersion,
        string? loader,
        string targetDirectory,
        CancellationToken cancellationToken = default)
    {
        // 优先 project_id（MCIM 镜像不认 slug），slug 仅作兜底。
        List<string> references = BuildProjectReferences(mod);
        if (references.Count == 0)
        {
            throw new InvalidOperationException("该模组缺少项目标识，无法安装。请返回搜索结果重新选择。");
        }
        string projectId = references[0];

        List<ModVersionInfo> versions = await GetVersionsAsync(
            projectId, gameVersion, loader, mod.Slug, cancellationToken);
        ModVersionInfo? version = SelectBestVersion(versions, loader)
            ?? throw new InvalidOperationException(
                $"未找到适用于 {gameVersion}" +
                (string.IsNullOrWhiteSpace(loader) ? string.Empty : $"（{loader}）") +
                " 的文件，请确认该资源支持所选版本。");

        // 版本可能同时提供多个加载器的文件（如 fabric/forge/neoforge 各一份）：
        // 必须按目标加载器挑对文件，而不是只取 primary（避免把 neoforge 构建装进 Forge）。
        ModFileInfo? selectedFile = SelectFileForLoader(version, loader);
        if (selectedFile is null)
        {
            throw new InvalidOperationException($"该版本没有可下载的文件，请换个版本重试。");
        }
        ModFileInfo primaryFile = selectedFile;

        var notes = new List<string>();
        if (loader is not null
            && HasAnyLoaderMarker(primaryFile.FileName)
            && !IsFileForLoader(primaryFile.FileName, loader))
        {
            notes.Add($"警告：所选文件 {primaryFile.FileName} 是其他加载器的构建，安装后可能无法加载。");
        }

        var items = new List<ModInstallItem>();
        var itemFileNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // BFS 展开必需依赖的传递闭包（防环、限深、限数）。
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { projectId };
        var queue = new Queue<ModDependencyInfo>(
            version.Dependencies.Where(d => d.IsRequired && !string.IsNullOrWhiteSpace(d.ProjectId)));

        int depth = 0;
        const int MaxDepth = 6;
        const int MaxItems = 64;

        while (queue.Count > 0 && depth < MaxDepth && items.Count < MaxItems)
        {
            int levelCount = queue.Count;
            for (int i = 0; i < levelCount; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                ModDependencyInfo dep = queue.Dequeue();
                string depProject = dep.ProjectId!.Trim();
                if (!visited.Add(depProject))
                {
                    continue; // 环或重复引用。
                }

                try
                {
                    List<ModVersionInfo> depVersions = await GetVersionsAsync(
                        depProject, gameVersion, loader, slug: null, cancellationToken);
                    ModVersionInfo? depVersion = SelectBestVersion(depVersions, loader);
                    ModFileInfo? depFile = depVersion is null ? null : SelectFileForLoader(depVersion, loader);
                    if (depVersion is null || depFile is null)
                    {
                        notes.Add($"前置模组 {depProject} 没有适用于 {gameVersion} 的版本，已跳过（可能导致游戏启动失败）。");
                        continue;
                    }

                    // 已安装（同名文件 SHA1 校验通过）则跳过。
                    if (HashUtils.VerifySha1(Path.Combine(targetDirectory, depFile.FileName), depFile.Sha1))
                    {
                        notes.Add($"已安装：{depFile.FileName}");
                        continue;
                    }

                    if (itemFileNames.Add(depFile.FileName))
                    {
                        items.Add(new ModInstallItem
                        {
                            ProjectId = depProject,
                            Title = depVersion.Name,
                            File = depFile
                        });
                    }

                    foreach (ModDependencyInfo sub in depVersion.Dependencies
                                 .Where(d => d.IsRequired && !string.IsNullOrWhiteSpace(d.ProjectId)))
                    {
                        queue.Enqueue(sub);
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    notes.Add($"前置模组 {depProject} 解析失败：{ex.Message}");
                }
            }
            depth++;
        }

        if (queue.Count > 0)
        {
            notes.Add("依赖层级过深或数量过多，部分依赖未继续解析。");
        }

        return new ModInstallPlan
        {
            Version = version,
            PrimaryFile = primaryFile,
            Dependencies = items,
            SkippedNotes = notes
        };
    }

    /// <summary>执行安装计划：下载主文件与全部依赖（已存在的自动跳过）。</summary>
    public Task<InstalledMod> ExecuteInstallPlanAsync(
        ModInstallPlan plan,
        string targetDirectory,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
        => ExecuteInstallPlanAsync(plan, targetDirectory, _downloads, progress, cancellationToken);

    /// <summary>执行安装计划的共享实现（Modrinth 与 CurseForge 复用）。</summary>
    public static async Task<InstalledMod> ExecuteInstallPlanAsync(
        ModInstallPlan plan,
        string targetDirectory,
        DownloadService downloads,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(targetDirectory);

        // 主文件 + 依赖，按文件名去重。
        var files = new List<ModFileInfo>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (ModFileInfo file in new[] { plan.PrimaryFile }
                     .Concat(plan.Dependencies.Select(d => d.File)))
        {
            if (seen.Add(file.FileName))
            {
                files.Add(file);
            }
        }

        var tasks = files
            .Where(f => !HashUtils.VerifySha1(Path.Combine(targetDirectory, f.FileName), f.Sha1))
            .Select(f => new DownloadTask(
                f.Url, Path.Combine(targetDirectory, f.FileName), f.Sha1, f.Size, f.FileName))
            .ToList();

        var downloadProgress = new Progress<DownloadProgress>(p => progress?.Report(p.Percent));
        await downloads.DownloadAsync(tasks, maxParallel: 1, downloadProgress, cancellationToken);

        string destination = Path.Combine(targetDirectory, plan.PrimaryFile.FileName);
        return new InstalledMod
        {
            FileName = plan.PrimaryFile.FileName,
            FullPath = destination,
            SizeBytes = plan.PrimaryFile.Size
        };
    }

    /// <summary>扫描指定版本 mods 目录下已安装的模组（含已禁用的 .jar.disabled）。</summary>
    public static List<InstalledMod> ScanInstalled(
        string gameDirectory, string versionId, bool versionIsolation)
    {
        string modsDir = GamePaths.GetModsDirectory(gameDirectory, versionId, versionIsolation);
        if (!Directory.Exists(modsDir))
        {
            return [];
        }

        return Directory.EnumerateFiles(modsDir)
            .Where(path =>
                path.EndsWith(".jar", StringComparison.OrdinalIgnoreCase)
                || path.EndsWith(".jar.disabled", StringComparison.OrdinalIgnoreCase))
            .Select(path => new InstalledMod
            {
                FileName = Path.GetFileName(EnabledPathOf(path)),
                FullPath = path,
                SizeBytes = new FileInfo(path).Length,
                IsEnabled = !path.EndsWith(".jar.disabled", StringComparison.OrdinalIgnoreCase)
            })
            .OrderBy(mod => mod.FileName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// 启用/禁用模组：启用 = 去掉 .disabled 后缀；禁用 = 追加 .disabled。
    /// 目标文件已存在时抛出异常，避免覆盖同名文件。
    /// </summary>
    public static void SetEnabled(InstalledMod mod, bool enabled)
    {
        string targetPath = EnabledPathOf(mod.FullPath);
        if (!enabled)
        {
            targetPath += ".disabled";
        }

        if (string.Equals(mod.FullPath, targetPath, StringComparison.OrdinalIgnoreCase))
        {
            return; // 状态未变化。
        }
        if (File.Exists(targetPath))
        {
            throw new IOException(
                $"目标文件已存在：{Path.GetFileName(targetPath)}。\n请先在 mods 目录中处理重复文件。");
        }
        File.Move(mod.FullPath, targetPath);
    }

    /// <summary>把 .jar.disabled 路径还原为 .jar 路径。</summary>
    private static string EnabledPathOf(string path)
        => path.EndsWith(".jar.disabled", StringComparison.OrdinalIgnoreCase)
            ? path[..^".disabled".Length]
            : path;

    /// <summary>
    /// 从版本列表中挑选最佳（第一个可用的）版本。
    /// 指定加载器时仅取带主文件的版本；“任意加载器”时额外排除服务端插件，
    /// 只保留 Fabric/Forge/NeoForge/Quilt 等会进入 mods 目录的客户端模组。
    /// </summary>
    public static ModVersionInfo? SelectBestVersion(IEnumerable<ModVersionInfo> versions, string? loader)
    {
        IEnumerable<ModVersionInfo> candidates = versions.Where(v => v.PrimaryFile is not null);

        if (string.IsNullOrWhiteSpace(loader))
        {
            // “任意加载器”：剔除服务端插件，只保留真正的客户端模组。
            candidates = candidates.Where(v => v.Loaders.Any(l => ModLoaders.Contains(l)));
        }
        else
        {
            // 指定加载器：客户端再过滤一次，避免服务端返回中混入其它加载器的文件。
            candidates = candidates.Where(v =>
                v.Loaders.Any(l => string.Equals(l, loader, StringComparison.OrdinalIgnoreCase)));
        }

        return candidates.FirstOrDefault();
    }

    /// <summary>文件名是否带有任何加载器构建标记（-forge / -neoforge / -fabric / -quilt）。</summary>
    public static bool HasAnyLoaderMarker(string fileName)
    {
        string name = fileName.ToLowerInvariant();
        return name.Contains("-neoforge") || name.Contains("neoforge-")
               || name.Contains("-forge") || name.Contains("forge-")
               || name.Contains("-fabric") || name.Contains("fabric-")
               || name.Contains("-quilt") || name.Contains("quilt-");
    }

    /// <summary>
    /// 判断文件名是否对应指定加载器的构建。
    /// 注意：forge 匹配必须排除 neoforge（文件名同时含 "neoforge" 时不算 forge 构建）。
    /// </summary>
    public static bool IsFileForLoader(string fileName, string loader)
    {
        string name = fileName.ToLowerInvariant();
        return loader.ToLowerInvariant() switch
        {
            "neoforge" => name.Contains("-neoforge") || name.Contains("neoforge-"),
            "forge" => (name.Contains("-forge") || name.Contains("forge-")) && !name.Contains("neoforge"),
            "fabric" => name.Contains("-fabric") || name.Contains("fabric-"),
            "quilt" => name.Contains("-quilt") || name.Contains("quilt-"),
            _ => false
        };
    }

    /// <summary>
    /// 从版本的文件列表中选择与目标加载器匹配的文件：
    /// 优先文件名带加载器标记的构建（如 -forge / -neoforge / -fabric），
    /// 找不到或未指定加载器时回退到 primary 文件。
    /// </summary>
    public static ModFileInfo? SelectFileForLoader(ModVersionInfo version, string? loader)
    {
        if (version.Files.Count == 0)
        {
            return null;
        }
        if (!string.IsNullOrWhiteSpace(loader))
        {
            foreach (ModFileInfo file in version.Files)
            {
                if (IsFileForLoader(file.FileName, loader))
                {
                    return file;
                }
            }
        }
        return version.PrimaryFile;
    }

    // ─────────────────────────────────────────────────────────
    // 映射
    // ─────────────────────────────────────────────────────────

    private ModSearchResult MapSearchHit(ModrinthSearchHit hit)
    {
        return new ModSearchResult
        {
            ProjectId = hit.ProjectId,
            Slug = hit.Slug,
            Title = hit.Title,
            TitleZh = ModNameLocalizer.Localize(hit.Title),
            Description = hit.Description,
            Author = hit.Author ?? string.Empty,
            Downloads = hit.Downloads,
            IconUrl = hit.IconUrl is null ? null : RewriteFileUrl(hit.IconUrl),
            Categories = hit.DisplayCategories
        };
    }

    private ModVersionInfo MapVersion(ModrinthVersion version)
    {
        // 只保留可安装的主 jar（排除源码/文档 jar），再交给业务模型选 primary。
        List<ModFileInfo> files = version.Files
            .Where(f =>
                f.Filename.EndsWith(".jar", StringComparison.OrdinalIgnoreCase) &&
                !f.Filename.Contains("-sources", StringComparison.OrdinalIgnoreCase) &&
                !f.Filename.Contains("-javadoc", StringComparison.OrdinalIgnoreCase))
            .Select(f => new ModFileInfo
            {
                FileName = f.Filename,
                Url = RewriteFileUrl(f.Url),
                Size = f.Size,
                Sha1 = f.Hashes?.Sha1,
                Primary = f.Primary
            })
            .ToList();

        List<ModDependencyInfo> dependencies = version.Dependencies
            .Select(d => new ModDependencyInfo
            {
                ProjectId = d.ProjectId,
                VersionId = d.VersionId,
                Type = string.IsNullOrWhiteSpace(d.DependencyType) ? "required" : d.DependencyType
            })
            .ToList();

        return new ModVersionInfo
        {
            VersionId = version.Id,
            Name = version.Name,
            VersionNumber = version.VersionNumber,
            Loaders = version.Loaders,
            GameVersions = version.GameVersions,
            Files = files,
            Dependencies = dependencies
        };
    }

    private HttpRequestMessage CreateRequest(HttpMethod method, string url)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.TryAddWithoutValidation("User-Agent", CurrentUserAgent);
        return request;
    }
}
