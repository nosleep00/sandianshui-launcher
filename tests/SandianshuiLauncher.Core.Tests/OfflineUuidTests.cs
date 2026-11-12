using SandianshuiLauncher.Core.Utils;
using Xunit;

namespace SandianshuiLauncher.Core.Tests;

public class OfflineUuidTests
{
    // 以下期望值由 Python 独立计算（Java UUID.nameUUIDFromBytes 等价算法）。
    [Theory]
    [InlineData("Player", "a01e3843-e521-3998-958a-f459800e4d11")]
    [InlineData("Steve", "5627dd98-e6be-3c21-b8a8-e92344183641")]
    [InlineData("42", "ee28158b-e78a-3da5-8f9d-0e0128585792")]
    public void Generate_MatchesJavaNameUuidFromBytes(string username, string expected)
    {
        Assert.Equal(expected, OfflineUuid.Generate(username));
    }

    [Fact]
    public void Generate_IsDeterministic()
    {
        Assert.Equal(OfflineUuid.Generate("Steve"), OfflineUuid.Generate("Steve"));
    }

    [Fact]
    public void Generate_HasVersion3AndIetfVariant()
    {
        string uuid = OfflineUuid.Generate("Player");

        // 形如 8-4-4-4-12。
        Assert.Equal(36, uuid.Length);
        Assert.Equal('-', uuid[8]);
        Assert.Equal('-', uuid[13]);
        Assert.Equal('-', uuid[18]);
        Assert.Equal('-', uuid[23]);

        // 第三段以 3 开头（version 3 = MD5 命名空间）。
        Assert.Equal('3', uuid[14]);

        // 第四段首字符为 8/9/a/b（IETF variant）。
        Assert.Contains(uuid[19], new[] { '8', '9', 'a', 'b' });
    }

    [Fact]
    public void GenerateDigits_Returns32LowercaseHexWithoutDashes()
    {
        string digits = OfflineUuid.GenerateDigits("Player");
        Assert.Equal(32, digits.Length);
        Assert.DoesNotContain('-', digits);
        Assert.Matches("^[0-9a-f]{32}$", digits);
    }

    [Fact]
    public void Generate_EmptyName_Throws()
    {
        Assert.Throws<ArgumentException>(() => OfflineUuid.Generate("   "));
    }
}
