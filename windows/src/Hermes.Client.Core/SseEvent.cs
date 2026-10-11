using System.Text.Json;

namespace Hermes.Client.Core;

/// <summary>
/// One parsed SSE frame. Owns its <see cref="JsonDocument"/>; dispose after handling.
/// Envelope fields (<c>session_id</c>, <c>run_id</c>, <c>seq</c>, <c>ts</c>) are stamped
/// on every payload by the gateway.
/// </summary>
public sealed class SseEvent : IDisposable
{
    private readonly JsonDocument _document;
    private bool _disposed;

    public SseEvent(string name, JsonDocument document, string? sessionId, string? runId, long seq, double ts)
    {
        Name = name;
        _document = document;
        SessionId = sessionId;
        RunId = runId;
        Seq = seq;
        Ts = ts;
    }

    public string Name { get; }
    public string? SessionId { get; }
    public string? RunId { get; }
    public long Seq { get; }
    public double Ts { get; }

    public JsonElement Data => _document.RootElement;

    public string? GetString(string propertyName) =>
        Data.ValueKind == JsonValueKind.Object
        && Data.TryGetProperty(propertyName, out var value)
        && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    public void Dispose()
    {
        if (!_disposed)
        {
            _disposed = true;
            _document.Dispose();
        }
    }
}
