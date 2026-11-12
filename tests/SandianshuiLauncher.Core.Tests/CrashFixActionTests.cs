using SandianshuiLauncher.Core.Models;
using SandianshuiLauncher.Core.Services;
using Xunit;

namespace SandianshuiLauncher.Core.Tests;

/// <summary>
/// 崩溃诊断的一键修复动作测试：各规则产出对应 <see cref="CrashFixAction"/>，
/// 缺少依赖时从日志提取缺失模组标识作为修复参数。
/// </summary>
public class CrashFixActionTests
{
    [Fact]
    public void OutOfMemory_ProducesIncreaseMemory()
    {
        CrashDiagnosis diagnosis = CrashAnalyzer.Analyze("java.lang.OutOfMemoryError: Java heap space");

        Assert.Equal(CrashFixAction.IncreaseMemory, diagnosis.FixAction);
    }

    [Fact]
    public void HeapReserveFailed_ProducesDecreaseMemory()
    {
        CrashDiagnosis diagnosis = CrashAnalyzer.Analyze("Could not reserve enough space for 4194304KB object heap");

        Assert.Equal(CrashFixAction.DecreaseMemory, diagnosis.FixAction);
    }

    [Fact]
    public void JavaVersionMismatch_ProducesAutoSelectJava()
    {
        CrashDiagnosis diagnosis = CrashAnalyzer.Analyze(
            "UnsupportedClassVersionError: ... has been compiled by a more recent version of the Java Runtime");

        Assert.Equal(CrashFixAction.AutoSelectJava, diagnosis.FixAction);
    }

    [Fact]
    public void NativeLibraryMissing_ProducesReinstallVersion()
    {
        CrashDiagnosis diagnosis = CrashAnalyzer.Analyze(
            "java.lang.UnsatisfiedLinkError: no lwjgl in java.library.path");

        Assert.Equal(CrashFixAction.ReinstallVersion, diagnosis.FixAction);
    }

    [Fact]
    public void MissingDependency_ForgeFormat_ExtractsModIdAndFixAction()
    {
        const string log = """
        [main/ERROR]: Missing or unsupported mandatory dependencies:
            Mod ID: 'cloth_config', Requested by: 'betterf3', Expected range: [13.0.0,14.0.0), Actual version: [MISSING]
        """;

        CrashDiagnosis diagnosis = CrashAnalyzer.Analyze(log);

        Assert.Equal(CrashFixAction.InstallMissingMod, diagnosis.FixAction);
        Assert.Equal("cloth_config", diagnosis.FixTarget);
    }

    [Fact]
    public void MissingDependency_FabricFormat_ExtractsModId()
    {
        const string log = "[main/ERROR]: Could not find required mod: sodium requires {minecraft @ [~1.20.4]}";

        CrashDiagnosis diagnosis = CrashAnalyzer.Analyze(log);

        Assert.Equal(CrashFixAction.InstallMissingMod, diagnosis.FixAction);
        Assert.Equal("sodium", diagnosis.FixTarget);
    }

    [Fact]
    public void MissingDependency_NoExtractableModId_NoAutoFix()
    {
        const string log = "Missing or unsupported mandatory dependencies"; // 无具体 Mod ID

        CrashDiagnosis diagnosis = CrashAnalyzer.Analyze(log);

        Assert.Equal(CrashFixAction.None, diagnosis.FixAction);
    }

    [Fact]
    public void MissingDependency_LoaderItself_ProducesUpgradeLoader()
    {
        const string log = """
        Missing or unsupported mandatory dependencies:
        Mod ID: 'forge', Requested by: 'runiclib', Expected range: '[47.1.3,)', Actual version: '47.0.1'
        """;

        CrashDiagnosis diagnosis = CrashAnalyzer.Analyze(log);

        Assert.Equal(CrashFixAction.UpgradeLoader, diagnosis.FixAction);
        Assert.Equal("forge", diagnosis.FixTarget);
        Assert.Equal("[47.1.3,)", diagnosis.FixVersionRange);
    }

    [Fact]
    public void MissingDependency_ForgeFormat_ExtractsExpectedRange()
    {
        const string log = """
        Missing or unsupported mandatory dependencies:
            Mod ID: 'forge', Requested by: 'chloride', Expected range: '[47.3.0,)', Actual version: '47.0.1'
        """;

        CrashDiagnosis diagnosis = CrashAnalyzer.Analyze(log);

        Assert.Equal("[47.3.0,)", diagnosis.FixVersionRange);
    }

    [Fact]
    public void AgeOfMythology_CatalogFailure_IsDiagnosed()
    {
        const string log = """
        java.lang.IllegalStateException: Parallel-world catalog reload candidate was not prepared
        	at com.kurome.ageofmythology.infinite_dimension.catalog.lifecycle.ParallelWorldCatalogLifecycle.onServerStarting(ParallelWorldCatalogLifecycle.java:94) ~[ageofmythology-0.3.1-all.jar%23388!/:0.3.1]
        """;

        CrashDiagnosis diagnosis = CrashAnalyzer.Analyze(log);

        Assert.Equal("mod_age_of_mythology_catalog", diagnosis.RuleId);
        Assert.Equal(CrashFixAction.None, diagnosis.FixAction); // 需手动处理，不自动修
        Assert.Equal("ageofmythology-0.3.1", diagnosis.RelatedMod);
    }

    [Theory]
    [InlineData("forge", true)]
    [InlineData("neoforge", true)]
    [InlineData("fabric", true)]
    [InlineData("fabricloader", true)]
    [InlineData("quilt", true)]
    [InlineData("cloth_config", false)]
    [InlineData("sodium", false)]
    public void IsLoaderId_Detection(string modId, bool expected)
    {
        Assert.Equal(expected, ModLoaderDetector.IsLoaderId(modId));
    }
}
