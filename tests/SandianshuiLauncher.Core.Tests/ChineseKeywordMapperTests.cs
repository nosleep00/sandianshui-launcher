using SandianshuiLauncher.Core.Services;
using Xunit;

namespace SandianshuiLauncher.Core.Tests;

/// <summary>
/// <see cref="ChineseKeywordMapper"/> 测试：中文→英文候选展开与兜底行为。
/// </summary>
public class ChineseKeywordMapperTests
{
    [Fact]
    public void Expand_PureChineseWord_ReturnsEnglishCandidatesFirst()
    {
        List<string> candidates = ChineseKeywordMapper.Expand("小地图");

        Assert.Equal(["minimap", "小地图"], candidates);
    }

    [Fact]
    public void Expand_MixedText_ReplacesKnownWord()
    {
        List<string> candidates = ChineseKeywordMapper.Expand("更好的小地图");

        Assert.Contains("更好的minimap", candidates);
        Assert.Equal("更好的小地图", candidates[^1]);
    }

    [Fact]
    public void Expand_UnknownChinese_ReturnsOriginalOnly()
    {
        List<string> candidates = ChineseKeywordMapper.Expand("赛博朋克");

        Assert.Equal(["赛博朋克"], candidates);
    }

    [Fact]
    public void Expand_PureEnglish_Unchanged()
    {
        Assert.Equal(["minimap"], ChineseKeywordMapper.Expand("minimap"));
    }

    [Fact]
    public void Expand_MultipleKnownWords_ProducesCombinations()
    {
        List<string> candidates = ChineseKeywordMapper.Expand("自动存储");

        Assert.Contains("automation存储", candidates);
        Assert.Contains("自动storage", candidates);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Expand_Blank_ReturnsEmpty(string? query)
    {
        Assert.Empty(ChineseKeywordMapper.Expand(query));
    }

    [Theory]
    [InlineData("小地图", true)]
    [InlineData("minimap", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void ContainsChinese_Detection(string? text, bool expected)
    {
        Assert.Equal(expected, ChineseKeywordMapper.ContainsChinese(text));
    }
}
