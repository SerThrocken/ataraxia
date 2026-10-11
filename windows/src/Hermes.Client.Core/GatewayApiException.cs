using System.Text.Json;

namespace Hermes.Client.Core;

/// <summary>Gateway error: parses the <c>{"error": {"message","type","code"}}</c> shape,
/// falling back to the raw body (unknown routes return plain-text 404s).</summary>
public sealed class GatewayApiException : Exception
{
    public GatewayApiException(int statusCode, string message, string? errorType = null, string? errorCode = null)
        : base(message)
    {
        StatusCode = statusCode;
        ErrorType = errorType;
        ErrorCode = errorCode;
    }

    public int StatusCode { get; }
    public string? ErrorType { get; }
    public string? ErrorCode { get; }

    public static GatewayApiException FromResponse(int statusCode, string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            if (root.ValueKind == JsonValueKind.Object
                && root.TryGetProperty("error", out var error)
                && error.ValueKind == JsonValueKind.Object)
            {
                return new GatewayApiException(
                    statusCode,
                    GetString(error, "message") ?? $"Gateway error {statusCode}.",
                    GetString(error, "type"),
                    GetString(error, "code"));
            }
        }
        catch (JsonException)
        {
            // Not JSON (e.g. plain-text 404): fall through to the raw body.
        }

        var text = body.Length > 300 ? body[..300] : body;
        return new GatewayApiException(
            statusCode,
            string.IsNullOrWhiteSpace(text) ? $"Gateway error {statusCode}." : text);
    }

    private static string? GetString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
}
