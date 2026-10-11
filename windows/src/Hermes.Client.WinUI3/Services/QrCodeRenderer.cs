using Microsoft.UI.Xaml.Media.Imaging;
using QRCoder;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Storage.Streams;

namespace Hermes.Client.WinUI3.Services;

/// <summary>
/// Renders QR payload text to a WinUI image. The pixels come from QRCoder
/// (MIT, verified at source); this glue only moves PNG bytes into a
/// <see cref="BitmapImage"/>. The QR encode runs on a worker thread; the
/// <see cref="BitmapImage"/> is created on the caller's (UI) thread.
/// </summary>
public interface IQrCodeRenderer
{
    Task<BitmapImage> RenderAsync(string payload, int pixelsPerModule = 12);
}

public sealed class QrCodeRenderer : IQrCodeRenderer
{
    public async Task<BitmapImage> RenderAsync(string payload, int pixelsPerModule = 12)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(payload);
        ArgumentOutOfRangeException.ThrowIfLessThan(pixelsPerModule, 1);

        var png = await Task.Run(() =>
        {
            using var generator = new QRCodeGenerator();
            using var data = generator.CreateQrCode(payload, QRCodeGenerator.ECCLevel.M);
            using var pngCode = new PngByteQRCode(data);
            return pngCode.GetGraphic(pixelsPerModule);
        });

        // Resumes on the caller's synchronization context (UI thread): the
        // BitmapImage must be created there.
        var image = new BitmapImage();
        using var stream = new InMemoryRandomAccessStream();
        await stream.WriteAsync(png.AsBuffer());
        stream.Seek(0);
        await image.SetSourceAsync(stream);
        return image;
    }
}
