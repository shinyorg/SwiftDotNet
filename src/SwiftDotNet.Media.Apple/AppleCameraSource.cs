#if !TVOS
using AVFoundation;
using CoreFoundation;
using CoreMedia;
using CoreVideo;
using Foundation;
using SwiftDotNet.Graphics;

namespace SwiftDotNet;

/// <summary>
/// A live AVFoundation camera preview delivered as engine frames — the Apple half of <c>CameraView</c>
/// on the self-drawing backends.
/// </summary>
/// <remarks>
/// <para>Uses <see cref="AVCaptureVideoDataOutput"/> (raw sample buffers) rather than an
/// <c>AVCaptureVideoPreviewLayer</c>: a preview layer is a real <c>CALayer</c> and could only ever be
/// floated <em>above</em> the canvas, which is the thing this whole seam exists to avoid. Pulling the
/// buffers means the preview clips, scrolls, rounds its corners and sits under a sheet like every other
/// pixel the engine draws.</para>
///
/// <para>Frames are published in the sensor's own orientation with <see cref="PixelFrame.Rotation"/> and
/// <see cref="PixelFrame.Mirrored"/> describing how to show them, because the canvas can fold both into
/// its matrix for free while rotating the buffer would cost another full pass per frame.</para>
///
/// <para><b>The app must declare <c>NSCameraUsageDescription</c></b> in its Info.plist, or the process is
/// killed the moment capture starts — that is an OS rule, not something this can work around.</para>
/// </remarks>
public sealed class AppleCameraSource : FrameSource
{
    readonly PixelBufferConverter _converter = new();
    readonly DispatchQueue _queue = new("swiftdotnet.camera");

    AVCaptureSession? _session;
    AVCaptureDeviceInput? _input;
    AVCaptureVideoDataOutput? _output;
    SampleDelegate? _delegate;
    AVCapturePhotoOutput? _photo;
    PhotoDelegate? _photoDelegate;

    bool _front;
    string _flash = "off";
    double _captureToken = double.NaN;
    bool _started;
    bool _ready;

    public override void Configure(IReadOnlyDictionary<string, object?> props)
    {
        if (props.TryGetValue("flash", out var flash) && flash is string f) _flash = f;

        var front = props.TryGetValue("facing", out var facing) && facing as string == "front";
        if (front != _front)
        {
            _front = front;
            if (_started) SwitchCamera();
        }

        // A capture is requested by bumping a token, so the *same* request can be made twice (two photos
        // in a row) without the prop diffing away to nothing.
        if (props.TryGetValue("captureToken", out var token) && token is double value && !Equals(value, _captureToken))
        {
            var first = double.IsNaN(_captureToken);
            _captureToken = value;
            if (!first) CapturePhoto();
        }
    }

    public override void Start()
    {
        AVCaptureDevice.RequestAccessForMediaType(AVAuthorizationMediaType.Video, granted =>
        {
            if (!granted)
            {
                Fail("Camera access was denied.");
                return;
            }
            _queue.DispatchAsync(StartSession);
        });
    }

    void StartSession()
    {
        try
        {
            var device = FindDevice(_front);
            if (device is null)
            {
                Fail(_front ? "No front camera on this device." : "No camera on this device.");
                return;
            }

            _session = new AVCaptureSession { SessionPreset = AVCaptureSession.PresetHigh };
            _session.BeginConfiguration();

            _input = AVCaptureDeviceInput.FromDevice(device, out var error);
            if (_input is null || error is not null)
            {
                Fail(error?.LocalizedDescription ?? "The camera could not be opened.");
                return;
            }
            if (_session.CanAddInput(_input)) _session.AddInput(_input);

            _delegate = new SampleDelegate(OnFrame);
            _output = new AVCaptureVideoDataOutput
            {
                AlwaysDiscardsLateVideoFrames = true,
                WeakVideoSettings = new CVPixelBufferAttributes { PixelFormatType = CVPixelFormatType.CV32BGRA }.Dictionary,
            };
            _output.SetSampleBufferDelegate(_delegate, _queue);
            if (_session.CanAddOutput(_output)) _session.AddOutput(_output);

            _photo = new AVCapturePhotoOutput();
            if (_session.CanAddOutput(_photo)) _session.AddOutput(_photo);

            _session.CommitConfiguration();
            _session.StartRunning();
            _started = true;
        }
        catch (Exception ex)
        {
            Fail(ex.Message);
        }
    }

    static AVCaptureDevice? FindDevice(bool front)
    {
        var position = front ? AVCaptureDevicePosition.Front : AVCaptureDevicePosition.Back;
        var discovery = AVCaptureDeviceDiscoverySession.Create(
            [AVCaptureDeviceType.BuiltInWideAngleCamera], AVMediaTypes.Video, position);
        return discovery.Devices.FirstOrDefault() ?? AVCaptureDevice.GetDefaultDevice(AVMediaTypes.Video);
    }

    void SwitchCamera()
    {
        if (_session is null) return;
        _queue.DispatchAsync(() =>
        {
            var device = FindDevice(_front);
            if (device is null) return;
            _session.BeginConfiguration();
            if (_input is not null) _session.RemoveInput(_input);
            _input = AVCaptureDeviceInput.FromDevice(device, out _);
            if (_input is not null && _session.CanAddInput(_input)) _session.AddInput(_input);
            _session.CommitConfiguration();
        });
    }

    void OnFrame(CMSampleBuffer sample)
    {
        if (sample.GetImageBuffer() is not CVPixelBuffer buffer) return;
        var pixels = _converter.Convert(buffer);
        if (pixels is null) return;

        if (!_ready)
        {
            _ready = true;
            Emit($"ready:{pixels.Width},{pixels.Height},0");
        }

        Publish(new PixelFrame(pixels, SensorRotation(), mirrored: _front));
    }

    /// <summary>
    /// How far the sensor buffer is from upright. A phone camera is mounted landscape, so a portrait app
    /// needs a quarter turn; a Mac's webcam is already the right way up.
    /// </summary>
    static int SensorRotation()
    {
#if IOS || MACCATALYST
        return UIKit.UIDevice.CurrentDevice.Orientation switch
        {
            UIKit.UIDeviceOrientation.LandscapeLeft => 180,
            UIKit.UIDeviceOrientation.LandscapeRight => 0,
            UIKit.UIDeviceOrientation.PortraitUpsideDown => 270,
            _ => 90,
        };
#else
        return 0;
#endif
    }

    void CapturePhoto()
    {
        if (_photo is null) return;
        try
        {
            var settings = AVCapturePhotoSettings.Create();
#if IOS || MACCATALYST
            if (_photo.SupportedFlashModes.Length > 0)
                settings.FlashMode = _flash switch
                {
                    "on" => AVCaptureFlashMode.On,
                    "auto" => AVCaptureFlashMode.Auto,
                    _ => AVCaptureFlashMode.Off,
                };
#endif
            _photoDelegate = new PhotoDelegate(OnPhoto, Fail);
            _photo.CapturePhoto(settings, _photoDelegate);
        }
        catch (Exception ex)
        {
            Fail(ex.Message);
        }
    }

    void OnPhoto(byte[] jpeg) => Emit("photo:" + Convert.ToBase64String(jpeg));

    public override void Stop()
    {
        if (_session is { Running: true }) _session.StopRunning();
        _started = false;
    }

    public override void Dispose()
    {
        Stop();
        _output?.Dispose();
        _output = null;
        _photo?.Dispose();
        _photo = null;
        _input?.Dispose();
        _input = null;
        _session?.Dispose();
        _session = null;
        _delegate?.Dispose();
        _delegate = null;
        base.Dispose();
    }

    sealed class SampleDelegate(Action<CMSampleBuffer> onFrame) : AVCaptureVideoDataOutputSampleBufferDelegate
    {
        public override void DidOutputSampleBuffer(AVCaptureOutput output, CMSampleBuffer sampleBuffer, AVCaptureConnection connection)
        {
            try { onFrame(sampleBuffer); }
            finally { sampleBuffer.Dispose(); }   // the delegate owns the buffer; leaking them stalls capture
        }
    }

    sealed class PhotoDelegate(Action<byte[]> onPhoto, Action<string> onError) : AVCapturePhotoCaptureDelegate
    {
        public override void DidFinishProcessingPhoto(AVCapturePhotoOutput output, AVCapturePhoto photo, NSError? error)
        {
            if (error is not null) { onError(error.LocalizedDescription); return; }
            using var data = photo.FileDataRepresentation;
            if (data is null) { onError("The photo could not be encoded."); return; }
            onPhoto(data.ToArray());
        }
    }
}
#endif
