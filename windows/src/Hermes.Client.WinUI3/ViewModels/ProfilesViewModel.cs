using CommunityToolkit.Mvvm.ComponentModel;

namespace Hermes.Client.WinUI3.ViewModels;

/// <summary>
/// User profiles. The gateway exposes no profile routes at all (0.21.6,
/// verified 2026-10-11), so this page is an honest empty state until a
/// future gateway adds profile APIs.
/// </summary>
public sealed partial class ProfilesViewModel : ObservableObject
{
    public string Notice =>
        "User profiles are not available: the gateway exposes no profile API "
        + "(0.21.6, verified 2026-10-11).";

    public string Detail =>
        "When a future gateway adds profile routes, profile management will "
        + "appear here.";
}
