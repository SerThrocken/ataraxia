using System.Linq;
using System.Text;
using Hermes.Client.Core.Terminal;
using Xunit;

namespace Hermes.Client.Core.Tests.Terminal;

/// <summary>
/// Screen buffer + ANSI parser (<c>TerminalScreen</c>): text layout, cursor
/// motion, erase, SGR colors, scrolling with scrollback, alt-screen, resize.
/// </summary>
public sealed class TerminalScreenTests
{
    private static TerminalScreen Screen(int cols = 10, int rows = 4) => new(cols, rows);

    private static void Write(TerminalScreen s, string text) =>
        s.Process(Encoding.UTF8.GetBytes(text));

    [Fact]
    public void PlainText_LandsOnFirstLine()
    {
        var s = Screen();
        Write(s, "hello");
        Assert.Equal("hello", s.GetLineText(0));
        Assert.Equal(5, s.CursorCol);
    }

    [Fact]
    public void CrLf_MovesToNextLineStart()
    {
        var s = Screen();
        Write(s, "ab\r\ncd");
        Assert.Equal("ab", s.GetLineText(0));
        Assert.Equal("cd", s.GetLineText(1));
    }

    [Fact]
    public void Lf_Alone_MovesDown_KeepingColumn()
    {
        var s = Screen();
        Write(s, "ab\ncd");
        Assert.Equal("  cd", s.GetLineText(1)); // column preserved (no CR)
    }

    [Fact]
    public void Backspace_ErasesPreviousChar()
    {
        var s = Screen();
        Write(s, "ab\x08" + "c"); // \x08 must not run into the 'c' (C# \x takes up to 4 hex digits)
        Assert.Equal("ac", s.GetLineText(0));
    }

    [Fact]
    public void Text_WrapsAtRightMargin()
    {
        var s = Screen(cols: 4, rows: 4);
        Write(s, "abcdef");
        Assert.Equal("abcd", s.GetLineText(0));
        Assert.Equal("ef", s.GetLineText(1));
    }

    [Fact]
    public void CursorPositioning_MovesCursor()
    {
        var s = Screen();
        Write(s, "\x1b[3;5HX");
        Assert.Equal(2, s.CursorRow);
        Assert.Equal(5, s.CursorCol);
        Assert.Equal("    X", s.GetLineText(2));
    }

    [Fact]
    public void CursorUpDownLeftRight_MoveRelatively()
    {
        var s = Screen();
        Write(s, "\x1b[3;3H\x1b[2A\x1b[2B\x1b[2C\x1b[2D");
        Assert.Equal(2, s.CursorRow);
        Assert.Equal(2, s.CursorCol);
    }

    [Fact]
    public void Cursor_ClampsToScreen()
    {
        var s = Screen(cols: 10, rows: 4);
        Write(s, "\x1b[99;99H");
        Assert.Equal(3, s.CursorRow);
        Assert.Equal(9, s.CursorCol);
    }

    [Fact]
    public void EraseDisplay_ClearsFromCursor()
    {
        var s = Screen();
        Write(s, "hello\x1b[1;3H\x1b[J");
        Assert.Equal("he", s.GetLineText(0));
    }

    [Fact]
    public void EraseDisplay_All_ClearsScreen()
    {
        var s = Screen();
        Write(s, "hello\r\nworld\x1b[2J");
        Assert.Equal("", s.GetLineText(0));
        Assert.Equal("", s.GetLineText(1));
    }

    [Fact]
    public void EraseLine_ClearsCurrentLine()
    {
        var s = Screen();
        Write(s, "hello\x1b[2K");
        Assert.Equal("", s.GetLineText(0));
    }

    [Fact]
    public void Sgr_ColorsProduceStyledRuns()
    {
        var s = Screen(cols: 20, rows: 4);
        Write(s, "\x1b[31mred\x1b[0m plain");
        var runs = s.GetLine(0);
        Assert.Equal(2, runs.Count);
        Assert.Equal("red", runs[0].Text);
        Assert.Equal(0xFFCD0000u, runs[0].ForegroundArgb); // xterm red
        Assert.Equal(" plain", runs[1].Text);
        Assert.Equal(TerminalScreen.DefaultForegroundArgb, runs[1].ForegroundArgb);
    }

    [Fact]
    public void Sgr256_And_Truecolor_Resolve()
    {
        var s = Screen(cols: 20, rows: 4);
        Write(s, "\x1b[38;5;196mX\r\n\x1b[38;2;1;2;3mY");
        Assert.Equal(0xFFFF0000u, s.GetLine(0)[0].ForegroundArgb); // 256-color red
        Assert.Equal(0xFF010203u, s.GetLine(1)[0].ForegroundArgb); // truecolor
    }

    [Fact]
    public void Inverse_SwapsFgAndBg()
    {
        var s = Screen(cols: 20, rows: 4);
        Write(s, "\x1b[7mX");
        var run = s.GetLine(0)[0];
        Assert.Equal(TerminalScreen.DefaultForegroundArgb, run.BackgroundArgb);
    }

    [Fact]
    public void Scrolling_PushesLinesIntoScrollback()
    {
        var s = Screen(cols: 10, rows: 2);
        Write(s, "one\r\ntwo\r\nthree\r\n");
        Assert.Equal(2, s.ScrollbackLineCount);
        Assert.Equal("one", s.GetLineText(0));
        Assert.Equal("two", s.GetLineText(1));
        Assert.Equal("three", s.GetLineText(2));
    }

    [Fact]
    public void Scrollback_IsCapped()
    {
        var s = Screen(cols: 10, rows: 2);
        for (var i = 0; i < TerminalScreen.MaxScrollbackLines + 50; i++)
        {
            Write(s, $"l{i}\r\n");
        }

        Assert.Equal(TerminalScreen.MaxScrollbackLines, s.ScrollbackLineCount);
    }

    [Fact]
    public void AltScreen_SwitchesBuffers_AndRestores()
    {
        var s = Screen();
        Write(s, "main");
        Write(s, "\x1b[?1049h");
        Assert.True(s.InAltScreen);
        Write(s, "alt");
        Assert.Equal("alt", s.GetLineText(0));
        Write(s, "\x1b[?1049l");
        Assert.False(s.InAltScreen);
        Assert.Equal("main", s.GetLineText(0));
    }

    [Fact]
    public void OscTitle_IsCaptured()
    {
        var s = Screen();
        Write(s, "\x1b]0;my-title\x07");
        Assert.Equal("my-title", s.Title);
    }

    [Fact]
    public void OscTitle_StTerminated_IsCaptured()
    {
        var s = Screen();
        Write(s, "\x1b]2;st-title\x1b\\");
        Assert.Equal("st-title", s.Title);
    }

    [Fact]
    public void Resize_PreservesOverlappingContent()
    {
        var s = Screen(cols: 10, rows: 4);
        Write(s, "hello\r\nworld");
        s.Resize(20, 6);
        Assert.Equal(20, s.Cols);
        Assert.Equal(6, s.Rows);
        Assert.Equal("hello", s.GetLineText(0));
        Assert.Equal("world", s.GetLineText(1));
    }

    [Fact]
    public void DirtyTracking_ReportsChangedLinesOnce()
    {
        var s = Screen();
        _ = s.TakeDirtyLines(); // drain initial state (none expected)
        Write(s, "hi");
        var dirty = s.TakeDirtyLines();
        Assert.Contains(0, dirty);
        Assert.Empty(s.TakeDirtyLines());
    }

    [Fact]
    public void InsertAndDeleteLines_ShiftContent()
    {
        var s = Screen(cols: 10, rows: 4);
        Write(s, "a\r\nb\r\nc\r\n\x1b[1;1H\x1b[2L");
        Assert.Equal("", s.GetLineText(0));
        Assert.Equal("", s.GetLineText(1));
        Assert.Equal("a", s.GetLineText(2));
        Write(s, "\x1b[1;1H\x1b[2M");
        Assert.Equal("a", s.GetLineText(0));
    }

    [Fact]
    public void CursorVisibility_Toggles()
    {
        var s = Screen();
        Assert.True(s.CursorVisible);
        Write(s, "\x1b[?25l");
        Assert.False(s.CursorVisible);
        Write(s, "\x1b[?25h");
        Assert.True(s.CursorVisible);
    }

    [Fact]
    public void SplitUtf8_AcrossProcessCalls_Decodes()
    {
        var s = Screen(cols: 20, rows: 4);
        var bytes = Encoding.UTF8.GetBytes("é");
        s.Process(bytes.AsSpan(0, 1));
        s.Process(bytes.AsSpan(1, 1));
        Assert.Equal("é", s.GetLineText(0));
    }

    [Fact]
    public void AutoWrap_Off_OverwritesLastColumn()
    {
        var s = Screen(cols: 4, rows: 4);
        Write(s, "\x1b[?7labcde");
        Assert.Equal("abce", s.GetLineText(0));
    }

    [Fact]
    public void GetLine_OutOfRange_ReturnsEmpty()
    {
        var s = Screen();
        Assert.Empty(s.GetLine(-1));
        Assert.Empty(s.GetLine(999));
        Assert.Equal("", s.GetLineText(999));
    }
}
