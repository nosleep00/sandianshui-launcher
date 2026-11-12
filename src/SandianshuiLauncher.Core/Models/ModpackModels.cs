using System.Text.Json.Serialization;

namespace SandianshuiLauncher.Core.Models;

/// <summary>Modrinth 整合包（.mrpack）的 modrinth.index.json。</summary>
public class ModrinthIndex
{
    [JsonPropertyName("formatVersion")]
    public int FormatVersion { get; set; }

    [JsonPropertyName("game")]
    public string Game { get; set; } = "minecraft";

    [JsonPropertyName("versionId")]
    public string? VersionId { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("dependencies")]
    public Dictionary<string, string> Dependencies { get; set; } = [];

    [JsonPropertyName("files")]
    public List<ModrinthIndexFile> Files { get; set; } = [];
}

/// <summary>整合包清单中的一个文件条目。</summary>
public class ModrinthIndexFile
{
    [JsonPropertyName("path")]
    public string? Path { get; set; }

    [JsonPropertyName("hashes")]
    public ModrinthIndexHashes? Hashes { get; set; }

    [JsonPropertyName("env")]
    public ModrinthIndexEnv? Env { get; set; }

    [JsonPropertyName("downloads")]
    public List<string> Downloads { get; set; } = [];

    [JsonPropertyName("fileSize")]
    public long FileSize { get; set; }
}

public class ModrinthIndexHashes
{
    [JsonPropertyName("sha1")]
    public string? Sha1 { get; set; }

    [JsonPropertyName("sha512")]
    public string? Sha512 { get; set; }
}

public class ModrinthIndexEnv
{
    /// <summary>required / optional / unsupported。</summary>
    [JsonPropertyName("client")]
    public string Client { get; set; } = "required";

    [JsonPropertyName("server")]
    public string Server { get; set; } = "required";
}

/// <summary>CurseForge 整合包 zip 内的 manifest.json。</summary>
public class CurseForgeManifest
{
    [JsonPropertyName("minecraft")]
    public CurseForgeManifestMinecraft? Minecraft { get; set; }

    [JsonPropertyName("manifestType")]
    public string? ManifestType { get; set; }

    [JsonPropertyName("manifestVersion")]
    public int ManifestVersion { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("version")]
    public string? Version { get; set; }

    [JsonPropertyName("author")]
    public string? Author { get; set; }

    [JsonPropertyName("files")]
    public List<CurseForgeManifestFile> Files { get; set; } = [];

    [JsonPropertyName("overrides")]
    public string? Overrides { get; set; }
}

public class CurseForgeManifestMinecraft
{
    [JsonPropertyName("version")]
    public string? Version { get; set; }

    [JsonPropertyName("modLoaders")]
    public List<CurseForgeModLoader> ModLoaders { get; set; } = [];
}

public class CurseForgeModLoader
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("primary")]
    public bool Primary { get; set; }
}

public class CurseForgeManifestFile
{
    [JsonPropertyName("projectID")]
    public long ProjectId { get; set; }

    [JsonPropertyName("fileID")]
    public long FileId { get; set; }

    [JsonPropertyName("required")]
    public bool Required { get; set; } = true;
}

/// <summary>整合包导入结果。</summary>
public sealed class ModpackImportResult
{
    public required string PackName { get; init; }

    public int ImportedFiles { get; init; }

    public int OverrideFiles { get; init; }

    /// <summary>导入过程中的非致命提示（依赖不匹配、无 API Key 降级等）。</summary>
    public IReadOnlyList<string> Warnings { get; init; } = [];
}

/// <summary>zip 整合包类型（按内部清单文件识别）。</summary>
public enum PackFileType
{
    Unknown,
    ModrinthMrpack,
    CurseForge,
    SdlauncherPack
}

/// <summary>SDLauncherPack 自定义整合包清单（sdlauncher-pack.json）。</summary>
public sealed class SdlauncherPackManifest
{
    [JsonPropertyName("format_version")]
    public int FormatVersion { get; set; } = 1;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("game_version")]
    public string GameVersion { get; set; } = string.Empty;

    [JsonPropertyName("loader")]
    public string Loader { get; set; } = "vanilla";

    [JsonPropertyName("version_id")]
    public string VersionId { get; set; } = string.Empty;

    [JsonPropertyName("mods")]
    public List<SdlauncherPackMod> Mods { get; set; } = [];

    [JsonPropertyName("configs")]
    public List<SdlauncherPackFile> Configs { get; set; } = [];
}

/// <summary>整合包内的一个模组条目（含启用状态）。</summary>
public sealed class SdlauncherPackMod
{
    [JsonPropertyName("file_name")]
    public string FileName { get; set; } = string.Empty;

    [JsonPropertyName("sha1")]
    public string Sha1 { get; set; } = string.Empty;

    [JsonPropertyName("size")]
    public long Size { get; set; }

    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; } = true;
}

/// <summary>整合包内的一个配置文件条目。</summary>
public sealed class SdlauncherPackFile
{
    [JsonPropertyName("path")]
    public string Path { get; set; } = string.Empty;

    [JsonPropertyName("sha1")]
    public string Sha1 { get; set; } = string.Empty;
}
