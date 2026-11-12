using SandianshuiLauncher.Services;

namespace SandianshuiLauncher.App.Tests;

/// <summary>
/// 注册表扫描器冒烟测试：任何注册表状态（含无 Java 环境）都不应抛异常。
/// </summary>
public class JavaRegistryScannerTests
{
    [Fact]
    public void CollectCandidates_NeverThrows()
    {
        List<string> candidates = JavaRegistryScanner.CollectCandidates();

        Assert.NotNull(candidates);
    }
}
