using SandianshuiLauncher.Core.Models;
using SandianshuiLauncher.Core.Services;
using Xunit;
namespace SandianshuiLauncher.Core.Tests;

public class JavaDetectorTests
{
    [Theory]
    [InlineData("1.8.0_392", 8)]
    [InlineData("1.8.0", 8)]
    [InlineData("1.6.0_45", 6)]
    [InlineData("9", 9)]
    [InlineData("11.0.20", 11)]
    [InlineData("17.0.10", 17)]
    [InlineData("21.0.1", 21)]
    [InlineData("1.8.0-internal", 8)]
    public void MajorFromVersionString(string version, int expected)
    {
        Assert.Equal(expected, JavaDetector.MajorFromVersionString(version));
    }

    [Fact]
    public void FindBest_PrefersClosestMatch()
    {
        var candidates = new List<JavaInfo>
        {
            new() { Path = "j8", MajorVersion = 8, Is64Bit = true },
            new() { Path = "j17", MajorVersion = 17, Is64Bit = true },
            new() { Path = "j21", MajorVersion = 21, Is64Bit = true }
        };

        var best = JavaDetector.FindBest(candidates, 17);
        Assert.Equal("j17", best?.Path);
    }

    [Fact]
    public void FindBest_Ignoring32Bit()
    {
        var candidates = new List<JavaInfo>
        {
            new() { Path = "j17-32", MajorVersion = 17, Is64Bit = false }
        };
        Assert.Null(JavaDetector.FindBest(candidates, 17));
    }

    [Fact]
    public void Select_OverridesMismatchedManualJavaWithBestDetected()
    {
        // 用户手动指定 Java 25，但本机另装了 Java 17；1.20.1 需要 Java 17，应自动选 17。
        var detected = new List<JavaInfo>
        {
            new() { Path = "j25", MajorVersion = 25, Is64Bit = true },
            new() { Path = "j17", MajorVersion = 17, Is64Bit = true }
        };
        var configured = new JavaInfo { Path = "j25", MajorVersion = 25, Is64Bit = true };

        var selected = JavaDetector.Select(detected, configured, 17);

        Assert.Equal("j17", selected.Path);
    }

    [Fact]
    public void Select_ManualJavaOutsideScanStillParticipates()
    {
        // 手动指定的自定义路径不在扫描结果里，且最匹配，应被选中。
        var detected = new List<JavaInfo>
        {
            new() { Path = "j25", MajorVersion = 25, Is64Bit = true }
        };
        var configured = new JavaInfo { Path = "D:\\custom\\jdk17\\bin\\java.exe", MajorVersion = 17, Is64Bit = true };

        var selected = JavaDetector.Select(detected, configured, 17);

        Assert.Equal("D:\\custom\\jdk17\\bin\\java.exe", selected.Path);
    }

    [Fact]
    public void Select_UsesManualJavaWhenOnlyCandidate()
    {
        var configured = new JavaInfo { Path = "j21", MajorVersion = 21, Is64Bit = true };
        var selected = JavaDetector.Select([], configured, 21);
        Assert.Equal("j21", selected.Path);
    }

    [Fact]
    public void Select_ThrowsWhenNoJavaAvailable()
    {
        Assert.Throws<InvalidOperationException>(() => JavaDetector.Select([], null, 21));
    }

    [Fact]
    public void Detect_WithInvalidAdditionalCandidates_DoesNotThrowAndFilters()
    {
        var detector = new JavaDetector([
            @"Z:\nonexistent\java.exe",
            Path.Combine(Path.GetTempPath(), "not-a-java.exe")
        ]);

        List<JavaInfo> result = detector.Detect();

        // 无效候选被探测过滤，不得进入结果，也不得抛异常。
        Assert.All(result, j => Assert.NotEqual(@"Z:\nonexistent\java.exe", j.Path));
    }

    [Fact]
    public void Detect_WithGarbageExecutable_IsFilteredOut()
    {
        // 内容非法的 java.exe 无法运行，探测失败后被静默过滤。
        string tempDir = Path.Combine(Path.GetTempPath(), "sdsl_javadet_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        string fakeJava = Path.Combine(tempDir, "java.exe");
        File.WriteAllText(fakeJava, "not an executable");
        try
        {
            var detector = new JavaDetector(new[] { fakeJava });

            List<JavaInfo> result = detector.Detect();

            Assert.All(result, j => Assert.NotEqual(fakeJava, j.Path));
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { }
        }
    }
}
