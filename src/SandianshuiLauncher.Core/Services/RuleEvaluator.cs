using SandianshuiLauncher.Core.Models;

namespace SandianshuiLauncher.Core.Services;

/// <summary>版本 JSON 规则评估（OS、features）。</summary>
public static class RuleEvaluator
{
    /// <summary>
    /// 一组规则是否允许当前环境。
    /// 规则按顺序处理：默认拒绝，匹配的 allow 放行、匹配的 disallow 否决。
    /// </summary>
    public static bool RulesAllow(IReadOnlyList<Rule>? rules, IReadOnlyDictionary<string, bool>? features = null)
    {
        if (rules is null || rules.Count == 0)
        {
            return true;
        }

        bool allowed = false;
        foreach (var rule in rules)
        {
            if (Matches(rule, features))
            {
                allowed = string.Equals(rule.Action, "allow", StringComparison.OrdinalIgnoreCase);
            }
        }
        return allowed;
    }

    private static bool Matches(Rule rule, IReadOnlyDictionary<string, bool>? features)
    {
        if (rule.Os is not null)
        {
            if (!string.IsNullOrEmpty(rule.Os.Name) &&
                !string.Equals(rule.Os.Name, CurrentOsName(), StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (!string.IsNullOrEmpty(rule.Os.Arch))
            {
                bool is64Bit = Environment.Is64BitOperatingSystem;
                bool archMatches = rule.Os.Arch switch
                {
                    "x64" => is64Bit,
                    "x86" => !is64Bit,
                    _ => true
                };
                if (!archMatches)
                {
                    return false;
                }
            }
        }

        if (rule.Features is not null)
        {
            foreach (var (key, expected) in rule.Features)
            {
                bool actual = features is not null && features.TryGetValue(key, out bool value) && value;
                if (actual != expected)
                {
                    return false;
                }
            }
        }

        return true;
    }

    public static string CurrentOsName() =>
        OperatingSystem.IsWindows() ? "windows" :
        OperatingSystem.IsMacOS() ? "osx" : "linux";
}
