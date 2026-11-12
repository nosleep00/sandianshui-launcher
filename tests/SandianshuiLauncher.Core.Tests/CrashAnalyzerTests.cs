using SandianshuiLauncher.Core.Models;
using SandianshuiLauncher.Core.Services;
using Xunit;

namespace SandianshuiLauncher.Core.Tests;

public class CrashAnalyzerTests
{
    [Theory]
    [InlineData("java.lang.OutOfMemoryError: Java heap space", "out_of_memory_heap")]
    [InlineData("Could not reserve enough space for 3145728KB object heap", "heap_reserve_failed")]
    [InlineData("java.lang.UnsupportedClassVersionError: some package has been compiled by a more recent version", "java_version_mismatch")]
    [InlineData("# EXCEPTION_ACCESS_VIOLATION (0xc0000005) at pc=0x0000", "jvm_native_crash")]
    [InlineData("java.lang.UnsatisfiedLinkError: Failed to locate library: lwjgl.dll", "native_library")]
    [InlineData("GLFW error 65543: WGL: OpenGL context creation failed", "graphics_driver")]
    [InlineData("Missing or unsupported mandatory dependencies:", "mod_missing_dependency")]
    [InlineData("net.minecraftforge.fml.loading.moddiscovery.DuplicateModsException: found duplicate mod", "mod_duplicate")]
    [InlineData("Mixin apply for mod foo failed : InjectionError", "mod_mixin_failure")]
    [InlineData("net.minecraftforge.fml.ModLoadingException: Loading failed", "mod_load_failure")]
    [InlineData("java.net.BindException: Address already in use", "port_in_use")]
    [InlineData("java.io.FileNotFoundException: file (Access is denied)", "file_locked")]
    public void Analyze_CommonCrashes_MatchesExpectedRule(string logLine, string expectedRuleId)
    {
        CrashDiagnosis diagnosis = CrashAnalyzer.Analyze(logLine);

        Assert.Equal(expectedRuleId, diagnosis.RuleId);
        Assert.False(string.IsNullOrWhiteSpace(diagnosis.Title));
        Assert.NotEmpty(diagnosis.Suggestions);
        Assert.False(string.IsNullOrWhiteSpace(diagnosis.Detail));
    }

    [Fact]
    public void Analyze_MatchIsCaseInsensitive()
    {
        CrashDiagnosis diagnosis = CrashAnalyzer.Analyze("OUTOFMEMORYERROR: JAVA HEAP SPACE");

        Assert.Equal("out_of_memory_heap", diagnosis.RuleId);
    }

    [Fact]
    public void Analyze_ForgeMissingDependency_ExtractsModId()
    {
        string text =
            "Missing or unsupported mandatory dependencies:\n" +
            "\tMod ID: 'glitchcore', Requested by: 'biomesoplenty', Expected range: [1.0.0.51,)";

        CrashDiagnosis diagnosis = CrashAnalyzer.Analyze(text);

        Assert.Equal("mod_missing_dependency", diagnosis.RuleId);
        Assert.Equal("glitchcore", diagnosis.RelatedMod);
    }

    [Fact]
    public void Analyze_FabricMissingDependency_ExtractsModId()
    {
        CrashDiagnosis diagnosis = CrashAnalyzer.Analyze(
            "net.fabricmc.loader.impl.FormattedException: requires mod 'fabric-api', any version");

        Assert.Equal("mod_missing_dependency", diagnosis.RuleId);
        Assert.Equal("fabric-api", diagnosis.RelatedMod);
    }

    [Fact]
    public void Analyze_UnrecognizedText_ReturnsFallback()
    {
        CrashDiagnosis diagnosis = CrashAnalyzer.Analyze("some totally normal and unrelated log line");

        Assert.Equal("unknown", diagnosis.RuleId);
        Assert.NotEmpty(diagnosis.Suggestions);
        Assert.Empty(diagnosis.MatchedLine);
    }

    [Fact]
    public void Analyze_NullOrEmpty_ReturnsFallback()
    {
        Assert.Equal("unknown", CrashAnalyzer.Analyze(null).RuleId);
        Assert.Equal("unknown", CrashAnalyzer.Analyze(string.Empty).RuleId);
    }

    [Fact]
    public void AnalyzeAll_MultipleProblems_ReturnsAllInPriorityOrder()
    {
        string text =
            "java.net.BindException: Address already in use\n" +
            "java.lang.OutOfMemoryError: Java heap space";

        List<CrashDiagnosis> all = CrashAnalyzer.AnalyzeAll(text);

        Assert.Equal(2, all.Count);
        // 内存不足规则在规则库中排在端口占用之前。
        Assert.Equal("out_of_memory_heap", all[0].RuleId);
        Assert.Equal("port_in_use", all[1].RuleId);
    }

    [Fact]
    public void AnalyzeAll_NoMatch_Empty()
    {
        Assert.Empty(CrashAnalyzer.AnalyzeAll("nothing suspicious here"));
    }

    [Fact]
    public void Severity_FatalAndError_ClassifiedCorrectly()
    {
        Assert.Equal(CrashSeverity.Fatal, CrashAnalyzer.Analyze("OutOfMemoryError: Java heap space").Severity);
        Assert.Equal(CrashSeverity.Warning, CrashAnalyzer.Analyze("BindException: Address already in use").Severity);
    }

    [Fact]
    public void SuggestionsText_NumberedAndJoined()
    {
        CrashDiagnosis diagnosis = CrashAnalyzer.Analyze("Address already in use");

        string text = diagnosis.SuggestionsText;
        Assert.StartsWith("1. ", text);
        Assert.Contains(Environment.NewLine, text);
    }

    [Fact]
    public void CreateNoLogDiagnosis_HasActionableSuggestions()
    {
        CrashDiagnosis diagnosis = CrashAnalyzer.CreateNoLogDiagnosis();

        Assert.Equal("no_game_log", diagnosis.RuleId);
        Assert.Equal(CrashSeverity.Error, diagnosis.Severity);
        Assert.Contains(diagnosis.Suggestions, s => s.Contains("Java"));
        Assert.Contains(diagnosis.Suggestions, s => s.Contains("内存"));
    }

    [Fact]
    public void ReadLatestLogFor_IsolatedInstanceNewer_PrefersInstanceLog()
    {
        string gameDir = Path.Combine(Path.GetTempPath(), "sds_crash_" + Guid.NewGuid().ToString("N"));
        try
        {
            string rootLog = Path.Combine(gameDir, "logs", "latest.log");
            string instanceLog = Path.Combine(gameDir, "versions", "1.20.1", "logs", "latest.log");
            Directory.CreateDirectory(Path.GetDirectoryName(rootLog)!);
            Directory.CreateDirectory(Path.GetDirectoryName(instanceLog)!);
            File.WriteAllText(rootLog, "旧日志");
            File.SetLastWriteTime(rootLog, DateTime.Now.AddDays(-3));
            File.WriteAllText(instanceLog, "新日志：OutOfMemoryError");
            File.SetLastWriteTime(instanceLog, DateTime.Now);

            string text = CrashAnalyzer.ReadLatestLogFor(gameDir, "1.20.1", versionIsolation: true);

            Assert.Contains("新日志", text);
        }
        finally
        {
            try { Directory.Delete(gameDir, true); } catch { }
        }
    }

    [Fact]
    public void ReadLatestLogFor_InstanceLogMissing_FallsBackToRoot()
    {
        string gameDir = Path.Combine(Path.GetTempPath(), "sds_crash_" + Guid.NewGuid().ToString("N"));
        try
        {
            string rootLog = Path.Combine(gameDir, "logs", "latest.log");
            Directory.CreateDirectory(Path.GetDirectoryName(rootLog)!);
            File.WriteAllText(rootLog, "根目录日志");

            string text = CrashAnalyzer.ReadLatestLogFor(gameDir, "1.20.1", versionIsolation: true);

            Assert.Contains("根目录日志", text);
        }
        finally
        {
            try { Directory.Delete(gameDir, true); } catch { }
        }
    }

    [Fact]
    public void ReadLatestLogFor_IsolationOff_AlwaysRoot()
    {
        string gameDir = Path.Combine(Path.GetTempPath(), "sds_crash_" + Guid.NewGuid().ToString("N"));
        try
        {
            string rootLog = Path.Combine(gameDir, "logs", "latest.log");
            string instanceLog = Path.Combine(gameDir, "versions", "1.20.1", "logs", "latest.log");
            Directory.CreateDirectory(Path.GetDirectoryName(rootLog)!);
            Directory.CreateDirectory(Path.GetDirectoryName(instanceLog)!);
            File.WriteAllText(rootLog, "根目录日志");
            File.WriteAllText(instanceLog, "实例日志（应被忽略）");
            File.SetLastWriteTime(instanceLog, DateTime.Now);

            string text = CrashAnalyzer.ReadLatestLogFor(gameDir, "1.20.1", versionIsolation: false);

            Assert.Contains("根目录日志", text);
        }
        finally
        {
            try { Directory.Delete(gameDir, true); } catch { }
        }
    }
}
