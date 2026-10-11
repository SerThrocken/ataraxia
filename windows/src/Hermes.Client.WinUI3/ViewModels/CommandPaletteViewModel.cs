using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Hermes.Client.Core;

namespace Hermes.Client.WinUI3.ViewModels;

/// <summary>
/// Ctrl+K command palette: fuzzy search across skills, sessions, pages and
/// actions. Pure selection/keyboard state; the host performs the effects.
/// Full keyboard operation: type to filter, Up/Down to move, Enter to run,
/// Esc to dismiss. Result count is announced via a polite live region.
/// </summary>
public sealed partial class CommandPaletteViewModel : ObservableObject
{
    private readonly ICommandPaletteHost _host;
    private readonly CommandPaletteEngine _engine = new();
    private IReadOnlyList<PaletteItem> _all = [];

    public CommandPaletteViewModel(ICommandPaletteHost host)
    {
        _host = host;
    }

    public ObservableCollection<PaletteItem> Results { get; } = new();

    [ObservableProperty]
    public partial string Query { get; set; } = "";

    [ObservableProperty]
    public partial int SelectedIndex { get; set; } = -1;

    [ObservableProperty]
    public partial bool IsLoading { get; set; }

    /// <summary>Bound to a polite live region: announces the result count.</summary>
    [ObservableProperty]
    public partial string StatusText { get; set; } = string.Empty;

    public event EventHandler? DismissRequested;

    partial void OnQueryChanged(string value) => ApplyFilter();

    /// <summary>Rebuilds rows (skills cached by the host) and resets the query.</summary>
    public async Task OpenAsync()
    {
        Results.Clear();
        SelectedIndex = -1;
        Query = string.Empty;
        IsLoading = true;
        StatusText = "Loading commands…";
        try
        {
            var skills = await _host.GetSkillsAsync();
            var sessions = await _host.GetSessionsAsync();
            _all = _engine.BuildItems(skills, sessions);
        }
        finally
        {
            IsLoading = false;
        }

        ApplyFilter();
    }

    public void MoveSelection(int delta)
    {
        if (Results.Count == 0)
        {
            return;
        }

        var next = SelectedIndex + delta;
        if (next < 0)
        {
            next = Results.Count - 1;
        }
        else if (next >= Results.Count)
        {
            next = 0;
        }

        SelectedIndex = next;
    }

    public void SelectIndex(int index)
    {
        if (index >= 0 && index < Results.Count)
        {
            SelectedIndex = index;
        }
    }

    public async Task ExecuteSelectedAsync()
    {
        if (SelectedIndex < 0 || SelectedIndex >= Results.Count)
        {
            return;
        }

        var effect = _engine.EffectFor(Results[SelectedIndex]);
        RequestDismiss();
        await _host.PerformEffectAsync(effect);
    }

    public void RequestDismiss() => DismissRequested?.Invoke(this, EventArgs.Empty);

    private void ApplyFilter()
    {
        var items = _engine.Query(_all, Query);
        Results.Clear();
        foreach (var item in items)
        {
            Results.Add(item);
        }

        SelectedIndex = Results.Count > 0 ? 0 : -1;
        StatusText = Results.Count switch
        {
            0 => "No results",
            1 => "1 result",
            var n => $"{n} results",
        };
    }
}
