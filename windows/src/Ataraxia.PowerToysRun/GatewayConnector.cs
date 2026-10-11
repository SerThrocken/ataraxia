using System.Net.Http;
using Hermes.Client.Core;
using Hermes.Client.Infrastructure;

namespace Ataraxia.PowerToysRun;

/// <summary>
/// Builds the shared <see cref="GatewayClient"/> from the same sources the
/// Ataraxia app uses — no duplicated secret handling:
/// <list type="bullet">
/// <item>The API key comes from the shared DPAPI secret store
/// (<c>gateway-api-key</c>, the same name the app's settings screen writes).</item>
/// <item>The gateway URL comes from the plugin's <c>settings.json</c> override,
/// else the app's saved <c>connection</c> blob, else the app's default.</item>
/// </list>
/// PowerToys calls <c>Query</c> synchronously, so calls here block with a
/// bounded timeout.
/// </summary>
internal sealed class GatewayConnector : IDisposable
{
    // Same names the WinUI3 app uses (its SettingsViewModel constants).
    private const string ApiKeySecretName = "gateway-api-key";
    private const string ConnectionSettingsName = "connection";
    private const string DefaultBaseUrl = "http://100.101.250.118:8642";

    private static readonly TimeSpan CallTimeout = TimeSpan.FromSeconds(12);

    private readonly string _pluginDirectory;
    private GatewayClient? _client;
    private bool _disposed;

    public GatewayConnector(string pluginDirectory)
    {
        _pluginDirectory = pluginDirectory;
    }

    public IReadOnlyList<Session> GetSessions() =>
        Call(static (c, t) => c.GetSessionsAsync(t));

    public Session CreateSession(string? title) =>
        Call((c, t) => c.CreateSessionAsync(title, t));

    public void SendChat(string sessionId, string message) =>
        Call((c, t) => c.SendChatAsync(sessionId, message, t));

    private T Call<T>(Func<GatewayClient, CancellationToken, Task<T>> call)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        using var cts = new CancellationTokenSource(CallTimeout);
        return call(Client(), cts.Token).GetAwaiter().GetResult();
    }

    private GatewayClient Client()
    {
        if (_client is not null)
        {
            return _client;
        }

        var overrides = PluginSettings.Load(_pluginDirectory);
        var shared = new JsonSettingsService()
            .LoadAsync<SharedConnectionSettings>(ConnectionSettingsName)
            .GetAwaiter().GetResult();
        var url = FirstNonEmpty(overrides.GatewayUrl, shared?.BaseUrl, DefaultBaseUrl);
        var key = new DpapiSecretStore()
            .GetAsync(ApiKeySecretName)
            .GetAwaiter().GetResult();

        var client = new GatewayClient(
            new HttpClient { Timeout = CallTimeout },
            new GatewayOptions { BaseUrl = new Uri(url) });
        client.SetApiKey(key);
        return _client = client;
    }

    private static string FirstNonEmpty(params string?[] values)
    {
        foreach (var v in values)
        {
            if (!string.IsNullOrWhiteSpace(v))
            {
                return v.Trim();
            }
        }

        return DefaultBaseUrl;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _client?.DisposeAsync().GetAwaiter().GetResult();
    }
}
