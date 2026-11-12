using System.IO;
using Microsoft.Win32;

namespace SandianshuiLauncher.Services;

/// <summary>
/// Windows 注册表 Java 扫描器（WPF 层）：收集注册表中 JDK/JRE 安装位置的
/// java.exe 候选路径，注入 <see cref="SandianshuiLauncher.Core.Services.JavaDetector"/>。
/// </summary>
public static class JavaRegistryScanner
{
    /// <summary>注册表中可能包含 JavaHome / Path 的基础键。</summary>
    private static readonly string[] BaseKeys =
    [
        @"SOFTWARE\JavaSoft\JDK",
        @"SOFTWARE\JavaSoft\Java Runtime Environment",
        @"SOFTWARE\JavaSoft\Java Development Kit",
        @"SOFTWARE\Microsoft\JDK"
    ];

    /// <summary>收集全部注册表 Java 候选路径；任何注册表异常都被静默忽略。</summary>
    public static List<string> CollectCandidates()
    {
        var result = new List<string>();
        try
        {
            foreach (RegistryKey hive in new[] { Registry.LocalMachine, Registry.CurrentUser })
            {
                foreach (string baseKey in BaseKeys)
                {
                    CollectFromKey(hive, baseKey, result);
                }
            }
        }
        catch
        {
            // 注册表不可用不影响其他扫描来源。
        }
        return result;
    }

    private static void CollectFromKey(RegistryKey hive, string baseKeyPath, List<string> result)
    {
        using RegistryKey? baseKey = hive.OpenSubKey(baseKeyPath);
        if (baseKey is null)
        {
            return;
        }

        // 部分安装（如 Microsoft JDK）可能直接在基础键上提供 Path 值。
        AddFromValues(baseKey, result);

        foreach (string subKeyName in baseKey.GetSubKeyNames())
        {
            try
            {
                using RegistryKey? subKey = baseKey.OpenSubKey(subKeyName);
                if (subKey is not null)
                {
                    AddFromValues(subKey, result);
                }
            }
            catch
            {
                // 单个子键读取失败不影响其余。
            }
        }
    }

    private static void AddFromValues(RegistryKey key, List<string> result)
    {
        string? home = key.GetValue("JavaHome") as string
                       ?? key.GetValue("Path") as string;
        if (string.IsNullOrWhiteSpace(home))
        {
            return;
        }
        string javaExe = Path.Combine(home, "bin", "java.exe");
        if (!result.Contains(javaExe, StringComparer.OrdinalIgnoreCase))
        {
            result.Add(javaExe);
        }
    }
}
