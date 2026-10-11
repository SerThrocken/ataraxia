using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Hermes.Client.Core;

/// <summary>
/// Session-handoff payload contract (v1). See HANDOFF-QR.md for the full
/// contract, token flow, and threat model. The QR encodes this JSON — it
/// NEVER contains the gateway API key. The Android scanner (worker F) reads
/// this same contract; keep it stable.
/// </summary>
public sealed record HandoffPayload(
    [property: JsonPropertyName("v")] int Version,
    [property: JsonPropertyName("gateway")] string GatewayUrl,
    [property: JsonPropertyName("session_id")] string SessionId,
    [property: JsonPropertyName("token")] string Token,
    [property: JsonPropertyName("exp")] long ExpiresAtUnix,
    [property: JsonPropertyName("label")] string? Label);

/// <summary>
/// Builds and validates <see cref="HandoffPayload"/> values. Validation is
/// shared by the QR generator (Windows) and the QR scanner (Android) so both
/// sides reject the same malformed payloads.
/// </summary>
public static class HandoffPayloadCodec
{
    public const int CurrentVersion = 1;

    /// <summary>Maximum token lifetime the contract allows (15 minutes).</summary>
    public static readonly TimeSpan MaxTokenLifetime = TimeSpan.FromMinutes(15);

    /// <summary>Minimum token entropy: 256 bits as base64url (43 chars, no padding).</summary>
    public const int MinTokenLength = 43;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static string Encode(HandoffPayload payload)
    {
        Validate(payload, DateTimeOffset.UtcNow);
        return JsonSerializer.Serialize(payload, Json);
    }

    public static HandoffPayload Decode(string json, DateTimeOffset? now = null)
    {
        HandoffPayload payload;
        try
        {
            payload = JsonSerializer.Deserialize<HandoffPayload>(json, Json)
                ?? throw new HandoffPayloadException("Payload is empty.");
        }
        catch (JsonException ex)
        {
            throw new HandoffPayloadException("Payload is not valid JSON.", ex);
        }

        Validate(payload, now ?? DateTimeOffset.UtcNow);
        return payload;
    }

    public static void Validate(HandoffPayload payload, DateTimeOffset now)
    {
        if (payload.Version != CurrentVersion)
        {
            throw new HandoffPayloadException($"Unsupported payload version {payload.Version} (expected {CurrentVersion}).");
        }

        if (!Uri.TryCreate(payload.GatewayUrl, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            throw new HandoffPayloadException("Gateway URL must be an absolute http(s) URL.");
        }

        if (string.IsNullOrWhiteSpace(payload.SessionId))
        {
            throw new HandoffPayloadException("Session id is required.");
        }

        if (payload.Token is null || payload.Token.Length < MinTokenLength)
        {
            throw new HandoffPayloadException($"Token must carry at least 256 bits of entropy ({MinTokenLength} base64url chars).");
        }

        var expiresAt = DateTimeOffset.FromUnixTimeSeconds(payload.ExpiresAtUnix);
        if (expiresAt <= now)
        {
            throw new HandoffPayloadException("Token is expired.");
        }

        if (expiresAt - now > MaxTokenLifetime)
        {
            throw new HandoffPayloadException($"Token lifetime exceeds the {MaxTokenLifetime.TotalMinutes:0}-minute contract maximum.");
        }

        if (payload.Label is { Length: > 64 })
        {
            throw new HandoffPayloadException("Label is longer than 64 characters.");
        }
    }
}

public sealed class HandoffPayloadException : Exception
{
    public HandoffPayloadException(string message) : base(message) { }
    public HandoffPayloadException(string message, Exception inner) : base(message, inner) { }
}

/// <summary>
/// Mints single-use pairing tokens: 256-bit CSPRNG, base64url, no padding.
/// The token is a bearer for ONE redemption of ONE session handoff — it is
/// not the gateway API key and must never be persisted or logged.
/// </summary>
public static class HandoffToken
{
    public static string Mint(int bytes = 32)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(bytes, 32);
        return Convert.ToBase64String(RandomNumberGenerator.GetBytes(bytes))
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');
    }
}

/// <summary>
/// Desktop-side registry of outstanding handoff tokens: mint on QR display,
/// consume on first redemption, expire on TTL. Single-use is enforced here;
/// the gateway enforces it again at redeem time (see HANDOFF-QR.md).
/// Thread-safe; tokens live in memory only.
/// </summary>
public sealed class HandoffTokenStore
{
    private readonly record struct Entry(string SessionId, string GatewayUrl, DateTimeOffset ExpiresAt);
    private readonly Dictionary<string, Entry> _tokens = new(StringComparer.Ordinal);
    private readonly Func<DateTimeOffset> _now;

    public HandoffTokenStore(Func<DateTimeOffset>? now = null) =>
        _now = now ?? (static () => DateTimeOffset.UtcNow);

    public (string Token, DateTimeOffset ExpiresAt) Mint(
        string sessionId, string gatewayUrl, TimeSpan? ttl = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(gatewayUrl);
        var lifetime = ttl ?? TimeSpan.FromMinutes(2);
        if (lifetime <= TimeSpan.Zero || lifetime > HandoffPayloadCodec.MaxTokenLifetime)
        {
            throw new ArgumentOutOfRangeException(nameof(ttl), "Token TTL must be positive and within the contract maximum.");
        }

        lock (_tokens)
        {
            SweepLocked();
            var token = HandoffToken.Mint();
            var expiresAt = _now().Add(lifetime);
            _tokens[token] = new Entry(sessionId, gatewayUrl, expiresAt);
            return (token, expiresAt);
        }
    }

    /// <summary>
    /// Atomically consumes a token. Returns the bound session/gateway on
    /// success; null when unknown, already consumed, or expired.
    /// </summary>
    public (string SessionId, string GatewayUrl)? TryConsume(string token)
    {
        lock (_tokens)
        {
            SweepLocked();
            if (!_tokens.Remove(token, out var entry))
            {
                return null;
            }

            return (entry.SessionId, entry.GatewayUrl);
        }
    }

    public bool IsOutstanding(string token)
    {
        lock (_tokens)
        {
            SweepLocked();
            return _tokens.ContainsKey(token);
        }
    }

    public void Revoke(string token)
    {
        lock (_tokens)
        {
            _tokens.Remove(token);
        }
    }

    private void SweepLocked()
    {
        var now = _now();
        foreach (var key in _tokens.Where(kv => kv.Value.ExpiresAt <= now).Select(kv => kv.Key).ToList())
        {
            _tokens.Remove(key);
        }
    }
}
