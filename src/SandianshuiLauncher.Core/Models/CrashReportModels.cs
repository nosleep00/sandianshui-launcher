namespace SandianshuiLauncher.Core.Models;

/// <summary>崩溃诊断的严重级别。</summary>
public enum CrashSeverity
{
    /// <summary>提示性信息。</summary>
    Info = 0,

    /// <summary>警告。</summary>
    Warning = 1,

    /// <summary>错误。</summary>
    Error = 2,

    /// <summary>致命错误（直接导致无法进入游戏）。</summary>
    Fatal = 3
}

/// <summary>诊断附带的可自动执行修复动作。</summary>
public enum CrashFixAction
{
    /// <summary>暂不支持自动修复，仅给出建议。</summary>
    None = 0,

    /// <summary>自动调大该版本的最大内存。</summary>
    IncreaseMemory = 1,

    /// <summary>自动调小该版本的最大内存。</summary>
    DecreaseMemory = 2,

    /// <summary>自动选择匹配的 Java 并写入该版本配置。</summary>
    AutoSelectJava = 3,

    /// <summary>自动重新下载校验该版本文件。</summary>
    ReinstallVersion = 4,

    /// <summary>自动搜索并安装缺失的前置模组（FixTarget = modid）。</summary>
    InstallMissingMod = 5,

    /// <summary>缺失/过旧的依赖是模组加载器自身——自动安装最新版加载器（FixTarget = 加载器名）。</summary>
    UpgradeLoader = 6
}

/// <summary>
/// 一次崩溃/异常的结构化诊断结果。
/// 由 <see cref="Services.CrashAnalyzer"/> 基于规则库匹配日志或崩溃报告生成。
/// </summary>
public sealed class CrashDiagnosis
{
    /// <summary>命中的规则标识（便于测试与追踪）。</summary>
    public required string RuleId { get; init; }

    /// <summary>一句话原因标题（如“内存不足”）。</summary>
    public required string Title { get; init; }

    /// <summary>严重级别。</summary>
    public CrashSeverity Severity { get; init; }

    /// <summary>对问题的详细说明。</summary>
    public string Detail { get; init; } = string.Empty;

    /// <summary>可执行的修复建议列表。</summary>
    public IReadOnlyList<string> Suggestions { get; init; } = [];

    /// <summary>相关模组标识（若能从日志/堆栈中提取），否则为空字符串。</summary>
    public string RelatedMod { get; set; } = string.Empty;

    /// <summary>命中规则的原始日志行（作为证据展示）。</summary>
    public string MatchedLine { get; init; } = string.Empty;

    /// <summary>可自动执行的修复动作（None 表示仅建议）。</summary>
    public CrashFixAction FixAction { get; set; } = CrashFixAction.None;

    /// <summary>修复动作的参数（如缺失模组的 modid）。</summary>
    public string FixTarget { get; set; } = string.Empty;

    /// <summary>日志中要求的版本范围（如 "[47.1.3,)"），用于精确挑选加载器版本。</summary>
    public string FixVersionRange { get; set; } = string.Empty;

    /// <summary>把建议合并为带序号的多行文本，方便 UI 直接显示。</summary>
    public string SuggestionsText =>
        Suggestions.Count == 0
            ? string.Empty
            : string.Join(Environment.NewLine, Suggestions.Select((s, i) => $"{i + 1}. {s}"));
}
