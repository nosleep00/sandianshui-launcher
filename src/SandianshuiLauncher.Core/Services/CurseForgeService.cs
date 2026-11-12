using System.Net;
using System.Text.Json;
using SandianshuiLauncher.Core.Models;
using SandianshuiLauncher.Core.Utils;

namespace SandianshuiLauncher.Core.Services;

/// <summary>
/// CurseForge 模组服务：搜索、文件列表、下载地址与安装计划。
/// 使用官方 API（需 API Key，见设置页 CurseForgeApiKey）。
/// </summary>
public class CurseForgeService
{
    public const string ApiBase = "https://api.curseforge.com/v1";

    private const int MinecraftGameId = 432;
    private const int ModsClassId = 6;

    private readonly HttpClient _http;
    private readonly DownloadService _downloads;
    private readonly ConfigService _config;

    public CurseForgeService(HttpClient http, DownloadService downloads, ConfigService config)
    {
        _http = http;
        _downloads = downloads;
        _config = config;
    }

    /// <summary>已配置的 API Key（未配置时为 null）。</summary>
    public string? ApiKey => string.IsNullOrWhiteSpace(_config.Config.CurseForgeApiKey)
        ? null
        : _config.Config.CurseForgeApiKey.Trim();

    public bool HasApiKey => ApiKey is not null;

    private void RequireKey()
    {
        if (ApiKey is null)
        {
            throw new InvalidOperationException(
                "使用 CurseForge 需要 API Key。\n请在“设置”中填写 CurseForge API Key" +
                "（https://console.curseforge.com 申请），或切换到 Modrinth 平台。");
        }
    }

    /// <summary>加载器 → CurseForge modLoaderType 参数（1=Forge 4=Fabric 5=Quilt 6=NeoForge）。</summary>
    internal static int? LoaderTypeId(string? loader) => loader?.ToLowerInvariant() switch
    {
        "forge" => 1,
        "fabric" => 4,
        "quilt" => 5,
        "neoforge" => 6,
        "liteloader" => 3,
        _ => null
    };

    /// <summary>搜索模组（按关键词，可按游戏版本与加载器过滤）。</summary>
    public async Task<List<CurseForgeMod>> SearchAsync(
        string query,
        string? gameVersion,
        string? loader,
        int pageSize = 30,
        CancellationToken cancellationToken = default)
    {
        RequireKey();
        string url = $"{ApiBase}/mods/search?gameId={MinecraftGameId}&classId={ModsClassId}" +
                     $"&searchFilter={Uri.EscapeDataString(query)}&sortField=2&sortOrder=desc&pageSize={pageSize}";
        if (!string.IsNullOrWhiteSpace(gameVersion))
        {
            url += "&gameVersion=" + Uri.EscapeDataString(gameVersion!);
        }
        if (!string.IsNullOrWhiteSpace(gameVersion) && LoaderTypeId(loader) is { } loaderType)
        {
            url += "&modLoaderType=" + loaderType;
        }

        using var request = CreateRequest(HttpMethod.Get, url);
        using var response = await _http.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(DescribeFailure(response));
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var data = await JsonSerializer.DeserializeAsync<CfSearchResponse>(stream, JsonOptions.Minecraft, cancellationToken);
        return (data?.Data ?? []).Select(dto => new CurseForgeMod
        {
            Id = dto.Id,
            Name = dto.Name,
            Slug = dto.Slug,
            Summary = dto.Summary,
            DownloadCount = dto.DownloadCount,
            IconUrl = dto.Logo?.ThumbnailUrl ?? dto.Logo?.Url,
            WebsiteUrl = dto.Links?.WebsiteUrl
        }).ToList();
    }

    /// <summary>获取模组的文件列表（可按游戏版本与加载器过滤，返回按上传时间降序）。</summary>
    public async Task<List<CurseForgeFileInfo>> GetFilesAsync(
        long modId,
        string? gameVersion,
        string? loader,
        CancellationToken cancellationToken = default)
    {
        RequireKey();
        string url = $"{ApiBase}/mods/{modId}/files?pageSize=50";
        if (!string.IsNullOrWhiteSpace(gameVersion))
        {
            url += "&gameVersion=" + Uri.EscapeDataString(gameVersion!);
        }
        if (!string.IsNullOrWhiteSpace(gameVersion) && LoaderTypeId(loader) is { } loaderType)
        {
            url += "&modLoaderType=" + loaderType;
        }

        using var request = CreateRequest(HttpMethod.Get, url);
        using var response = await _http.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(DescribeFailure(response));
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var data = await JsonSerializer.DeserializeAsync<CfFilesResponse>(stream, JsonOptions.Minecraft, cancellationToken);
        return (data?.Data ?? []).Select(dto => new CurseForgeFileInfo
        {
            Id = dto.Id,
            FileName = dto.FileName,
            Length = dto.FileLength,
            Sha1 = dto.Hashes.FirstOrDefault(h => h.Algo == 1)?.Value,
            DownloadUrl = dto.DownloadUrl,
            GameVersions = dto.GameVersions,
            Dependencies = dto.Dependencies
                .Select(d => new CurseForgeDependency { ModId = d.ModId, RelationType = d.RelationType })
                .ToList(),
            ReleaseType = dto.ReleaseType
        }).ToList();
    }

    /// <summary>换取文件下载地址（需要 API Key）。</summary>
    public async Task<string> GetDownloadUrlAsync(
        long modId, long fileId, CancellationToken cancellationToken = default)
    {
        RequireKey();
        using var request = CreateRequest(
            HttpMethod.Get, $"{ApiBase}/mods/{modId}/files/{fileId}/download-url");
        using var response = await _http.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(DescribeFailure(response));
        }
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var data = await JsonSerializer.DeserializeAsync<CfDownloadUrlResponse>(
            stream, JsonOptions.Minecraft, cancellationToken);
        if (string.IsNullOrWhiteSpace(data?.Data))
        {
            throw new InvalidDataException("CurseForge 未返回下载地址，请稍后重试。");
        }
        return data.Data!;
    }

    /// <summary>选择最佳文件：正式版（Release）优先，其次按 ID 降序（较新）。</summary>
    public static CurseForgeFileInfo? SelectBestFile(IEnumerable<CurseForgeFileInfo> files)
        => files
            .OrderBy(f => f.ReleaseType != 1)
            .ThenByDescending(f => f.Id)
            .FirstOrDefault();

    /// <summary>
    /// 构建 CurseForge 安装计划：主文件 + 必需依赖（relationType=3）的传递闭包，
    /// 输出与 Modrinth 相同的 <see cref="ModInstallPlan"/> 供统一执行。
    /// </summary>
    public async Task<ModInstallPlan> BuildInstallPlanAsync(
        long modId,
        string gameVersion,
        string? loader,
        string targetDirectory,
        CancellationToken cancellationToken = default)
    {
        List<CurseForgeFileInfo> files = await GetFilesAsync(modId, gameVersion, loader, cancellationToken);
        CurseForgeFileInfo? primary = SelectBestFile(files);
        if (primary is null)
        {
            throw new InvalidOperationException(
                $"该模组没有适用于 {gameVersion}（{loader ?? "任意加载器"}）的文件，请换个版本或加载器。");
        }

        string primaryUrl = primary.DownloadUrl ?? await GetDownloadUrlAsync(modId, primary.Id, cancellationToken);
        var primaryModFile = new ModFileInfo
        {
            FileName = primary.FileName,
            Url = primaryUrl,
            Size = primary.Length,
            Sha1 = primary.Sha1,
            Primary = true
        };

        var items = new List<ModInstallItem>();
        var notes = new List<string>();
        var itemFileNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var visited = new HashSet<long> { modId };
        var queue = new Queue<CurseForgeDependency>(primary.Dependencies.Where(d => d.IsRequired));

        int depth = 0;
        const int MaxDepth = 6;
        const int MaxItems = 64;

        while (queue.Count > 0 && depth < MaxDepth && items.Count < MaxItems)
        {
            int levelCount = queue.Count;
            for (int i = 0; i < levelCount; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                CurseForgeDependency dep = queue.Dequeue();
                if (!visited.Add(dep.ModId))
                {
                    continue;
                }

                try
                {
                    List<CurseForgeFileInfo> depFiles = await GetFilesAsync(
                        dep.ModId, gameVersion, loader, cancellationToken);
                    CurseForgeFileInfo? depFile = SelectBestFile(depFiles);
                    if (depFile is null)
                    {
                        notes.Add($"前置模组 {dep.ModId} 没有适用于 {gameVersion} 的文件，已跳过（可能导致游戏启动失败）。");
                        continue;
                    }

                    string depPath = Path.Combine(targetDirectory, depFile.FileName);
                    if (HashUtils.VerifySha1(depPath, depFile.Sha1))
                    {
                        notes.Add($"已安装：{depFile.FileName}");
                        continue;
                    }

                    string depUrl = depFile.DownloadUrl ?? await GetDownloadUrlAsync(dep.ModId, depFile.Id, cancellationToken);
                    if (itemFileNames.Add(depFile.FileName))
                    {
                        items.Add(new ModInstallItem
                        {
                            ProjectId = dep.ModId.ToString(),
                            Title = depFile.FileName,
                            File = new ModFileInfo
                            {
                                FileName = depFile.FileName,
                                Url = depUrl,
                                Size = depFile.Length,
                                Sha1 = depFile.Sha1
                            }
                        });
                    }

                    foreach (CurseForgeDependency sub in depFile.Dependencies.Where(d => d.IsRequired))
                    {
                        queue.Enqueue(sub);
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    notes.Add($"前置模组 {dep.ModId} 解析失败：{ex.Message}");
                }
            }
            depth++;
        }

        return new ModInstallPlan
        {
            Version = new ModVersionInfo
            {
                VersionId = primary.Id.ToString(),
                Name = primary.FileName,
                Files = [primaryModFile]
            },
            PrimaryFile = primaryModFile,
            Dependencies = items,
            SkippedNotes = notes
        };
    }

    /// <summary>执行 CurseForge 安装计划（复用 ModService 的统一执行器）。</summary>
    public Task<InstalledMod> ExecuteInstallPlanAsync(
        ModInstallPlan plan,
        string targetDirectory,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
        => ModService.ExecuteInstallPlanAsync(plan, targetDirectory, _downloads, progress, cancellationToken);

    private HttpRequestMessage CreateRequest(HttpMethod method, string url)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.TryAddWithoutValidation("x-api-key", ApiKey);
        request.Headers.TryAddWithoutValidation("User-Agent", LauncherInfo.UserAgent);
        return request;
    }

    /// <summary>把 CurseForge API 失败转成中文说明（公开工具，便于复用与测试）。</summary>
    public static string DescribeFailure(HttpResponseMessage response)
    {
        int status = (int)response.StatusCode;
        return status switch
        {
            403 => "CurseForge API 拒绝了请求（HTTP 403）。\n请检查设置中的 API Key 是否正确、是否已被吊销。",
            429 => "请求过于频繁（HTTP 429）。\n请稍等片刻后重试。",
            >= 500 => $"CurseForge 服务暂时不可用（HTTP {status}）。\n请稍后重试。",
            _ => $"CurseForge 请求失败（HTTP {status}）。\n请检查网络与 API Key 后重试。"
        };
    }
}
