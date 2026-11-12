using System.Text.Json;
using SandianshuiLauncher.Core.Models;
using SandianshuiLauncher.Core.Services;
using Xunit;

namespace SandianshuiLauncher.Core.Tests;

/// <summary>
/// 启动参数构造测试：使用临时目录模拟 .minecraft 结构，
/// 验证内存、classpath、占位符替换、规则参数、新旧版本分支等。
/// </summary>
public class LaunchArgumentsTests : IDisposable
{
    private readonly string _gameDir;

    private const string ModernJson = """
    {
      "id": "1.20.1",
      "type": "release",
      "mainClass": "net.minecraft.client.main.Main",
      "assets": "5",
      "javaVersion": { "majorVersion": 17 },
      "libraries": [
        { "name": "com.example:lib1:1.0",
          "downloads": { "artifact": { "path": "com/example/lib1/1.0/lib1-1.0.jar" } } },
        { "name": "org.lwjgl:lwjgl:3.3.1:natives-windows",
          "downloads": { "artifact": { "path": "org/lwjgl/lwjgl/3.3.1/lwjgl-3.3.1-natives-windows.jar" } } },
        { "name": "com.example:serveronly:1.0", "clientreq": false }
      ],
      "arguments": {
        "game": [
          "--username", "${auth_player_name}",
          "--version", "${version_name}",
          "--gameDir", "${game_directory}",
          "--assetsDir", "${assets_root}",
          "--assetIndex", "${assets_index_name}",
          "--uuid", "${auth_uuid}",
          "--accessToken", "${auth_access_token}",
          "--userType", "${user_type}",
          "--versionType", "${version_type}",
          { "rules": [ { "action": "allow", "features": { "has_custom_resolution": true } } ],
            "value": [ "--width", "${resolution_width}", "--height", "${resolution_height}" ] },
          { "rules": [ { "action": "allow", "features": { "is_demo_user": true } } ],
            "value": [ "--demo" ] }
        ],
        "jvm": [
          "-Djava.library.path=${natives_directory}",
          "-cp", "${classpath}"
        ]
      }
    }
    """;

    private const string LegacyJson = """
    {
      "id": "1.7.10",
      "type": "release",
      "mainClass": "net.minecraft.launchwrapper.Launch",
      "assets": "legacy",
      "javaVersion": { "majorVersion": 8 },
      "minecraftArguments": "--username ${auth_player_name} --session ${auth_session} --version ${version_name} --gameDir ${game_directory} --assetsDir ${assets_root} --assetIndex ${assets_index_name} --uuid ${auth_uuid} --accessToken ${auth_access_token} --userType ${user_type}"
    }
    """;

    /// <summary>Forge 风格：实例继承 1.20.1，客户端 jar 名为 1.20.1.jar。</summary>
    private const string ForgeJson = """
    {
      "id": "【导入包】ExamplePack-Client-0.1.1",
      "inheritsFrom": "1.20.1",
      "jar": "1.20.1",
      "type": "release",
      "mainClass": "cpw.mods.bootstraplauncher.BootstrapLauncher",
      "assets": "5",
      "javaVersion": { "majorVersion": 17 },
      "arguments": {
        "jvm": [
          "-Djava.library.path=${natives_directory}",
          "-DignoreList=bootstraplauncher,securejarhandler,client-extra,forge-,${version_name}.jar",
          "-DlibraryDirectory=${library_directory}",
          "-cp", "${classpath}"
        ],
        "game": [ "--username", "${auth_player_name}", "--version", "${version_name}" ]
      }
    }
    """;

    /// <summary>与 ForgeJson 相同，但 ignoreList 已显式包含真实客户端 jar 名。</summary>
    private const string ForgeJsonWithClientJarInIgnoreList = """
    {
      "id": "【导入包】ExamplePack-Client-0.1.1",
      "inheritsFrom": "1.20.1",
      "jar": "1.20.1",
      "type": "release",
      "mainClass": "cpw.mods.bootstraplauncher.BootstrapLauncher",
      "assets": "5",
      "javaVersion": { "majorVersion": 17 },
      "arguments": {
        "jvm": [
          "-DignoreList=bootstraplauncher,client-extra,forge-,1.20.1.jar,${version_name}.jar",
          "-cp", "${classpath}"
        ],
        "game": [ "--username", "${auth_player_name}" ]
      }
    }
    """;

    public LaunchArgumentsTests()
    {
        _gameDir = Path.Combine(Path.GetTempPath(), "sds-launch-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_gameDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_gameDir, true); }
        catch { /* 临时目录清理失败可忽略 */ }
    }

    private VersionJson Load(string json)
    {
        var version = JsonSerializer.Deserialize<VersionJson>(json, JsonOptions.Minecraft)
                      ?? throw new InvalidDataException("测试 JSON 解析失败");
        string versionDir = Path.Combine(_gameDir, "versions", version.Id!);
        Directory.CreateDirectory(versionDir);
        File.WriteAllText(Path.Combine(versionDir, version.Id + ".jar"), "fake");
        return version;
    }

    /// <summary>按 jarId 造出假客户端 jar（继承版本的 jar 名与实例名不同）。</summary>
    private void CreateClientJar(string jarId)
    {
        string directory = Path.Combine(_gameDir, "versions", jarId);
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, jarId + ".jar"), "fake");
    }

    private static string? ValueAfter(List<string> args, string key)
    {
        int i = args.IndexOf(key);
        return i >= 0 && i + 1 < args.Count ? args[i + 1] : null;
    }

    private LaunchOptions BuildOptions(VersionJson version,
        int? width = null, int? height = null, bool fullscreen = false) => new()
    {
        Account = new GameAccount
        {
            Type = AccountType.Offline,
            Username = "Tester",
            Uuid = "0123456789abcdef0123456789abcdef",
            AccessToken = "0"
        },
        VersionId = version.Id!,
        Version = version,
        GameDirectory = _gameDir,
        JavaPath = "java.exe",
        MinMemoryMb = 512,
        MaxMemoryMb = 2048,
        WindowWidth = width,
        WindowHeight = height,
        Fullscreen = fullscreen
    };

    [Fact]
    public void ModernVersion_BasicArguments()
    {
        var version = Load(ModernJson);
        var service = new LaunchService();

        var args = service.BuildArguments(BuildOptions(version), javaMajorVersion: 17);

        Assert.Contains("-Xms512M", args);
        Assert.Contains("-Xmx2048M", args);
        Assert.Equal("Tester", ValueAfter(args, "--username"));
        Assert.Equal("0123456789abcdef0123456789abcdef", ValueAfter(args, "--uuid"));
        Assert.Equal("1.20.1", ValueAfter(args, "--version"));
        Assert.Equal("5", ValueAfter(args, "--assetIndex"));
        Assert.Equal("legacy", ValueAfter(args, "--userType"));

        // 主类必须位于 JVM 参数（含 -cp）之后、游戏参数（--username）之前。
        int mainIndex = args.IndexOf("net.minecraft.client.main.Main");
        int cpIndex = args.IndexOf("-cp");
        int usernameIndex = args.IndexOf("--username");
        Assert.True(cpIndex >= 0, "缺少 -cp 参数");
        Assert.True(mainIndex > cpIndex, "主类应在 -cp 之后");
        Assert.True(usernameIndex > mainIndex, "游戏参数应在主类之后");
    }

    [Fact]
    public void ModernVersion_Classpath_IncludesLwjglNatives_ExcludesServerOnly()
    {
        var version = Load(ModernJson);
        var service = new LaunchService();

        var args = service.BuildArguments(BuildOptions(version), javaMajorVersion: 17);
        string classpath = ValueAfter(args, "-cp")!;
        var entries = classpath.Split(';');

        Assert.Contains(entries, e => e.Replace('\\', '/').EndsWith("com/example/lib1/1.0/lib1-1.0.jar"));
        // 客户端 jar 必须在 classpath 末尾。
        Assert.Contains(entries, e => e.Replace('\\', '/').EndsWith("versions/1.20.1/1.20.1.jar"));
        // LWJGL native jar 必须在 classpath（LWJGL 自行提取 native，双保险）。
        Assert.Contains(entries, e => e.Contains("lwjgl-3.3.1-natives-windows.jar"));
        Assert.DoesNotContain(entries, e => e.Contains("serveronly"));
    }

    [Fact]
    public void BuildArguments_IncludesPclStyleJvmTuning()
    {
        var version = Load(ModernJson);
        var service = new LaunchService();

        var args = service.BuildArguments(BuildOptions(version), javaMajorVersion: 17);

        Assert.Contains("-XX:+UseG1GC", args);
        Assert.Contains("-XX:+PerfDisableSharedMem", args);
        Assert.Contains("-XX:-OmitStackTraceInFastThrow", args);
        Assert.Contains("-Dfml.ignoreInvalidMinecraftCertificates=true", args);
        Assert.Contains("-Dfml.ignorePatchDiscrepancies=true", args);
        Assert.Contains("-Dstdout.encoding=utf-8", args);
        Assert.Contains("-Dstderr.encoding=utf-8", args);
        Assert.Contains("-Djdk.lang.Process.allowAmbiguousCommands=true", args);
        Assert.Contains(args, a => a.StartsWith("-XX:G1NewSizePercent="));
        Assert.Contains(args, a => a.StartsWith("-XX:MaxGCPauseMillis="));
    }

    [Fact]
    public void BuildArguments_ExperimentalJvmFlags_PrecededByUnlock()
    {
        var version = Load(ModernJson);
        var service = new LaunchService();

        var args = service.BuildArguments(BuildOptions(version), javaMajorVersion: 17);

        int unlockIndex = args.IndexOf("-XX:+UnlockExperimentalVMOptions");
        Assert.True(unlockIndex >= 0,
            "缺少 -XX:+UnlockExperimentalVMOptions：JVM 会因实验性参数直接拒绝启动");

        // 所有实验性参数必须出现在解锁参数之后，否则 JVM 报
        // "VM option 'X' is experimental and must be enabled via -XX:+UnlockExperimentalVMOptions"。
        string[] experimental =
        [
            "-XX:G1NewSizePercent=20",
            "-XX:G1ReservePercent=20",
            "-XX:MinHeapFreeRatio=25",
            "-XX:MaxHeapFreeRatio=40"
        ];
        foreach (string flag in experimental)
        {
            int flagIndex = args.IndexOf(flag);
            Assert.True(flagIndex >= 0, $"缺少参数 {flag}");
            Assert.True(flagIndex > unlockIndex, $"{flag} 必须位于解锁参数之后");
        }
    }

    [Fact]
    public void ModernVersion_NoResolutionFeature_NoWidth()
    {
        var version = Load(ModernJson);
        var service = new LaunchService();

        var args = service.BuildArguments(BuildOptions(version), javaMajorVersion: 17);

        Assert.Null(ValueAfter(args, "--width"));
        Assert.DoesNotContain("--demo", args);
    }

    [Fact]
    public void ModernVersion_CustomResolution_ExpandsWidthHeight()
    {
        var version = Load(ModernJson);
        var service = new LaunchService();

        var args = service.BuildArguments(BuildOptions(version, width: 1280, height: 720), javaMajorVersion: 17);

        Assert.Equal("1280", ValueAfter(args, "--width"));
        Assert.Equal("720", ValueAfter(args, "--height"));
    }

    [Fact]
    public void Fullscreen_AddsFlag()
    {
        var version = Load(ModernJson);
        var service = new LaunchService();

        var args = service.BuildArguments(BuildOptions(version, fullscreen: true), javaMajorVersion: 17);

        Assert.Contains("--fullscreen", args);
    }

    [Fact]
    public void ModernJava17_OnModernVersion_NoAddOpens()
    {
        var version = Load(ModernJson);
        var service = new LaunchService();

        var args = service.BuildArguments(BuildOptions(version), javaMajorVersion: 17);

        Assert.DoesNotContain("--add-opens", args);
    }

    [Fact]
    public void ModernJavaRunningLegacyVersion_AddsAddOpens()
    {
        var version = Load(LegacyJson);
        var service = new LaunchService();

        var args = service.BuildArguments(BuildOptions(version), javaMajorVersion: 17);

        Assert.Contains("--add-opens", args);
        // 旧版参数模板生效。
        Assert.Equal("Tester", ValueAfter(args, "--username"));
        Assert.NotNull(ValueAfter(args, "--session"));
    }

    [Fact]
    public void ModernJavaRunningLegacyVersion_AddsLog4jDefense()
    {
        var version = Load(LegacyJson);
        var service = new LaunchService();

        var args = service.BuildArguments(BuildOptions(version), javaMajorVersion: 17);

        Assert.Contains("-Dlog4j2.formatMsgNoLookups=true", args);
    }

    [Fact]
    public void LegacyVersion_WithLegacyJava_NoLog4jDefense()
    {
        var version = Load(LegacyJson);
        var service = new LaunchService();

        var args = service.BuildArguments(BuildOptions(version), javaMajorVersion: 8);

        Assert.DoesNotContain("-Dlog4j2.formatMsgNoLookups=true", args);
    }

    [Fact]
    public void LegacyVersion_LegacyArgumentTemplate()
    {
        var version = Load(LegacyJson);
        var service = new LaunchService();

        var args = service.BuildArguments(BuildOptions(version), javaMajorVersion: 8);

        Assert.Equal("legacy", ValueAfter(args, "--assetIndex"));
        Assert.Equal("0", ValueAfter(args, "--accessToken"));
        // 无 jvm 模板时自动补 classpath。
        Assert.NotNull(ValueAfter(args, "-cp"));
    }

    [Fact]
    public void MissingClientJar_Throws()
    {
        var version = Load(ModernJson);
        File.Delete(Path.Combine(_gameDir, "versions", "1.20.1", "1.20.1.jar"));
        var service = new LaunchService();

        Assert.Throws<FileNotFoundException>(
            () => service.BuildArguments(BuildOptions(version), javaMajorVersion: 17));
    }

    [Fact]
    public void VersionIsolation_GameDirPointsToVersionFolder()
    {
        var version = Load(ModernJson);
        var service = new LaunchService();

        var isolated = new LaunchOptions
        {
            Account = new GameAccount
            {
                Type = AccountType.Offline,
                Username = "Tester",
                Uuid = "0123456789abcdef0123456789abcdef",
                AccessToken = "0"
            },
            VersionId = version.Id!,
            Version = version,
            GameDirectory = _gameDir,
            JavaPath = "java.exe",
            VersionIsolation = true
        };
        var args = service.BuildArguments(isolated, javaMajorVersion: 17);

        string gameDir = ValueAfter(args, "--gameDir")!;
        Assert.Contains(Path.Combine("versions", "1.20.1"), gameDir);
    }

    [Fact]
    public void AuthlibAccount_UserTypeMojang_WithJavaAgentArgument()
    {
        var version = Load(ModernJson);
        var service = new LaunchService();

        var options = new LaunchOptions
        {
            Account = new GameAccount
            {
                Type = AccountType.AuthlibInjector,
                Username = "SkinPlayer",
                Uuid = "abcdef1234567890abcdef1234567890",
                AccessToken = "ygg-access-token",
                AuthServerUrl = "https://littleskin.cn/api/yggdrasil"
            },
            VersionId = version.Id!,
            Version = version,
            GameDirectory = _gameDir,
            JavaPath = "java.exe",
            AuthlibInjectorPath = @"C:\data\authlib-injector.jar"
        };
        var args = service.BuildArguments(options, javaMajorVersion: 17);

        // 外置登录模拟 Mojang 认证体系，user_type 必须为 mojang。
        Assert.Equal("mojang", ValueAfter(args, "--userType"));
        // javaagent 参数按 -javaagent:<jar路径>=<认证服务器> 拼接。
        Assert.Contains(@"-javaagent:C:\data\authlib-injector.jar=https://littleskin.cn/api/yggdrasil", args);
    }

    /// <summary>
    /// Forge 的 -DignoreList 只写 ${version_name}.jar，继承版本的客户端 jar 却叫 1.20.1.jar：
    /// 名字不匹配时 Bootstraplauncher 会把它当自动模块，与 minecraft 模块重复导出包而崩溃
    /// （java.lang.module.ResolutionException），所以启动参数必须补上真实 jar 名。
    /// </summary>
    [Fact]
    public void ForgeVersion_IgnoreList_AppendsRealClientJarName()
    {
        var version = Load(ForgeJson);
        CreateClientJar("1.20.1");
        var service = new LaunchService();

        var args = service.BuildArguments(BuildOptions(version), javaMajorVersion: 17);

        string ignoreList = Assert.Single(args, a => a.StartsWith("-DignoreList=", StringComparison.Ordinal));
        string[] parts = ignoreList["-DignoreList=".Length..].Split(',');
        Assert.Contains("1.20.1.jar", parts);
        Assert.Contains("【导入包】ExamplePack-Client-0.1.1.jar", parts);
        Assert.Equal(1, parts.Count(part => part == "1.20.1.jar"));
    }

    [Fact]
    public void EmptyUsername_ThrowsInsteadOfLaunching()
    {
        // 仿 PCL2：允许创建空用户名的离线账号，但启动参数构造必须直接失败，
        // 避免用空名字把游戏拉起来（游戏内会显示异常名字或连接被拒）。
        var version = Load(ModernJson);
        var service = new LaunchService();
        var options = BuildOptions(version);
        options.Account.Username = string.Empty;

        var error = Assert.Throws<InvalidOperationException>(
            () => service.BuildArguments(options, javaMajorVersion: 17));

        Assert.Contains("用户名", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ForgeVersion_IgnoreListAlreadyHasClientJar_NotDuplicated()
    {
        var version = Load(ForgeJsonWithClientJarInIgnoreList);
        CreateClientJar("1.20.1");
        var service = new LaunchService();

        var args = service.BuildArguments(BuildOptions(version), javaMajorVersion: 17);

        string ignoreList = Assert.Single(args, a => a.StartsWith("-DignoreList=", StringComparison.Ordinal));
        Assert.Equal(1, ignoreList.Split(',').Count(part => part == "1.20.1.jar"));
    }
}
