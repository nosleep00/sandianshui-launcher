namespace SandianshuiLauncher.Core.Models;

// ─────────────────────────────────────────────────────────────
// 统一资源模型（对标 PCL 的 Comp 抽象）
//
// 设计目标：用同一套「类型 + 搜索请求 + 排序」抽象描述 Mod 以及未来的
// 资源包、光影、数据包、整合包、世界等资源，让新增资源类型时复用
// 相同的搜索 / 下载 / 缓存逻辑，而不必为每种资源各写一套。
//
// 当前仅 Mod 完整接入；其余类型先预留枚举与映射，后续逐步启用。
// ─────────────────────────────────────────────────────────────

/// <summary>
/// 统一资源类型（对标 PCL 的 CompType）。
/// 枚举名保持稳定，对外 API 字符串通过 <see cref="ResourceTypeExtensions.ToApiName"/> 映射。
/// </summary>
public enum ResourceType
{
    /// <summary>模组（当前已完整接入）。</summary>
    Mod,

    /// <summary>整合包（预留）。</summary>
    Modpack,

    /// <summary>资源包（预留）。</summary>
    ResourcePack,

    /// <summary>光影包（预留）。</summary>
    Shader,

    /// <summary>数据包（预留）。</summary>
    DataPack,

    /// <summary>服务端插件（预留）。</summary>
    Plugin,

    /// <summary>世界 / 地图存档（预留）。</summary>
    World
}

/// <summary>
/// 资源排序方式（对标 PCL 的 CompSortType），
/// 映射到 Modrinth 搜索接口的 <c>index</c> 参数。
/// </summary>
public enum ResourceSortType
{
    /// <summary>相关性（默认）。</summary>
    Relevance,

    /// <summary>下载量从高到低。</summary>
    Downloads,

    /// <summary>关注量从高到低。</summary>
    Follows,

    /// <summary>最新发布。</summary>
    Newest,

    /// <summary>最近更新。</summary>
    Updated
}

/// <summary>
/// 统一资源搜索请求（对标 PCL 的 CompProjectRequest）。
/// 不可变，由调用方按需初始化，默认值对应「模组 + 相关性排序」。
/// </summary>
public sealed class ResourceSearchRequest
{
    /// <summary>资源类型，默认模组。</summary>
    public ResourceType Type { get; init; } = ResourceType.Mod;

    /// <summary>搜索关键词（可为空）。</summary>
    public string Query { get; init; } = string.Empty;

    /// <summary>游戏版本过滤（如 1.20.4），可为空。</summary>
    public string? GameVersion { get; init; }

    /// <summary>加载器过滤（如 fabric / forge），可为空表示任意加载器。</summary>
    public string? Loader { get; init; }

    /// <summary>
    /// 功能分类过滤（如 worldgen、adventure、utility），可为空。
    /// 功能分类与加载器在 Modrinth 同属 categories，通过 facets 叠加。
    /// </summary>
    public string? Category { get; init; }

    /// <summary>排序方式，默认相关性。</summary>
    public ResourceSortType Sort { get; init; } = ResourceSortType.Relevance;

    /// <summary>返回条数上限（接口允许 1~100）。</summary>
    public int Limit { get; init; } = 30;
}

/// <summary><see cref="ResourceType"/> 到 Modrinth API 字符串的映射扩展。</summary>
public static class ResourceTypeExtensions
{
    /// <summary>返回 Modrinth <c>project_type</c> 使用的小写名称。</summary>
    public static string ToApiName(this ResourceType type) => type switch
    {
        ResourceType.Mod => "mod",
        ResourceType.Modpack => "modpack",
        ResourceType.ResourcePack => "resourcepack",
        ResourceType.Shader => "shader",
        ResourceType.DataPack => "datapack",
        ResourceType.Plugin => "plugin",
        ResourceType.World => "world",
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, "未知的资源类型。")
    };
}

/// <summary><see cref="ResourceSortType"/> 到 Modrinth <c>index</c> 参数的映射扩展。</summary>
public static class ResourceSortTypeExtensions
{
    /// <summary>返回 Modrinth 搜索 <c>index</c> 使用的小写名称。</summary>
    public static string ToApiIndex(this ResourceSortType sort) => sort switch
    {
        ResourceSortType.Relevance => "relevance",
        ResourceSortType.Downloads => "downloads",
        ResourceSortType.Follows => "follows",
        ResourceSortType.Newest => "newest",
        ResourceSortType.Updated => "updated",
        _ => throw new ArgumentOutOfRangeException(nameof(sort), sort, "未知的排序方式。")
    };
}
