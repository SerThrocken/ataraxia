using CommunityToolkit.Mvvm.ComponentModel;
using Hermes.Client.Core;
using Hermes.Client.Infrastructure;

namespace Hermes.Client.WinUI3.ViewModels;

/// <summary>
/// Voice I/O controls. The gateway reports audio_api=false and
/// realtime_voice=false and exposes no /v1/voice or /v1/audio routes
/// (verified on 0.21.6, 2026-10-11), so these controls are stored locally
/// and take effect if a future gateway adds voice.
/// </summary>
public sealed partial class VoiceViewModel : ObservableObject
{
    public const string SettingsName = "voice-settings";

    private readonly ISettingsService _settings;

    public VoiceViewModel(ISettingsService settings)
    {
        _settings = settings;
    }

    [ObservableProperty]
    public partial bool VoiceInputEnabled { get; set; }

    [ObservableProperty]
    public partial bool VoiceOutputEnabled { get; set; }

    [ObservableProperty]
    public partial string TtsVoiceName { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string MicrophoneDevice { get; set; } = string.Empty;

    public string Notice =>
        "Voice I/O is not available: this gateway reports audio_api=false and "
        + "realtime_voice=false and has no /v1/voice or /v1/audio routes "
        + "(0.21.6, verified 2026-10-11). Settings below are stored locally.";

    private bool _initialized;

    public async Task InitializeAsync()
    {
        if (_initialized)
        {
            return;
        }

        _initialized = true;
        var saved = await _settings.LoadAsync<VoiceSettings>(SettingsName);
        if (saved is not null)
        {
            VoiceInputEnabled = saved.InputEnabled;
            VoiceOutputEnabled = saved.OutputEnabled;
            TtsVoiceName = saved.TtsVoiceName ?? string.Empty;
            MicrophoneDevice = saved.MicrophoneDevice ?? string.Empty;
        }

        PropertyChanged += async (_, e) =>
        {
            if (e.PropertyName is nameof(VoiceInputEnabled) or nameof(VoiceOutputEnabled)
                or nameof(TtsVoiceName) or nameof(MicrophoneDevice))
            {
                await _settings.SaveAsync(SettingsName, new VoiceSettings(
                    VoiceInputEnabled, VoiceOutputEnabled, TtsVoiceName, MicrophoneDevice));
            }
        };
    }

    public sealed record VoiceSettings(
        bool InputEnabled,
        bool OutputEnabled,
        string? TtsVoiceName,
        string? MicrophoneDevice);
}
