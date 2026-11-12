using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using SandianshuiLauncher.Core.Models;
using SandianshuiLauncher.Core.Utils;

namespace SandianshuiLauncher.Core.Services;

/// <summary>
/// 启动核心：解压原生库、拼接 classpath 与启动参数、调用 Java 启动游戏并接管日志。
/// </summary>
public class LaunchService
{
    /// <summary>构造完整启动参数（独立出来便于单元测试）。</summary>
    public List<string> BuildArguments(LaunchOptions options, int javaMajorVersion)
    {
        VersionJson version = options.Version;

        // 兜底：仿 PCL2 允许创建空用户名的离线账号，但这样的账号不能进游戏。
        if (string.IsNullOrWhiteSpace(options.Account.Username))
        {
            throw new InvalidOperationException("该账号尚未设置用户名，无法启动游戏。");
        }
        string rootGameDir = options.GameDirectory;
        string actualGameDir = GamePaths.GetActualGameDirectory(
            rootGameDir, options.VersionId, options.VersionIsolation);
        Directory.CreateDirectory(actualGameDir);

        string librariesDir = Path.Combine(rootGameDir, "libraries");
        string assetsDir = Path.Combine(rootGameDir, "assets");
        string nativesDir = Path.Combine(rootGameDir, "versions", options.VersionId, "natives");
        string jarId = version.Jar ?? version.Id
            ?? throw new InvalidDataException("版本 JSON 缺少 id");
        string clientJar = Path.Combine(rootGameDir, "versions", jarId, jarId + ".jar");

        if (string.IsNullOrEmpty(version.MainClass))
        {
            throw new InvalidDataException("版本 JSON 缺少 mainClass，无法启动。");
        }
        if (!File.Exists(clientJar))
        {
            throw new FileNotFoundException(
                $"客户端文件不存在：{clientJar}\n请先在“下载”页安装该版本，或检查游戏目录是否正确。");
        }

        var classpath = new List<string>();
        CollectClasspath(version, librariesDir, classpath);
        classpath.Add(clientJar);
        string classpathString = string.Join(";", classpath.Distinct(StringComparer.OrdinalIgnoreCase));

        var features = new Dictionary<string, bool>
        {
            ["is_demo_user"] = false,
            ["has_custom_resolution"] = options.WindowWidth is > 0 && options.WindowHeight is > 0,
            ["has_quick_plays_singleplayer"] = false,
            ["has_quick_plays_multiplayer"] = false,
            ["has_quick_plays_realms"] = false,
            ["has_quick_plays_legacy_quick_play"] = false
        };

        var placeholders = new Dictionary<string, string>
        {
            ["${auth_player_name}"] = options.Account.Username,
            ["${version_name}"] = options.VersionId,
            ["${game_directory}"] = actualGameDir,
            ["${assets_root}"] = assetsDir,
            ["${assets_index_name}"] = version.Assets ?? "legacy",
            ["${game_assets}"] = Path.Combine(assetsDir, "virtual", version.Assets ?? "legacy"),
            ["${auth_uuid}"] = options.Account.Uuid,
            ["${auth_access_token}"] = options.Account.AccessToken ?? "0",
            ["${auth_session}"] = options.Account.AccessToken ?? "0",
            ["${clientid}"] = string.Empty,
            ["${auth_xuid}"] = string.Empty,
            ["${user_type}"] = options.Account.Type switch
            {
                AccountType.Offline => "legacy",
                // 外置登录通过 authlib-injector 模拟 Mojang 认证体系，user_type 必须为 mojang。
                AccountType.AuthlibInjector => "mojang",
                _ => "msa"
            },
            ["${version_type}"] = version.Type ?? "release",
            ["${user_properties}"] = "{}",
            ["${resolution_width}"] = options.WindowWidth?.ToString() ?? "854",
            ["${resolution_height}"] = options.WindowHeight?.ToString() ?? "480",
            ["${natives_directory}"] = nativesDir,
            ["${launcher_name}"] = "SandianshuiLauncher",
            ["${launcher_version}"] = "1.0.0",
            ["${classpath}"] = classpathString,
            ["${classpath_separator}"] = ";",
            ["${library_directory}"] = librariesDir,
            ["${quickPlayPath}"] = string.Empty,
            ["${quickPlaySingleplayer}"] = string.Empty,
            ["${quickPlayMultiplayer}"] = string.Empty,
            ["${quickPlayRealms}"] = string.Empty
        };

        var args = new List<string>
        {
            $"-Xms{Math.Max(128, options.MinMemoryMb)}M",
            $"-Xmx{Math.Max(512, options.MaxMemoryMb)}M"
        };

        // 对齐 PCL 的 JVM 调优：大整合包下更稳定的 G1GC、禁用 perf 共享内存、
        // 统一 stdout/stderr 编码（避免中文 Windows 下日志乱码丢失）、Forge 宽容参数。
        // 注意：G1NewSizePercent / G1ReservePercent / MinHeapFreeRatio / MaxHeapFreeRatio
        // 属于实验性参数，必须先用 -XX:+UnlockExperimentalVMOptions 解锁，否则 JVM 拒绝启动。
        args.AddRange(
        [
            "-XX:+UnlockExperimentalVMOptions",
            "-XX:+UseG1GC",
            "-XX:G1NewSizePercent=20",
            "-XX:G1ReservePercent=20",
            "-XX:G1HeapRegionSize=32M",
            "-XX:MaxGCPauseMillis=50",
            "-XX:+PerfDisableSharedMem",
            "-XX:MinHeapFreeRatio=25",
            "-XX:MaxHeapFreeRatio=40",
            "-XX:-OmitStackTraceInFastThrow",
            "-Djdk.lang.Process.allowAmbiguousCommands=true",
            "-Dfml.ignoreInvalidMinecraftCertificates=true",
            "-Dfml.ignorePatchDiscrepancies=true",
            "-Dstdout.encoding=utf-8",
            "-Dstderr.encoding=utf-8"
        ]);

        // Java 16 及以上启动老版本（Java 8 时代的游戏）需要开放模块反射。
        int requiredJava = version.JavaVersion?.MajorVersion ?? 8;
        if (javaMajorVersion >= 16 && requiredJava <= 16)
        {
            args.AddRange(AddOpensArguments);
            // Log4j CVE-2021-44228 防御：老版本游戏自带易受攻击的 log4j2。
            args.Add("-Dlog4j2.formatMsgNoLookups=true");
        }

        // 外置登录（阶段 4 启用）。
        if (options.Account.Type == AccountType.AuthlibInjector
            && !string.IsNullOrEmpty(options.AuthlibInjectorPath)
            && !string.IsNullOrEmpty(options.Account.AuthServerUrl))
        {
            args.Add($"-javaagent:{options.AuthlibInjectorPath}={options.Account.AuthServerUrl}");
        }

        if (!string.IsNullOrWhiteSpace(options.ExtraJvmArgs))
        {
            args.AddRange(Tokenize(options.ExtraJvmArgs!));
        }

        // JVM 参数。
        if (version.Arguments?.Jvm is { Count: > 0 } jvmArgs)
        {
            ExpandArguments(jvmArgs, features, placeholders, args);
        }
        else
        {
            args.Add($"-Djava.library.path={nativesDir}");
            args.Add("-cp");
            args.Add(classpathString);
        }

        // Forge（Bootstraplauncher）用 -DignoreList 排除不该被当成模块加载的 jar。
        // 官方版本 JSON 里只写了 ${version_name}.jar，而继承版本的客户端 jar 往往叫
        // 1.20.1.jar —— 名字对不上时，该 jar 会被当作自动模块加载，与 minecraft 模块
        // 重复导出 net.minecraft.client.main，抛 java.lang.module.ResolutionException。
        EnsureClientJarIgnored(args, Path.GetFileName(clientJar));

        // 主类必须位于 JVM 参数之后、游戏参数之前：
        // java [JVM 参数] <主类> [游戏参数]。
        args.Add(version.MainClass!);

        // 游戏参数。
        if (version.Arguments?.Game is { Count: > 0 } gameArgs)
        {
            ExpandArguments(gameArgs, features, placeholders, args);
        }
        else if (!string.IsNullOrEmpty(version.MinecraftArguments))
        {
            foreach (string token in Tokenize(version.MinecraftArguments!))
            {
                args.Add(ReplacePlaceholders(token, placeholders));
            }
        }

        if (options.Fullscreen)
        {
            args.Add("--fullscreen");
        }

        return args;
    }

    /// <summary>解压原生库并启动游戏，返回游戏进程。</summary>
    public Process Launch(
        LaunchOptions options,
        Action<string>? onLog = null,
        Action<int>? onExited = null,
        CancellationToken cancellationToken = default)
    {
        VersionJson version = options.Version;
        string rootGameDir = options.GameDirectory;
        string nativesDir = Path.Combine(rootGameDir, "versions", options.VersionId, "natives");

        PrepareNatives(version, Path.Combine(rootGameDir, "libraries"), nativesDir);

        JavaInfo java = JavaDetector.Probe(options.JavaPath)
            ?? throw new InvalidOperationException($"无法运行指定的 Java：{options.JavaPath}");

        List<string> args = BuildArguments(options, java.MajorVersion);

        string actualGameDir = GamePaths.GetActualGameDirectory(
            rootGameDir, options.VersionId, options.VersionIsolation);

        var psi = new ProcessStartInfo
        {
            FileName = options.JavaPath,
            WorkingDirectory = actualGameDir,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };
        foreach (string arg in args)
        {
            psi.ArgumentList.Add(arg);
        }

        var process = new Process
        {
            StartInfo = psi,
            EnableRaisingEvents = true
        };
        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is not null)
            {
                onLog?.Invoke(e.Data);
            }
        };
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is not null)
            {
                onLog?.Invoke(e.Data);
            }
        };
        process.Exited += (_, _) => onExited?.Invoke(process.ExitCode);

        if (!process.Start())
        {
            throw new InvalidOperationException("游戏进程启动失败。");
        }
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        return process;
    }

    /// <summary>解压版本 JSON 中全部适用于 Windows 的原生库。</summary>
    public void PrepareNatives(VersionJson version, string librariesDir, string nativesDir)
    {
        // 新版（1.21.5+ / 26.x，LWJGL 3.4 起）把原生库按用途拆到四个子目录：
        //   lwjgl/  LWJGL native jar 放入 classpath，由 LWJGL 自行提取到这里
        //           （形如 lwjgl/<版本>/<架构>/lwjgl.dll，启动器无需手动解压）；
        //   java/   其余通过 System.loadLibrary 加载的 DLL（如 jtracy），平铺，
        //           由 -Djava.library.path 定位；
        //   jna/    JNA 运行时自动提取（jna.tmpdir）；
        //   netty/  netty native 运行时自动提取（Windows 上通常为空）。
        // 旧版（1.21.1 及更早）全部直接解压到 natives 根目录（平铺，供 java.library.path 定位）。
        bool splitLayout = UsesSplitNativesLayout(version);
        string javaDir = nativesDir;

        if (splitLayout)
        {
            javaDir = Path.Combine(nativesDir, "java");
            RecreateDirectory(nativesDir, "java");
            RecreateDirectory(nativesDir, "jna");
            RecreateDirectory(nativesDir, "lwjgl");
            RecreateDirectory(nativesDir, "netty");
        }
        else
        {
            Directory.CreateDirectory(nativesDir);
            foreach (string file in Directory.GetFiles(nativesDir))
            {
                try { File.Delete(file); } catch { /* 文件被占用则覆盖式解压 */ }
            }
        }

        var nativeJars = new List<(string JarPath, List<string>? Excludes, string TargetDir, bool Flatten)>();

        foreach (var library in version.Libraries ?? [])
        {
            if (!RuleEvaluator.RulesAllow(library.Rules) || library.Name is null)
            {
                continue;
            }

            bool isLwjgl = library.Name.StartsWith("org.lwjgl:", StringComparison.OrdinalIgnoreCase);

            // 新版 LWJGL native jar 已加入 classpath，由 LWJGL 自动提取，无需解压。
            if (splitLayout && isLwjgl)
            {
                continue;
            }

            // 旧版全部平铺到 natives 根目录（java.library.path 只搜根，不递归）；新版其余 native 平铺到 java/。
            string targetDir = splitLayout ? javaDir : nativesDir;
            bool flatten = true;

            // 1.18 及更早：natives 映射 + classifiers。
            if (library.Natives is not null
                && library.Natives.TryGetValue("windows", out string? classifierTemplate)
                && library.Downloads?.Classifiers is { } classifiers)
            {
                string classifier = classifierTemplate.Replace("${arch}", Environment.Is64BitOperatingSystem ? "64" : "32");
                if (classifiers.TryGetValue(classifier, out DownloadInfo? native) && !string.IsNullOrEmpty(native.Path))
                {
                    nativeJars.Add((
                        Path.Combine(librariesDir, native.Path!.Replace('/', Path.DirectorySeparatorChar)),
                        library.Extract?.Exclude,
                        targetDir,
                        flatten));
                }
                continue;
            }

            // 1.19 及更新：natives 作为带 classifier 的普通构件（按当前架构选择，避免混入 arm64/x86）。
            var coordinate = MavenName.Parse(library.Name!);
            if (MavenName.IsNatives(coordinate.Classifier)
                && NativeClassifierAllowed(coordinate.Classifier))
            {
                string relativePath = library.Downloads?.Artifact?.Path ?? coordinate.ToRepositoryPath();
                nativeJars.Add((
                    Path.Combine(librariesDir, relativePath.Replace('/', Path.DirectorySeparatorChar)),
                    library.Extract?.Exclude,
                    targetDir,
                    flatten));
            }
        }

        foreach (var (jarPath, excludes, targetDir, flatten) in nativeJars)
        {
            ExtractNatives(jarPath, targetDir, excludes, flatten);
        }
    }

    /// <summary>natives classifier 是否匹配当前进程架构（排除 arm64/x86 混入）。</summary>
    internal static bool NativeClassifierAllowed(string? classifier)
        => classifier?.ToLowerInvariant() switch
        {
            "natives-windows" => true, // 内含 x64/x86 内容，解压时再按架构过滤
            "natives-windows-arm64" => RuntimeInformation.ProcessArchitecture == Architecture.Arm64,
            "natives-windows-x86" => RuntimeInformation.ProcessArchitecture == Architecture.X86,
            _ => true
        };

    /// <summary>jar 内条目路径是否属于当前进程架构（平铺时过滤用）。</summary>
    internal static bool IsCurrentArchitecturePath(string entryPath)
    {
        string lower = entryPath.Replace('\\', '/').ToLowerInvariant();
        if (lower.Contains("/arm64/"))
        {
            return RuntimeInformation.ProcessArchitecture == Architecture.Arm64;
        }
        if (lower.Contains("/x86/"))
        {
            return RuntimeInformation.ProcessArchitecture == Architecture.X86;
        }
        if (lower.Contains("/x64/"))
        {
            return RuntimeInformation.ProcessArchitecture is Architecture.X64;
        }
        return true; // 无架构信息的条目（license 等）保留
    }

    /// <summary>
    /// 判断是否使用新版（1.21.5+ / 26.x）原生库分目录布局：
    /// 其 JVM 参数会把 java.library.path 指向 <c>${natives_directory}/java</c>。
    /// </summary>
    private static bool UsesSplitNativesLayout(VersionJson version)
    {
        if (version.Arguments?.Jvm is not { } jvmArgs)
        {
            return false;
        }

        foreach (JsonElement element in jvmArgs)
        {
            if (element.ValueKind == JsonValueKind.String
                && element.GetString() is { } value
                && value.Contains("${natives_directory}/java", StringComparison.Ordinal))
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// 删除并重建指定子目录。先清除只读属性、对杀软扫描等瞬时锁定做有限重试；
    /// 即使个别文件暂时无法删除也不抛异常，残留文件交给后续逐文件覆盖式解压处理。
    /// </summary>
    private static void RecreateDirectory(string parent, string name)
    {
        string dir = Path.Combine(parent, name);
        TryDeleteDirectoryRobust(dir);
        Directory.CreateDirectory(dir);
    }

    /// <summary>清除目录下所有文件与子目录的只读/隐藏等属性，便于删除或覆盖。</summary>
    private static void ClearAttributesRecursive(string dir)
    {
        try { File.SetAttributes(dir, FileAttributes.Normal); }
        catch { /* 目录属性不影响其中文件删除 */ }

        foreach (string path in Directory.EnumerateFileSystemEntries(dir, "*", SearchOption.AllDirectories))
        {
            try { File.SetAttributes(path, FileAttributes.Normal); }
            catch { /* 个别条目属性设置失败不影响其它条目 */ }
        }
    }

    /// <summary>健壮删除目录：先清只读属性，再对瞬时锁定做有限重试；最终失败则保留目录。</summary>
    private static void TryDeleteDirectoryRobust(string dir)
    {
        if (!Directory.Exists(dir))
        {
            return;
        }

        for (int attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                ClearAttributesRecursive(dir);
                Directory.Delete(dir, recursive: true);
                return;
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException || ex is IOException)
            {
                // 杀软实时扫描或文件句柄延迟释放会造成瞬时锁定，等待后重试。
                if (attempt == 4)
                {
                    return; // 放弃整目录删除，残留文件交由逐文件覆盖处理。
                }
                Thread.Sleep(200 * (attempt + 1));
            }
        }
    }

    /// <summary>
    /// 将单个压缩包条目健壮解压到目标路径：先清除只读属性并删除旧文件，
    /// 对杀软扫描等瞬时锁定做有限重试，避免重复启动时偶发“拒绝访问”。
    /// </summary>
    private static void ExtractEntryRobust(ZipArchiveEntry entry, string destination)
    {
        Exception? lastError = null;

        for (int attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                if (File.Exists(destination))
                {
                    File.SetAttributes(destination, FileAttributes.Normal);
                    File.Delete(destination);
                }
                entry.ExtractToFile(destination, overwrite: false);
                return;
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException || ex is IOException)
            {
                lastError = ex;
                Thread.Sleep(200 * (attempt + 1));
            }
        }

        throw new IOException(
            $"无法写入原生库文件：{destination}。请确认游戏已完全关闭后重试。",
            lastError);
    }

    private static void CollectClasspath(VersionJson version, string librariesDir, List<string> result)
    {
        // 新版（LWJGL 3.4 分目录布局）与旧版一致：LWJGL native jar 都放进 classpath，
        // 由 LWJGL SharedLibraryLoader 自行提取（java.library.path 平铺作为回退）。
        // 其余 native（如 jtracy）仍解压到 java.library.path、不进 classpath。
        foreach (var library in version.Libraries ?? [])
        {
            if (!RuleEvaluator.RulesAllow(library.Rules) || library.Name is null)
            {
                continue;
            }
            if (library.ClientReq == false)
            {
                continue;
            }
            // 1.18 及更早的原生库分发构件不进 classpath。
            if (library.Natives is not null)
            {
                continue;
            }

            var coordinate = MavenName.Parse(library.Name!);
            if (MavenName.IsNatives(coordinate.Classifier))
            {
                // LWJGL native jar 按当前架构进 classpath；其余 native 跳过。
                bool isLwjgl = library.Name.StartsWith("org.lwjgl:", StringComparison.OrdinalIgnoreCase);
                if (!(isLwjgl && NativeClassifierAllowed(coordinate.Classifier)))
                {
                    continue;
                }
            }

            string relativePath = library.Downloads?.Artifact?.Path ?? coordinate.ToRepositoryPath();
            result.Add(Path.Combine(librariesDir, relativePath.Replace('/', Path.DirectorySeparatorChar)));
        }
    }

    private static void ExtractNatives(string jarPath, string targetDirectory, List<string>? excludes, bool flatten)
    {
        if (!File.Exists(jarPath))
        {
            throw new FileNotFoundException($"缺少原生库文件：{jarPath}\n请重新下载或修复该版本。");
        }

        using ZipArchive archive = ZipFile.OpenRead(jarPath);
        foreach (ZipArchiveEntry entry in archive.Entries)
        {
            if (entry.FullName.EndsWith('/'))
            {
                continue;
            }
            if (entry.FullName.StartsWith("META-INF/", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            if (excludes is not null &&
                excludes.Any(e => entry.FullName.StartsWith(e, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            // flatten=true 时只保留文件名（平铺到 java.library.path），否则保留 jar 内 bundle 路径；
            // 平铺时按当前架构过滤（natives jar 内常同时含 windows/x64 与 windows/arm64 子树）。
            string relativeName;
            if (flatten)
            {
                if (!IsCurrentArchitecturePath(entry.FullName))
                {
                    continue;
                }
                relativeName = Path.GetFileName(entry.FullName);
            }
            else
            {
                relativeName = entry.FullName;
            }
            if (string.IsNullOrEmpty(relativeName))
            {
                continue;
            }

            string destination = Path.Combine(
                targetDirectory,
                relativeName.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            ExtractEntryRobust(entry, destination);
        }
    }

    /// <summary>展开 1.13+ 参数模板（元素可能是字符串或带规则的对象）。</summary>
    private static void ExpandArguments(
        List<JsonElement> elements,
        IReadOnlyDictionary<string, bool> features,
        IReadOnlyDictionary<string, string> placeholders,
        List<string> output)
    {
        foreach (JsonElement element in elements)
        {
            if (element.ValueKind == JsonValueKind.String)
            {
                output.Add(ReplacePlaceholders(element.GetString()!, placeholders));
            }
            else if (element.ValueKind == JsonValueKind.Object
                     && element.TryGetProperty("rules", out JsonElement rulesElement))
            {
                var rules = rulesElement.Deserialize<List<Rule>>(JsonOptions.Minecraft);
                if (!RuleEvaluator.RulesAllow(rules, features))
                {
                    continue;
                }
                if (element.TryGetProperty("value", out JsonElement value))
                {
                    if (value.ValueKind == JsonValueKind.String)
                    {
                        output.Add(ReplacePlaceholders(value.GetString()!, placeholders));
                    }
                    else if (value.ValueKind == JsonValueKind.Array)
                    {
                        foreach (JsonElement item in value.EnumerateArray())
                        {
                            if (item.ValueKind == JsonValueKind.String)
                            {
                                output.Add(ReplacePlaceholders(item.GetString()!, placeholders));
                            }
                        }
                    }
                }
            }
        }
    }

    /// <summary>
    /// 保证 Forge 的 -DignoreList 里含有真实客户端 jar 文件名。
    /// 官方 Forge 版本 JSON 只写 ${version_name}.jar（即实例名），而继承版本的客户端 jar
    /// 通常叫 1.20.1.jar：名字不匹配时 Bootstraplauncher 会把它当自动模块加载，
    /// 与 minecraft 模块重复导出 net.minecraft.client.main，导致模块解析失败。
    /// </summary>
    internal static void EnsureClientJarIgnored(List<string> args, string clientJarFileName)
    {
        const string prefix = "-DignoreList=";
        if (string.IsNullOrEmpty(clientJarFileName))
        {
            return;
        }

        for (int i = 0; i < args.Count; i++)
        {
            if (!args[i].StartsWith(prefix, StringComparison.Ordinal))
            {
                continue;
            }

            string[] parts = args[i][prefix.Length..]
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Any(part => string.Equals(part, clientJarFileName, StringComparison.OrdinalIgnoreCase)))
            {
                return;
            }

            args[i] = prefix + string.Join(',', parts.Append(clientJarFileName));
            return;
        }
    }

    private static string ReplacePlaceholders(string token, IReadOnlyDictionary<string, string> values)
    {
        foreach (var (placeholder, value) in values)
        {
            token = token.Replace(placeholder, value);
        }
        return token;
    }

    /// <summary>按空白分词，支持引号分组与转义（用户 JVM 参数、老版本参数字符串共用）。</summary>
    private static List<string> Tokenize(string arguments) =>
        CommandLineTokenizer.Split(arguments);

    /// <summary>Java 17+ 运行老版本游戏所需的模块开放参数（社区通用集合）。</summary>
    private static readonly string[] AddOpensArguments =
    [
        "--add-opens", "java.base/java.lang=ALL-UNNAMED",
        "--add-opens", "java.base/java.io=ALL-UNNAMED",
        "--add-opens", "java.base/java.util=ALL-UNNAMED",
        "--add-opens", "java.base/java.util.concurrent=ALL-UNNAMED",
        "--add-opens", "java.base/java.lang.invoke=ALL-UNNAMED",
        "--add-opens", "java.base/java.lang.reflect=ALL-UNNAMED",
        "--add-opens", "java.base/java.math=ALL-UNNAMED",
        "--add-opens", "java.base/java.nio=ALL-UNNAMED",
        "--add-opens", "java.base/java.security=ALL-UNNAMED",
        "--add-opens", "java.base/java.text=ALL-UNNAMED",
        "--add-opens", "java.base/java.time=ALL-UNNAMED",
        "--add-opens", "java.base/java.net=ALL-UNNAMED",
        "--add-opens", "java.base/sun.security.util=ALL-UNNAMED",
        "--add-opens", "java.base/sun.security.action=ALL-UNNAMED",
        "--add-opens", "java.base/sun.util.calendar=ALL-UNNAMED",
        "--add-opens", "java.base/jdk.internal.misc=ALL-UNNAMED",
        "--add-opens", "java.desktop/java.awt=ALL-UNNAMED",
        "--add-opens", "java.desktop/java.awt.font=ALL-UNNAMED",
        "--add-opens", "java.desktop/java.awt.image=ALL-UNNAMED",
        "--add-opens", "java.desktop/sun.awt=ALL-UNNAMED",
        "--add-opens", "java.desktop/sun.java2d=ALL-UNNAMED",
        "--add-opens", "java.desktop/sun.awt.image=ALL-UNNAMED"
    ];
}
