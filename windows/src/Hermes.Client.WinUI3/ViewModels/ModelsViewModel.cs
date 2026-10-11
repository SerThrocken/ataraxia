using CommunityToolkit.Mvvm.ComponentModel;

namespace Hermes.Client.WinUI3.ViewModels;

public sealed partial class ModelsViewModel : ObservableObject
{
    [ObservableProperty]
    public partial string Status { get; set; } = "Model list (GET /v1/models) lands in Phase 1 with the protocol client.";
}
