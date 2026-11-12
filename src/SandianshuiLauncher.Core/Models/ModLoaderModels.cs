using System.Text.Json.Serialization;

namespace SandianshuiLauncher.Core.Models;

/// <summary>Fabric loader 列表元素（/v2/versions/loader/{game_version}）。</summary>
public class FabricLoaderListEntry
{
    [JsonPropertyName("loader")]
    public FabricLoaderInfo? Loader { get; set; }
}

/// <summary>Fabric loader 信息。</summary>
public class FabricLoaderInfo
{
    [JsonPropertyName("separator")]
    public string? Separator { get; set; }

    [JsonPropertyName("build")]
    public int? Build { get; set; }

    [JsonPropertyName("maven")]
    public string? Maven { get; set; }

    [JsonPropertyName("version")]
    public string? Version { get; set; }

    [JsonPropertyName("stable")]
    public bool Stable { get; set; }
}

/// <summary>Forge 版本列表元素（BMCLAPI /forge/minecraft/{mcversion}）。</summary>
public class ForgeVersionEntry
{
    [JsonPropertyName("version")]
    public string? Version { get; set; }

    [JsonPropertyName("build")]
    public int? Build { get; set; }

    [JsonPropertyName("mcversion")]
    public string? McVersion { get; set; }
}

/// <summary>Forge 官方 promotions_slim.json（版本列表回退源）。</summary>
public class ForgePromotions
{
    [JsonPropertyName("promos")]
    public Dictionary<string, string> Promos { get; set; } = [];
}

/// <summary>Forge 安装器内的 install_profile.json。</summary>
public class ForgeInstallProfile
{
    [JsonPropertyName("spec")]
    public int? Spec { get; set; }

    [JsonPropertyName("profile")]
    public string? Profile { get; set; }

    /// <summary>安装后版本 Id，如 1.20.4-forge-49.2.13。</summary>
    [JsonPropertyName("version")]
    public string? Version { get; set; }

    /// <summary>主构件坐标（如 net.minecraftforge:forge:...:shim）。</summary>
    [JsonPropertyName("path")]
    public string? InstallPath { get; set; }

    /// <summary>对应的原版 Minecraft 版本。</summary>
    [JsonPropertyName("minecraft")]
    public string? Minecraft { get; set; }

    /// <summary>版本模板在安装器内的路径，如 /version.json。</summary>
    [JsonPropertyName("json")]
    public string? JsonPath { get; set; }

    /// <summary>数据文件（含客户端/服务器取值）。</summary>
    [JsonPropertyName("data")]
    public Dictionary<string, ForgeDataEntry>? Data { get; set; }

    /// <summary>安装过程要运行的处理器。</summary>
    [JsonPropertyName("processors")]
    public List<ForgeProcessor>? Processors { get; set; }

    /// <summary>安装过程需要的依赖库。</summary>
    [JsonPropertyName("libraries")]
    public List<LibraryInfo>? Libraries { get; set; }
}

/// <summary>Forge 数据条目，分别给出客户端与服务器取值。</summary>
public class ForgeDataEntry
{
    [JsonPropertyName("client")]
    public string? Client { get; set; }

    [JsonPropertyName("server")]
    public string? Server { get; set; }
}

/// <summary>Forge 安装处理器。</summary>
public class ForgeProcessor
{
    /// <summary>适用端：client / server；为空表示两端都运行。</summary>
    [JsonPropertyName("sides")]
    public List<string>? Sides { get; set; }

    /// <summary>处理器主程序 jar 的 Maven 坐标。</summary>
    [JsonPropertyName("jar")]
    public string? Jar { get; set; }

    /// <summary>处理器依赖的 classpath Maven 坐标。</summary>
    [JsonPropertyName("classpath")]
    public List<string>? Classpath { get; set; }

    /// <summary>命令行参数（含占位符）。</summary>
    [JsonPropertyName("args")]
    public List<string>? Args { get; set; }

    /// <summary>输出文件与期望 SHA1（用于断点跳过与校验）。</summary>
    [JsonPropertyName("outputs")]
    public Dictionary<string, string>? Outputs { get; set; }
}

/// <summary>Forge 处理器运行上下文（占位符替换所需）。</summary>
public sealed class ForgeProcessorContext
{
    /// <summary>游戏根目录（{ROOT}）。</summary>
    public required string GameDirectory { get; init; }

    /// <summary>libraries 目录（{LIBRARY_DIR}）。</summary>
    public required string LibrariesDir { get; init; }

    /// <summary>原版 Minecraft 版本（{MINECRAFT_VERSION}）。</summary>
    public required string McVersion { get; init; }

    /// <summary>安装器 jar 路径（{INSTALLER}）。</summary>
    public required string InstallerPath { get; init; }

    /// <summary>安装器解压目录（/开头的 data 取值指向此处）。</summary>
    public required string InstallerExt { get; init; }

    /// <summary>install_profile 的 data 表。</summary>
    public required Dictionary<string, ForgeDataEntry> Data { get; init; }
}
