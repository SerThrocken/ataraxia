// Connection-flow smoke test. Runs ON the Acer (the gateway host).
// The API key is read from the gateway's own .env into process memory only —
// it is never printed, logged, or written anywhere by this program.
using Hermes.Client.Core;
using Hermes.Client.Infrastructure;

var failures = new List<string>();
void Check(bool ok, string name)
{
    Console.WriteLine((ok ? "PASS" : "FAIL") + "  " + name);
    if (!ok)
    {
        failures.Add(name);
    }
}

var apiKey = ReadApiServerKey();
Check(!string.IsNullOrEmpty(apiKey), "API_SERVER_KEY present in gateway .env (memory only, never printed)");
Check(ConnectionValidation.IsValidApiKey(apiKey), "stored key passes client-side validation (>=16 chars)");
Check(!ConnectionValidation.IsValidApiKey("too-short"), "short key rejected client-side");
Check(!ConnectionValidation.IsValidApiKey(null), "missing key rejected client-side");
Check(ConnectionValidation.IsValidBaseUrl("http://100.101.250.118:8642"), "tailscale URL valid");
Check(ConnectionValidation.IsValidBaseUrl("http://127.0.0.1:8642"), "loopback URL valid");
Check(!ConnectionValidation.IsValidBaseUrl("not a url"), "bad URL rejected");
Check(!ConnectionValidation.IsValidBaseUrl("ftp://example.com/x"), "non-http URL rejected");

var tester = new ConnectionTester();

var live = await tester.TestAsync(new Uri("http://100.101.250.118:8642"), apiKey);
Console.WriteLine($"live: {live.Outcome} — {live.Message}");
Check(live.Succeeded, "live connectivity test succeeds");
Check(live.Health?.Version == "0.21.6", $"gateway version is 0.21.6 (got '{live.Health?.Version}')");
Check(live.Capabilities?.EnabledFeatures.Count > 0, "capabilities parsed (enabled features > 0)");
Check(live.Capabilities?.EndpointCount > 0, "capabilities parsed (endpoint count > 0)");
Console.WriteLine("enabled features: " + string.Join(", ", live.Capabilities?.EnabledFeatures.Take(8) ?? []));

var loopback = await tester.TestAsync(new Uri("http://127.0.0.1:8642"), apiKey);
Check(loopback.Succeeded && loopback.Health?.Version == "0.21.6", "loopback fallback also reaches the gateway");

var badKey = await tester.TestAsync(new Uri("http://100.101.250.118:8642"), "wrong-key-0000000000");
Check(badKey.Outcome == ConnectionTestOutcome.Unauthorized,
    $"wrong key classifies as Unauthorized (got {badKey.Outcome}) — {badKey.Message}");

var dead = await tester.TestAsync(new Uri("http://127.0.0.1:8649"), apiKey);
Check(dead.Outcome == ConnectionTestOutcome.Unreachable,
    $"dead port classifies as Unreachable (got {dead.Outcome}) — {dead.Message}");

var store = new DpapiSecretStore();
var secret = "smoke-" + Guid.NewGuid();
await store.SetAsync("smoke-test", secret);
Check(await store.GetAsync("smoke-test") == secret, "DPAPI round-trip (set/get)");
await store.DeleteAsync("smoke-test");
Check(await store.GetAsync("smoke-test") is null, "DPAPI delete");

Console.WriteLine(failures.Count == 0 ? "SMOKE OK" : $"SMOKE FAILED ({failures.Count})");
return failures.Count == 0 ? 0 : 1;

static string? ReadApiServerKey()
{
    var env = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "hermes", ".env");
    foreach (var line in File.ReadLines(env))
    {
        if (line.StartsWith("API_SERVER_KEY=", StringComparison.Ordinal))
        {
            return line["API_SERVER_KEY=".Length..].Trim();
        }
    }

    return null;
}
