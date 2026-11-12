using System.IO.Compression;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace SandianshuiLauncher.Core.Models;

/// <summary>一个模组的归因信息：模组标识 + 可归因的包名前缀。</summary>
public sealed record ModIdentity(string ModId, IReadOnlyList<string> Packages);

/// <summary>
/// 堆栈归因：从崩溃日志的堆栈帧中提取类名，按最长包名前缀匹配模组索引，
/// 反推出导致崩溃的模组（PCL 三级专家系统的“堆栈分析”级）。
/// </summary>
public static partial class StackAttributor
{
    /// <summary>从堆栈文本提取候选类名（"at 包.类.方法(...)" 行）。</summary>
    public static List<string> ExtractClasses(string text)
    {
        var result = new List<string>();
        if (string.IsNullOrEmpty(text))
        {
            return result;
        }
        foreach (Match match in AtLineRegex().Matches(text))
        {
            string cls = match.Groups[1].Value;
            if (!result.Contains(cls, StringComparer.Ordinal))
            {
                result.Add(cls);
            }
        }
        return result;
    }

    /// <summary>
    /// 按最长包名前缀把类名归因到模组；无命中返回 null。
    /// </summary>
    public static string? Attribute(
        IEnumerable<string> classes,
        IReadOnlyDictionary<string, string> packageToMod)
    {
        foreach (string cls in classes)
        {
            string? bestPackage = null;
            foreach ((string package, _) in packageToMod)
            {
                if (cls.StartsWith(package + ".", StringComparison.OrdinalIgnoreCase)
                    && (bestPackage is null || package.Length > bestPackage.Length))
                {
                    bestPackage = package;
                }
            }
            if (bestPackage is not null)
            {
                return packageToMod[bestPackage];
            }
        }
        return null;
    }

    [GeneratedRegex(@"(?m)^\s*at\s+([\w$]+(?:\.[\w$]+)*)\.[\w$<>]+\(", RegexOptions.CultureInvariant)]
    private static partial Regex AtLineRegex();
}

/// <summary>
/// 模组元数据读取：从已安装的 mod jar 中提取模组标识与可归因包名。
/// 支持 Fabric/Quilt（fabric.mod.json）与 Forge/NeoForge（META-INF/mods.toml）。
/// </summary>
public static class ModMetadataReader
{
    /// <summary>Forge 归因时排除的第三方/共享顶层包。</summary>
    private static readonly HashSet<string> ForgeSharedPackages = new(StringComparer.OrdinalIgnoreCase)
    {
        "META-INF", "net", "com", "org", "io", "javax", "joptsimple", "it", "club", "mezz"
    };

    /// <summary>读取 jar 内的模组元数据；无法识别时返回 null。</summary>
    public static ModIdentity? Read(string jarPath)
    {
        try
        {
            using ZipArchive zip = ZipFile.OpenRead(jarPath);
            ZipArchiveEntry? fabric = zip.GetEntry("fabric.mod.json");
            if (fabric is not null)
            {
                return ReadFabric(fabric);
            }
            ZipArchiveEntry? toml = zip.GetEntry("META-INF/mods.toml");
            if (toml is not null)
            {
                return ReadForge(toml, zip);
            }
        }
        catch
        {
            // 损坏的 jar 无法归因，返回 null。
        }
        return null;
    }

    private static ModIdentity? ReadFabric(ZipArchiveEntry entry)
    {
        using var stream = entry.Open();
        using JsonDocument doc = JsonDocument.Parse(stream);
        JsonElement root = doc.RootElement;

        string? modId = null;
        if (root.TryGetProperty("id", out JsonElement idElement) && idElement.ValueKind == JsonValueKind.String)
        {
            modId = idElement.GetString();
        }
        if (string.IsNullOrWhiteSpace(modId))
        {
            return null;
        }

        var packages = new List<string> { modId! };
        // entrypoints 的值是类名字符串 / 字符串数组 / 对象，全部收集并映射包名。
        if (root.TryGetProperty("entrypoints", out JsonElement entrypoints) && entrypoints.ValueKind == JsonValueKind.Object)
        {
            foreach (JsonProperty section in entrypoints.EnumerateObject())
            {
                CollectEntrypointPackages(section.Value, packages);
            }
        }
        return new ModIdentity(modId!, packages.Distinct(StringComparer.OrdinalIgnoreCase).ToList());
    }

    private static void CollectEntrypointPackages(JsonElement value, List<string> packages)
    {
        switch (value.ValueKind)
        {
            case JsonValueKind.String:
                AddPackageOf(value.GetString(), packages);
                break;
            case JsonValueKind.Array:
                foreach (JsonElement item in value.EnumerateArray())
                {
                    CollectEntrypointPackages(item, packages);
                }
                break;
            case JsonValueKind.Object:
                foreach (JsonProperty prop in value.EnumerateObject())
                {
                    CollectEntrypointPackages(prop.Value, packages);
                }
                break;
        }
    }

    private static void AddPackageOf(string? className, List<string> packages)
    {
        if (string.IsNullOrWhiteSpace(className))
        {
            return;
        }
        int lastDot = className.LastIndexOf('.');
        string package = lastDot > 0 ? className[..lastDot] : className;
        if (!packages.Contains(package, StringComparer.OrdinalIgnoreCase))
        {
            packages.Add(package);
        }
    }

    private static ModIdentity? ReadForge(ZipArchiveEntry tomlEntry, ZipArchive zip)
    {
        using var reader = new StreamReader(tomlEntry.Open());
        string toml = reader.ReadToEnd();

        string? modId = null;
        foreach (string line in toml.Split('\n'))
        {
            string trimmed = line.Trim();
            if (trimmed.StartsWith("modId", StringComparison.OrdinalIgnoreCase))
            {
                int eq = trimmed.IndexOf('=');
                if (eq >= 0)
                {
                    modId = trimmed[(eq + 1)..].Trim().Trim('"', '\'');
                    break;
                }
            }
        }
        if (string.IsNullOrWhiteSpace(modId))
        {
            return null;
        }

        // 统计 class 文件顶层包出现次数，剔除共享第三方包后取出现最多的包作为归因前缀。
        var packageCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (ZipArchiveEntry entry in zip.Entries)
        {
            string name = entry.FullName;
            if (!name.EndsWith(".class", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            int slash = name.IndexOf('/');
            if (slash <= 0)
            {
                continue;
            }
            string topPackage = name[..slash];
            if (ForgeSharedPackages.Contains(topPackage))
            {
                continue;
            }
            packageCounts.TryGetValue(topPackage, out int count);
            packageCounts[topPackage] = count + 1;
        }

        string? attributed = packageCounts
            .OrderByDescending(kv => kv.Value)
            .Select(kv => kv.Key)
            .FirstOrDefault();
        var packages = new List<string> { modId };
        if (attributed is not null)
        {
            packages.Add(attributed);
        }
        return new ModIdentity(modId, packages);
    }
}
