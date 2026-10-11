using CommunityToolkit.Mvvm.ComponentModel;
using Hermes.Client.Infrastructure;

namespace Hermes.Client.WinUI3.ViewModels;

/// <summary>
/// Terminal backends: settings-only. The gateway exposes no terminal or PTY
/// API (verified on 0.21.6, 2026-10-11), so there is no backend list to show
/// and interactive terminal sessions are not available. The preferred local
/// terminal is persisted for when local terminal integration lands.
/// </summary>
public sealed partial class TerminalBackendsViewModel : ObservableObject
{
    public const string SettingsName = "terminal-settings";

    private readonly ISettingsService _settings;

    public TerminalBackendsViewModel(ISettingsService settings)
    {
        _settings = settings;
    }

    public IReadOnlyList<string> TerminalOptions { get; } =
        ["Windows Terminal", "PowerShell", "Command Prompt"];

    [ObservableProperty]
    public partial string PreferredTerminal { get; set; } = "Windows Terminal";

    public string Notice =>
        "No terminal backends: the gateway exposes no terminal or PTY API "
        + "(verified on 0.21.6, 2026-10-11), so interactive terminal sessions "
        + "are not available. The preference below is stored locally.";

    private bool _initialized;

    public async Task InitializeAsync()
    {
        if (_initialized)
        {
            return;
        }

        _initialized = true;
        var saved = await _settings.LoadAsync<TerminalSettings>(SettingsName);
        if (saved?.PreferredTerminal is { } preferred && TerminalOptions.Contains(preferred))
        {
            PreferredTerminal = preferred;
        }

        PropertyChanged += async (_, e) =>
        {
            if (e.PropertyName == nameof(PreferredTerminal))
            {
                await _settings.SaveAsync(SettingsName, new TerminalSettings(PreferredTerminal));
            }
        };
    }

    public sealed record TerminalSettings(string PreferredTerminal);
}
