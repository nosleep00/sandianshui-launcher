namespace SandianshuiLauncher.Core.Services;

/// <summary>
/// 版本范围匹配：从日志的 Expected range（如 "[47.1.3,)" / "[47.1,)" / "(47.0,48.0)"）
/// 中解析约束，并在候选版本列表里选出满足范围的最高版本。
/// 范围缺失或解析失败时回退为候选列表中的最高版本。
/// </summary>
public static class VersionRangeMatcher
{
    /// <summary>选择满足 <paramref name="expectedRange"/> 的最高版本；无匹配时返回 null。</summary>
    public static string? SelectBest(IEnumerable<string> versions, string? expectedRange)
    {
        var parsed = versions
            .Select(v => (Raw: v, Version: ParseVersion(v)))
            .Where(x => x.Version is not null)
            .Select(x => (x.Raw, Version: x.Version!))
            .ToList();
        if (parsed.Count == 0)
        {
            return versions.OrderByDescending(v => v, StringComparer.OrdinalIgnoreCase).FirstOrDefault();
        }

        (Version? min, Version? max, bool maxInclusive) = ParseRange(expectedRange);

        List<(string Raw, Version Version)> matching = parsed;
        if (min is not null || max is not null)
        {
            matching = parsed.Where(x =>
                (min is null || x.Version!.CompareTo(min) >= 0)
                && (max is null || x.Version!.CompareTo(max) < 0 || (maxInclusive && x.Version!.Equals(max))))
                .ToList();
        }

        (string Raw, Version Version)? best = matching.Count > 0
            ? matching.OrderByDescending(x => x.Version).ThenByDescending(x => x.Raw).First()
            : null;
        return best?.Raw;
    }

    /// <summary>解析版本号（"47.4.12" / "0.15.11" / "47.1"），失败返回 null。</summary>
    public static Version? ParseVersion(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }
        string trimmed = raw.Trim().TrimStart('v', 'V');
        return System.Version.TryParse(trimmed, out Version? version) ? version : null;
    }

    /// <summary>解析范围字符串，返回（下界含、上界、上界是否含）。</summary>
    internal static (Version? Min, Version? Max, bool MaxInclusive) ParseRange(string? range)
    {
        if (string.IsNullOrWhiteSpace(range))
        {
            return (null, null, false);
        }
        string trimmed = range.Trim();
        if (trimmed.Length >= 2 && trimmed[0] is '[' or '(' && trimmed[^1] is ']' or ')')
        {
            bool minInclusive = trimmed[0] == '[';
            bool maxInclusive = trimmed[^1] == ']';
            string inner = trimmed[1..^1];
            string[] parts = inner.Split(',', StringSplitOptions.TrimEntries);
            Version? min = parts.Length > 0 && parts[0].Length > 0 ? ParseVersion(parts[0]) : null;
            Version? max = parts.Length > 1 && parts[1].Length > 0 ? ParseVersion(parts[1]) : null;
            // 开区间下界：加最小增量（版本第四段 +1）。
            if (min is not null && !minInclusive)
            {
                min = new Version(min.Major, min.Minor, Math.Max(0, min.Build), min.Revision + 1);
            }
            return (min, max, maxInclusive);
        }
        // 容错：无括号的单版本号按 ">= x" 处理。
        Version? single = ParseVersion(trimmed);
        return single is null ? (null, null, false) : (single, null, false);
    }
}
