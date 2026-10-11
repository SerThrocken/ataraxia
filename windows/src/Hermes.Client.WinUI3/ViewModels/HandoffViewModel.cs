using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Hermes.Client.Core;
using Hermes.Client.WinUI3.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media.Imaging;

namespace Hermes.Client.WinUI3.ViewModels;

/// <summary>
/// Session handoff: mints a short-lived single-use pairing token, encodes
/// {gateway URL, session id, token} as a QR code for the Android app to scan.
/// The QR never contains the gateway API key. See HANDOFF-QR.md.
/// </summary>
public sealed partial class HandoffViewModel : ObservableObject
{
    private static readonly TimeSpan TokenLifetime = TimeSpan.FromMinutes(2);

    private readonly HandoffTokenStore _tokenStore;
    private readonly IQrCodeRenderer _qrRenderer;
    private readonly IGatewayClient _gateway;
    private readonly string _gatewayUrl;

    [ObservableProperty]
    public partial BitmapImage? QrImage { get; set; }

    [ObservableProperty]
    public partial string StatusText { get; set; } = "Generate a code to hand this session to your phone.";

    [ObservableProperty]
    public partial string ExpiresText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string SessionLabel { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsExpired { get; set; }

    private string _sessionId = string.Empty;
    private DateTimeOffset _expiresAt;
    private string? _currentToken;
    private readonly DispatcherTimer _timer;

    public HandoffViewModel(
        HandoffTokenStore tokenStore,
        IQrCodeRenderer qrRenderer,
        IGatewayClient gateway,
        GatewayOptions gatewayOptions)
    {
        _tokenStore = tokenStore;
        _qrRenderer = qrRenderer;
        _gateway = gateway;
        _gatewayUrl = gatewayOptions.BaseUrl?.ToString().TrimEnd('/') ?? string.Empty;

        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += (_, _) => UpdateCountdown();
    }

    public void Initialize(string sessionId)
    {
        _sessionId = sessionId;
        SessionLabel = $"Session {sessionId}";
    }

    [RelayCommand]
    public async Task GenerateAsync()
    {
        try
        {
            if (string.IsNullOrWhiteSpace(_sessionId))
            {
                // No session was passed in: hand off the most recent one.
                var latest = (await _gateway.GetSessionsAsync()).FirstOrDefault();
                if (latest is null)
                {
                    StatusText = "No sessions on the gateway yet — start a chat first, then hand it off.";
                    return;
                }

                _sessionId = latest.Id;
                SessionLabel = $"Session {latest.Id}";
            }

            if (_currentToken is not null)
            {
                _tokenStore.Revoke(_currentToken);
            }

            var (token, expiresAt) = _tokenStore.Mint(_sessionId, _gatewayUrl, TokenLifetime);
            _currentToken = token;
            _expiresAt = expiresAt;

            var payload = new HandoffPayload(
                HandoffPayloadCodec.CurrentVersion,
                _gatewayUrl,
                _sessionId,
                token,
                expiresAt.ToUnixTimeSeconds(),
                Environment.MachineName);
            var json = HandoffPayloadCodec.Encode(payload);

            StatusText = "Scan with the Ataraxia Android app. Single use — expires in 2 minutes.";
            IsExpired = false;
            QrImage = await _qrRenderer.RenderAsync(json);
            _timer.Start();
            UpdateCountdown();
        }
        catch (Exception ex) when (ex is HttpRequestException or HandoffPayloadException or GatewayApiException)
        {
            StatusText = $"Couldn't generate the code: {ex.Message}";
        }
    }

    private void UpdateCountdown()
    {
        var remaining = _expiresAt - DateTimeOffset.UtcNow;
        if (remaining <= TimeSpan.Zero)
        {
            _timer.Stop();
            IsExpired = true;
            ExpiresText = "Expired";
            StatusText = "This code expired. Generate a new one to hand off.";
            if (_currentToken is not null)
            {
                _tokenStore.Revoke(_currentToken);
                _currentToken = null;
            }

            QrImage = null;
            return;
        }

        ExpiresText = $"Expires in {remaining:m\\:ss}";
    }
}
