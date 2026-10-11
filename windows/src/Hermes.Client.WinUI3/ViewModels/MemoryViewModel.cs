using CommunityToolkit.Mvvm.ComponentModel;

namespace Hermes.Client.WinUI3.ViewModels;

/// <summary>
/// Memory and vault. The gateway reports memory_write_api=false and exposes
/// no /v1/memory or /v1/vault routes (verified on 0.21.6, 2026-10-11), so
/// this page is an honest empty state until a future gateway adds memory APIs.
/// </summary>
public sealed partial class MemoryViewModel : ObservableObject
{
    public string Notice =>
        "Memory and vault are not available: the gateway reports "
        + "memory_write_api=false and exposes no /v1/memory or /v1/vault routes "
        + "(0.21.6, verified 2026-10-11).";

    public string Detail =>
        "When a future gateway adds memory APIs, saved memories and vault "
        + "entries will appear here.";
}
