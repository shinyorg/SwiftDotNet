using System.Runtime.InteropServices;
using SwiftDotNet.Graphics;
using Windows.Devices.Enumeration;
using Windows.Graphics.Imaging;
using Windows.Media.Capture;
using Windows.Media.Capture.Frames;
using Windows.Media.MediaProperties;

namespace SwiftDotNet;

/// <summary>
/// A live <see cref="MediaCapture"/> preview delivered as engine frames — the Windows half of
/// <c>CameraView</c> on the self-drawing backends.
/// </summary>
/// <remarks>
/// <para>A <see cref="MediaFrameReader"/> is asked for <c>Bgra8</c> frames with
/// <see cref="MediaCaptureMemoryPreference.Cpu"/>, which is the one configuration that reliably yields a
/// <see cref="SoftwareBitmap"/> this process can read without Direct3D. A
/// <c>CaptureElement</c>/<c>MediaPlayerElement</c> would be a real XAML control and could only be floated
/// above the canvas, which is the thing this seam exists to avoid.</para>
///
/// <para><b>Status: written against the documented API but never compiled or run</b> — it needs a Windows
/// machine, which this repository has not been built on. Treat it as scaffolding until someone runs it;
/// see <c>docs/video-and-camera.md</c>.</para>
///
/// <para>Packaged apps need the <c>webcam</c> capability in their manifest.</para>
/// </remarks>
public sealed class WindowsCameraSource : FrameSource
{
    MediaCapture? _capture;
    MediaFrameReader? _reader;
    byte[]?[] _buffers = new byte[]?[2];
    int _next;
    int _width, _height;
    bool _front;
    bool _ready;
    double _captureToken = double.NaN;
    Pixels? _latest;

    public override void Configure(IReadOnlyDictionary<string, object?> props)
    {
        var front = props.TryGetValue("facing", out var facing) && facing as string == "front";
        if (front != _front)
        {
            _front = front;
            _ = Restart();
        }

        if (props.TryGetValue("captureToken", out var token) && token is double value && !Equals(value, _captureToken))
        {
            var first = double.IsNaN(_captureToken);
            _captureToken = value;
            if (!first) CapturePhoto();
        }
    }

    public override void Start() => _ = StartAsync();

    async Task Restart()
    {
        await StopAsync().ConfigureAwait(false);
        await StartAsync().ConfigureAwait(false);
    }

    async Task StartAsync()
    {
        try
        {
            var panel = _front ? Panel.Front : Panel.Back;
            var devices = await DeviceInformation.FindAllAsync(DeviceClass.VideoCapture);
            if (devices.Count == 0)
            {
                Fail("No camera is attached.");
                return;
            }

            // Prefer a camera on the requested side; a laptop has only a front one, so fall back rather
            // than refusing to show anything.
            var device = devices.FirstOrDefault(d => d.EnclosureLocation?.Panel == panel) ?? devices[0];

            _capture = new MediaCapture();
            await _capture.InitializeAsync(new MediaCaptureInitializationSettings
            {
                VideoDeviceId = device.Id,
                StreamingCaptureMode = StreamingCaptureMode.Video,
                MemoryPreference = MediaCaptureMemoryPreference.Cpu,
                SharingMode = MediaCaptureSharingMode.ExclusiveControl,
            });

            var source = _capture.FrameSources.Values.FirstOrDefault(s =>
                             s.Info.MediaStreamType == MediaStreamType.VideoPreview)
                         ?? _capture.FrameSources.Values.FirstOrDefault(s =>
                             s.Info.MediaStreamType == MediaStreamType.VideoRecord);
            if (source is null)
            {
                Fail("The camera exposes no video frame source.");
                return;
            }

            _reader = await _capture.CreateFrameReaderAsync(source, MediaEncodingSubtypes.Bgra8);
            _reader.FrameArrived += OnFrameArrived;
            var status = await _reader.StartAsync();
            if (status != MediaFrameReaderStartStatus.Success) Fail($"The camera would not start ({status}).");
        }
        catch (UnauthorizedAccessException)
        {
            Fail("Camera access has not been granted.");
        }
        catch (Exception ex)
        {
            Fail(ex.Message);
        }
    }

    void OnFrameArrived(MediaFrameReader sender, MediaFrameArrivedEventArgs args)
    {
        using var frame = sender.TryAcquireLatestFrame();
        var bitmap = frame?.VideoMediaFrame?.SoftwareBitmap;
        if (bitmap is null) return;

        var pixels = Convert(bitmap);
        if (pixels is null) return;
        _latest = pixels;

        if (!_ready)
        {
            _ready = true;
            Emit($"ready:{pixels.Width},{pixels.Height},0");
        }

        Publish(new PixelFrame(pixels, mirrored: _front));
    }

    /// <summary>BGRA8 (what the reader produces) to the engine's straight RGBA, into a reused buffer.</summary>
    unsafe Pixels? Convert(SoftwareBitmap bitmap)
    {
        var width = bitmap.PixelWidth;
        var height = bitmap.PixelHeight;
        if (width <= 0 || height <= 0) return null;

        if (width != _width || height != _height)
        {
            _width = width;
            _height = height;
            _buffers = new byte[]?[2];
        }

        var destination = _buffers[_next] ??= new byte[width * height * 4];
        _next ^= 1;

        using var buffer = bitmap.LockBuffer(BitmapBufferAccessMode.Read);
        using var reference = buffer.CreateReference();
        ((IMemoryBufferByteAccess)reference).GetBuffer(out var source, out var capacity);
        if (source is null) return null;

        var plane = buffer.GetPlaneDescription(0);
        var stride = plane.Stride;
        if ((uint)(plane.StartIndex + stride * height) > capacity) return null;

        fixed (byte* target = destination)
        {
            for (var y = 0; y < height; y++)
            {
                var src = source + plane.StartIndex + y * stride;
                var dst = target + y * width * 4;
                for (var x = 0; x < width; x++)
                {
                    dst[0] = src[2];
                    dst[1] = src[1];
                    dst[2] = src[0];
                    dst[3] = src[3];
                    src += 4;
                    dst += 4;
                }
            }
        }

        return new Pixels(width, height, destination);
    }

    /// <summary>Encode the frame on screen as a PNG — the frame the user was looking at is the photo.</summary>
    void CapturePhoto()
    {
        var pixels = _latest;
        if (pixels is null) { Fail("There is no frame to capture yet."); return; }
        Emit("photo:" + System.Convert.ToBase64String(PngEncoder.Encode(pixels)));
    }

    async Task StopAsync()
    {
        if (_reader is not null)
        {
            _reader.FrameArrived -= OnFrameArrived;
            try { await _reader.StopAsync(); } catch (Exception) { /* already torn down */ }
            _reader.Dispose();
            _reader = null;
        }
        _capture?.Dispose();
        _capture = null;
    }

    public override void Stop() => _ = StopAsync();

    public override void Dispose()
    {
        _ = StopAsync();
        base.Dispose();
    }

    /// <summary>The COM interface that gives a WinRT memory buffer's raw pointer. Standard boilerplate.</summary>
    [ComImport]
    [Guid("5B0D3235-4DBA-4D44-865E-8F1D0E4FD04D")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    unsafe interface IMemoryBufferByteAccess
    {
        void GetBuffer(out byte* buffer, out uint capacity);
    }
}
