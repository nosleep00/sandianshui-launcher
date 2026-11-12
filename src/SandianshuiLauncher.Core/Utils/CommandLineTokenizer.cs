using System.Text;

namespace SandianshuiLauncher.Core.Utils;

/// <summary>
/// 命令行分词：按空白切分，支持双引号分组与反斜杠转义（对齐 Windows 命令行习惯）。
/// 用于解析用户填写的额外 JVM 参数与老版本 minecraftArguments 字符串，
/// 避免带引号参数（如 -Dfoo="a b"）被空白硬拆坏。
/// </summary>
public static class CommandLineTokenizer
{
    public static List<string> Split(string? arguments)
    {
        var result = new List<string>();
        if (string.IsNullOrEmpty(arguments))
        {
            return result;
        }

        var current = new StringBuilder();
        bool inQuotes = false;

        // 当前 token 是否已开始（含仅由空引号产生的空 token）。
        bool hasToken = false;

        for (int i = 0; i < arguments.Length; i++)
        {
            char c = arguments[i];

            if (c == '"')
            {
                inQuotes = !inQuotes;
                hasToken = true;
                continue;
            }

            // 引号内的 \" 转义：保留字面引号。
            if (c == '\\' && inQuotes && i + 1 < arguments.Length && arguments[i + 1] == '"')
            {
                current.Append('"');
                i++;
                continue;
            }

            if (!inQuotes && char.IsWhiteSpace(c))
            {
                if (hasToken)
                {
                    result.Add(current.ToString());
                    current.Clear();
                    hasToken = false;
                }
                continue;
            }

            current.Append(c);
            hasToken = true;
        }

        if (hasToken)
        {
            result.Add(current.ToString());
        }
        return result;
    }
}
