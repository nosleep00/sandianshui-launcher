using SandianshuiLauncher.Core.Utils;
using Xunit;

namespace SandianshuiLauncher.Core.Tests;

/// <summary>
/// <see cref="CommandLineTokenizer"/> 测试：
/// 验证带引号、转义、连续空白与中文路径的命令行分词结果。
/// </summary>
public class CommandLineTokenizerTests
{
    [Fact]
    public void Split_PlainTokens_SplitsByWhitespace()
    {
        List<string> tokens = CommandLineTokenizer.Split("a b  c");

        Assert.Equal(["a", "b", "c"], tokens);
    }

    [Fact]
    public void Split_QuotedTokenWithSpaces_KeepsAsOneToken()
    {
        List<string> tokens = CommandLineTokenizer.Split("-Dfoo=\"hello world\" --bar");

        Assert.Equal(["-Dfoo=hello world", "--bar"], tokens);
    }

    [Fact]
    public void Split_MultipleSpacesInsideQuotes_ArePreserved()
    {
        List<string> tokens = CommandLineTokenizer.Split("\"a  b   c\"");

        Assert.Equal(["a  b   c"], tokens);
    }

    [Fact]
    public void Split_EmptyQuotes_ProducesEmptyToken()
    {
        List<string> tokens = CommandLineTokenizer.Split("--flag \"\" tail");

        Assert.Equal(["--flag", "", "tail"], tokens);
    }

    [Fact]
    public void Split_EscapedQuote_KeepsQuoteInToken()
    {
        List<string> tokens = CommandLineTokenizer.Split("\"say \\\"hi\\\" now\"");

        Assert.Equal(["say \"hi\" now"], tokens);
    }

    [Fact]
    public void Split_UnterminatedQuote_ToleratesToEnd()
    {
        List<string> tokens = CommandLineTokenizer.Split("-Dfoo=\"a b");

        Assert.Equal(["-Dfoo=a b"], tokens);
    }

    [Fact]
    public void Split_ChinesePathWithSpaces_StaysInOneToken()
    {
        List<string> tokens = CommandLineTokenizer.Split("--gameDir \"D:\\我的 游戏 目录\" --demo");

        Assert.Equal(["--gameDir", "D:\\我的 游戏 目录", "--demo"], tokens);
    }

    [Fact]
    public void Split_ConsecutiveWhitespaceAndTabs_AreSkipped()
    {
        List<string> tokens = CommandLineTokenizer.Split("a\t\tb \t c");

        Assert.Equal(["a", "b", "c"], tokens);
    }

    [Fact]
    public void Split_EmptyOrWhitespace_ReturnsEmpty()
    {
        Assert.Empty(CommandLineTokenizer.Split(string.Empty));
        Assert.Empty(CommandLineTokenizer.Split("   \t "));
    }
}
