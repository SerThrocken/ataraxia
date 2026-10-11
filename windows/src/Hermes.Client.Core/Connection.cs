using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

namespace Hermes.Client.Core;

/// <summary>
/// Precise outcome of a gateway connectivity test, so the UI can tell
/// "wrong key" (401) apart from "can't reach the box" and "timed out".
/// </summary>
public enum ConnectionTestOutcome
{
    Success,
    Unauthorized,
    Unreachable,
    TimedOut,
    HttpError,
    InvalidResponse,
}

/// <summary>
/// GET /health shape — verified 2026-10-11 against gateway 0.21.6.
/// </summary>
public sealed record HealthInfo(string Status, string Platform, string Version);

/// <summary>
/// Minimal runtime read of GET /v1/capabilities: platform, enabled
/// top-level feature flags, endpoint count. The contract is
/// self-describing; the client reads it instead of hardcoding paths.
/// </summary>
public sealed record GatewayCapabilities(
    string Platform,
    IReadOnlyList<string> EnabledFeatures,
    int EndpointCount);

public sealed record ConnectionTestResult(
    ConnectionTestOutcome Outcome,
    string Message,
    HealthInfo? Health = null,
    GatewayCapabilities? Capabilities = null)
{
    public bool Succeeded => Outcome == ConnectionTestOutcome.Success;
}

/// <summary>
/// App-wide connection state: untested (no key stored yet), connected,
/// or disconnected (last test failed).
/// </summary>
public enum ConnectionState
{
    Untested,
    Connected,
    Disconnected,
}

public interface IConnectionState
{
    ConnectionState State { get; }
    event EventHandler<ConnectionState>? StateChanged;
    void SetState(ConnectionState state);
}

public sealed class ConnectionStateService : IConnectionState
{
    private ConnectionState _state = ConnectionState.Untested;

    public ConnectionState State => _state;

    public event EventHandler<ConnectionState>? StateChanged;

    public void SetState(ConnectionState state)
    {
        if (_state == state)
        {
            return;
        }

        _state = state;
        StateChanged?.Invoke(this, state);
    }
}

/// <summary>
/// Client-side validation for the connect screen. The server refuses keys
/// shorter than 16 chars (api_server.py: has_usable_secret, min_length=16).
/// </summary>
public static class ConnectionValidation
{
    public const int MinApiKeyLength = 16;

    public static bool IsValidBaseUrl(string? value) =>
        Uri.TryCreate(value?.Trim(), UriKind.Absolute, out var uri) &&
        (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);

    public static bool IsValidApiKey(string? value) =>
        !string.IsNullOrWhiteSpace(value) && value.Trim().Length >= MinApiKeyLength;
}

/// <summary>
/// Connectivity test: GET /health (no auth), then GET /v1/capabilities
/// (Bearer). Failures are classified precisely: 401 vs unreachable vs
/// timeout vs other HTTP errors.
/// </summary>
public sealed class ConnectionTester
{
    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(10);

    public async Task<ConnectionTestResult> TestAsync(
        Uri baseUrl, string? apiKey, CancellationToken cancellationToken = default)
    {
        using var http = new HttpClient { BaseAddress = baseUrl };
        if (!string.IsNullOrWhiteSpace(apiKey))
        {
            http.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", apiKey.Trim());
        }

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(TestTimeout);
        var ct = cts.Token;

        try
        {
            var health = await GetHealthAsync(http, ct).ConfigureAwait(false);
            var capabilities = await GetCapabilitiesAsync(http, ct).ConfigureAwait(false);
            return new ConnectionTestResult(
                ConnectionTestOutcome.Success,
                $"Connected — gateway {health.Version} ({health.Platform}).",
                health,
                capabilities);
        }
        catch (ConnectionTestFailureException ex)
        {
            return ex.Result;
        }
        catch (HttpRequestException ex)
        {
            return Fail(ConnectionTestOutcome.Unreachable,
                $"Cannot reach the gateway at {baseUrl}: {Trim(ex.Message)}");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return Fail(ConnectionTestOutcome.TimedOut,
                $"Timed out after {TestTimeout.TotalSeconds:0} seconds waiting for {baseUrl}.");
        }
        catch (JsonException ex)
        {
            return Fail(ConnectionTestOutcome.InvalidResponse,
                $"The gateway returned an unexpected response: {Trim(ex.Message)}");
        }
    }

    private static async Task<HealthInfo> GetHealthAsync(HttpClient http, CancellationToken ct)
    {
        using var response = await http.GetAsync("/health", ct).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new ConnectionTestFailureException(Fail(
                response.StatusCode == HttpStatusCode.Unauthorized
                    ? ConnectionTestOutcome.Unauthorized
                    : ConnectionTestOutcome.HttpError,
                await HttpErrorMessageAsync("/health", response, ct).ConfigureAwait(false)));
        }

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false));
        var root = doc.RootElement;
        return new HealthInfo(
            root.GetProperty("status").GetString() ?? "?",
            root.GetProperty("platform").GetString() ?? "?",
            root.GetProperty("version").GetString() ?? "?");
    }

    private static async Task<GatewayCapabilities> GetCapabilitiesAsync(HttpClient http, CancellationToken ct)
    {
        using var response = await http.GetAsync("/v1/capabilities", ct).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            throw new ConnectionTestFailureException(Fail(
                ConnectionTestOutcome.Unauthorized,
                "Invalid API key (401). Check the key and try again."));
        }

        if (!response.IsSuccessStatusCode)
        {
            throw new ConnectionTestFailureException(Fail(
                ConnectionTestOutcome.HttpError,
                await HttpErrorMessageAsync("/v1/capabilities", response, ct).ConfigureAwait(false)));
        }

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false));
        var root = doc.RootElement;
        var enabled = new List<string>();
        if (root.TryGetProperty("features", out var features) &&
            features.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in features.EnumerateObject())
            {
                if (property.Value.ValueKind == JsonValueKind.True)
                {
                    enabled.Add(property.Name);
                }
            }
        }

        var endpointCount = 0;
        if (root.TryGetProperty("endpoints", out var endpoints) &&
            endpoints.ValueKind == JsonValueKind.Object)
        {
            foreach (var _ in endpoints.EnumerateObject())
            {
                endpointCount++;
            }
        }

        return new GatewayCapabilities(
            root.GetProperty("platform").GetString() ?? "?",
            enabled,
            endpointCount);
    }

    private static async Task<string> HttpErrorMessageAsync(
        string path, HttpResponseMessage response, CancellationToken ct)
    {
        var body = Trim(await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false));
        return $"GET {path} failed: HTTP {(int)response.StatusCode} {response.ReasonPhrase}."
            + (string.IsNullOrEmpty(body) ? string.Empty : $" Body: {body}");
    }

    private static ConnectionTestResult Fail(ConnectionTestOutcome outcome, string message) =>
        new(outcome, message);

    private static string Trim(string? value, int maxLength = 160) =>
        string.IsNullOrEmpty(value)
            ? string.Empty
            : value.Length <= maxLength ? value : value[..maxLength] + "…";

    private sealed class ConnectionTestFailureException(ConnectionTestResult result) : Exception
    {
        public ConnectionTestResult Result { get; } = result;
    }
}
