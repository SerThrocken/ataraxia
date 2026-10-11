using Xunit;
using Hermes.Client.Core;

namespace Hermes.Client.Core.Tests;

/// <summary>
/// TDD for camera-to-agent: the captured JPEG plus its MIME type must become
/// a gateway-accepted data URL. The gateway (api_server.py _normalize_image_part)
/// only accepts data:image/... URLs or http(s) URLs — file paths are rejected.
/// </summary>
public sealed class ChatImageTests
{
    private static readonly byte[] JpegBytes = [0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10];

    [Fact]
    public void ToDataUrlProducesImageDataUrl()
    {
        var attachment = new ChatImageAttachment(JpegBytes, "image/jpeg");

        var url = attachment.ToDataUrl();

        Assert.StartsWith("data:image/jpeg;base64,", url);
        Assert.Equal("data:image/jpeg;base64," + Convert.ToBase64String(JpegBytes), url);
    }

    [Fact]
    public void EmptyDataIsRejected()
    {
        Assert.Throws<ArgumentException>(() => new ChatImageAttachment([], "image/jpeg"));
    }

    [Fact]
    public void NonImageMimeTypeIsRejected()
    {
        // The gateway rejects non-image data URLs (unsupported_content_type).
        Assert.Throws<ArgumentException>(() => new ChatImageAttachment(JpegBytes, "application/pdf"));
        Assert.Throws<ArgumentException>(() => new ChatImageAttachment(JpegBytes, "text/plain"));
    }

    [Fact]
    public void BlankMimeTypeIsRejected()
    {
        Assert.Throws<ArgumentException>(() => new ChatImageAttachment(JpegBytes, ""));
    }

    [Fact]
    public void PngMimeTypeIsAccepted()
    {
        var attachment = new ChatImageAttachment(JpegBytes, "image/png");

        Assert.StartsWith("data:image/png;base64,", attachment.ToDataUrl());
    }
}
