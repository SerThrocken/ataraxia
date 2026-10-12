using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Hermes.Client.Core.Terminal;
using Hermes.Client.WinUI3.Terminal;
using Microsoft.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Input;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Input;
using Windows.ApplicationModel.DataTransfer;
using Windows.System;
using Windows.UI;
using Windows.UI.Core;

namespace Hermes.Client.WinUI3.Controls;

/// <summary>
/// Interactive terminal view: renders the <see cref="TerminalScreen"/>
/// model as virtualized text rows (only changed rows are re-rendered),
/// sends full keyboard input (Ctrl+C as 0x03, arrows, F-keys — VT
/// sequences via <see cref="VtKeyEncoder"/>), resizes the pseudoconsole
/// with the view, and announces new output to screen readers on a quiet
/// window so narration never chases the stream.
/// Palette: deep slate background, cream text, amber cursor/accents.
/// </summary>
public sealed partial class TerminalControl : UserControl
{
    private const uint SlateArgb = 0xFF0D1417;
    private const uint CreamArgb = 0xFFF5EFE0;
    private const uint AmberArgb = 0xFFE8B45A;
    private const double FontSize = 14;

    private readonly ObservableCollection<TerminalRow> _rows = new();
    private readonly Dictionary<uint, SolidColorBrush> _brushes = new();
    private readonly DispatcherQueueTimer _announceTimer;
    private readonly DispatcherQueueTimer _resizeTimer;
    private readonly DispatcherQueueTimer _cursorTimer;

    private PtySession? _session;
    private double _charWidth;
    private double _lineHeight;
    private bool _followTail = true;
    private bool _cursorOn = true;
    private int _lastCursorAbsRow = -1;
    private string? _pendingAnnouncement;

    public TerminalControl()
    {
        InitializeComponent();
        Background = new SolidColorBrush(ToColor(SlateArgb));
        // Headless measure: no layout needed, so font metrics are valid
        // before the first render (RenderRow needs a real line height).
        MeasureFont();
        Rows.ItemsSource = _rows;
        Rows.ElementPrepared += OnElementPrepared;
        Scroller.ViewChanged += OnViewChanged;
        Scroller.SizeChanged += OnSizeChanged;
        KeyDown += OnKeyDown;
        CharacterReceived += OnCharacterReceived;
        PointerPressed += (_, _) => Focus(FocusState.Pointer);

        // Screen-reader announcements wait for a quiet window (1.5s) so a
        // fast stream is announced once, with the latest line.
        _announceTimer = DispatcherQueue.CreateTimer();
        _announceTimer.Interval = TimeSpan.FromSeconds(1.5);
        _announceTimer.Tick += (_, _) => AnnouncePending();

        // Resize storms (window drag) collapse into one pseudoconsole resize.
        _resizeTimer = DispatcherQueue.CreateTimer();
        _resizeTimer.Interval = TimeSpan.FromMilliseconds(250);
        _resizeTimer.Tick += (_, _) =>
        {
            _resizeTimer.Stop();
            ApplyResize();
        };

        _cursorTimer = DispatcherQueue.CreateTimer();
        _cursorTimer.Interval = TimeSpan.FromMilliseconds(530);
        _cursorTimer.Tick += (_, _) =>
        {
            _cursorOn = !_cursorOn;
            InvalidateCursorRows();
        };

        Loaded += (_, _) =>
        {
            MeasureFont();
            _cursorTimer.Start();
        };
        Unloaded += (_, _) =>
        {
            _cursorTimer.Stop();
            _announceTimer.Stop();
            _resizeTimer.Stop();
        };
    }

    public PtySession? Session
    {
        get => _session;
        set
        {
            if (ReferenceEquals(_session, value))
            {
                return;
            }

            if (_session is not null)
            {
                _session.OutputProcessed -= OnOutputProcessed;
                _session.Exited -= OnSessionExited;
            }

            _session = value;

            if (_session is not null)
            {
                _session.OutputProcessed += OnOutputProcessed;
                _session.Exited += OnSessionExited;
            }

            RebuildAll();
        }
    }

    /// <summary>
    /// Current terminal size in cells, from measured font metrics and the
    /// viewport. Falls back to 80x24 before first layout (the debounced
    /// SizeChanged resize corrects it as soon as layout lands).
    /// </summary>
    public (int Cols, int Rows) MeasureTerminal()
    {
        if (_charWidth <= 0)
        {
            MeasureFont();
        }

        if (Scroller.ActualWidth <= 0 || Scroller.ViewportHeight <= 0)
        {
            return (80, 24);
        }

        var cols = Math.Max(20, (int)(Scroller.ActualWidth / _charWidth));
        var rows = Math.Max(5, (int)(Scroller.ViewportHeight / _lineHeight));
        return (cols, rows);
    }

    // ------------------------------------------------------------------
    // Rendering
    // ------------------------------------------------------------------

    private void MeasureFont()
    {
        var probe = new TextBlock
        {
            FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Cascadia Mono, Consolas"),
            FontSize = FontSize,
            Text = new string('M', 64),
        };
        probe.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        _charWidth = Math.Max(1, probe.DesiredSize.Width / 64);
        _lineHeight = Math.Max(8, probe.DesiredSize.Height);
    }

    private void OnElementPrepared(ItemsRepeater sender, ItemsRepeaterElementPreparedEventArgs e)
    {
        if (e.Element is TextBlock tb && e.Index >= 0 && e.Index < _rows.Count)
        {
            var cursorAbs = -1;
            var cursorCol = -1;
            _session?.UseScreen(s => (cursorAbs, cursorCol) = CursorPosition(s));
            RenderRow(tb, _rows[e.Index], cursorAbs, cursorCol);
        }
    }

    private void OnOutputProcessed(object? sender, EventArgs e) =>
        DispatcherQueue.TryEnqueue(() =>
        {
            RebuildDirty();
            QueueAnnouncement();
        });

    private void RebuildAll()
    {
        var session = _session;
        _rows.Clear();
        _lastCursorAbsRow = -1;
        if (session is null)
        {
            return;
        }

        var cursorAbs = -1;
        var cursorCol = -1;
        session.UseScreen(s =>
        {
            for (var i = 0; i < s.TotalLineCount; i++)
            {
                var row = new TerminalRow(i)
                {
                    Runs = s.GetLine(i),
                    PlainText = s.GetLineText(i),
                };
                _rows.Add(row);
            }

            (cursorAbs, cursorCol) = CursorPosition(s);
        });

        foreach (var (index, element) in RealizedElements())
        {
            RenderRow((TextBlock)element, _rows[index], cursorAbs, cursorCol);
        }

        _lastCursorAbsRow = cursorAbs;
        FollowTail();
    }

    private void RebuildDirty()
    {
        var session = _session;
        if (session is null)
        {
            return;
        }

        List<int>? dirty = null;
        var cursorAbs = -1;
        var cursorCol = -1;
        session.UseScreen(s =>
        {
            dirty = new List<int>(s.TakeDirtyLines());
            foreach (var i in dirty)
            {
                while (_rows.Count <= i)
                {
                    _rows.Add(new TerminalRow(_rows.Count));
                }

                _rows[i].Runs = s.GetLine(i);
                _rows[i].PlainText = s.GetLineText(i);
            }

            (cursorAbs, cursorCol) = CursorPosition(s);
        });

        if (dirty is null)
        {
            return;
        }

        foreach (var i in dirty)
        {
            if (Rows.TryGetElement(i) is TextBlock tb)
            {
                RenderRow(tb, _rows[i], cursorAbs, cursorCol);
            }
        }

        _lastCursorAbsRow = cursorAbs;
        FollowTail();
    }

    private static (int AbsRow, int Col) CursorPosition(TerminalScreen s)
    {
        if (!s.CursorVisible)
        {
            return (-1, -1);
        }

        var abs = s.InAltScreen ? s.CursorRow : s.ScrollbackLineCount + s.CursorRow;
        return (abs, s.CursorCol);
    }

    private void InvalidateCursorRows()
    {
        var session = _session;
        if (session is null)
        {
            return;
        }

        var cursorAbs = -1;
        var cursorCol = -1;
        session.UseScreen(s => (cursorAbs, cursorCol) = CursorPosition(s));

        // Re-render the old and new cursor rows (blink on/off).
        foreach (var abs in new[] { _lastCursorAbsRow, cursorAbs })
        {
            if (abs >= 0 && abs < _rows.Count && Rows.TryGetElement(abs) is TextBlock tb)
            {
                RenderRow(tb, _rows[abs], cursorAbs, cursorCol);
            }
        }

        _lastCursorAbsRow = cursorAbs;
    }

    private IEnumerable<(int Index, UIElement Element)> RealizedElements()
    {
        for (var i = 0; i < _rows.Count; i++)
        {
            if (Rows.TryGetElement(i) is { } element)
            {
                yield return (i, element);
            }
        }
    }

    private void RenderRow(TextBlock tb, TerminalRow row, int cursorAbsRow, int cursorCol)
    {
        tb.Inlines.Clear();
        tb.LineHeight = _lineHeight;

        var cursorHere = cursorAbsRow == row.Index ? cursorCol : -1;
        var offset = 0;
        foreach (var run in row.Runs)
        {
            if (cursorHere >= 0 && cursorHere >= offset && cursorHere < offset + run.Text.Length)
            {
                var ci = cursorHere - offset;
                AddStyledRun(tb, run.Text[..ci], run, isCursor: false);
                AddStyledRun(tb, run.Text.Substring(ci, 1), run, isCursor: true);
                AddStyledRun(tb, run.Text[(ci + 1)..], run, isCursor: false);
            }
            else
            {
                AddStyledRun(tb, run.Text, run, isCursor: false);
            }

            offset += run.Text.Length;
        }

        if (cursorHere >= offset)
        {
            // Cursor past end of line: pad out and draw the block.
            var pad = cursorHere - offset;
            if (pad > 0)
            {
                AddStyledRun(tb, new string(' ', pad), default, isCursor: false);
            }

            AddStyledRun(tb, " ", default, isCursor: true);
        }
    }

    private void AddStyledRun(TextBlock tb, string text, TerminalTextRun run, bool isCursor)
    {
        if (text.Length == 0)
        {
            return;
        }

        var r = new Run { Text = text };
        if (isCursor)
        {
            r.Foreground = BrushFor(SlateArgb);
            r.Background = BrushFor(AmberArgb);
        }
        else
        {
            r.Foreground = BrushFor(run.ForegroundArgb == 0 ? CreamArgb : run.ForegroundArgb);
            if (run.BackgroundArgb != 0)
            {
                r.Background = BrushFor(run.BackgroundArgb);
            }

            if (run.Bold)
            {
                r.FontWeight = FontWeights.Bold;
            }

            if (run.Underline)
            {
                r.TextDecorations = Windows.UI.Text.TextDecorations.Underline;
            }
        }

        tb.Inlines.Add(r);
    }

    private SolidColorBrush BrushFor(uint argb)
    {
        if (!_brushes.TryGetValue(argb, out var brush))
        {
            brush = new SolidColorBrush(ToColor(argb));
            _brushes[argb] = brush;
        }

        return brush;
    }

    private static Color ToColor(uint argb) => Color.FromArgb(
        (byte)(argb >> 24), (byte)(argb >> 16), (byte)(argb >> 8), (byte)argb);

    private void OnViewChanged(object? sender, ScrollViewerViewChangedEventArgs e)
    {
        // Follow the tail only while the user sits at the bottom; the moment
        // they scroll up, output stops yanking the view.
        _followTail = Scroller.VerticalOffset >= Scroller.ScrollableHeight - _lineHeight * 2;
    }

    private void FollowTail()
    {
        if (!_followTail || _rows.Count == 0)
        {
            return;
        }

        Scroller.UpdateLayout();
        Scroller.ScrollToVerticalOffset(Scroller.ScrollableHeight);
    }

    // ------------------------------------------------------------------
    // Resize
    // ------------------------------------------------------------------

    private void OnSizeChanged(object sender, SizeChangedEventArgs e) => _resizeTimer.Start();

    private void ApplyResize()
    {
        var session = _session;
        if (session is null)
        {
            return;
        }

        var (cols, rows) = MeasureTerminal();
        session.Resize(cols, rows);
    }

    // ------------------------------------------------------------------
    // Keyboard input
    // ------------------------------------------------------------------

    private static bool IsDown(VirtualKey key) =>
        InputKeyboardSource.GetKeyStateForCurrentThread(key).HasFlag(CoreVirtualKeyStates.Down);

    private void OnKeyDown(object sender, KeyRoutedEventArgs e)
    {
        var session = _session;
        if (session is null || !session.IsRunning)
        {
            return;
        }

        var mods = TerminalModifiers.None;
        if (IsDown(VirtualKey.Shift))
        {
            mods |= TerminalModifiers.Shift;
        }

        if (IsDown(VirtualKey.Control))
        {
            mods |= TerminalModifiers.Control;
        }

        if (IsDown(VirtualKey.Menu))
        {
            mods |= TerminalModifiers.Alt;
        }

        // Ctrl+V pastes (intercepted here so the encoder never sees it).
        if (e.Key == VirtualKey.V && mods == TerminalModifiers.Control)
        {
            e.Handled = true;
            PasteAsync();
            return;
        }

        var press = e.Key switch
        {
            VirtualKey.Enter => new TerminalKeyPress(TerminalKey.Enter, mods),
            VirtualKey.Back => new TerminalKeyPress(TerminalKey.Backspace, mods),
            VirtualKey.Tab => new TerminalKeyPress(TerminalKey.Tab, mods),
            VirtualKey.Escape => new TerminalKeyPress(TerminalKey.Escape, mods),
            VirtualKey.Delete => new TerminalKeyPress(TerminalKey.Delete, mods),
            VirtualKey.Insert => new TerminalKeyPress(TerminalKey.Insert, mods),
            VirtualKey.Home => new TerminalKeyPress(TerminalKey.Home, mods),
            VirtualKey.End => new TerminalKeyPress(TerminalKey.End, mods),
            VirtualKey.PageUp => new TerminalKeyPress(TerminalKey.PageUp, mods),
            VirtualKey.PageDown => new TerminalKeyPress(TerminalKey.PageDown, mods),
            VirtualKey.Up => new TerminalKeyPress(TerminalKey.ArrowUp, mods),
            VirtualKey.Down => new TerminalKeyPress(TerminalKey.ArrowDown, mods),
            VirtualKey.Left => new TerminalKeyPress(TerminalKey.ArrowLeft, mods),
            VirtualKey.Right => new TerminalKeyPress(TerminalKey.ArrowRight, mods),
            VirtualKey.F1 => new TerminalKeyPress(TerminalKey.F1, mods),
            VirtualKey.F2 => new TerminalKeyPress(TerminalKey.F2, mods),
            VirtualKey.F3 => new TerminalKeyPress(TerminalKey.F3, mods),
            VirtualKey.F4 => new TerminalKeyPress(TerminalKey.F4, mods),
            VirtualKey.F5 => new TerminalKeyPress(TerminalKey.F5, mods),
            VirtualKey.F6 => new TerminalKeyPress(TerminalKey.F6, mods),
            VirtualKey.F7 => new TerminalKeyPress(TerminalKey.F7, mods),
            VirtualKey.F8 => new TerminalKeyPress(TerminalKey.F8, mods),
            VirtualKey.F9 => new TerminalKeyPress(TerminalKey.F9, mods),
            VirtualKey.F10 => new TerminalKeyPress(TerminalKey.F10, mods),
            VirtualKey.F11 => new TerminalKeyPress(TerminalKey.F11, mods),
            VirtualKey.F12 => new TerminalKeyPress(TerminalKey.F12, mods),
            _ => MapModifiedCharacterKey(e.Key, mods),
        };

        if (press.Key != TerminalKey.None)
        {
            e.Handled = true;
            session.SendKey(press);
        }
    }

    /// <summary>
    /// Ctrl/Alt+letter/digit/space come through KeyDown: CharacterReceived
    /// yields only the raw control char for Ctrl combos and nothing at all
    /// for Alt combos. Plain printable text arrives via CharacterReceived so
    /// layouts and IME behave.
    /// </summary>
    private static TerminalKeyPress MapModifiedCharacterKey(VirtualKey key, TerminalModifiers mods)
    {
        var ctrl = mods.HasFlag(TerminalModifiers.Control);
        var alt = mods.HasFlag(TerminalModifiers.Alt);
        if (!ctrl && !alt)
        {
            return new TerminalKeyPress(TerminalKey.None);
        }

        var c = key switch
        {
            >= VirtualKey.A and <= VirtualKey.Z => (char)('a' + (key - VirtualKey.A)),
            >= VirtualKey.Number0 and <= VirtualKey.Number9 => (char)('0' + (key - VirtualKey.Number0)),
            VirtualKey.Space => ' ',
            _ => '\0',
        };
        return c == '\0'
            ? new TerminalKeyPress(TerminalKey.None)
            : new TerminalKeyPress(TerminalKey.Character, mods, c);
    }

    private void OnCharacterReceived(UIElement sender, CharacterReceivedRoutedEventArgs e)
    {
        var session = _session;
        if (session is null || !session.IsRunning)
        {
            return;
        }

        // Control characters were already handled in OnKeyDown; only real
        // text reaches the shell from here.
        if (char.IsControl(e.Character))
        {
            return;
        }

        e.Handled = true;
        session.SendText(e.Character.ToString());
    }

    private async void PasteAsync()
    {
        try
        {
            var content = Clipboard.GetContent();
            if (content.Contains(StandardDataFormats.Text))
            {
                _session?.SendText(await content.GetTextAsync());
            }
        }
        catch
        {
            // Clipboard contention (another app holds it open): benign, the
            // user retries. Nothing to report and nothing lost.
        }
    }

    // ------------------------------------------------------------------
    // Screen-reader announcements
    // ------------------------------------------------------------------

    private void QueueAnnouncement()
    {
        string? last = null;
        _session?.UseScreen(s =>
        {
            for (var i = s.TotalLineCount - 1; i >= 0; i--)
            {
                var text = s.GetLineText(i).Trim();
                if (text.Length > 0)
                {
                    last = text;
                    break;
                }
            }
        });

        if (last is not null)
        {
            _pendingAnnouncement = last.Length > 240 ? last[..240] : last;
            _announceTimer.Start(); // restarts the quiet window
        }
    }

    private void AnnouncePending()
    {
        _announceTimer.Stop();
        if (_pendingAnnouncement is not { } text)
        {
            return;
        }

        _pendingAnnouncement = null;
        Announce(text, "TerminalOutput");
    }

    private void OnSessionExited(object? sender, int code) =>
        DispatcherQueue.TryEnqueue(() =>
            Announce($"Terminal process exited with code {code}.", "TerminalExited"));

    private void Announce(string text, string activityId)
    {
        var peer = FrameworkElementAutomationPeer.FromElement(this);
        peer?.RaiseNotificationEvent(
            AutomationNotificationKind.ActionCompleted,
            AutomationNotificationProcessing.ImportantMostRecent,
            text,
            activityId);
    }
}
