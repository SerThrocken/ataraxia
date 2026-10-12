using System;
using System.Collections.Generic;
using System.Text;

namespace Hermes.Client.Core.Terminal;

/// <summary>
/// One styled run of text on a terminal line. Colors are raw ARGB uints so
/// this stays UI-free; the view turns them into brushes. Inverse is already
/// resolved (fg/bg swapped) when the run is built.
/// </summary>
public readonly record struct TerminalTextRun(
    string Text,
    uint ForegroundArgb,
    uint BackgroundArgb,
    bool Bold,
    bool Underline);

/// <summary>
/// VT100/xterm screen model: grid of cells with attributes, an alternate
/// screen buffer, a capped scrollback ring, and an ANSI/VT parser covering
/// the subset interactive shells and full-screen apps (vim, less, tmux)
/// actually emit: cursor motion, erase, SGR colors (16/256/truecolor),
/// insert/delete lines and chars, scroll regions, alt-screen, titles.
/// UI-free: the WinUI view renders <see cref="GetLine"/> runs.
/// Not thread-safe by itself — <see cref="PtySession"/> serializes access.
/// </summary>
public sealed class TerminalScreen
{
    public const int MaxScrollbackLines = 10000;

    // xterm 16-color palette, ARGB.
    private static readonly uint[] Ansi16 =
    [
        0xFF000000, 0xFFCD0000, 0xFF00CD00, 0xFFCDCD00,
        0xFF0000EE, 0xFFCD00CD, 0xFF00CDCD, 0xFFE5E5E5,
        0xFF7F7F7F, 0xFFFF0000, 0xFF00FF00, 0xFFFFFF00,
        0xFF5C5CFF, 0xFFFF00FF, 0xFF00FFFF, 0xFFFFFFFF,
    ];

    // App palette family (deep slate / cream / amber): default fg is cream,
    // default bg is transparent so the view's slate shows through.
    public const uint DefaultForegroundArgb = 0xFFF5EFE0; // cream
    public const uint DefaultBackgroundArgb = 0x00000000; // transparent

    private const byte AttrBold = 1;
    private const byte AttrUnderline = 2;
    private const byte AttrInverse = 4;

    private enum ParseState { Ground, Escape, Csi, Osc, OscEscape, IgnoreUntilSt, Charset }

    private sealed class Line
    {
        public char[] Chars;
        public uint[] Fg;
        public uint[] Bg;
        public byte[] Attr;

        public Line(int cols, uint fg, uint bg)
        {
            Chars = new char[cols];
            Fg = new uint[cols];
            Bg = new uint[cols];
            Attr = new byte[cols];
            Array.Fill(Chars, ' ');
            Array.Fill(Fg, fg);
            Array.Fill(Bg, bg);
        }

        public void Resize(int newCols, uint fg, uint bg)
        {
            if (newCols == Chars.Length)
            {
                return;
            }

            var chars = new char[newCols];
            var fgArr = new uint[newCols];
            var bgArr = new uint[newCols];
            var attr = new byte[newCols];
            var copy = Math.Min(newCols, Chars.Length);
            Array.Copy(Chars, chars, copy);
            Array.Copy(Fg, fgArr, copy);
            Array.Copy(Bg, bgArr, copy);
            Array.Copy(Attr, attr, copy);
            for (var i = copy; i < newCols; i++)
            {
                chars[i] = ' ';
                fgArr[i] = fg;
                bgArr[i] = bg;
            }

            Chars = chars;
            Fg = fgArr;
            Bg = bgArr;
            Attr = attr;
        }
    }

    private readonly List<Line> _scrollback = new();
    private Line[] _screen;
    private Line[]? _altScreen;
    private readonly HashSet<int> _dirty = new();
    private readonly Decoder _decoder = Encoding.UTF8.GetDecoder();

    private ParseState _state = ParseState.Ground;
    private readonly int[] _csiParams = new int[16];
    private readonly bool[] _csiHasDigits = new bool[16];
    private int _csiParamCount;
    private bool _csiPrivate; // '?' prefix
    private readonly StringBuilder _osc = new();

    private int _row;
    private int _col;
    private int _scrollTop; // 0-based margin top
    private int _scrollBottom; // 0-based margin bottom
    private bool _autoWrap = true;
    private bool _cursorVisible = true;
    private uint _fg = DefaultForegroundArgb;
    private uint _bg = DefaultBackgroundArgb;
    private byte _attr;
    private int _savedRow;
    private int _savedCol;

    public int Cols { get; private set; }
    public int Rows { get; private set; }
    public int CursorRow => _row;
    public int CursorCol => _col;
    public bool CursorVisible => _cursorVisible;
    public bool InAltScreen => _altScreen is not null;
    public string? Title { get; private set; }
    public int ScrollbackLineCount => _scrollback.Count;
    public int TotalLineCount => _scrollback.Count + Rows;

    public TerminalScreen(int cols = 80, int rows = 24)
    {
        Cols = Math.Max(1, cols);
        Rows = Math.Max(1, rows);
        _screen = NewBlankScreen(Rows);
        _scrollTop = 0;
        _scrollBottom = Rows - 1;
    }

    // ------------------------------------------------------------------
    // Input: byte stream from the PTY
    // ------------------------------------------------------------------

    public void Process(ReadOnlySpan<byte> data)
    {
        if (data.IsEmpty)
        {
            return;
        }

        // Incremental UTF-8 decode: split multi-byte sequences across calls
        // are held by the Decoder until complete.
        var charCount = _decoder.GetCharCount(data, flush: false);
        Span<char> chars = charCount <= 512 ? stackalloc char[512] : new char[charCount];
        var written = _decoder.GetChars(data, chars, flush: false);
        for (var i = 0; i < written; i++)
        {
            ProcessChar(chars[i]);
        }
    }

    private void ProcessChar(char c)
    {
        switch (_state)
        {
            case ParseState.Ground:
                ProcessGround(c);
                break;
            case ParseState.Escape:
                ProcessEscape(c);
                break;
            case ParseState.Charset:
                _state = ParseState.Ground; // consume one charset-designator char
                break;
            case ParseState.Csi:
                ProcessCsi(c);
                break;
            case ParseState.Osc:
                ProcessOsc(c);
                break;
            case ParseState.OscEscape:
                if (c == '\\')
                {
                    HandleOsc();
                }
                else
                {
                    _osc.Append('\x1b');
                    _osc.Append(c);
                    _state = ParseState.Osc;
                    return;
                }

                _state = ParseState.Ground;
                break;
            case ParseState.IgnoreUntilSt:
                if (c == '\a')
                {
                    _state = ParseState.Ground;
                }
                else if (c == '\x1b')
                {
                    _state = ParseState.OscEscape; // ESC \ terminates too
                }

                break;
        }
    }

    private void ProcessGround(char c)
    {
        switch (c)
        {
            case '\0':
            case '\x0e': // SO
            case '\x0f': // SI
            case '\x7f': // DEL
                break;
            case '\a': // BEL: nothing audible headless; ignored
                break;
            case '\b':
                _col = Math.Max(0, _col - 1);
                break;
            case '\t':
                _col = Math.Min(Cols - 1, ((_col / 8) + 1) * 8);
                break;
            case '\n':
            case '\v':
            case '\f':
                LineFeed();
                break;
            case '\r':
                _col = 0;
                break;
            case '\x1b':
                _state = ParseState.Escape;
                break;
            default:
                if (c < 0x20)
                {
                    break; // other C0 controls: ignore
                }

                PutChar(c);
                break;
        }
    }

    private void ProcessEscape(char c)
    {
        switch (c)
        {
            case '[':
                _csiParamCount = 0;
                _csiPrivate = false;
                Array.Clear(_csiParams, 0, _csiParams.Length);
                Array.Clear(_csiHasDigits, 0, _csiHasDigits.Length);
                _state = ParseState.Csi;
                return;
            case ']':
                _osc.Clear();
                _state = ParseState.Osc;
                return;
            case '(':
            case ')':
            case '#':
                _state = ParseState.Charset; // consume the designator, ignore
                return;
            case 'M':
                ReverseIndex();
                break;
            case '7':
                _savedRow = _row;
                _savedCol = _col;
                break;
            case '8':
                _row = Math.Clamp(_savedRow, 0, Rows - 1);
                _col = Math.Clamp(_savedCol, 0, Cols - 1);
                break;
            case 'c': // full reset
                Reset();
                break;
            case '=': // keypad application mode: ignored
            case '>': // keypad numeric mode: ignored
                break;
            case 'P': // DCS
            case 'X': // SOS
            case '^': // PM
            case '_': // APC
                _state = ParseState.IgnoreUntilSt;
                return;
            case '\x1b':
                return; // stay in Escape
            case '\x18': // CAN
            case '\x1a': // SUB
                break;
            default:
                break; // unknown: ignore
        }

        _state = ParseState.Ground;
    }

    private void ProcessCsi(char c)
    {
        if (c is >= '0' and <= '9')
        {
            _csiParams[_csiParamCount] = _csiParams[_csiParamCount] * 10 + (c - '0');
            _csiHasDigits[_csiParamCount] = true;
            return;
        }

        switch (c)
        {
            case ';':
                _csiParamCount = Math.Min(_csiParamCount + 1, _csiParams.Length - 1);
                return;
            case '?':
                _csiPrivate = true;
                return;
            case ' ':
            case '"':
            case '\'':
            case '$':
            case '!':
                return; // intermediates we don't need
            case '\x1b':
                _state = ParseState.Escape;
                return;
            case '\x18':
            case '\x1a':
                _state = ParseState.Ground;
                return;
        }

        if (c is >= '@' and <= '~')
        {
            HandleCsi(c);
            _state = ParseState.Ground;
        }
        // else: ignore stray chars inside the sequence
    }

    private void ProcessOsc(char c)
    {
        if (c == '\a')
        {
            HandleOsc();
            _state = ParseState.Ground;
        }
        else if (c == '\x1b')
        {
            _state = ParseState.OscEscape;
        }
        else
        {
            _osc.Append(c);
        }
    }

    private void HandleOsc()
    {
        var s = _osc.ToString();
        var semi = s.IndexOf(';');
        if (semi > 0 && (s.StartsWith("0;") || s.StartsWith("2;")))
        {
            Title = s[(semi + 1)..];
        }
    }

    // ------------------------------------------------------------------
    // CSI dispatch
    // ------------------------------------------------------------------

    /// <summary>
    /// CSI parameter: an absent parameter yields the per-command default
    /// (an explicit 0 stays 0). Call sites clamp/Max as each command needs.
    /// </summary>
    private int Param(int i, int @default = 1) =>
        i <= _csiParamCount && _csiHasDigits[i] ? _csiParams[i] : @default;

    private void HandleCsi(char final)
    {
        var p0 = Param(0);
        var p1 = Param(1);
        switch (final)
        {
            // Cursor motion
            case 'A': _row = Math.Max(0, _row - Math.Max(1, p0)); break; // CUU
            case 'B': _row = Math.Min(Rows - 1, _row + Math.Max(1, p0)); break; // CUD
            case 'C': _col = Math.Min(Cols - 1, _col + Math.Max(1, p0)); break; // CUF
            case 'D': _col = Math.Max(0, _col - Math.Max(1, p0)); break; // CUB
            case 'E': _row = Math.Min(Rows - 1, _row + Math.Max(1, p0)); _col = 0; break; // CNL
            case 'F': _row = Math.Max(0, _row - Math.Max(1, p0)); _col = 0; break; // CPL
            case 'G': _col = Math.Clamp(p0 - 1, 0, Cols - 1); break; // CHA
            case 'd': _row = Math.Clamp(p0 - 1, 0, Rows - 1); break; // VPA
            case 'H':
            case 'f': // CUP / HVP
                _row = Math.Clamp(p0 - 1, 0, Rows - 1);
                _col = Math.Clamp(p1 - 1, 0, Cols - 1);
                break;

            // Erase (default 0: from cursor; unlike motion commands)
            case 'J': EraseDisplay(Param(0, 0)); break;
            case 'K': EraseLine(Param(0, 0)); break;
            case 'X': EraseChars(Math.Max(1, p0)); break; // ECH

            // Insert/delete
            case '@': InsertChars(Math.Max(1, p0)); break; // ICH
            case 'P': DeleteChars(Math.Max(1, p0)); break; // DCH
            case 'L': InsertLines(Math.Max(1, p0)); break; // IL
            case 'M': DeleteLines(Math.Max(1, p0)); break; // DL

            // Scroll
            case 'S': ScrollRegionUp(Math.Max(1, p0)); break; // SU
            case 'T': ScrollRegionDown(Math.Max(1, p0)); break; // SD

            // Modes and attributes
            case 'm': HandleSgr(); break;
            case 'r': SetScrollMargins(Param(0, 0), Param(1, 0)); break; // DECSTBM (0/0 = full)
            case 'h': SetMode(true); break;
            case 'l': SetMode(false); break;
            case 's': _savedRow = _row; _savedCol = _col; break; // SCOSC
            case 'u': // SCORC
                _row = Math.Clamp(_savedRow, 0, Rows - 1);
                _col = Math.Clamp(_savedCol, 0, Cols - 1);
                break;
        }
    }

    private void SetMode(bool on)
    {
        if (!_csiPrivate)
        {
            return; // ANSI modes (e.g. IRM): not emulated
        }

        foreach (var n in PrivateParams())
        {
            switch (n)
            {
                case 7: _autoWrap = on; break;
                case 25: _cursorVisible = on; break;
                case 1049:
                    if (on)
                    {
                        EnterAltScreen();
                    }
                    else
                    {
                        ExitAltScreen();
                    }

                    break;
            }
        }
    }

    private IEnumerable<int> PrivateParams()
    {
        for (var i = 0; i <= _csiParamCount; i++)
        {
            yield return _csiParams[i];
        }
    }

    private void HandleSgr()
    {
        // Resolve the parameter list: SGR's default for an absent parameter
        // is 0 (reset), so "ESC[m" == "ESC[0m".
        var plist = new List<int>(_csiParamCount + 1);
        for (var k = 0; k <= _csiParamCount; k++)
        {
            plist.Add(_csiHasDigits[k] ? _csiParams[k] : 0);
        }

        var count = plist.Count;
        var i = 0;
        while (i < count)
        {
            var p = plist[i];
            switch (p)
            {
                case 0: ResetAttributes(); break;
                case 1: _attr |= AttrBold; break;
                case 4: _attr |= AttrUnderline; break;
                case 7: _attr |= AttrInverse; break;
                case 22: _attr &= unchecked((byte)~AttrBold); break;
                case 24: _attr &= unchecked((byte)~AttrUnderline); break;
                case 27: _attr &= unchecked((byte)~AttrInverse); break;
                case >= 30 and <= 37: _fg = Ansi16[p - 30]; break;
                case 39: _fg = DefaultForegroundArgb; break;
                case >= 40 and <= 47: _bg = Ansi16[p - 40]; break;
                case 49: _bg = DefaultBackgroundArgb; break;
                case >= 90 and <= 97: _fg = Ansi16[p - 90 + 8]; break;
                case >= 100 and <= 107: _bg = Ansi16[p - 100 + 8]; break;
                case 38:
                case 48:
                    // 38;5;n / 38;2;r;g;b (fg) and 48;* (bg)
                    var isFg = p == 38;
                    if (i + 2 < count && plist[i + 1] == 5)
                    {
                        var color = Color256(plist[i + 2]);
                        if (isFg)
                        {
                            _fg = color;
                        }
                        else
                        {
                            _bg = color;
                        }

                        i += 2;
                    }
                    else if (i + 4 < count && plist[i + 1] == 2)
                    {
                        var color = TrueColor(plist[i + 2], plist[i + 3], plist[i + 4]);
                        if (isFg)
                        {
                            _fg = color;
                        }
                        else
                        {
                            _bg = color;
                        }

                        i += 4;
                    }

                    break;
            }

            i++;
        }
    }

    private static uint Color256(int n)
    {
        n = Math.Clamp(n, 0, 255);
        if (n < 16)
        {
            return Ansi16[n];
        }

        if (n < 232)
        {
            var v = n - 16;
            var r = v / 36;
            var g = (v % 36) / 6;
            var b = v % 6;
            return TrueColor(r == 0 ? 0 : 55 + r * 40, g == 0 ? 0 : 55 + g * 40, b == 0 ? 0 : 55 + b * 40);
        }

        var gray = 8 + (n - 232) * 10;
        return TrueColor(gray, gray, gray);
    }

    private static uint TrueColor(int r, int g, int b) =>
        0xFF000000u | ((uint)Math.Clamp(r, 0, 255) << 16) | ((uint)Math.Clamp(g, 0, 255) << 8) | (uint)Math.Clamp(b, 0, 255);

    private void ResetAttributes()
    {
        _fg = DefaultForegroundArgb;
        _bg = DefaultBackgroundArgb;
        _attr = 0;
    }

    // ------------------------------------------------------------------
    // Screen operations
    // ------------------------------------------------------------------

    private Line[] Active => _altScreen ?? _screen;

    private Line NewLine() => new(Cols, _fg, _bg);

    private Line[] NewBlankScreen(int rows)
    {
        var screen = new Line[rows];
        for (var i = 0; i < rows; i++)
        {
            screen[i] = NewLine();
        }

        return screen;
    }

    private int AbsoluteRow(int screenRow) => _altScreen is null ? _scrollback.Count + screenRow : screenRow;

    private void MarkDirty(int screenRow) => _dirty.Add(AbsoluteRow(screenRow));

    private void MarkAllDirty()
    {
        _dirty.Clear();
        for (var i = 0; i < TotalLineCount; i++)
        {
            _dirty.Add(i);
        }
    }

    private void PutChar(char c)
    {
        if (_col >= Cols)
        {
            if (!_autoWrap)
            {
                _col = Cols - 1;
            }
            else
            {
                LineFeed();
                _col = 0;
            }
        }

        var line = Active[_row];
        line.Chars[_col] = c;
        line.Fg[_col] = _fg;
        line.Bg[_col] = _bg;
        line.Attr[_col] = _attr;
        MarkDirty(_row);
        _col++;
    }

    private void LineFeed()
    {
        if (_row == _scrollBottom)
        {
            ScrollRegionUp(1);
        }
        else
        {
            _row = Math.Min(Rows - 1, _row + 1);
        }
    }

    private void ReverseIndex()
    {
        if (_row == _scrollTop)
        {
            ScrollRegionDown(1);
        }
        else
        {
            _row = Math.Max(0, _row - 1);
        }
    }

    /// <summary>
    /// Scrolls the [scrollTop, scrollBottom] region up by n. Lines scrolled
    /// off the top of a full-screen scroll enter scrollback (main buffer only).
    /// </summary>
    private void ScrollRegionUp(int n)
    {
        var active = Active;
        var fullScreen = _scrollTop == 0 && _scrollBottom == Rows - 1 && _altScreen is null;
        for (var k = 0; k < n; k++)
        {
            if (fullScreen)
            {
                _scrollback.Add(active[0]);
                if (_scrollback.Count > MaxScrollbackLines)
                {
                    _scrollback.RemoveAt(0);
                }
            }

            for (var r = _scrollTop; r < _scrollBottom; r++)
            {
                active[r] = active[r + 1];
            }

            active[_scrollBottom] = NewLine();
        }

        // Absolute indices shifted: simplest correct invalidation is all.
        MarkAllDirty();
    }

    private void ScrollRegionDown(int n)
    {
        var active = Active;
        for (var k = 0; k < n; k++)
        {
            for (var r = _scrollBottom; r > _scrollTop; r--)
            {
                active[r] = active[r - 1];
            }

            active[_scrollTop] = NewLine();
        }

        MarkAllDirty();
    }

    private void EraseDisplay(int mode)
    {
        var active = Active;
        switch (mode)
        {
            case 1: // from start to cursor
                for (var r = 0; r < _row; r++)
                {
                    BlankLine(active[r]);
                    MarkDirty(r);
                }

                for (var c = 0; c <= _col; c++)
                {
                    BlankCell(active[_row], c);
                }

                MarkDirty(_row);
                break;
            case 2:
            case 3: // whole screen (3 also clears scrollback)
                if (mode == 3 && _altScreen is null)
                {
                    _scrollback.Clear();
                }

                for (var r = 0; r < Rows; r++)
                {
                    BlankLine(active[r]);
                    MarkDirty(r);
                }

                break;
            default: // 0: from cursor to end
                for (var c = _col; c < Cols; c++)
                {
                    BlankCell(active[_row], c);
                }

                MarkDirty(_row);
                for (var r = _row + 1; r < Rows; r++)
                {
                    BlankLine(active[r]);
                    MarkDirty(r);
                }

                break;
        }
    }

    private void EraseLine(int mode)
    {
        var line = Active[_row];
        switch (mode)
        {
            case 1:
                for (var c = 0; c <= _col; c++)
                {
                    BlankCell(line, c);
                }

                break;
            case 2:
                BlankLine(line);
                break;
            default:
                for (var c = _col; c < Cols; c++)
                {
                    BlankCell(line, c);
                }

                break;
        }

        MarkDirty(_row);
    }

    private void EraseChars(int n)
    {
        var line = Active[_row];
        for (var c = _col; c < Math.Min(Cols, _col + n); c++)
        {
            BlankCell(line, c);
        }

        MarkDirty(_row);
    }

    private void InsertChars(int n)
    {
        var line = Active[_row];
        for (var c = Cols - 1; c >= _col + n; c--)
        {
            CopyCell(line, c - n, c);
        }

        for (var c = _col; c < Math.Min(Cols, _col + n); c++)
        {
            BlankCell(line, c);
        }

        MarkDirty(_row);
    }

    private void DeleteChars(int n)
    {
        var line = Active[_row];
        for (var c = _col; c < Cols - n; c++)
        {
            CopyCell(line, c + n, c);
        }

        for (var c = Math.Max(_col, Cols - n); c < Cols; c++)
        {
            BlankCell(line, c);
        }

        MarkDirty(_row);
    }

    private void InsertLines(int n)
    {
        var active = Active;
        var bottom = Math.Min(_scrollBottom, Rows - 1);
        for (var r = bottom; r >= _row + n; r--)
        {
            active[r] = active[r - n];
        }

        for (var r = _row; r < Math.Min(bottom + 1, _row + n); r++)
        {
            active[r] = NewLine();
        }

        MarkAllDirty();
    }

    private void DeleteLines(int n)
    {
        var active = Active;
        var bottom = Math.Min(_scrollBottom, Rows - 1);
        for (var r = _row; r <= bottom - n; r++)
        {
            active[r] = active[r + n];
        }

        for (var r = Math.Max(_row, bottom - n + 1); r <= bottom; r++)
        {
            active[r] = NewLine();
        }

        MarkAllDirty();
    }

    private void SetScrollMargins(int top, int bottom)
    {
        // 0/0 (or missing) means full screen.
        var t = top <= 0 ? 1 : top;
        var b = bottom <= 0 ? Rows : bottom;
        if (t < b && b <= Rows)
        {
            _scrollTop = t - 1;
            _scrollBottom = b - 1;
            _row = 0;
            _col = 0;
        }
    }

    private void EnterAltScreen()
    {
        if (_altScreen is not null)
        {
            return;
        }

        _savedRow = _row;
        _savedCol = _col;
        _altScreen = NewBlankScreen(Rows);
        _row = 0;
        _col = 0;
        _scrollTop = 0;
        _scrollBottom = Rows - 1;
        MarkAllDirty();
    }

    private void ExitAltScreen()
    {
        if (_altScreen is null)
        {
            return;
        }

        _altScreen = null;
        _row = Math.Clamp(_savedRow, 0, Rows - 1);
        _col = Math.Clamp(_savedCol, 0, Cols - 1);
        MarkAllDirty();
    }

    private void Reset()
    {
        _scrollback.Clear();
        _screen = NewBlankScreen(Rows);
        _altScreen = null;
        _row = 0;
        _col = 0;
        _scrollTop = 0;
        _scrollBottom = Rows - 1;
        _autoWrap = true;
        _cursorVisible = true;
        ResetAttributes();
        Title = null;
        MarkAllDirty();
    }

    private void BlankLine(Line line)
    {
        Array.Fill(line.Chars, ' ');
        Array.Fill(line.Fg, _fg);
        Array.Fill(line.Bg, _bg);
        Array.Fill(line.Attr, (byte)0);
    }

    private void BlankCell(Line line, int c)
    {
        line.Chars[c] = ' ';
        line.Fg[c] = _fg;
        line.Bg[c] = _bg;
        line.Attr[c] = 0;
    }

    private static void CopyCell(Line line, int from, int to)
    {
        line.Chars[to] = line.Chars[from];
        line.Fg[to] = line.Fg[from];
        line.Bg[to] = line.Bg[from];
        line.Attr[to] = line.Attr[from];
    }

    // ------------------------------------------------------------------
    // Resize
    // ------------------------------------------------------------------

    /// <summary>
    /// Resizes the screen, preserving the overlapping content (no reflow —
    /// same as xterm). Scrollback is untouched.
    /// </summary>
    public void Resize(int cols, int rows)
    {
        cols = Math.Max(1, cols);
        rows = Math.Max(1, rows);
        if (cols == Cols && rows == Rows)
        {
            return;
        }

        var oldScreen = _screen;
        var oldAlt = _altScreen;
        Cols = cols;
        Rows = rows;

        _screen = new Line[rows];
        for (var r = 0; r < rows; r++)
        {
            var line = NewLine();
            if (r < oldScreen.Length)
            {
                oldScreen[r].Resize(cols, _fg, _bg);
                line = oldScreen[r];
            }

            _screen[r] = line;
        }

        if (oldAlt is not null)
        {
            _altScreen = new Line[rows];
            for (var r = 0; r < rows; r++)
            {
                var line = NewLine();
                if (r < oldAlt.Length)
                {
                    oldAlt[r].Resize(cols, _fg, _bg);
                    line = oldAlt[r];
                }

                _altScreen[r] = line;
            }
        }

        // Resize each scrollback line's width too so GetLine stays rectangular.
        foreach (var line in _scrollback)
        {
            line.Resize(cols, DefaultForegroundArgb, DefaultBackgroundArgb);
        }

        _scrollTop = 0;
        _scrollBottom = rows - 1;
        _row = Math.Clamp(_row, 0, rows - 1);
        _col = Math.Clamp(_col, 0, cols - 1);
        _savedRow = Math.Clamp(_savedRow, 0, rows - 1);
        _savedCol = Math.Clamp(_savedCol, 0, cols - 1);
        MarkAllDirty();
    }

    // ------------------------------------------------------------------
    // Readout for the renderer
    // ------------------------------------------------------------------

    /// <summary>
    /// Absolute line indices (0 = oldest scrollback) changed since the last
    /// call. The renderer updates only these rows.
    /// </summary>
    public IReadOnlyList<int> TakeDirtyLines()
    {
        if (_dirty.Count == 0)
        {
            return [];
        }

        var list = new List<int>(_dirty);
        list.Sort();
        _dirty.Clear();
        return list;
    }

    /// <summary>
    /// Styled runs for the absolute line <paramref name="index"/>. Trailing
    /// blank cells are trimmed; inverse is resolved into the colors.
    /// </summary>
    public IReadOnlyList<TerminalTextRun> GetLine(int index)
    {
        var line = GetRawLine(index);
        if (line is null)
        {
            return [];
        }

        // Trim trailing cells that are blank with default styling.
        var end = Cols;
        while (end > 0 && line.Chars[end - 1] == ' ' && line.Attr[end - 1] == 0
               && line.Fg[end - 1] == DefaultForegroundArgb && line.Bg[end - 1] == DefaultBackgroundArgb)
        {
            end--;
        }

        if (end == 0)
        {
            return [];
        }

        var runs = new List<TerminalTextRun>();
        var runStart = 0;
        for (var c = 1; c <= end; c++)
        {
            var boundary = c == end || line.Fg[c] != line.Fg[runStart] || line.Bg[c] != line.Bg[runStart]
                           || line.Attr[c] != line.Attr[runStart];
            if (!boundary)
            {
                continue;
            }

            var text = new string(line.Chars, runStart, c - runStart);
            var fg = line.Fg[runStart];
            var bg = line.Bg[runStart];
            var attr = line.Attr[runStart];
            var inverse = (attr & AttrInverse) != 0;
            runs.Add(new TerminalTextRun(
                text,
                inverse ? bg : fg,
                inverse ? fg : bg,
                (attr & AttrBold) != 0,
                (attr & AttrUnderline) != 0));
            runStart = c;
        }

        return runs;
    }

    /// <summary>Plain text of the absolute line (for screen-reader announcements).</summary>
    public string GetLineText(int index)
    {
        var line = GetRawLine(index);
        return line is null ? "" : new string(line.Chars).TrimEnd();
    }

    private Line? GetRawLine(int index)
    {
        if (index < 0)
        {
            return null;
        }

        if (_altScreen is not null)
        {
            return index < Rows ? _altScreen[index] : null;
        }

        if (index < _scrollback.Count)
        {
            return _scrollback[index];
        }

        var screenIndex = index - _scrollback.Count;
        return screenIndex < Rows ? _screen[screenIndex] : null;
    }
}
