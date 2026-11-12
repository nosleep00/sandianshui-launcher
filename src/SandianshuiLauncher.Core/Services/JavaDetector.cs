using SandianshuiLauncher.Core.Models;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;

namespace SandianshuiLauncher.Core.Services;

/// <summary>扫描本机可用的 Java 运行环境。</summary>
public partial class JavaDetector
{
    /// <summary>
    /// 平台相关的额外候选路径（如 Windows 注册表收集，由 UI 层注入，保持 Core 纯逻辑）。
    /// </summary>
    private readonly IReadOnlyList<string>? _additionalCandidates;

    public JavaDetector(IReadOnlyList<string>? additionalCandidates = null)
    {
        _additionalCandidates = additionalCandidates;
    }

    /// <summary>
    /// 启动器自己下载的 Java 根目录（通常是 <c>{数据目录}/java</c>）。每次扫描时都会检查其中的 java.exe，
    /// 因此用户一键下载 Java 之后无需重启启动器即可被识别。
    /// </summary>
    public IReadOnlyList<string> ManagedRuntimeRoots { get; set; } = [];

    /// <summary>扫描常见位置并返回全部可用 Java（按主版本号降序）。</summary>
    public List<JavaInfo> Detect(string? gameDirectory = null)
    {
        var javaPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        CollectFromJavaHome(javaPaths);
        CollectFromPath(javaPaths);
        CollectFromCommonDirectories(javaPaths);
        if (_additionalCandidates is not null)
        {
            foreach (string candidate in _additionalCandidates)
            {
                if (!string.IsNullOrWhiteSpace(candidate))
                {
                    javaPaths.Add(candidate);
                }
            }
        }
        foreach (string root in ManagedRuntimeRoots)
        {
            foreach (string managed in JavaRuntimeService.EnumerateJavaExe(root))
            {
                javaPaths.Add(managed);
            }
        }
        if (!string.IsNullOrEmpty(gameDirectory))
        {
            CollectFromMinecraftRuntime(gameDirectory!, javaPaths);
        }

        var result = new List<JavaInfo>();
        foreach (string javaExe in javaPaths)
        {
            try
            {
                if (!File.Exists(javaExe))
                {
                    continue;
                }
                JavaInfo? info = Probe(javaExe);
                if (info is not null)
                {
                    result.Add(info);
                }
            }
            catch
            {
                // 无法运行的 Java 直接忽略。
            }
        }

        return result
            .GroupBy(j => j.Path, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .OrderByDescending(j => j.MajorVersion)
            .ThenByDescending(j => j.Is64Bit)
            .ToList();
    }

    /// <summary>从候选列表中挑选最匹配指定主版本的 Java；找不到精确版本时取更高版本。</summary>
    public static JavaInfo? FindBest(IEnumerable<JavaInfo> candidates, int requiredMajorVersion)
    {
        var list = candidates.Where(j => j.Is64Bit).ToList();
        if (list.Count == 0)
        {
            return null;
        }
        return list
            .OrderBy(j => Math.Abs(j.MajorVersion - requiredMajorVersion))
            .ThenByDescending(j => j.MajorVersion >= requiredMajorVersion)
            .FirstOrDefault();
    }

    /// <summary>
    /// 从已检测到的 Java 与用户手动指定的 Java 中，选出最匹配版本要求的运行时。
    /// 手动指定的路径也会作为候选参与匹配，因此指定的版本不匹配时会自动切换到更合适的版本。
    /// </summary>
    public static JavaInfo Select(IEnumerable<JavaInfo>? detected, JavaInfo? configured, int requiredMajorVersion)
    {
        var pool = new List<JavaInfo>();
        if (configured is not null && configured.Is64Bit)
        {
            pool.Add(configured);
        }
        if (detected is not null)
        {
            pool.AddRange(detected.Where(j => j.Is64Bit));
        }
        pool = pool
            .GroupBy(j => j.Path, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .ToList();

        JavaInfo? best = FindBest(pool, requiredMajorVersion);
        if (best is not null)
        {
            return best;
        }
        // 没有 64 位候选时，退而求其次返回用户手动指定的；都没有则报错。
        if (configured is not null)
        {
            return configured;
        }
        JavaInfo? any = detected?.FirstOrDefault();
        if (any is not null)
        {
            return any;
        }
        throw new InvalidOperationException(
            $"未找到任何可用的 Java（该版本需要 Java {requiredMajorVersion}）。\n" +
            "请在“设置”页手动指定 java.exe 路径，或安装对应版本的 Java。");
    }

    /// <summary>
    /// 结合本机扫描结果与用户手动指定的路径，解析出最适合指定版本的 Java。
    /// </summary>
    public virtual JavaInfo Resolve(string? configuredPath, int requiredMajorVersion, string? gameDirectory)
    {
        List<JavaInfo> detected = Detect(gameDirectory);

        JavaInfo? configured = null;
        if (!string.IsNullOrWhiteSpace(configuredPath) && File.Exists(configuredPath))
        {
            try
            {
                configured = Probe(configuredPath);
            }
            catch
            {
                // 手动指定的路径无法运行时忽略，交由 Select 从扫描结果中选择。
            }
        }

        return Select(detected, configured, requiredMajorVersion);
    }

    /// <summary>运行 java -version 并解析信息。</summary>
    public static JavaInfo? Probe(string javaExePath)
    {
        var psi = new ProcessStartInfo
        {
            FileName = javaExePath,
            Arguments = "-version",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(psi);
        if (process is null)
        {
            return null;
        }
        string stderr = process.StandardError.ReadToEnd();
        string stdout = process.StandardOutput.ReadToEnd();
        if (!process.WaitForExit(10000))
        {
            process.Kill();
            return null;
        }

        string output = stderr + "\n" + stdout;
        var match = VersionQuoteRegex().Match(output);
        if (!match.Success)
        {
            return null;
        }

        string version = match.Groups[1].Value;
        return new JavaInfo
        {
            Path = javaExePath,
            Version = version,
            MajorVersion = MajorFromVersionString(version),
            Is64Bit = output.Contains("64-Bit", StringComparison.OrdinalIgnoreCase)
                     || output.Contains("x86_64", StringComparison.OrdinalIgnoreCase)
                     || output.Contains("amd64", StringComparison.OrdinalIgnoreCase),
            Vendor = new DirectoryInfo(Path.GetDirectoryName(Path.GetDirectoryName(javaExePath)!)!).Name
        };
    }

    /// <summary>从版本字符串解析主版本号：1.8.0_392 → 8，17.0.10 → 17。</summary>
    public static int MajorFromVersionString(string version)
    {
        var match = MajorVersionRegex().Match(version);
        if (!match.Success)
        {
            return 0;
        }
        int first = int.Parse(match.Groups[1].Value);
        if (first == 1 && match.Groups[2].Success)
        {
            return int.Parse(match.Groups[2].Value);
        }
        return first;
    }

    private static void CollectFromJavaHome(HashSet<string> result)
    {
        string? javaHome = Environment.GetEnvironmentVariable("JAVA_HOME");
        if (!string.IsNullOrEmpty(javaHome))
        {
            result.Add(Path.Combine(javaHome!, "bin", "java.exe"));
        }
    }

    private static void CollectFromPath(HashSet<string> result)
    {
        string? pathEnv = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrEmpty(pathEnv))
        {
            return;
        }
        foreach (string dir in pathEnv.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                string candidate = Path.Combine(dir.Trim('"'), "java.exe");
                if (File.Exists(candidate))
                {
                    result.Add(candidate);
                }
            }
            catch
            {
                // 非法 PATH 项忽略。
            }
        }
    }

    private static void CollectFromCommonDirectories(HashSet<string> result)
    {
        string programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

        string[] roots =
        [
            Path.Combine(programFiles, "Java"),
            Path.Combine(programFiles, "Eclipse Adoptium"),
            Path.Combine(programFiles, "Microsoft"),
            Path.Combine(programFiles, "Zulu"),
            Path.Combine(programFiles, "Amazon Corretto"),
            Path.Combine(programFiles, "BellSoft"),
            Path.Combine(programFiles, "Semeru"),
            Path.Combine(localAppData, "Programs", "Eclipse Adoptium"),
            Path.Combine(localAppData, "Programs", "Microsoft")
        ];

        foreach (string root in roots)
        {
            if (!Directory.Exists(root))
            {
                continue;
            }
            foreach (string javaExe in Directory.EnumerateFiles(root, "java.exe", new EnumerationOptions
            {
                RecurseSubdirectories = true,
                MaxRecursionDepth = 3,
                IgnoreInaccessible = true
            }))
            {
                result.Add(javaExe);
            }
        }
    }

    private static void CollectFromMinecraftRuntime(string gameDirectory, HashSet<string> result)
    {
        string runtimeDir = Path.Combine(gameDirectory, "runtime");
        if (!Directory.Exists(runtimeDir))
        {
            return;
        }
        foreach (string javaExe in Directory.EnumerateFiles(runtimeDir, "java.exe", new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true
        }))
        {
            // 官方运行时路径形如 runtime/<component>/windows-x64/<component>/bin/java.exe。
            if (javaExe.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            {
                result.Add(javaExe);
            }
        }
    }

    [GeneratedRegex(@"""?([0-9]+(?:\.[0-9]+){0,2}[^""\s]*)""?")]
    private static partial Regex VersionQuoteRegex();

    [GeneratedRegex(@"^(\d+)(?:\.(\d+))?")]
    private static partial Regex MajorVersionRegex();
}
