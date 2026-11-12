using System.IO;
using System.Text.RegularExpressions;
using SandianshuiLauncher.Core.Models;

namespace SandianshuiLauncher.Core.Services;

/// <summary>
/// 崩溃分析专家系统：基于内置规则库，对游戏日志或崩溃报告文本做关键字匹配，
/// 输出人类可读的原因与可执行修复建议。纯静态、不依赖网络，便于单元测试。
/// </summary>
public static class CrashAnalyzer
{
    /// <summary>
    /// 规则库，顺序即优先级：越具体、越确定的规则放在越前面。
    /// 分析时按此顺序返回，命中靠前的规则即作为最可能的主因。
    /// </summary>
    private static readonly IReadOnlyList<CrashRule> Rules =
    [
        new CrashRule(
            "out_of_memory_heap",
            "内存不足（堆溢出）",
            CrashSeverity.Fatal,
            "游戏运行过程中可用内存耗尽，Java 抛出了 OutOfMemoryError，通常是分配的内存偏小或同时加载了过多模组。",
            [
                "在“设置”中调大“最大内存”（例如从 2048 MB 提高到 4096 MB）。",
                "暂时移除部分不必要的模组后再试。",
                "关闭其他占用内存的软件（浏览器、大型程序等）。"
            ],
            ["outofmemoryerror: java heap space", "gc overhead limit exceeded", "java.lang.outofmemoryerror"]),

        new CrashRule(
            "heap_reserve_failed",
            "内存分配失败（设置过大）",
            CrashSeverity.Fatal,
            "Java 虚拟机启动时无法申请到指定大小的内存，通常是最大内存设置超过了系统当前可用内存。",
            [
                "在“设置”中调小“最大内存”，建议不超过物理内存的一半。",
                "关闭其他占用内存的软件后重新启动。",
                "确认 64 位 Java 已正确安装（32 位 Java 最多只能分配约 1.5 GB）。"
            ],
            ["could not reserve enough space", "could not create the java virtual machine",
             "error occurred during initialization of vm", "insufficient memory"]),

        new CrashRule(
            "java_version_mismatch",
            "Java 版本不兼容",
            CrashSeverity.Fatal,
            "游戏或模组使用了更高版本 Java 编译的类，当前 Java 运行环境版本过低，无法加载。",
            [
                "在“设置”中让启动器自动选择 Java，或手动指定更高版本的 java.exe。",
                "新版 Minecraft（1.17+）需要 Java 17，1.20.5+ 建议 Java 21。",
                "升级后重新启动游戏。"
            ],
            ["unsupportedclassversionerror", "has been compiled by a more recent version",
             "class file version", "requires java runtime", "unsupportedclassformaterror"]),

        new CrashRule(
            "jvm_native_crash",
            "Java 虚拟机崩溃",
            CrashSeverity.Fatal,
            "JVM 发生了底层致命错误（如访问冲突），常见诱因是显卡驱动、原生库或某些模组的本地代码。",
            [
                "更新显卡驱动到最新稳定版。",
                "移除最近安装的可疑模组后重试。",
                "确认游戏文件完整，必要时在版本管理中重新安装该版本。"
            ],
            ["exception_access_violation", "a fatal error has been detected",
             "hs_err_pid", "sigsegv"]),

        new CrashRule(
            "native_library",
            "原生库加载失败",
            CrashSeverity.Fatal,
            "游戏所需的本地动态库（如 LWJGL 的 lwjgl.dll）未能找到或加载，通常是原生库文件缺失或解压不完整。",
            [
                "在“版本管理”中重新安装该版本，以补齐缺失的原生库。",
                "检查杀毒软件是否误删了游戏目录下的 dll 文件，必要时恢复并加白名单。",
                "确认使用的是 64 位 Java 与 64 位系统匹配。"
            ],
            ["unsatisfiedlinkerror", "failed to locate library", "no lwjgl in java.library.path",
             "could not extract native libraries", "loadnatives"]),

        new CrashRule(
            "graphics_driver",
            "显卡驱动 / OpenGL 问题",
            CrashSeverity.Error,
            "无法初始化图形环境，通常是显卡驱动过旧、不支持所需 OpenGL 版本，或系统错误地使用了不兼容的显示适配器。",
            [
                "更新显卡（独立显卡与核显）驱动到最新版本。",
                "在显卡控制面板中为 javaw.exe 指定使用独立显卡。",
                "若使用虚拟机或远程桌面，请改用本机物理显示环境运行。"
            ],
            ["pixel format not accelerated", "does not appear to support opengl",
             "glfw error 65543", "glfw error 65542", "opengl error", "failed to create window"]),

        new CrashRule(
            "mod_missing_dependency",
            "模组缺少前置依赖",
            CrashSeverity.Error,
            "某个模组要求的其他模组（前置/依赖）没有安装或版本不满足要求，导致加载中止。",
            [
                "根据日志提示安装缺少的前置模组，并确认其版本在要求范围内。",
                "在模组页搜索对应模组标识后安装，注意选择与当前游戏版本、加载器匹配的文件。",
                "若不需要该模组，可先移除它再启动。"
            ],
            ["missing or unsupported mandatory dependency", "modresolutionexception",
             "missing dependencies", "requires", "missing or unsupported",
             "could not find required mod", "missing required mod"],
            [
                @"Mod ID:\s*'([^']+)'",
                @"Mod ID:\s*([a-z0-9_\-]+)",
                @"could not find required mod:?\s*([a-z0-9_\-]+)",
                @"missing required mod:?\s*([a-z0-9_\-]+)",
                @"requires\s+(?:\{?\s*mod\s+)?'?([a-z0-9_\-]+)"
            ],
            @"Expected range:\s*'?(\[[^'\r\n]*\)|\([^'\r\n]*\)|[0-9][^',\r\n]*)"),

        new CrashRule(
            "mod_duplicate",
            "存在重复模组",
            CrashSeverity.Error,
            "检测到同一个模组被安装了多份（不同文件名的相同 jar），加载器拒绝继续。",
            [
                "打开 mods 目录，删除重复的模组 jar，只保留一份。",
                "注意检查是否同时存在新旧版本的同名模组。",
                "处理后重新启动游戏。"
            ],
            ["duplicatemodsexception", "found duplicate mod", "duplicate mod", "duplicate library"]),

        new CrashRule(
            "mod_mixin_failure",
            "Mixin 注入失败",
            CrashSeverity.Error,
            "模组通过 Mixin 修改游戏代码时失败，常见于模组与游戏/加载器版本不匹配，或多个模组的修改相互冲突。",
            [
                "确认模组版本与当前游戏版本、加载器版本匹配。",
                "更新或移除最近安装的模组，排查冲突来源。",
                "更新 Fabric/Forge 加载器到兼容版本。"
            ],
            ["mixin apply failed", "mixin apply for", "injectionerror",
             "critical injection problem", "mixin transform failed"]),

        new CrashRule(
            "mod_age_of_mythology_catalog",
            "神话纪元（Age of Mythology）维度目录加载失败",
            CrashSeverity.Error,
            "神话纪元模组的并行世界目录未能在服务器启动前完成重载，通常与其缓存数据损坏或版本不匹配有关。",
            [
                "删除神话纪元模组的维度缓存数据后重试（版本目录内 ageofmythology 相关数据）。",
                "确认神话纪元模组版本与整合包要求匹配，或更新到修复版。",
                "若反复出现，请向整合包作者反馈该模组的兼容问题。"
            ],
            ["parallel-world catalog reload candidate was not prepared",
             "parallellworldcataloglifecycle"],
            [@"(ageofmythology-[0-9]+(?:\.[0-9]+)*)"]),

        new CrashRule(
            "mod_load_failure",
            "模组加载失败",
            CrashSeverity.Error,
            "模组在加载阶段抛出异常，可能是版本不兼容、配置错误或模组本身存在问题。",
            [
                "查看日志中该模组的具体报错，确认其支持当前游戏版本。",
                "更新或移除最近安装的模组。",
                "若问题持续，可逐个禁用模组以定位问题来源。"
            ],
            ["modloadingexception", "failed to create mod", "error during pre-init",
             "caught exception from mod", "an exception was thrown during mod loading"]),

        new CrashRule(
            "port_in_use",
            "端口被占用",
            CrashSeverity.Warning,
            "游戏需要的网络端口已被其他进程占用，集成联机或相关网络功能无法绑定。",
            [
                "关闭其他可能占用该端口的程序（其他游戏、服务器、下载工具等）。",
                "重启电脑后再试。",
                "检查是否已经启动了一个游戏实例。"
            ],
            ["address already in use", "bindexception", "port already in use"]),

        new CrashRule(
            "file_locked",
            "文件被占用或权限不足",
            CrashSeverity.Warning,
            "游戏无法访问所需文件，可能正被其他程序占用，或当前账户没有写入权限。",
            [
                "关闭可能占用游戏目录的程序（另一个游戏实例、编辑器等）。",
                "确认对游戏目录拥有读写权限，必要时移动到用户目录下运行。",
                "不要把游戏放在受系统保护的目录中。"
            ],
            ["access is denied", "being used by another process",
             "the process cannot access the file", "另一个程序正在使用"])
    ];

    /// <summary>
    /// 分析日志/崩溃报告，返回最可能的主诊断。
    /// 提供「包名 → 模组」索引时，一级规则未归因的诊断会用堆栈做二级归因（最长前缀匹配）。
    /// 没有任何规则命中时，返回一个通用诊断（保证调用方总能拿到结果）。
    /// </summary>
    public static CrashDiagnosis Analyze(
        string? text, IReadOnlyDictionary<string, string>? packageToMod = null)
    {
        var all = AnalyzeAll(text);
        if (all.Count > 0)
        {
            CrashDiagnosis primary = all[0];
            if (string.IsNullOrEmpty(primary.RelatedMod) && packageToMod is not null)
            {
                string? mod = StackAttributor.Attribute(
                    StackAttributor.ExtractClasses(text ?? string.Empty), packageToMod);
                if (!string.IsNullOrEmpty(mod))
                {
                    primary.RelatedMod = mod;
                }
            }
            AttachFixAction(primary);
            return primary;
        }
        return CreateFallback(text);
    }

    /// <summary>按规则产出可自动执行的修复动作（含参数）。</summary>
    internal static void AttachFixAction(CrashDiagnosis diagnosis)
    {
        switch (diagnosis.RuleId)
        {
            case "out_of_memory_heap":
                diagnosis.FixAction = CrashFixAction.IncreaseMemory;
                break;
            case "heap_reserve_failed":
                diagnosis.FixAction = CrashFixAction.DecreaseMemory;
                break;
            case "java_version_mismatch":
                diagnosis.FixAction = CrashFixAction.AutoSelectJava;
                break;
            case "native_library":
                diagnosis.FixAction = CrashFixAction.ReinstallVersion;
                break;
            case "mod_missing_dependency" when !string.IsNullOrEmpty(diagnosis.RelatedMod):
                // 缺失的是加载器自身（如 Mod ID: 'forge'）→ 升级加载器，而不是去装一个叫 forge 的模组。
                if (ModLoaderDetector.IsLoaderId(diagnosis.RelatedMod))
                {
                    diagnosis.FixAction = CrashFixAction.UpgradeLoader;
                    diagnosis.FixTarget = diagnosis.RelatedMod.ToLowerInvariant();
                }
                else
                {
                    diagnosis.FixAction = CrashFixAction.InstallMissingMod;
                    diagnosis.FixTarget = diagnosis.RelatedMod;
                }
                break;
            default:
                diagnosis.FixAction = CrashFixAction.None;
                break;
        }
    }

    /// <summary>分析并返回全部命中的诊断（按规则优先级排序、已去重）。</summary>
    public static List<CrashDiagnosis> AnalyzeAll(string? text)
    {
        text ??= string.Empty;
        string[] lines = text.Split('\n');

        var result = new List<CrashDiagnosis>();
        foreach (CrashRule rule in Rules)
        {
            string? matchedLine = null;
            foreach (string raw in lines)
            {
                string line = raw.EndsWith('\r') ? raw[..^1] : raw;
                if (rule.Matches(line))
                {
                    matchedLine = line;
                    break;
                }
            }

            if (matchedLine is not null)
            {
                result.Add(rule.ToDiagnosis(matchedLine, text));
            }
        }
        return result;
    }

    /// <summary>
    /// 读取游戏目录下的 latest.log（使用共享读取，避免游戏运行时文件被独占锁定）。
    /// 文件不存在或为空时返回空字符串。
    /// </summary>
    public static string ReadLatestLog(string gameDirectory)
        => ReadLatestLogFile(Path.Combine(gameDirectory, "logs", "latest.log"));

    /// <summary>
    /// 读取本次运行最相关的日志：版本隔离开启时优先实例目录
    /// （versions/{id}/logs/latest.log），并按最后写入时间与根目录日志比较取新者，
    /// 避免读到根目录遗留的旧日志导致误诊。
    /// </summary>
    public static string ReadLatestLogFor(string gameDirectory, string? versionId, bool versionIsolation)
    {
        string rootLog = Path.Combine(gameDirectory, "logs", "latest.log");
        string? instanceLog = versionIsolation && !string.IsNullOrWhiteSpace(versionId)
            ? Path.Combine(gameDirectory, "versions", versionId, "logs", "latest.log")
            : null;

        DateTime rootTime = File.Exists(rootLog) ? File.GetLastWriteTime(rootLog) : DateTime.MinValue;
        DateTime instanceTime = instanceLog is not null && File.Exists(instanceLog)
            ? File.GetLastWriteTime(instanceLog)
            : DateTime.MinValue;

        if (instanceTime >= rootTime && instanceTime > DateTime.MinValue)
        {
            return ReadLatestLogFile(instanceLog!);
        }
        return ReadLatestLogFile(rootLog);
    }

    private static string ReadLatestLogFile(string path)
    {
        if (!File.Exists(path))
        {
            return string.Empty;
        }

        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    /// <summary>读取并分析游戏目录下最新一次运行日志。</summary>
    public static CrashDiagnosis AnalyzeLatestLog(string gameDirectory)
        => Analyze(ReadLatestLog(gameDirectory));

    /// <summary>
    /// 「无日志即退出」诊断：进程瞬间退出（通常 &lt; 1 秒）且没有游戏日志，
    /// 说明 JVM 在加载游戏前就被拒绝启动（Java 路径/参数/内存设置错误），
    /// 而不是游戏内部崩溃。
    /// </summary>
    public static CrashDiagnosis CreateNoLogDiagnosis()
        => new()
        {
            RuleId = "no_game_log",
            Title = "游戏未产生日志即退出",
            Severity = CrashSeverity.Error,
            Detail = "游戏进程启动后立刻退出，且没有生成任何日志——说明 Java 虚拟机在加载游戏前就失败了，" +
                     "常见原因是 Java 路径无效、JVM 参数不被支持，或内存设置超出可用范围。",
            Suggestions =
            [
                "检查“运行日志”中 Java 报错行（如 Unrecognized VM option / Could not create the Java Virtual Machine）。",
                "在版本设置中确认 Java 路径指向有效的 java.exe，并确认其版本满足游戏要求。",
                "把最大内存调小一些（如 4096MB）再试，避免超出系统可用内存。"
            ],
            MatchedLine = string.Empty
        };

    private static CrashDiagnosis CreateFallback(string? text)
    {
        bool hasContent = !string.IsNullOrWhiteSpace(text);
        return new CrashDiagnosis
        {
            RuleId = "unknown",
            Title = "未能识别的异常",
            Severity = CrashSeverity.Warning,
            Detail = hasContent
                ? "现有规则未能匹配到明确的崩溃特征，需要结合完整日志进一步判断。"
                : "没有读取到可用的日志内容，可能游戏尚未生成日志，或日志目录位置不正确。",
            Suggestions =
            [
                "查看“运行日志”中的 ERROR / FATAL 行与堆栈信息。",
                "若存在 crash-reports 目录，打开其中最新的崩溃报告。",
                "确认游戏目录设置正确后重新启动。"
            ],
            MatchedLine = string.Empty
        };
    }

    // ─────────────────────────────────────────────────────────
    // 规则定义（内部）
    // ─────────────────────────────────────────────────────────

    private sealed class CrashRule
    {
        private readonly string[] _keywords;
        private readonly Regex[] _modPatterns;
        private readonly Regex? _versionRangePattern;

        public string Id { get; }
        public string Title { get; }
        public CrashSeverity Severity { get; }
        public string Detail { get; }
        public IReadOnlyList<string> Suggestions { get; }

        public CrashRule(
            string id,
            string title,
            CrashSeverity severity,
            string detail,
            IReadOnlyList<string> suggestions,
            string[] keywords,
            string[]? modPatterns = null,
            string? versionRangePattern = null)
        {
            Id = id;
            Title = title;
            Severity = severity;
            Detail = detail;
            Suggestions = suggestions;
            _keywords = keywords;
            _modPatterns = (modPatterns ?? [])
                .Select(p => new Regex(p, RegexOptions.IgnoreCase | RegexOptions.Compiled))
                .ToArray();
            _versionRangePattern = versionRangePattern is null
                ? null
                : new Regex(versionRangePattern, RegexOptions.IgnoreCase | RegexOptions.Compiled);
        }

        /// <summary>关键字任一命中即视为匹配（不区分大小写、不依赖大小写规则）。</summary>
        public bool Matches(string line)
        {
            if (string.IsNullOrEmpty(line))
            {
                return false;
            }
            foreach (string keyword in _keywords)
            {
                if (line.Contains(keyword, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            return false;
        }

        public CrashDiagnosis ToDiagnosis(string matchedLine, string fullText)
        {
            return new CrashDiagnosis
            {
                RuleId = Id,
                Title = Title,
                Severity = Severity,
                Detail = Detail,
                Suggestions = Suggestions,
                RelatedMod = ExtractMod(fullText),
                MatchedLine = matchedLine.Trim(),
                FixVersionRange = ExtractVersionRange(fullText)
            };
        }

        private string ExtractVersionRange(string fullText)
        {
            if (_versionRangePattern is null)
            {
                return string.Empty;
            }
            Match match = _versionRangePattern.Match(fullText);
            return match.Success && match.Groups.Count > 1 ? match.Groups[1].Value.Trim() : string.Empty;
        }

        private string ExtractMod(string fullText)
        {
            foreach (Regex pattern in _modPatterns)
            {
                Match match = pattern.Match(fullText);
                if (match.Success && match.Groups.Count > 1 && match.Groups[1].Value.Length > 0)
                {
                    return match.Groups[1].Value;
                }
            }
            return string.Empty;
        }
    }
}
