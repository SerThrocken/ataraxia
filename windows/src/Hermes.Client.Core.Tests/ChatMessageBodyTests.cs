using System.Text.Json;
using Xunit;
using Hermes.Client.Core;

namespace Hermes.Client.Core.Tests;

/// <summary>
/// TDD for the chat-stream request body. Text-only sends keep the exact wire
/// shape the gateway already accepts (<c>{"message":"..."}</c>); an attached
/// photo switches the body to the OpenAI-style parts array the gateway's
/// <c>_normalize_multimodal_content</c> validates
/// (api_server.py: text + image_url parts, data:image/... URLs).
/// </summary>
public sealed class ChatMessageBodyTests
{
    private static readonly byte[] JpegBytes = [0xFF, 0xD8, 0xFF, 0xE0];

    [Fact]
    public void TextOnlyBodyKeepsLegacyShape()
    {
        var json = GatewayClientBase.BuildChatMessageJson("hello", image: null);

        using var doc = JsonDocument.Parse(json);
        Assert.Equal("hello", doc.RootElement.GetProperty("message").GetString());
        Assert.Equal(JsonValueKind.String, doc.RootElement.GetProperty("message").ValueKind);
    }

    [Fact]
    public void ImageBodyUsesPartsArray()
    {
        var image = new ChatImageAttachment(JpegBytes, "image/jpeg");

        var json = GatewayClientBase.BuildChatMessageJson("look at this", image);

        using var doc = JsonDocument.Parse(json);
        var parts = doc.RootElement.GetProperty("message");
        Assert.Equal(JsonValueKind.Array, parts.ValueKind);
        Assert.Equal(2, parts.GetArrayLength());

        var textPart = parts[0];
        Assert.Equal("text", textPart.GetProperty("type").GetString());
        Assert.Equal("look at this", textPart.GetProperty("text").GetString());

        var imagePart = parts[1];
        Assert.Equal("image_url", imagePart.GetProperty("type").GetString());
        Assert.Equal(
            "data:image/jpeg;base64," + Convert.ToBase64String(JpegBytes),
            imagePart.GetProperty("image_url").GetProperty("url").GetString());
    }

    [Fact]
    public void ImageOnlyBodyIsAccepted()
    {
        // The gateway treats any image part as a visible payload, so an empty
        // caption with a photo is a legal turn.
        var image = new ChatImageAttachment(JpegBytes, "image/jpeg");

        var json = GatewayClientBase.BuildChatMessageJson("", image);

        using var doc = JsonDocument.Parse(json);
        var parts = doc.RootElement.GetProperty("message");
        Assert.Equal(JsonValueKind.Array, parts.ValueKind);
        Assert.Equal("image_url", parts[1].GetProperty("type").GetString());
    }
}
