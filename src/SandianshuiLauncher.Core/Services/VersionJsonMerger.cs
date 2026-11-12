using SandianshuiLauncher.Core.Models;
using SandianshuiLauncher.Core.Utils;

namespace SandianshuiLauncher.Core.Services;

/// <summary>
/// 合并带 inheritsFrom 的版本 JSON：子版本覆盖父版本的标量字段，
/// 依赖库按 Maven 坐标去重（子版本优先），参数数组前后拼接。
/// </summary>
public static class VersionJsonMerger
{
    public static VersionJson Merge(VersionJson child, VersionJson parent)
    {
        ArgumentNullException.ThrowIfNull(child);
        ArgumentNullException.ThrowIfNull(parent);

        // 确定客户端 jar 归属：
        // 1) 子版本显式 jar 字段优先；
        // 2) 子版本自带有效 client 下载时，用子版本自身（jar 留空，回退到 merged.Id）；
        // 3) 子版本没有 client（继承自父版本）时，复用父版本的 jar，避免重复下载一份。
        bool childHasClient = child.Downloads is not null
            && child.Downloads.TryGetValue("client", out DownloadInfo? childClient)
            && !string.IsNullOrEmpty(childClient.Url);
        string? resolvedJar;
        if (child.Jar is not null)
        {
            resolvedJar = child.Jar;
        }
        else if (childHasClient)
        {
            resolvedJar = null;
        }
        else
        {
            resolvedJar = parent.Jar ?? parent.Id;
        }

        var merged = new VersionJson
        {
            Id = child.Id ?? parent.Id,
            Type = child.Type ?? parent.Type,
            MainClass = child.MainClass ?? parent.MainClass,
            Assets = child.Assets ?? parent.Assets,
            AssetIndex = child.AssetIndex ?? parent.AssetIndex,
            Jar = resolvedJar,
            InheritsFrom = parent.InheritsFrom, // 继续向上（递归合并后父链已处理）
            MinecraftArguments = child.MinecraftArguments ?? parent.MinecraftArguments,
            JavaVersion = child.JavaVersion ?? parent.JavaVersion,
            Logging = child.Logging ?? parent.Logging,
            ComplianceLevel = child.ComplianceLevel ?? parent.ComplianceLevel,
            MinimumLauncherVersion = child.MinimumLauncherVersion ?? parent.MinimumLauncherVersion,
            Downloads = MergeDownloads(parent.Downloads, child.Downloads),
            Libraries = MergeLibraries(parent.Libraries, child.Libraries),
            Arguments = MergeArguments(parent.Arguments, child.Arguments)
        };

        return merged;
    }

    private static Dictionary<string, DownloadInfo>? MergeDownloads(
        Dictionary<string, DownloadInfo>? parent,
        Dictionary<string, DownloadInfo>? child)
    {
        if (parent is null && child is null)
        {
            return null;
        }
        var result = new Dictionary<string, DownloadInfo>();
        if (parent is not null)
        {
            foreach (var kv in parent)
            {
                result[kv.Key] = kv.Value;
            }
        }
        if (child is not null)
        {
            foreach (var kv in child)
            {
                result[kv.Key] = kv.Value;
            }
        }
        return result;
    }

    private static List<LibraryInfo>? MergeLibraries(
        List<LibraryInfo>? parent,
        List<LibraryInfo>? child)
    {
        var result = new List<LibraryInfo>();
        var indexByKey = new Dictionary<string, int>();

        void AddOrReplace(LibraryInfo library)
        {
            if (string.IsNullOrWhiteSpace(library.Name))
            {
                result.Add(library);
                return;
            }

            // 仅当完整坐标（含 classifier）一致时才视为同一构件；
            // natives 等 classifier 不同的构件必须共存。
            var coordinate = MavenName.Parse(library.Name!);
            string key = coordinate.BaseKey + ":" + (coordinate.Classifier ?? string.Empty);
            if (indexByKey.TryGetValue(key, out int existingIndex))
            {
                result[existingIndex] = library;
            }
            else
            {
                indexByKey[key] = result.Count;
                result.Add(library);
            }
        }

        if (parent is not null)
        {
            foreach (var library in parent)
            {
                AddOrReplace(library);
            }
        }
        if (child is not null)
        {
            foreach (var library in child)
            {
                AddOrReplace(library);
            }
        }
        return result;
    }

    private static ArgumentsInfo? MergeArguments(ArgumentsInfo? parent, ArgumentsInfo? child)
    {
        if (parent is null && child is null)
        {
            return null;
        }
        return new ArgumentsInfo
        {
            Game = Concat(parent?.Game, child?.Game),
            Jvm = Concat(parent?.Jvm, child?.Jvm)
        };
    }

    private static List<System.Text.Json.JsonElement>? Concat(
        List<System.Text.Json.JsonElement>? parent,
        List<System.Text.Json.JsonElement>? child)
    {
        if (parent is null && child is null)
        {
            return null;
        }
        var result = new List<System.Text.Json.JsonElement>();
        if (parent is not null)
        {
            result.AddRange(parent);
        }
        if (child is not null)
        {
            result.AddRange(child);
        }
        return result;
    }
}
