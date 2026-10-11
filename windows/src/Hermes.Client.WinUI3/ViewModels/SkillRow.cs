using CommunityToolkit.Mvvm.ComponentModel;

namespace Hermes.Client.WinUI3.ViewModels;

/// <summary>
/// One skill row in the skills browser. <see cref="IsHidden"/> is a
/// client-local preference (the gateway exposes no skill toggle route on
/// 0.21.6): hidden skills are dimmed here and skipped by the command
/// palette's skill suggestions.
/// </summary>
public sealed partial class SkillRow : ObservableObject
{
    public SkillRow(string name, string? description)
    {
        Name = name;
        Description = description;
    }

    public string Name { get; }

    public string? Description { get; }

    [ObservableProperty]
    public partial bool IsHidden { get; set; }
}
