namespace Hermes.Client.Core;

/// <summary>
/// A photo captured on-device (camera) to send with a chat turn. Carries raw
/// encoded bytes (JPEG from the capture service) plus the MIME type so the
/// client can build the <c>data:image/...;base64,...</c> URL the gateway's
/// <c>_normalize_image_part</c> accepts (api_server.py: data:image URLs or
/// http(s) URLs only — file paths are rejected).
/// </summary>
public sealed record ChatImageAttachment(byte[] Data, string MimeType)
{
    public byte[] Data { get; init; } =
        Data is { Length: > 0 } ? Data : throw new ArgumentException("Image data must not be empty.", nameof(Data));

    public string MimeType { get; init; } =
        MimeType.StartsWith("image/", StringComparison.OrdinalIgnoreCase)
            ? MimeType
            : throw new ArgumentException("MIME type must be an image/* type.", nameof(MimeType));

    /// <summary>Gateway-accepted data URL for the image_url content part.</summary>
    public string ToDataUrl() => $"data:{MimeType};base64,{Convert.ToBase64String(Data)}";
}
