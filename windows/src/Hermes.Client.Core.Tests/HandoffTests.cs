using Xunit;
using Hermes.Client.Core;

namespace Hermes.Client.Core.Tests;

public sealed class HandoffTests
{
    // Anchored to real time: Encode validates against DateTimeOffset.UtcNow,
    // so the fixture expiry must be relative to the actual clock.
    private readonly DateTimeOffset _now = DateTimeOffset.UtcNow;
    private DateTimeOffset Now => _now;

    private HandoffPayload ValidPayload() => new(
        Version: 1,
        GatewayUrl: "http://100.101.250.118:8642",
        SessionId: "sess_abc123",
        Token: HandoffToken.Mint(),
        ExpiresAtUnix: Now.AddMinutes(2).ToUnixTimeSeconds(),
        Label: "Thomas's Desktop");

    [Fact]
    public void EncodeDecodeRoundTrips()
    {
        var payload = ValidPayload();
        var json = HandoffPayloadCodec.Encode(payload);
        var decoded = HandoffPayloadCodec.Decode(json, Now);
        Assert.Equal(payload, decoded);
    }

    [Fact]
    public void EncodedPayloadNeverContainsApiKeyMaterial()
    {
        // The contract forbids the API key in the QR; the payload shape has
        // no field for it, so a key can never sneak in via serialization.
        var json = HandoffPayloadCodec.Encode(ValidPayload());
        Assert.DoesNotContain("api_key", json);
        Assert.DoesNotContain("apikey", json);
        Assert.Contains("\"token\"", json);
        Assert.Contains("\"gateway\"", json);
        Assert.Contains("\"session_id\"", json);
    }

    [Fact]
    public void DecodeRejectsWrongVersion() =>
        Assert.Throws<HandoffPayloadException>(() =>
            HandoffPayloadCodec.Decode(HandoffPayloadCodec.Encode(ValidPayload() with { Version = 2 }), Now));

    [Fact]
    public void DecodeRejectsNonHttpGateway() =>
        Assert.Throws<HandoffPayloadException>(() =>
            HandoffPayloadCodec.Decode(HandoffPayloadCodec.Encode(ValidPayload() with { GatewayUrl = "ftp://x/y" }), Now));

    [Fact]
    public void DecodeRejectsShortToken() =>
        Assert.Throws<HandoffPayloadException>(() =>
            HandoffPayloadCodec.Decode(
                """{"v":1,"gateway":"http://100.101.250.118:8642","session_id":"s","token":"short","exp":9999999999}""",
                Now));

    [Fact]
    public void DecodeRejectsExpiredToken() =>
        Assert.Throws<HandoffPayloadException>(() =>
            HandoffPayloadCodec.Decode(HandoffPayloadCodec.Encode(ValidPayload()), Now.AddMinutes(3)));

    [Fact]
    public void DecodeRejectsOverlongLifetime() =>
        Assert.Throws<HandoffPayloadException>(() =>
            HandoffPayloadCodec.Decode(
                HandoffPayloadCodec.Encode(ValidPayload() with { ExpiresAtUnix = Now.AddHours(1).ToUnixTimeSeconds() }),
                Now));

    [Fact]
    public void DecodeRejectsGarbage() =>
        Assert.Throws<HandoffPayloadException>(() => HandoffPayloadCodec.Decode("not json", Now));

    [Fact]
    public void MintedTokensAreUniqueAndLongEnough()
    {
        var tokens = Enumerable.Range(0, 100).Select(_ => HandoffToken.Mint()).ToList();
        Assert.Equal(100, tokens.Distinct(StringComparer.Ordinal).Count());
        Assert.All(tokens, t => Assert.True(t.Length >= HandoffPayloadCodec.MinTokenLength));
    }

    [Fact]
    public void StoreConsumeIsSingleUse()
    {
        var now = Now;
        var store = new HandoffTokenStore(() => now);
        var (token, _) = store.Mint("sess_1", "http://100.101.250.118:8642");
        Assert.True(store.IsOutstanding(token));

        var consumed = store.TryConsume(token);
        Assert.Equal(("sess_1", "http://100.101.250.118:8642"), consumed);
        Assert.False(store.IsOutstanding(token));
        Assert.Null(store.TryConsume(token)); // second use fails
    }

    [Fact]
    public void StoreExpiresTokens()
    {
        var now = Now;
        var store = new HandoffTokenStore(() => now);
        var (token, _) = store.Mint("sess_1", "http://100.101.250.118:8642", TimeSpan.FromMinutes(2));
        now = now.AddMinutes(3);
        Assert.Null(store.TryConsume(token));
        Assert.False(store.IsOutstanding(token));
    }

    [Fact]
    public void StoreRejectsUnknownToken()
    {
        var store = new HandoffTokenStore(() => Now);
        Assert.Null(store.TryConsume(HandoffToken.Mint()));
    }

    [Fact]
    public void StoreRejectsExcessiveTtl() =>
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new HandoffTokenStore(() => Now).Mint("s", "http://x", TimeSpan.FromHours(1)));
}
