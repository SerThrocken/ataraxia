using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;

namespace Hermes.Client.Core;

/// <summary>
/// Parses the gateway's SSE wire format: <c>event: &lt;name&gt;</c> + <c>data: &lt;json&gt;</c>
/// lines terminated by a blank line; <c>: keepalive</c> comment lines are skipped.
/// </summary>
public static class SseParser
{
    public static async IAsyncEnumerable<SseEvent> ReadEventsAsync(
        Stream stream,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        using var reader = new StreamReader(stream, Encoding.UTF8);
        string? eventName = null;
        var dataLines = new List<string>();

        while (await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false) is { } line)
        {
            if (line.Length == 0)
            {
                if (eventName is not null || dataLines.Count > 0)
                {
                    var e = Build(eventName, dataLines);
                    eventName = null;
                    dataLines.Clear();
                    if (e is not null)
                    {
                        yield return e;
                    }
                }

                continue;
            }

            if (line[0] == ':')
            {
                continue; // SSE comment (idle keepalive)
            }

            var colon = line.IndexOf(':');
            if (colon < 0)
            {
                continue;
            }

            var value = line.Substring(colon + 1).TrimStart(' ');
            if (line.StartsWith("event", StringComparison.Ordinal))
            {
                eventName = value;
            }
            else if (line.StartsWith("data", StringComparison.Ordinal))
            {
                dataLines.Add(value);
            }
        }

        // Trailing frame without a terminating blank line.
        if (eventName is not null || dataLines.Count > 0)
        {
            var e = Build(eventName, dataLines);
            if (e is not null)
            {
                yield return e;
            }
        }
    }

    private static SseEvent? Build(string? eventName, List<string> dataLines)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(string.Join("\n", dataLines));
        }
        catch (JsonException)
        {
            return null; // Malformed frame: skip, keep the stream alive.
        }

        var root = document.RootElement;
        string? sessionId = null;
        string? runId = null;
        long seq = 0;
        double ts = 0;
        if (root.ValueKind == JsonValueKind.Object)
        {
            sessionId = GetString(root, "session_id");
            runId = GetString(root, "run_id");
            if (root.TryGetProperty("seq", out var seqProp) && seqProp.TryGetInt64(out var s))
            {
                seq = s;
            }

            if (root.TryGetProperty("ts", out var tsProp) && tsProp.TryGetDouble(out var t))
            {
                ts = t;
            }
        }

        return new SseEvent(eventName ?? "message", document, sessionId, runId, seq, ts);
    }

    private static string? GetString(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
}
