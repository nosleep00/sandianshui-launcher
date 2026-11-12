using System.Text.Json;
using System.Text.Json.Serialization;

namespace SandianshuiLauncher.Core.Models;

/// <summary>
/// 单个版本的版本 JSON（versions/{id}/{id}.json）。
/// 加载器版本可通过 inheritsFrom 继承原版 JSON。
/// </summary>
public class VersionJson
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("type")]
    public string? Type { get; set; }

    [JsonPropertyName("mainClass")]
    public string? MainClass { get; set; }

    [JsonPropertyName("assets")]
    public string? Assets { get; set; }

    [JsonPropertyName("assetIndex")]
    public AssetIndexInfo? AssetIndex { get; set; }

    /// <summary>父版本 Id（如 Fabric/Forge 版本继承原版）。</summary>
    [JsonPropertyName("inheritsFrom")]
    public string? InheritsFrom { get; set; }

    /// <summary>实际使用的 jar 版本 Id（不填时等于 id）。</summary>
    [JsonPropertyName("jar")]
    public string? Jar { get; set; }

    /// <summary>client / server 等文件下载信息。</summary>
    [JsonPropertyName("downloads")]
    public Dictionary<string, DownloadInfo>? Downloads { get; set; }

    [JsonPropertyName("libraries")]
    public List<LibraryInfo>? Libraries { get; set; }

    /// <summary>1.13+ 的新式参数模板。</summary>
    [JsonPropertyName("arguments")]
    public ArgumentsInfo? Arguments { get; set; }

    /// <summary>1.12- 的旧式参数字符串。</summary>
    [JsonPropertyName("minecraftArguments")]
    public string? MinecraftArguments { get; set; }

    [JsonPropertyName("javaVersion")]
    public JavaVersionInfo? JavaVersion { get; set; }

    /// <summary>日志配置（当前不使用，保留以兼容反序列化）。</summary>
    [JsonPropertyName("logging")]
    public JsonElement? Logging { get; set; }

    [JsonPropertyName("complianceLevel")]
    public int? ComplianceLevel { get; set; }

    [JsonPropertyName("minimumLauncherVersion")]
    public int? MinimumLauncherVersion { get; set; }
}

/// <summary>资源索引信息。</summary>
public class AssetIndexInfo
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("sha1")]
    public string? Sha1 { get; set; }

    [JsonPropertyName("size")]
    public long? Size { get; set; }

    [JsonPropertyName("totalSize")]
    public long? TotalSize { get; set; }

    [JsonPropertyName("url")]
    public string? Url { get; set; }
}

/// <summary>通用下载文件描述。</summary>
public class DownloadInfo
{
    [JsonPropertyName("sha1")]
    public string? Sha1 { get; set; }

    [JsonPropertyName("size")]
    public long Size { get; set; }

    [JsonPropertyName("url")]
    public string? Url { get; set; }

    /// <summary>在 libraries 目录中的相对路径。</summary>
    [JsonPropertyName("path")]
    public string? Path { get; set; }
}

/// <summary>一个依赖库。</summary>
public class LibraryInfo
{
    /// <summary>Maven 坐标 group:artifact:version[:classifier][@ext]。</summary>
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("downloads")]
    public LibraryDownloads? Downloads { get; set; }

    /// <summary>平台规则（是否适用于当前系统）。</summary>
    [JsonPropertyName("rules")]
    public List<Rule>? Rules { get; set; }

    /// <summary>1.19- 原生库分类器映射，如 {"windows": "natives-windows"}。</summary>
    [JsonPropertyName("natives")]
    public Dictionary<string, string>? Natives { get; set; }

    [JsonPropertyName("extract")]
    public ExtractInfo? Extract { get; set; }

    /// <summary>老版本 Forge 风格的 Maven 仓库基础地址。</summary>
    [JsonPropertyName("url")]
    public string? Url { get; set; }

    /// <summary>1.13 前老版本标记：客户端是否需要此库。</summary>
    [JsonPropertyName("clientreq")]
    public bool? ClientReq { get; set; }

    /// <summary>1.13 前老版本标记：服务器是否需要此库。</summary>
    [JsonPropertyName("serverreq")]
    public bool? ServerReq { get; set; }

    /// <summary>Fabric 等加载器在库顶层提供的 SHA1 校验值。</summary>
    [JsonPropertyName("sha1")]
    public string? Sha1 { get; set; }

    /// <summary>Fabric 等加载器在库顶层提供的文件大小。</summary>
    [JsonPropertyName("size")]
    public long? Size { get; set; }
}

public class LibraryDownloads
{
    [JsonPropertyName("artifact")]
    public DownloadInfo? Artifact { get; set; }

    [JsonPropertyName("classifiers")]
    public Dictionary<string, DownloadInfo>? Classifiers { get; set; }
}

public class ExtractInfo
{
    [JsonPropertyName("exclude")]
    public List<string>? Exclude { get; set; }
}

/// <summary>规则（OS、功能特性）。</summary>
public class Rule
{
    /// <summary>allow 或 disallow。</summary>
    [JsonPropertyName("action")]
    public string Action { get; set; } = "allow";

    [JsonPropertyName("os")]
    public OsRule? Os { get; set; }

    [JsonPropertyName("features")]
    public Dictionary<string, bool>? Features { get; set; }
}

public class OsRule
{
    /// <summary>windows / osx / linux。</summary>
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("version")]
    public string? Version { get; set; }

    /// <summary>x86 / x64。</summary>
    [JsonPropertyName("arch")]
    public string? Arch { get; set; }
}

/// <summary>1.13+ 参数模板。数组元素可能是字符串，也可能是带规则的对象。</summary>
public class ArgumentsInfo
{
    [JsonPropertyName("game")]
    public List<JsonElement>? Game { get; set; }

    [JsonPropertyName("jvm")]
    public List<JsonElement>? Jvm { get; set; }
}

public class JavaVersionInfo
{
    [JsonPropertyName("component")]
    public string? Component { get; set; }

    [JsonPropertyName("majorVersion")]
    public int? MajorVersion { get; set; }
}
