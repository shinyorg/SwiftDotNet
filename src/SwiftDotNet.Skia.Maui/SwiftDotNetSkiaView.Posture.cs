#if ANDROID
using Android.Runtime;
using AndroidX.Core.Content;
using AndroidX.Window.Java.Layout;
using AndroidX.Window.Layout;
#elif IOS
using System.Runtime.InteropServices;
using CoreGraphics;
using Foundation;
using ObjCRuntime;
using UIKit;
#endif

namespace SwiftDotNet;

// Window posture for the Skia MAUI host. The self-drawing engine has no platform layout to ask, so the host
// reads the platform API itself and feeds WindowPosture.Report — the same values the native shims send on
// `$posture`. Coordinates are relative to this view (the Skia root area), in DIPs/points:
//   • every TFM — the view's size, so TwoPaneView's width rule works on Mac Catalyst / Windows too;
//   • Android  — Jetpack WindowManager's FoldingFeature (via the window-java callback adapter);
//   • iOS 27.1+ — UIHingeInteraction for status/angle, UIView.GetReservedRegions(division) for geometry.
public partial class SwiftDotNetSkiaView
{
#pragma warning disable CS0649 // only the Android/iOS halves assign these; elsewhere a size-only report is the point
    FoldState _foldState;
    FoldOrientation _foldOrientation;
    double _foldX, _foldY, _foldW, _foldH;
    double? _hingeAngle;
#pragma warning restore CS0649

    /// <summary>Publishes the current size + fold. Cheap and de-duplicated by WindowPosture.Report.</summary>
    void ReportPosture()
    {
        if (Width <= 0 || Height <= 0)
            return;
#if IOS
        ReadReservedRegions();
#endif
        WindowPosture.Report(new PostureInfo(_foldState, _foldOrientation, _foldX, _foldY, _foldW, _foldH,
            _hingeAngle, Width, Height));
    }

#if ANDROID
    WindowInfoTrackerCallbackAdapter? _tracker;
    LayoutConsumer? _consumer;

    void AttachPosture()
    {
        if (Handler?.PlatformView is not Android.Views.View view)
        {
            DetachPosture();
            return;
        }
        if (_tracker is not null)
            return;
        var activity = view.Context as Android.App.Activity ?? Microsoft.Maui.ApplicationModel.Platform.CurrentActivity;
        if (activity is null)
            return;

        _tracker = new WindowInfoTrackerCallbackAdapter(WindowInfoTracker.Companion.GetOrCreate(activity));
        _consumer = new LayoutConsumer(info => OnWindowLayout(view, info));
        _tracker.AddWindowLayoutInfoListener(activity, ContextCompat.GetMainExecutor(activity)!, _consumer);
    }

    void DetachPosture()
    {
        if (_tracker is not null && _consumer is not null)
            _tracker.RemoveWindowLayoutInfoListener(_consumer);
        _tracker = null;
        _consumer = null;
    }

    void OnWindowLayout(Android.Views.View view, WindowLayoutInfo info)
    {
        // The list hands back IDisplayFeature invokers, which a managed `is IFoldingFeature` / OfType never
        // matches — ask Java whether each one is a FoldingFeature and JavaCast it.
        var foldingClass = Java.Lang.Class.FromType(typeof(IFoldingFeature));
        var fold = info.DisplayFeatures
            .Select(f => f as Java.Lang.Object)
            .Where(o => o is not null && foldingClass.IsInstance(o))
            .Select(o => o!.JavaCast<IFoldingFeature>())
            .FirstOrDefault();
        _foldState = FoldState.None;
        _foldOrientation = FoldOrientation.None;
        _foldX = _foldY = _foldW = _foldH = 0;

        if (fold is not null)
        {
            var divides = fold.State == FoldingFeatureState.HalfOpened || fold.IsSeparating;
            _foldState = divides ? FoldState.HalfOpen : FoldState.Flat;
            if (divides)
            {
                // FoldingFeature bounds are window pixels; make them view-relative DIPs.
                var origin = new int[2];
                view.GetLocationInWindow(origin);
                var density = view.Resources?.DisplayMetrics?.Density ?? 1f;
                var b = ((IDisplayFeature)fold).Bounds;
                _foldOrientation = fold.Orientation == FoldingFeatureOrientation.Vertical
                    ? FoldOrientation.Vertical : FoldOrientation.Horizontal;
                _foldX = (b.Left - origin[0]) / density;
                _foldY = (b.Top - origin[1]) / density;
                _foldW = b.Width() / density;
                _foldH = b.Height() / density;
            }
        }
        ReportPosture();
    }

    sealed class LayoutConsumer(Action<WindowLayoutInfo> onLayout) : Java.Lang.Object, AndroidX.Core.Util.IConsumer
    {
        public void Accept(Java.Lang.Object? value)
        {
            if (value is WindowLayoutInfo info)
                onLayout(info);
        }
    }
#elif IOS
    // iOS 27.1 hinge API. The MAUI workload compiles net10.0-ios against the iOS 26 reference pack, which
    // has no UIHinge*/UIViewReservedRegion* bindings (they arrive with the 27.1 pack, net10.0-ios27.1).
    // Retargeting this library would force every app on it to that TFM, so the handful of calls go through
    // the Objective-C runtime instead — same selectors the 27.1 bindings wrap. See UIKitPosture below.
    IntPtr _hinge;
    long _hingeStatus;   // UIHingeStatus: 0 unknown, 1 closed, 2 partiallyOpen, 3 fullyOpen

    void AttachPosture()
    {
        if (!UIKitPosture.Available || Handler?.PlatformView is not UIView view || _hinge != IntPtr.Zero)
            return;

        _hinge = UIKitPosture.AddHingeInteraction(view, (status, angleRadians) =>
        {
            _hingeStatus = status;
            _hingeAngle = status == 0 ? null : angleRadians * 180.0 / Math.PI;
            ReportPosture();
        });
    }

    /// <summary>The division region exists only while the device is partly open; its frame is the fold.</summary>
    void ReadReservedRegions()
    {
        if (!UIKitPosture.Available || Handler?.PlatformView is not UIView view)
            return;

        if (UIKitPosture.ActiveDivision(view) is { } f)
        {
            _foldState = FoldState.HalfOpen;
            _foldOrientation = f.Height >= f.Width ? FoldOrientation.Vertical : FoldOrientation.Horizontal;
            (_foldX, _foldY, _foldW, _foldH) = (f.X, f.Y, f.Width, f.Height);
        }
        else
        {
            _foldState = _hingeStatus is 2 or 3 ? FoldState.Flat : FoldState.None;
            _foldOrientation = FoldOrientation.None;
            _foldX = _foldY = _foldW = _foldH = 0;
        }
    }
#else
    void AttachPosture() { }
#endif
}

#if IOS
/// <summary>
/// The iOS 27.1 hinge / reserved-region calls, by selector. Arm64 only: <c>frame</c> returns a CGRect, which
/// arm64 returns in registers through plain <c>objc_msgSend</c> (x64 would need the <c>_stret</c> variant),
/// and every device and Apple-silicon simulator that runs iOS 27.1 is arm64.
/// </summary>
static unsafe class UIKitPosture
{
    const string Libobjc = "/usr/lib/libobjc.dylib";

    [DllImport(Libobjc, EntryPoint = "objc_msgSend")] static extern IntPtr Send(IntPtr receiver, IntPtr selector);
    [DllImport(Libobjc, EntryPoint = "objc_msgSend")] static extern IntPtr Send(IntPtr receiver, IntPtr selector, IntPtr arg);
    [DllImport(Libobjc, EntryPoint = "objc_msgSend")] static extern IntPtr SendBlock(IntPtr receiver, IntPtr selector, BlockLiteral* block);
    [DllImport(Libobjc, EntryPoint = "objc_msgSend")] static extern nint SendNInt(IntPtr receiver, IntPtr selector);
    [DllImport(Libobjc, EntryPoint = "objc_msgSend")] static extern double SendDouble(IntPtr receiver, IntPtr selector);
    [DllImport(Libobjc, EntryPoint = "objc_msgSend")] [return: MarshalAs(UnmanagedType.I1)] static extern bool SendBool(IntPtr receiver, IntPtr selector);
    [DllImport(Libobjc, EntryPoint = "objc_msgSend")] static extern CGRect SendRect(IntPtr receiver, IntPtr selector);

    public static bool Available =>
        OperatingSystem.IsIOSVersionAtLeast(27, 1) && RuntimeInformation.ProcessArchitecture == Architecture.Arm64
        && Class.GetHandle("UIHingeInteraction") != IntPtr.Zero;

    /// <summary>The first active <c>division</c> region's frame in <paramref name="view"/>'s coordinates.</summary>
    public static CGRect? ActiveDivision(UIView view)
    {
        var kind = Send(Class.GetHandle("UIViewReservedRegionKind"), Selector.GetHandle("divisionRegionKind"));
        var regions = Runtime.GetNSObject<NSArray>(Send(view.Handle, Selector.GetHandle("reservedRegionsOfKind:"), kind));
        if (regions is null)
            return null;
        for (nuint i = 0; i < regions.Count; i++)
        {
            var region = regions.ValueAt(i);
            if (SendBool(region, Selector.GetHandle("isActive")))
                return SendRect(region, Selector.GetHandle("frame"));
        }
        return null;
    }

    /// <summary>Adds a UIHingeInteraction whose updates call <paramref name="onUpdate"/>(status, angle in
    /// radians); status 0 means "no hinge in this hierarchy". Returns the interaction (retained).</summary>
    public static IntPtr AddHingeInteraction(UIView view, Action<long, double> onUpdate)
    {
        delegate* unmanaged<IntPtr, IntPtr, IntPtr, void> trampoline = &OnHinge;
        using var block = new BlockLiteral(trampoline, onUpdate, typeof(UIKitPosture), nameof(OnHinge));
        var alloc = Send(Class.GetHandle("UIHingeInteraction"), Selector.GetHandle("alloc"));
        var interaction = SendBlock(alloc, Selector.GetHandle("initWithUpdateHandler:"), &block);
        Send(view.Handle, Selector.GetHandle("addInteraction:"), interaction);
        return interaction;
    }

    [UnmanagedCallersOnly]
    static void OnHinge(IntPtr block, IntPtr interaction, IntPtr update)
    {
        var callback = BlockLiteral.GetTarget<Action<long, double>>(block);
        var hinge = update == IntPtr.Zero ? IntPtr.Zero : Send(update, Selector.GetHandle("hinge"));
        if (hinge == IntPtr.Zero)
            callback(0, 0);
        else
            callback(SendNInt(hinge, Selector.GetHandle("status")), SendDouble(hinge, Selector.GetHandle("angle")));
    }
}
#endif
