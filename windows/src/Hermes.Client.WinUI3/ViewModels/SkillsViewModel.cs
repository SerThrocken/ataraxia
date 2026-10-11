using CommunityToolkit.Mvvm.ComponentModel;

namespace Hermes.Client.WinUI3.ViewModels;

public sealed partial class SkillsViewModel : ObservableObject
{
    [ObservableProperty]
    public partial string Status { get; set; } = "Skill browser (GET /v1/skills) lands in Phase 1 with the protocol client.";
}
