using Windows.Devices.Enumeration;
using Windows.Media.Capture;
using Windows.Media.MediaProperties;
using Windows.Storage.Streams;

namespace Hermes.Client.WinUI3.Services;

/// <summary>A still photo captured from the device camera (encoded JPEG bytes).</summary>
public sealed record CapturedPhoto(byte[] JpegData);

/// <summary>Captures a still photo from the device camera.</summary>
public interface ICameraCaptureService
{
    Task<CapturedPhoto> CapturePhotoAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// WinRT <c>Windows.Media.Capture.MediaCapture</c> still-photo capture —
/// native platform feature, no new dependency. Uses the first video-capture
/// device, captures one JPEG frame, and releases the device immediately; the
/// camera is never held open between captures.
/// Throws <see cref="InvalidOperationException"/> with a user-facing message
/// when no camera is present or access is denied (Windows Settings →
/// Privacy &amp; security → Camera).
/// </summary>
public sealed class MediaCaptureCameraService : ICameraCaptureService
{
    public async Task<CapturedPhoto> CapturePhotoAsync(CancellationToken cancellationToken = default)
    {
        var devices = await DeviceInformation.FindAllAsync(DeviceClass.VideoCapture).AsTask(cancellationToken);
        var device = devices.FirstOrDefault()
            ?? throw new InvalidOperationException("No camera was found on this device.");

        using var capture = new MediaCapture();
        try
        {
            await capture.InitializeAsync(new MediaCaptureInitializationSettings
            {
                VideoDeviceId = device.Id,
                StreamingCaptureMode = StreamingCaptureMode.Video,
                MemoryPreference = MediaCaptureMemoryPreference.Cpu,
            }).AsTask(cancellationToken);
        }
        catch (UnauthorizedAccessException ex)
        {
            throw new InvalidOperationException(
                "Camera access was denied. Allow camera access for this app in Windows Settings → Privacy & security → Camera.",
                ex);
        }

        using var stream = new InMemoryRandomAccessStream();
        await capture.CapturePhotoToStreamAsync(ImageEncodingProperties.CreateJpeg(), stream).AsTask(cancellationToken);

        var size = (uint)stream.Size;
        if (size == 0)
        {
            throw new InvalidOperationException("The camera returned an empty frame.");
        }

        var bytes = new byte[size];
        using (var input = stream.GetInputStreamAt(0))
        {
            var reader = new DataReader(input);
            await reader.LoadAsync(size).AsTask(cancellationToken);
            reader.ReadBytes(bytes);
        }

        return new CapturedPhoto(bytes);
    }
}
