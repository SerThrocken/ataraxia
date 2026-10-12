using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Hermes.Client.Core.Terminal;
using Hermes.Client.Infrastructure;
using Hermes.Client.WinUI3.Terminal;
using Microsoft.UI.Dispatching;

namespace Hermes.Client.WinUI3.ViewModels;

/// <summary>
/// Interactive terminal page: starts (or reattaches to) the singleton PTY
/// session, surfaces its status, and offers restart/kill. The session
/// itself lives in <see cref="TerminalSessionHost"/> so it survives
/// navigation; the window-close teardown is wired in MainWindow.
/// </summary>
public sealed partial class TerminalPageViewModel : ObservableObject
{
    private readonly TerminalSessionHost _host;
    private readonly ISettingsService _settings;

    // Captured on the UI thread at construction (DI resolves this on the UI
    // thread): the backend's Exited event fires on its reader thread, so
    // status updates must hop back here.
    private readonly DispatcherQueue _dispatcher = DispatcherQueue.GetForCurrentThread();
    private string _shellExe = "";
    private int _cols = 80;
    private int _rows = 24;

    public TerminalPageViewModel(TerminalSessionHost host, ISettingsService settings)
    {
        _host = host;
        _settings = settings;
    }

    [ObservableProperty]
    public partial string StatusText { get; set; } = "Starting terminal…";

    [ObservableProperty]
    public partial bool IsRunning { get; set; }

    [ObservableProperty]
    public partial string ShellName { get; set; } = "PowerShell";

    public PtySession? Session => _host.Session;

    public event EventHandler? SessionReplaced;

    public async Task InitializeAsync(int cols, int rows)
    {
        _cols = cols;
        _rows = rows;
        if (string.IsNullOrEmpty(_shellExe))
        {
            _shellExe = await ResolveShellAsync();
        }

        var session = _host.EnsureRunning(_shellExe, cols, rows);
        session.Exited -= OnExited;
        session.Exited += OnExited;
        RefreshStatus();
        SessionReplaced?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    public void Restart()
    {
        if (string.IsNullOrEmpty(_shellExe))
        {
            return;
        }

        var session = _host.StartNew(_shellExe, _cols, _rows);
        session.Exited -= OnExited;
        session.Exited += OnExited;
        RefreshStatus();
        SessionReplaced?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    public void Kill()
    {
        // Disposing the session terminates the child and — via the job
        // object — its whole process tree.
        _host.Session?.Dispose();
        RefreshStatus();
    }

    private void OnExited(object? sender, int code) =>
        _dispatcher.TryEnqueue(RefreshStatus);

    private void RefreshStatus()
    {
        var session = _host.Session;
        IsRunning = session?.IsRunning == true;
        StatusText = session switch
        {
            null => "No session.",
            { IsRunning: true } => $"Running: {ShellName}",
            _ => $"Process exited (code {session.ExitCode}). Restart for a new session.",
        };
    }

    /// <summary>
    /// Reuses the Phase 2 preferred-terminal setting (the existing terminal
    /// abstraction): "Windows Terminal" is a host app, not a shell, so it
    /// maps to its default shell, PowerShell.
    /// </summary>
    private async Task<string> ResolveShellAsync()
    {
        var saved = await _settings.LoadAsync<TerminalBackendsViewModel.TerminalSettings>(
            TerminalBackendsViewModel.SettingsName);
        if (saved?.PreferredTerminal == "Command Prompt")
        {
            ShellName = "Command Prompt";
            return @"C:\Windows\System32\cmd.exe";
        }

        ShellName = "PowerShell";
        return @"C:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe";
    }
}
