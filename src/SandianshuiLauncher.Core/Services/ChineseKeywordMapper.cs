namespace SandianshuiLauncher.Core.Services;

/// <summary>
/// 中文搜索关键词适配：把常见中文模组用语映射为英文候选词，
/// 与 Modrinth / CurseForge 这类以英文为主的平台兼容。
/// 映射是尽力而为的轻量方案：候选词按序尝试，原词恒作兜底。
/// </summary>
public static class ChineseKeywordMapper
{
    /// <summary>常见中文词 → 英文候选（按优先级排列）。</summary>
    private static readonly Dictionary<string, string[]> Map = new(StringComparer.OrdinalIgnoreCase)
    {
        ["小地图"] = ["minimap"],
        ["地图"] = ["minimap", "map"],
        ["光影"] = ["shader", "shaders"],
        ["优化"] = ["optimization", "performance"],
        ["性能"] = ["performance", "optimization"],
        ["背包"] = ["inventory", "backpack"],
        ["附魔"] = ["enchanting", "enchant"],
        ["生物"] = ["mob", "creature"],
        ["怪物"] = ["mob", "monster"],
        ["村民"] = ["villager"],
        ["村庄"] = ["village"],
        ["遗迹"] = ["dungeon", "structure"],
        ["结构"] = ["structure", "worldgen"],
        ["矿物"] = ["ore"],
        ["矿石"] = ["ore"],
        ["存储"] = ["storage"],
        ["自动"] = ["automation", "auto"],
        ["界面"] = ["ui", "hud"],
        ["配方"] = ["recipe"],
        ["传送"] = ["teleport", "waypoints"],
        ["移动"] = ["movement", "mobility"]
    };

    /// <summary>判断文本是否包含中文字符。</summary>
    public static bool ContainsChinese(string? text)
        => !string.IsNullOrEmpty(text) && text.Any(ch => ch is >= '\u4e00' and <= '\u9fff');

    /// <summary>
    /// 展开查询词为按优先级排序的候选列表：
    /// 对每个命中的中文词（按词长降序匹配、命中区间互不重叠）生成
    /// 「仅替换该词为英文」的候选在前，原词恒在最后兜底。
    /// 不含中文时原样返回单元素列表。
    /// </summary>
    public static List<string> Expand(string? query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return [];
        }
        string trimmed = query.Trim();
        if (!ContainsChinese(trimmed))
        {
            return [trimmed];
        }

        var candidates = new List<string>();
        var consumed = new List<(int Start, int Length)>();

        // 词长降序：优先匹配更长、更具体的词（如「小地图」优先于「地图」）。
        foreach (string zh in Map.Keys.OrderByDescending(k => k.Length))
        {
            int index = 0;
            while ((index = trimmed.IndexOf(zh, index, StringComparison.OrdinalIgnoreCase)) >= 0)
            {
                bool overlapped = consumed.Any(r => index < r.Start + r.Length && r.Start < index + zh.Length);
                if (!overlapped)
                {
                    consumed.Add((index, zh.Length));
                    foreach (string en in Map[zh])
                    {
                        string replaced = string.Concat(
                            trimmed[..index], en, trimmed[(index + zh.Length)..]);
                        if (!candidates.Contains(replaced, StringComparer.OrdinalIgnoreCase))
                        {
                            candidates.Add(replaced);
                        }
                    }
                }
                index += zh.Length;
            }
        }

        if (!candidates.Contains(trimmed, StringComparer.OrdinalIgnoreCase))
        {
            candidates.Add(trimmed);
        }
        return candidates;
    }
}
