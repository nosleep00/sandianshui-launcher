namespace SandianshuiLauncher.Core.Models;

/// <summary>
/// Minecraft 日志级别（对应 Log4j2 的级别）。
/// 数值越大越严重，便于在 UI 中按严重程度筛选与着色。
/// </summary>
public enum GameLogLevel
{
    /// <summary>无法解析出标准格式的行（如堆栈延续、空行）。</summary>
    Unknown = 0,

    /// <summary>最详细的跟踪信息。</summary>
    Trace = 1,

    /// <summary>调试信息。</summary>
    Debug = 2,

    /// <summary>常规运行信息。</summary>
    Info = 3,

    /// <summary>警告：通常不影响运行，但值得留意。</summary>
    Warn = 4,

    /// <summary>错误：出现异常，但进程可能仍在运行。</summary>
    Error = 5,

    /// <summary>致命错误：往往直接导致游戏崩溃退出。</summary>
    Fatal = 6
}

/// <summary>
/// 解析后的一行游戏日志。
/// 标准格式形如：[时间] [线程/级别] [logger/]: 消息。
/// </summary>
public sealed class GameLogEntry
{
    /// <summary>原始整行文本。</summary>
    public required string Raw { get; init; }

    /// <summary>时间戳原文（方括号内），无法解析时为空字符串。</summary>
    public string Timestamp { get; init; } = string.Empty;

    /// <summary>线程名，无法解析时为空字符串。</summary>
    public string Thread { get; init; } = string.Empty;

    /// <summary>日志级别。</summary>
    public GameLogLevel Level { get; init; } = GameLogLevel.Unknown;

    /// <summary>Logger 名称（含可能的标记段），无法解析时为空字符串。</summary>
    public string Logger { get; init; } = string.Empty;

    /// <summary>日志正文（去掉时间、线程、级别、logger 前缀后的部分）。</summary>
    public string Message { get; init; } = string.Empty;

    /// <summary>是否为错误或更严重级别（用于快速统计与崩溃判定）。</summary>
    public bool IsError => Level is GameLogLevel.Error or GameLogLevel.Fatal;
}
