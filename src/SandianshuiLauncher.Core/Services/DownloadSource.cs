using SandianshuiLauncher.Core.Models;

namespace SandianshuiLauncher.Core.Services;

/// <summary>
/// 下载源抽象：负责把官方地址重写为镜像地址，并提供版本清单入口。
/// 自定义镜像默认采用与 BMCLAPI 相同的路径规则（项目所有者提供镜像后只需填写基础地址）。
/// </summary>
public abstract class DownloadSource
{
    public abstract string Id { get; }

    public abstract string DisplayName { get; }

    /// <summary>版本清单地址。</summary>
    public abstract string ManifestUrl { get; }

    /// <summary>
    /// 版本清单的候选地址（主源在前，失败时按顺序回退）。
    /// 镜像站偶尔会出现证书失效、节点故障，仅靠单一地址会导致「刷新版本列表」直接失败，
    /// 因此与文件下载一样保留回退链。
    /// </summary>
    public virtual IReadOnlyList<string> BuildManifestCandidates() => [ManifestUrl];

    /// <summary>把官方文件 URL 重写为当前下载源的 URL。</summary>
    public abstract string Rewrite(string officialUrl);

    /// <summary>
    /// 给出官方文件的全部候选下载地址（主地址在前，失败时按顺序回退）。
    /// 默认仅当前源；BMCL 兼容镜像额外回退到 BMCLAPI 与官方地址。
    /// </summary>
    public virtual IReadOnlyList<string> BuildCandidates(string officialUrl)
        => [Rewrite(officialUrl)];

    /// <summary>
    /// 把（可能已被镜像改写的）URL 还原为官方 URL。
    /// 镜像返回的版本 JSON 中，库地址往往已指向镜像自身，
    /// 还原后才能正确生成"镜像 → BMCL → 官方"的候选链。默认原样返回。
    /// </summary>
    public virtual string ToOfficialUrl(string url) => url;

    // —— 内置镜像基址 ——

    /// <summary>MC 光速镜像（BMCLAPI 兼容）主线路，白山云节点。</summary>
    public const string GuangsuBaseUrl = "https://bsmirror.hkmc.online";

    /// <summary>MC 光速镜像备用线路（EO 节点），清单内嵌文件可能使用该域名。</summary>
    public const string GuangsuBackupBaseUrl = "https://mirror.hkmc.online";

    /// <summary>BMCLAPI 公益源基址。</summary>
    public const string BmclBaseUrl = "https://bmclapi2.bangbang93.com";

    /// <summary>内置下载源标识（顺序即设置页展示顺序）。</summary>
    public static readonly string[] BuiltInIds = ["guangsu", "mojang", "bmclapi", "custom"];

    /// <summary>根据配置创建下载源。</summary>
    public static DownloadSource Create(LauncherConfig config)
    {
        return config.DownloadSourceId switch
        {
            "guangsu" => new BmclCompatibleSource(
                GuangsuBaseUrl,
                "guangsu",
                "MC 光速镜像（推荐·主线路）",
                // 清单内嵌的文件可能指向备用线路，统一归一化到主线路。
                [GuangsuBaseUrl, GuangsuBackupBaseUrl]),
            "bmclapi" => new BmclCompatibleSource(
                BmclBaseUrl,
                "bmclapi",
                "BMCLAPI 公益源"),
            "custom" when !string.IsNullOrWhiteSpace(config.CustomMirrorBaseUrl)
                => new BmclCompatibleSource(
                    config.CustomMirrorBaseUrl!.TrimEnd('/'),
                    "custom",
                    "自定义镜像（BMCLAPI 兼容）"),
            _ => new MojangSource()
        };
    }

    /// <summary>官方下载源。</summary>
    public const string OfficialManifestUrl =
        "https://piston-meta.mojang.com/mc/game/version_manifest_v2.json";
}

/// <summary>Mojang 官方源：不做重写。</summary>
public sealed class MojangSource : DownloadSource
{
    public override string Id => "mojang";
    public override string DisplayName => "Mojang 官方源";
    public override string ManifestUrl => OfficialManifestUrl;
    public override string Rewrite(string officialUrl) => officialUrl;
}

/// <summary>BMCLAPI 兼容镜像（路径规则公开，多数国内镜像站兼容）。</summary>
public class BmclCompatibleSource : DownloadSource
{
    private readonly string _baseUrl;
    private readonly string _id;
    private readonly string _displayName;

    /// <summary>
    /// 镜像自身域名集合：清单内嵌的文件可能已经使用这些域名，
    /// 重写时只替换域名、保留完整路径，从而把流量统一到主线路基址。
    /// </summary>
    private readonly HashSet<string> _mirrorHosts;

    public BmclCompatibleSource(
        string baseUrl,
        string id = "mirror",
        string displayName = "国内镜像源（BMCLAPI 兼容）",
        IEnumerable<string>? aliasBaseUrls = null)
    {
        _baseUrl = baseUrl.TrimEnd('/');
        _id = id;
        _displayName = displayName;
        _mirrorHosts = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { HostOf(_baseUrl) };
        if (aliasBaseUrls is not null)
        {
            foreach (string alias in aliasBaseUrls)
            {
                _mirrorHosts.Add(HostOf(alias.TrimEnd('/')));
            }
        }
    }

    private static string HostOf(string url)
        => Uri.TryCreate(url, UriKind.Absolute, out var uri) ? uri.Host.ToLowerInvariant() : url;

    public override string Id => _id;

    public override string DisplayName => _displayName;

    public override string ManifestUrl => Rewrite(OfficialManifestUrl);

    /// <summary>清单候选：当前镜像 → BMCLAPI（非 BMCL 自身时）→ 官方。</summary>
    public override IReadOnlyList<string> BuildManifestCandidates()
    {
        var candidates = new List<string> { ManifestUrl };

        if (!string.Equals(_id, "bmclapi", StringComparison.OrdinalIgnoreCase))
        {
            string bmclUrl = new BmclCompatibleSource(BmclBaseUrl, "bmclapi").ManifestUrl;
            if (!candidates.Contains(bmclUrl))
            {
                candidates.Add(bmclUrl);
            }
        }

        if (!candidates.Contains(OfficialManifestUrl))
        {
            candidates.Add(OfficialManifestUrl);
        }
        return candidates;
    }

    public override string Rewrite(string officialUrl)
    {
        if (string.IsNullOrWhiteSpace(officialUrl) || !Uri.TryCreate(officialUrl, UriKind.Absolute, out var uri))
        {
            return officialUrl;
        }

        string host = uri.Host.ToLowerInvariant();

        // 已经是镜像域名（主线路自身/备用线路）：仅统一替换域名，保留完整路径。
        if (_mirrorHosts.Contains(host))
        {
            return $"{_baseUrl}{uri.AbsolutePath}{uri.Query}";
        }

        return host switch
        {
            // 资源文件：/assets/{hash 前两位}/{hash}
            "resources.download.minecraft.net" => $"{_baseUrl}/assets{uri.AbsolutePath}",
            // 依赖库：/maven/...
            "libraries.minecraft.net" => $"{_baseUrl}/maven{uri.AbsolutePath}",
            "maven.fabricmc.net" => $"{_baseUrl}/maven{uri.AbsolutePath}",
            // Forge / NeoForge 依赖库与安装器
            "maven.minecraftforge.net" => $"{_baseUrl}/maven{uri.AbsolutePath}",
            // NeoForge 官方仓库 URL 带 /releases 前缀（仓库标识，不属于 Maven 路径），
            // 镜像统一为 /maven/{group}/{artifact}/... 路径。
            "maven.neoforged.net" => $"{_baseUrl}/maven" + (
                uri.AbsolutePath.StartsWith("/releases/", StringComparison.OrdinalIgnoreCase)
                    ? uri.AbsolutePath["/releases".Length..]
                    : uri.AbsolutePath),
            // files.minecraftforge.net 的官方路径已带 /maven 前缀，仅替换域名。
            "files.minecraftforge.net" => $"{_baseUrl}{uri.AbsolutePath}",
            // Fabric / Quilt 元数据
            "meta.fabricmc.net" => $"{_baseUrl}/fabric-meta{uri.AbsolutePath}",
            "meta.quiltmc.org" => $"{_baseUrl}/quilt-meta{uri.AbsolutePath}",
            // 元数据与游戏 jar
            "piston-meta.mojang.com"
                or "piston-data.mojang.com"
                or "launchermeta.mojang.com"
                or "launchers.mojang.com"
                or "launcher.mojang.com" => $"{_baseUrl}{uri.AbsolutePath}",
            _ => officialUrl
        };
    }

    /// <summary>
    /// 候选地址：当前镜像 → BMCLAPI（非 BMCL 自身时）→ 官方。
    /// 这样即使某个镜像缺少文件或传输中断，也能自动回退到其他源。
    /// </summary>
    public override IReadOnlyList<string> BuildCandidates(string officialUrl)
    {
        var candidates = new List<string> { Rewrite(officialUrl) };

        if (!string.Equals(_id, "bmclapi", StringComparison.OrdinalIgnoreCase))
        {
            string bmclUrl = new BmclCompatibleSource(BmclBaseUrl, "bmclapi").Rewrite(officialUrl);
            if (!candidates.Contains(bmclUrl))
            {
                candidates.Add(bmclUrl);
            }
        }

        if (!candidates.Contains(officialUrl))
        {
            candidates.Add(officialUrl);
        }
        return candidates;
    }

    /// <summary>
    /// 把镜像（含 BMCLAPI、光速主/备线路）改写的 URL 还原为官方 URL。
    /// </summary>
    public override string ToOfficialUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url) || !Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return url;
        }

        string host = uri.Host.ToLowerInvariant();
        bool isMirror = _mirrorHosts.Contains(host) || host == "bmclapi2.bangbang93.com";
        if (!isMirror)
        {
            // 非镜像地址，已经是官方形态。
            return url;
        }

        string path = uri.AbsolutePath;

        // Fabric 依赖库：/maven/net/fabricmc/...
        if (path.StartsWith("/maven/net/fabricmc/", StringComparison.OrdinalIgnoreCase))
        {
            return "https://maven.fabricmc.net" + path.Substring("/maven".Length);
        }
        // NeoForge 依赖库：/maven/net/neoforged/... → 官方仓库（带 /releases 前缀）
        if (path.StartsWith("/maven/net/neoforged/", StringComparison.OrdinalIgnoreCase))
        {
            return "https://maven.neoforged.net/releases" + path.Substring("/maven".Length);
        }
        // Forge / NeoForge 依赖库：/maven/net/minecraftforge/...
        if (path.StartsWith("/maven/net/minecraftforge/", StringComparison.OrdinalIgnoreCase))
        {
            return "https://maven.minecraftforge.net" + path.Substring("/maven".Length);
        }
        // Fabric / Quilt 元数据
        if (path.StartsWith("/fabric-meta/", StringComparison.OrdinalIgnoreCase))
        {
            return "https://meta.fabricmc.net" + path.Substring("/fabric-meta".Length);
        }
        if (path.StartsWith("/quilt-meta/", StringComparison.OrdinalIgnoreCase))
        {
            return "https://meta.quiltmc.org" + path.Substring("/quilt-meta".Length);
        }
        // 资源文件：/assets/...
        if (path.StartsWith("/assets/", StringComparison.OrdinalIgnoreCase))
        {
            return "https://resources.download.minecraft.net" + path.Substring("/assets".Length);
        }
        // 其他依赖库：/maven/... → libraries.minecraft.net
        if (path.StartsWith("/maven/", StringComparison.OrdinalIgnoreCase))
        {
            return "https://libraries.minecraft.net" + path.Substring("/maven".Length);
        }

        // 无法识别的镜像路径，原样返回。
        return url;
    }
}
