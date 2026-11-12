using System.IO.Compression;
using System.Text;
using System.Text.Json;
using SandianshuiLauncher.Core.Models;
using SandianshuiLauncher.Core.Services;
using Xunit;

namespace SandianshuiLauncher.Core.Tests;

/// <summary>
/// 原生库解压与 classpath 测试：模拟 native jar，验证旧版（解压到根目录）与
/// 新版（26.3 起，LWJGL native jar 进 classpath 自动提取、其余 native 平铺到 java/）。
/// </summary>
public class NativesExtractionTests : IDisposable
{
    private readonly string _root;
    private readonly string _librariesDir;
    private readonly string _nativesDir;

    public NativesExtractionTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "sds-natives-tests-" + Guid.NewGuid().ToString("N"));
        _librariesDir = Path.Combine(_root, "libraries");
        _nativesDir = Path.Combine(_root, "versions", "26.3", "natives");
        Directory.CreateDirectory(_librariesDir);
        Directory.CreateDirectory(_nativesDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, true); }
        catch { /* 临时目录清理失败可忽略 */ }
    }

    private const string NewLayoutJson = """
    {
      "id": "26.3",
      "mainClass": "net.minecraft.client.main.Main",
      "javaVersion": { "majorVersion": 25 },
      "libraries": [
        { "name": "com.example:lib1:1.0",
          "downloads": { "artifact": { "path": "com/example/lib1/1.0/lib1-1.0.jar" } } },
        { "name": "org.lwjgl:lwjgl:3.4.3:natives-windows",
          "downloads": { "artifact": { "path": "org/lwjgl/lwjgl/3.4.3/lwjgl-3.4.3-natives-windows.jar" } } },
        { "name": "org.lwjgl:lwjgl-sdl:3.4.3:natives-windows",
          "downloads": { "artifact": { "path": "org/lwjgl/lwjgl-sdl/3.4.3/lwjgl-sdl-3.4.3-natives-windows.jar" } } },
        { "name": "com.mojang:jtracy:1.14.38:natives-windows",
          "downloads": { "artifact": { "path": "com/mojang/jtracy/1.14.38/jtracy-1.14.38-natives-windows.jar" } } }
      ],
      "arguments": {
        "jvm": [
          "-Djava.library.path=${natives_directory}/java",
          "-Djna.tmpdir=${natives_directory}/jna",
          "-Dorg.lwjgl.system.SharedLibraryExtractPath=${natives_directory}/lwjgl",
          "-Dio.netty.native.workdir=${natives_directory}/netty",
          "-cp", "${classpath}"
        ]
      }
    }
    """;

    private const string OldLayoutJson = """
    {
      "id": "1.20.1",
      "mainClass": "net.minecraft.client.main.Main",
      "javaVersion": { "majorVersion": 17 },
      "libraries": [
        { "name": "org.lwjgl:lwjgl:3.3.1:natives-windows",
          "downloads": { "artifact": { "path": "org/lwjgl/lwjgl/3.3.1/lwjgl-3.3.1-natives-windows.jar" } } }
      ],
      "arguments": {
        "jvm": [
          "-Djava.library.path=${natives_directory}",
          "-Djna.tmpdir=${natives_directory}",
          "-Dorg.lwjgl.system.SharedLibraryExtractPath=${natives_directory}",
          "-Dio.netty.native.workdir=${natives_directory}",
          "-cp", "${classpath}"
        ]
      }
    }
    """;

    private static VersionJson Load(string json) =>
        JsonSerializer.Deserialize<VersionJson>(json, JsonOptions.Minecraft)
        ?? throw new InvalidDataException("测试 JSON 解析失败");

    /// <summary>在 libraries 目录的 maven 相对路径下创建一个含指定条目的 jar（zip）。</summary>
    private string CreateLibraryJar(string mavenPath, params (string Name, string Content)[] entries)
    {
        string full = Path.Combine(_librariesDir, mavenPath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        using (var fs = File.Create(full))
        using (var zip = new ZipArchive(fs, ZipArchiveMode.Create))
        {
            foreach (var (name, content) in entries)
            {
                ZipArchiveEntry entry = zip.CreateEntry(name);
                using Stream stream = entry.Open();
                byte[] bytes = Encoding.UTF8.GetBytes(content);
                stream.Write(bytes, 0, bytes.Length);
            }
        }
        return full;
    }

    /// <summary>创建 26.3 新版布局所需的 native jar：LWJGL bundle（含 SDL3）与平铺的 jtracy。</summary>
    private void SetupNewLayoutJars()
    {
        CreateLibraryJar("org/lwjgl/lwjgl/3.4.3/lwjgl-3.4.3-natives-windows.jar",
            ("windows/x64/org/lwjgl/lwjgl.dll", "lwjgl"),
            ("windows/x64/org/lwjgl/lwjgl.dll.sha1", "hash"));
        CreateLibraryJar("org/lwjgl/lwjgl-sdl/3.4.3/lwjgl-sdl-3.4.3-natives-windows.jar",
            ("windows/x64/org/lwjgl/sdl/SDL3.dll", "sdl"));
        CreateLibraryJar("com/mojang/jtracy/1.14.38/jtracy-1.14.38-natives-windows.jar",
            ("jtracy-jni-windows.dll", "jtracy"),
            ("Tracy_LICENSE", "license"));
    }

    [Fact]
    public void NewLayout_LwjglNativesNotExtracted_ButLwjglDirectoryCreated()
    {
        SetupNewLayoutJars();

        var service = new LaunchService();
        service.PrepareNatives(Load(NewLayoutJson), _librariesDir, _nativesDir);

        // LWJGL 靠 classpath 自动提取，启动器不再手动解压到磁盘。
        Assert.True(Directory.Exists(Path.Combine(_nativesDir, "lwjgl")));
        Assert.False(Directory.Exists(Path.Combine(_nativesDir, "windows")));
        Assert.False(Directory.Exists(Path.Combine(_nativesDir, "lwjgl", "windows")));
    }

    [Fact]
    public void NewLayout_FlattensOtherNativesIntoJavaSubdir()
    {
        SetupNewLayoutJars();

        var service = new LaunchService();
        service.PrepareNatives(Load(NewLayoutJson), _librariesDir, _nativesDir);

        // jtracy 平铺到 java/（java.library.path 指向这里），不能保留平台子目录。
        Assert.True(File.Exists(Path.Combine(_nativesDir, "java", "jtracy-jni-windows.dll")));
        Assert.False(Directory.Exists(Path.Combine(_nativesDir, "java", "windows")));
    }

    [Fact]
    public void NewLayout_CreatesJnaAndNettyDirectories()
    {
        SetupNewLayoutJars();

        var service = new LaunchService();
        service.PrepareNatives(Load(NewLayoutJson), _librariesDir, _nativesDir);

        Assert.True(Directory.Exists(Path.Combine(_nativesDir, "jna")));
        Assert.True(Directory.Exists(Path.Combine(_nativesDir, "netty")));
        Assert.True(Directory.Exists(Path.Combine(_nativesDir, "java")));
        Assert.True(Directory.Exists(Path.Combine(_nativesDir, "lwjgl")));
    }

    [Fact]
    public void NewLayout_LwjglNativeJarsOnClasspath_ButNotJtracy()
    {
        SetupNewLayoutJars();
        // BuildArguments 要求客户端 jar 存在。
        string versionDir = Path.Combine(_root, "versions", "26.3");
        Directory.CreateDirectory(versionDir);
        File.WriteAllText(Path.Combine(versionDir, "26.3.jar"), "fake");

        var version = Load(NewLayoutJson);
        var service = new LaunchService();
        var options = new LaunchOptions
        {
            Account = new GameAccount
            {
                Type = AccountType.Offline,
                Username = "Tester",
                Uuid = "0123456789abcdef0123456789abcdef",
                AccessToken = "0"
            },
            VersionId = "26.3",
            Version = version,
            GameDirectory = _root,
            JavaPath = "java.exe",
            MinMemoryMb = 512,
            MaxMemoryMb = 2048
        };

        var args = service.BuildArguments(options, javaMajorVersion: 25);
        int cpIndex = args.IndexOf("-cp");
        Assert.True(cpIndex >= 0, "缺少 -cp 参数");
        var entries = args[cpIndex + 1].Split(';');

        // LWJGL native jar 必须在 classpath（供 LWJGL 自动提取）。
        Assert.Contains(entries, e => e.Replace('\\', '/').Contains("lwjgl-3.4.3-natives-windows.jar"));
        Assert.Contains(entries, e => e.Replace('\\', '/').Contains("lwjgl-sdl-3.4.3-natives-windows.jar"));
        // 普通库仍在 classpath。
        Assert.Contains(entries, e => e.Replace('\\', '/').Contains("lib1-1.0.jar"));
        // jtracy 等非 LWJGL native 不进 classpath（改为解压到 java/）。
        Assert.DoesNotContain(entries, e => e.Contains("jtracy-1.14.38-natives-windows.jar"));
    }

    [Fact]
    public void OldLayout_FlattensNativesToRoot_CurrentArchOnly()
    {
        // natives-windows jar 同时含 x64 与 arm64 子树：仅平铺当前架构（x64）的 dll。
        CreateLibraryJar("org/lwjgl/lwjgl/3.3.1/lwjgl-3.3.1-natives-windows.jar",
            ("windows/x64/org/lwjgl/lwjgl.dll", "lwjgl-x64"),
            ("windows/x64/org/lwjgl/glfw/glfw.dll", "glfw-x64"),
            ("windows/arm64/org/lwjgl/lwjgl.dll", "lwjgl-arm64"),
            ("windows/arm64/org/lwjgl/glfw/glfw.dll", "glfw-arm64"));

        var service = new LaunchService();
        service.PrepareNatives(Load(OldLayoutJson), _librariesDir, _nativesDir);

        // 平铺到 natives 根目录（java.library.path 只搜根目录）。
        Assert.True(File.Exists(Path.Combine(_nativesDir, "lwjgl.dll")));
        Assert.True(File.Exists(Path.Combine(_nativesDir, "glfw.dll")));
        Assert.Equal("lwjgl-x64", File.ReadAllText(Path.Combine(_nativesDir, "lwjgl.dll")));
        // 不保留 bundle 路径、不混入其它架构。
        Assert.False(Directory.Exists(Path.Combine(_nativesDir, "windows")));
        Assert.False(Directory.Exists(Path.Combine(_nativesDir, "lwjgl")));
        Assert.False(Directory.Exists(Path.Combine(_nativesDir, "java")));
    }

    [Fact]
    public void OldLayout_SkipsNonCurrentArchClassifierJars()
    {
        // 同时存在 natives-windows 与 natives-windows-arm64 构件：arm64 构件不应被解压。
        CreateLibraryJar("org/lwjgl/lwjgl/3.3.1/lwjgl-3.3.1-natives-windows.jar",
            ("windows/x64/org/lwjgl/lwjgl.dll", "lwjgl-x64"));
        CreateLibraryJar("org/lwjgl/lwjgl/3.3.1/lwjgl-3.3.1-natives-windows-arm64.jar",
            ("windows/arm64/org/lwjgl/lwjgl-arm64-marker.dll", "arm64-marker"));

        const string json = """
        {
          "id": "1.20.1",
          "mainClass": "net.minecraft.client.main.Main",
          "javaVersion": { "majorVersion": 17 },
          "libraries": [
            { "name": "org.lwjgl:lwjgl:3.3.1:natives-windows",
              "downloads": { "artifact": { "path": "org/lwjgl/lwjgl/3.3.1/lwjgl-3.3.1-natives-windows.jar" } } },
            { "name": "org.lwjgl:lwjgl:3.3.1:natives-windows-arm64",
              "downloads": { "artifact": { "path": "org/lwjgl/lwjgl/3.3.1/lwjgl-3.3.1-natives-windows-arm64.jar" } } }
          ],
          "arguments": { "jvm": [ "-Djava.library.path=${natives_directory}" ] }
        }
        """;

        var service = new LaunchService();
        service.PrepareNatives(Load(json), _librariesDir, _nativesDir);

        Assert.True(File.Exists(Path.Combine(_nativesDir, "lwjgl.dll")));
        // 当前架构为 x64：arm64 构件跳过，其 marker 不得出现。
        Assert.False(File.Exists(Path.Combine(_nativesDir, "lwjgl-arm64-marker.dll")));
    }

    [Fact]
    public void OldLayout_LwjglNativeJarOnClasspath()
    {
        CreateLibraryJar("org/lwjgl/lwjgl/3.3.1/lwjgl-3.3.1-natives-windows.jar",
            ("windows/x64/org/lwjgl/lwjgl.dll", "lwjgl"));
        string versionDir = Path.Combine(_root, "versions", "1.20.1");
        Directory.CreateDirectory(versionDir);
        File.WriteAllText(Path.Combine(versionDir, "1.20.1.jar"), "fake");

        var version = Load(OldLayoutJson);
        var service = new LaunchService();
        var options = new LaunchOptions
        {
            Account = new GameAccount
            {
                Type = AccountType.Offline,
                Username = "Tester",
                Uuid = "0123456789abcdef0123456789abcdef",
                AccessToken = "0"
            },
            VersionId = "1.20.1",
            Version = version,
            GameDirectory = _root,
            JavaPath = "java.exe",
            MinMemoryMb = 512,
            MaxMemoryMb = 2048
        };

        var args = service.BuildArguments(options, javaMajorVersion: 17);
        int cpIndex = args.IndexOf("-cp");
        Assert.True(cpIndex >= 0, "缺少 -cp 参数");
        var entries = args[cpIndex + 1].Split(';');

        // 旧版 LWJGL native jar 也必须在 classpath（LWJGL 自行提取，双保险）。
        Assert.Contains(entries, e => e.Replace('\\', '/').Contains("lwjgl-3.3.1-natives-windows.jar"));
    }

    [Fact]
    public void NewLayout_Rerun_WithReadOnlyLeftoverFiles_Succeeds()
    {
        SetupNewLayoutJars();
        var service = new LaunchService();

        // 第一次启动：正常解压。
        service.PrepareNatives(Load(NewLayoutJson), _librariesDir, _nativesDir);

        // 模拟重复启动前，上一次的产物残留并带只读属性（曾导致整目录删除失败、覆盖时报“拒绝访问”）。
        string javaDir = Path.Combine(_nativesDir, "java");
        foreach (string file in Directory.GetFiles(javaDir))
        {
            File.SetAttributes(file, FileAttributes.ReadOnly);
        }
        string stale = Path.Combine(javaDir, "stale.tmp");
        File.WriteAllText(stale, "old");
        File.SetAttributes(stale, FileAttributes.ReadOnly);

        // 第二次启动：必须成功重建并重新解压，且新文件非只读、内容正确。
        service.PrepareNatives(Load(NewLayoutJson), _librariesDir, _nativesDir);

        string dll = Path.Combine(javaDir, "jtracy-jni-windows.dll");
        Assert.True(File.Exists(dll));
        Assert.Equal("jtracy", File.ReadAllText(dll));
        Assert.False(File.GetAttributes(dll).HasFlag(FileAttributes.ReadOnly));
    }
}
