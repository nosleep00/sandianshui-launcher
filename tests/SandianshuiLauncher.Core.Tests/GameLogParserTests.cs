using SandianshuiLauncher.Core.Models;
using SandianshuiLauncher.Core.Services;
using Xunit;

namespace SandianshuiLauncher.Core.Tests;

public class GameLogParserTests
{
    private const string StandardLine =
        "[0411/2026 10:01:47.005] [main/INFO] [cpw.mods.modlauncher.Launcher/MODLAUNCHER]: ModLauncher running";

    [Fact]
    public void ParseLine_StandardLine_ParsesAllFields()
    {
        GameLogEntry entry = GameLogParser.ParseLine(StandardLine);

        Assert.Equal("0411/2026 10:01:47.005", entry.Timestamp);
        Assert.Equal("main", entry.Thread);
        Assert.Equal(GameLogLevel.Info, entry.Level);
        Assert.Equal("cpw.mods.modlauncher.Launcher/MODLAUNCHER", entry.Logger);
        Assert.Equal("ModLauncher running", entry.Message);
        Assert.Equal(StandardLine, entry.Raw);
    }

    [Theory]
    [InlineData("TRACE", GameLogLevel.Trace)]
    [InlineData("DEBUG", GameLogLevel.Debug)]
    [InlineData("INFO", GameLogLevel.Info)]
    [InlineData("WARN", GameLogLevel.Warn)]
    [InlineData("ERROR", GameLogLevel.Error)]
    [InlineData("FATAL", GameLogLevel.Fatal)]
    public void ParseLine_AllLevels_Parsed(string levelText, GameLogLevel expected)
    {
        string line = $"[10:00:00] [Render thread/{levelText}] [com.example/]: a message";

        GameLogEntry entry = GameLogParser.ParseLine(line);

        Assert.Equal(expected, entry.Level);
        Assert.Equal("Render thread", entry.Thread);
        Assert.Equal("a message", entry.Message);
    }

    [Fact]
    public void ParseLine_LineWithoutLogger_StillParsed()
    {
        string line = "[10:00:00] [main/INFO] some plain message";

        GameLogEntry entry = GameLogParser.ParseLine(line);

        Assert.Equal(GameLogLevel.Info, entry.Level);
        Assert.Equal("main", entry.Thread);
        Assert.Empty(entry.Logger);
        Assert.Equal("some plain message", entry.Message);
    }

    [Fact]
    public void ParseLine_StackFrame_UnknownLevel()
    {
        string line = "\tat org.lwjgl.system.Library.loadSystem(Library.java:177)";

        GameLogEntry entry = GameLogParser.ParseLine(line);

        Assert.Equal(GameLogLevel.Unknown, entry.Level);
        Assert.Equal(line, entry.Message);
    }

    [Fact]
    public void ParseLine_EmptyLine_UnknownLevel()
    {
        GameLogEntry entry = GameLogParser.ParseLine(string.Empty);

        Assert.Equal(GameLogLevel.Unknown, entry.Level);
        Assert.Empty(entry.Message);
    }

    [Fact]
    public void ParseLine_Null_DoesNotThrow()
    {
        GameLogEntry entry = GameLogParser.ParseLine(null!);

        Assert.Equal(GameLogLevel.Unknown, entry.Level);
    }

    [Fact]
    public void IsError_ErrorAndFatal_TrueOnlyForErrors()
    {
        var error = GameLogParser.ParseLine("[t] [th/ERROR] [l]: x");
        var fatal = GameLogParser.ParseLine("[t] [th/FATAL] [l]: x");
        var warn = GameLogParser.ParseLine("[t] [th/WARN] [l]: x");

        Assert.True(error.IsError);
        Assert.True(fatal.IsError);
        Assert.False(warn.IsError);
    }

    [Fact]
    public void ParseLevel_InvalidValue_Unknown()
    {
        Assert.Equal(GameLogLevel.Unknown, GameLogParser.ParseLevel("BOGUS"));
        Assert.Equal(GameLogLevel.Unknown, GameLogParser.ParseLevel(null));
    }

    [Fact]
    public void Parse_MultilineWithCrlf_SplitsAllRows()
    {
        string text = "[t1] [main/INFO] [l/]: first\r\n[t2] [main/WARN] [l/]: second\r\n";

        List<GameLogEntry> entries = GameLogParser.Parse(text);

        Assert.Equal(2, entries.Count);
        Assert.Equal("first", entries[0].Message);
        Assert.Equal(GameLogLevel.Warn, entries[1].Level);
    }

    [Fact]
    public void Parse_EmptyText_ReturnsEmpty()
    {
        Assert.Empty(GameLogParser.Parse(string.Empty));
        Assert.Empty(GameLogParser.ParseLines([]));
    }
}
