using System.Collections.Generic;
using Hermes.Client.Core.Terminal;

namespace Hermes.Client.WinUI3.Terminal;

/// <summary>
/// One rendered terminal line for the <c>ItemsRepeater</c>: absolute index
/// (0 = oldest scrollback) plus the styled runs built from the screen model.
/// </summary>
public sealed class TerminalRow
{
    public TerminalRow(int index)
    {
        Index = index;
    }

    public int Index { get; }

    public IReadOnlyList<TerminalTextRun> Runs { get; set; } =
        new List<TerminalTextRun>();

    public string PlainText { get; set; } = "";
}
