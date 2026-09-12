using Android.Content;
using Android.Graphics;
using Android.Hardware.Camera2;
using Android.Hardware.Camera2.Params;
using Android.Media;
using Android.OS;
using Java.Nio;
using SwiftDotNet.Graphics;
using Size = Android.Util.Size;

namespace SwiftDotNet;

/// <summary>
/// A live Camera2 preview delivered as engine frames — the Android half of <c>CameraView</c> on the
/// self-drawing backends.
/// </summary>
/// <remarks>
/// <para>Camera2 straight out of the platform, with no CameraX: an <see cref="ImageReader"/> is the
/// capture target and its <c>YUV_420_888</c> images are converted on the CPU. That is the price of a
/// preview the canvas can actually composite — a <c>SurfaceView</c> or <c>TextureView</c> would have to
/// be floated above it, which is the thing this seam exists to avoid.</para>
///
/// <para>Frames are published in the sensor's own orientation: a phone sensor is mounted 90° out, and
/// carrying that in <see cref="PixelFrame.Rotation"/> lets the canvas fold it into the matrix instead of
/// spending a second full pass per frame rotating pixels.</para>
///
/// <para><b>The app must hold <c>android.permission.CAMERA</c></b> and have requested it at runtime;
/// this reports a clear error rather than silently showing nothing when it does not.</para>
/// </remarks>
public sealed class AndroidCameraSource : FrameSource
{
    readonly YuvConverter _converter = new();

    HandlerThread? _thread;
    Handler? _handler;
    CameraManager? _manager;
    CameraDevice? _device;
    CameraCaptureSession? _session;
    ImageReader? _reader;

    bool _front;
    double _captureToken = double.NaN;
    int _sensorRotation;
    bool _ready;
    Pixels? _latest;

    public override void Configure(IReadOnlyDictionary<string, object?> props)
    {
        var front = props.TryGetValue("facing", out var facing) && facing as string == "front";
        if (front != _front)
        {
            _front = front;
            if (_device is not null) { Close(); Open(); }
        }

        if (props.TryGetValue("captureToken", out var token) && token is double value && !Equals(value, _captureToken))
        {
            var first = double.IsNaN(_captureToken);
            _captureToken = value;
            if (!first) CapturePhoto();
        }
    }

    public override void Start()
    {
        var context = Android.App.Application.Context;
        if (context.CheckSelfPermission(Android.Manifest.Permission.Camera) != Android.Content.PM.Permission.Granted)
        {
            Fail("Camera permission has not been granted.");
            return;
        }

        _thread = new HandlerThread("swiftdotnet-camera");
        _thread.Start();
        _handler = new Handler(_thread.Looper!);
        _manager = (CameraManager?)context.GetSystemService(Context.CameraService);
        Open();
    }

    void Open()
    {
        if (_manager is null) return;
        try
        {
            var id = FindCamera(_manager, _front);
            if (id is null)
            {
                Fail(_front ? "No front camera on this device." : "No camera on this device.");
                return;
            }

            var characteristics = _manager.GetCameraCharacteristics(id);
            _sensorRotation = (characteristics.Get(CameraCharacteristics.SensorOrientation) as Java.Lang.Integer)?.IntValue() ?? 90;

            var size = PreviewSize(characteristics);
            _reader = ImageReader.NewInstance(size.Width, size.Height, ImageFormatType.Yuv420888, maxImages: 2);
            _reader.SetOnImageAvailableListener(new FrameListener(OnImage), _handler);

            _manager.OpenCamera(id, new DeviceCallback(OnOpened, Fail), _handler);
        }
        catch (Java.Lang.Exception ex)
        {
            Fail(ex.Message ?? "The camera could not be opened.");
        }
    }

    static string? FindCamera(CameraManager manager, bool front)
    {
        var want = front ? (int)LensFacing.Front : (int)LensFacing.Back;
        string? fallback = null;
        foreach (var id in manager.GetCameraIdList())
        {
            fallback ??= id;
            var facing = manager.GetCameraCharacteristics(id).Get(CameraCharacteristics.LensFacing) as Java.Lang.Integer;
            if (facing?.IntValue() == want) return id;
        }
        return fallback;
    }

    /// <summary>
    /// The smallest supported YUV size at least 720p wide. A preview is composited by the CPU here, so
    /// capturing at the sensor's full resolution would spend most of a core converting pixels nobody can
    /// see on a phone-sized box.
    /// </summary>
    static Size PreviewSize(CameraCharacteristics characteristics)
    {
        var map = characteristics.Get(CameraCharacteristics.ScalerStreamConfigurationMap) as StreamConfigurationMap;
        var sizes = map?.GetOutputSizes((int)ImageFormatType.Yuv420888);
        if (sizes is null || sizes.Length == 0) return new Size(1280, 720);

        Size? best = null;
        foreach (var size in sizes)
        {
            if (size.Width < 1280) continue;
            if (best is null || size.Width * (long)size.Height < best.Width * (long)best.Height) best = size;
        }
        return best ?? sizes[0];
    }

    void OnOpened(CameraDevice device)
    {
        _device = device;
        try
        {
            var surface = _reader?.Surface;
            if (surface is null) return;
            device.CreateCaptureSession([surface], new SessionCallback(session =>
            {
                _session = session;
                var request = device.CreateCaptureRequest(CameraTemplate.Preview);
                request.AddTarget(surface);
                session.SetRepeatingRequest(request.Build(), null, _handler);
            }, Fail), _handler);
        }
        catch (Java.Lang.Exception ex)
        {
            Fail(ex.Message ?? "The capture session could not be configured.");
        }
    }

    void OnImage(ImageReader reader)
    {
        using var image = reader.AcquireLatestImage();
        if (image is null) return;

        var pixels = _converter.Convert(image);
        if (pixels is null) return;
        _latest = pixels;

        if (!_ready)
        {
            _ready = true;
            Emit($"ready:{pixels.Width},{pixels.Height},0");
        }

        Publish(new PixelFrame(pixels, _sensorRotation, mirrored: _front));
    }

    /// <summary>
    /// Encode the frame on screen as a JPEG. Deliberately not a second <c>JPEG</c> capture stream: a
    /// still-capture target costs memory and start-up latency for every preview, and "the frame the user
    /// was looking at" is what a viewfinder shutter means.
    /// </summary>
    void CapturePhoto()
    {
        var pixels = _latest;
        if (pixels is null) { Fail("There is no frame to capture yet."); return; }

        try
        {
            using var bitmap = Bitmap.CreateBitmap(pixels.Width, pixels.Height, Bitmap.Config.Argb8888!);
            if (bitmap is null) { Fail("The photo bitmap could not be allocated."); return; }
            bitmap.CopyPixelsFromBuffer(ByteBuffer.Wrap(pixels.Rgba));

            using var rotated = Rotate(bitmap, _sensorRotation, _front);
            using var stream = new MemoryStream();
            rotated.Compress(Bitmap.CompressFormat.Jpeg!, 90, stream);
            Emit("photo:" + Convert.ToBase64String(stream.ToArray()));
        }
        catch (Java.Lang.Exception ex)
        {
            Fail(ex.Message ?? "The photo could not be encoded.");
        }
    }

    /// <summary>A saved photo has to be baked upright — unlike a frame, nothing downstream carries its rotation.</summary>
    static Bitmap Rotate(Bitmap bitmap, int degrees, bool mirrored)
    {
        if (degrees == 0 && !mirrored) return bitmap;
        var matrix = new Matrix();
        if (mirrored) matrix.PreScale(-1, 1);
        matrix.PostRotate(degrees);
        return Bitmap.CreateBitmap(bitmap, 0, 0, bitmap.Width, bitmap.Height, matrix, filter: true) ?? bitmap;
    }

    void Close()
    {
        _session?.Close();
        _session = null;
        _device?.Close();
        _device = null;
        _reader?.Close();
        _reader = null;
    }

    public override void Stop() => Close();

    public override void Dispose()
    {
        Close();
        _thread?.QuitSafely();
        _thread = null;
        _handler = null;
        base.Dispose();
    }

    sealed class FrameListener(Action<ImageReader> onImage) : Java.Lang.Object, ImageReader.IOnImageAvailableListener
    {
        public void OnImageAvailable(ImageReader? reader)
        {
            if (reader is not null) onImage(reader);
        }
    }

    sealed class DeviceCallback(Action<CameraDevice> onOpened, Action<string> onError) : CameraDevice.StateCallback
    {
        public override void OnOpened(CameraDevice camera) => onOpened(camera);

        public override void OnDisconnected(CameraDevice camera) => camera.Close();

        public override void OnError(CameraDevice camera, CameraError error)
        {
            camera.Close();
            onError($"The camera reported {error}.");
        }
    }

    sealed class SessionCallback(Action<CameraCaptureSession> onConfigured, Action<string> onError)
        : CameraCaptureSession.StateCallback
    {
        public override void OnConfigured(CameraCaptureSession session) => onConfigured(session);

        public override void OnConfigureFailed(CameraCaptureSession session) =>
            onError("The capture session could not be configured.");
    }
}
