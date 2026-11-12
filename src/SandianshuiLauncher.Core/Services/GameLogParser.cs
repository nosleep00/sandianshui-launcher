using System.Text.RegularExpressions;
using SandianshuiLauncher.Core.Models;

namespace SandianshuiLauncher.Core.Services;

/// <summary>
/// Minecraft 日志解析器：把 Log4j2 风格的日志行拆成结构化的 <see cref="GameLogEntry"/>。
/// 纯静态、无副作用，便于单元测试与在 UI 中按级别着色。
/// </summary>
public static partial class GameLogParser
{
    /// <summary>
    /// 标准行格式：[时间] [线程/级别] [logger/]: 消息。
    /// logger 段允许缺失；时间戳内的内容不做假设（不同语言/版本月份写法不同）。
    /// </summary>
    [GeneratedRegex(
        @"^\[(?<time>[^\]]+)\]\s+\[(?<thread>[^/\]]+)/(?<level>[A-Z]+)\]\s*(?:\[(?<logger>[^\]]+)\]:\s*)?(?<msg>.*)$",
        RegexOptions.CultureInvariant)]
    private static partial Regex StandardLineRegex();

    /// <summary>解析单行；不符合标准格式（堆栈、空行等）返回 <see cref="GameLogLevel.Unknown"/>。</summary>
    public static GameLogEntry ParseLine(string line)
    {
        line ??= string.Empty;

        Match match = StandardLineRegex().Match(line);
        if (!match.Success)
        {
            return new GameLogEntry
            {
                Raw = line,
                Message = line
            };
        }

        return new GameLogEntry
        {
            Raw = line,
            Timestamp = match.Groups["time"].Value.Trim(),
            Thread = match.Groups["thread"].Value.Trim(),
            Level = ParseLevel(match.Groups["level"].Value),
            Logger = match.Groups["logger"].Value.Trim(),
            Message = match.Groups["msg"].Value
        };
    }

    /// <summary>逐行解析整段日志文本。</summary>
    public static List<GameLogEntry> Parse(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return [];
        }

        string[] parts = text.Split('\n');
        // 文本以换行结尾时 Split 会产生一个末尾空元素，丢弃它（保留中间空行）。
        if (parts.Length > 0 && parts[^1] is "" or "\r")
        {
            parts = parts[..^1];
        }
        return ParseLines(parts);
    }

    /// <summary>解析多行（自动去掉行尾的 \r）。</summary>
    public static List<GameLogEntry> ParseLines(IEnumerable<string> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);

        var result = new List<GameLogEntry>();
        foreach (string raw in lines)
        {
            string line = raw.EndsWith('\r') ? raw[..^1] : raw;
            result.Add(ParseLine(line));
        }
        return result;
    }

    /// <summary>把日志级别字符串解析为枚举，无法识别时返回 <see cref="GameLogLevel.Unknown"/>。</summary>
    public static GameLogLevel ParseLevel(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return GameLogLevel.Unknown;
        }
        return Enum.TryParse(value.Trim(), ignoreCase: true, out GameLogLevel level)
            ? level
            : GameLogLevel.Unknown;
    }
}
