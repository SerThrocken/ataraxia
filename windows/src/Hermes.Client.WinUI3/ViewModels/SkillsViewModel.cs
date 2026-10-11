using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Hermes.Client.Core;
using Hermes.Client.Infrastructure;

namespace Hermes.Client.WinUI3.ViewModels;

/// <summary>
/// Skills browser: GET /v1/skills (list) + GET /v1/toolsets (server tool
/// bundles with enabled/configured flags). Verified against gateway 0.21.6
/// (2026-10-11):
/// <list type="bullet">
/// <item>There is no skill enable/disable route — the toggle below is a
/// client-local "hidden" preference, persisted in settings.</item>
/// <item>There is no SKILL.md content route — the detail panel shows the
/// skill's description and says so.</item>
/// <item>There is no hub/install route — installing from a hub is not offered.</item>
/// <item>Toolsets carry an `enabled` flag but no toggle route (PATCH 404s);
/// they are shown read-only.</item>
/// </list>
/// </summary>
public sealed partial class SkillsViewModel : ObservableObject
{
    public const string HiddenSkillsSettingsName = "skills-hidden";

    private readonly IGatewayClient _client;
    private readonly ISettingsService _settings;

    public SkillsViewModel(IGatewayClient client, ISettingsService settings)
    {
        _client = client;
        _settings = settings;
    }

    public ObservableCollection<SkillRow> Skills { get; } = new();

    public ObservableCollection<ToolsetInfo> Toolsets { get; } = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    public partial string? Error { get; set; }

    public bool HasError => Error is not null;

    [ObservableProperty]
    public partial bool IsLoading { get; set; }

    /// <summary>
    /// Non-error notice, e.g. the /v1/skills 500 the gateway serves today.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SkillsNoticeVisible))]
    public partial string? SkillsNotice { get; set; }

    public bool SkillsNoticeVisible => SkillsNotice is not null;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SkillDetail))]
    public partial SkillRow? SelectedSkill { get; set; }

    /// <summary>
    /// Detail panel text for the selected skill: description plus the
    /// verified reason no SKILL.md body can be shown.
    /// </summary>
    public string SkillDetail => SelectedSkill is null
        ? "Select a skill to see details."
        : SelectedSkill.Name + "\n\n"
            + (SelectedSkill.Description ?? "No description.") + "\n\n"
            + "SKILL.md content is not available: the gateway exposes no skill-content "
            + "route (no /v1/skills/{name}/content on 0.21.6, verified 2026-10-11).";

    public bool HasSkills => Skills.Count > 0;

    [RelayCommand]
    public async Task RefreshAsync()
    {
        if (IsLoading)
        {
            return;
        }

        IsLoading = true;
        Error = null;
        SkillsNotice = null;
        try
        {
            var hidden = await _settings.LoadAsync<HashSet<string>>(HiddenSkillsSettingsName)
                ?? new HashSet<string>(StringComparer.Ordinal);

            Skills.Clear();
            try
            {
                foreach (var skill in await _client.GetSkillsAsync())
                {
                    var row = new SkillRow(skill.Name, skill.Description)
                    {
                        IsHidden = hidden.Contains(skill.Name),
                    };
                    row.PropertyChanged += (_, e) =>
                    {
                        if (e.PropertyName == nameof(SkillRow.IsHidden))
                        {
                            _ = PersistHiddenAsync();
                        }
                    };
                    Skills.Add(row);
                }
            }
            catch (GatewayApiException ex) when (ex.StatusCode == 500)
            {
                // Gateway-side bug (verified 2026-10-11): /v1/skills 500s.
                // Not fatal — toolsets below still load.
                SkillsNotice = "The skill list is unavailable: the gateway's /v1/skills "
                    + "endpoint returns HTTP 500 (server-side bug, verified 2026-10-11). "
                    + "Toolsets below are live.";
            }

            Toolsets.Clear();
            foreach (var toolset in await _client.GetToolsetsAsync())
            {
                Toolsets.Add(toolset);
            }

            OnPropertyChanged(nameof(HasSkills));
        }
        catch (Exception ex)
        {
            Error = FriendlyError(ex);
        }
        finally
        {
            IsLoading = false;
        }
    }

    private async Task PersistHiddenAsync()
    {
        var hidden = new HashSet<string>(
            Skills.Where(s => s.IsHidden).Select(s => s.Name),
            StringComparer.Ordinal);
        await _settings.SaveAsync(HiddenSkillsSettingsName, hidden);
    }

    private static string FriendlyError(Exception ex) => ex switch
    {
        GatewayApiException { StatusCode: 401 } =>
            "Invalid gateway API key. Check the connection settings.",
        GatewayApiException api => $"Gateway error {api.StatusCode}: {api.Message}",
        HttpRequestException => "Cannot reach the gateway. Check the connection settings.",
        _ => ex.Message,
    };
}
