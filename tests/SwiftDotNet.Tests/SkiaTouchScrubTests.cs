using SkiaSharp;
using SwiftDotNet;
using Xunit;

namespace SwiftDotNet.Tests;

/// <summary>
/// Two things a finger must be able to do on a self-drawing backend that a mouse got for free:
/// <b>scrub a Slider</b> and <b>scroll a ScrollView</b>.
///
/// Both were inert. The engine's continuous-drag path keys off an <c>.OnDrag</c> *modifier*, which the
/// Controls library's sliders and panels carry but the built-in <c>Slider</c> does not — the built-in one
/// was tap-to-set only. And scrolling only ever arrived through <see cref="SkiaBridge.Scroll"/>, which a
/// host raises from a *wheel*; a touch host has no wheel, so a long <c>Form</c> could not be scrolled at
/// all. Worse than "drag does nothing": a drag past <see cref="SkiaPointerRouter.TapSlop"/> also cancels
/// the tap, so dragging a slider left it exactly where it was.
///
/// The router now falls back, in order: an <c>.OnDrag</c> node → a continuous control to scrub → the
/// innermost scrollable to pan. See <see cref="SkiaBridge.BeginScrub"/> / <see cref="SkiaBridge.PanScroll"/>.
/// </summary>
[Collection(nameof(SwiftAppSerial))]
public class SkiaTouchScrubTests
{
    const int W = 400, H = 800;

    [Fact]
    public void DraggingABuiltInSlider_ScrubsItContinuously()
    {
        var volume = new State<double>(0.0);
        var (bridge, router) = Host(new SliderPage(volume));
        Assert.True(bridge.TryGetFrame("0.1", out var track));

        // Press near the left of the track, drag to the right, release.
        var y = track.MidY;
        router.Down(new SKPoint(track.Left + 12, y), 0);
        var atPress = volume.Value;

        router.Move(new SKPoint(track.MidX, y), 0.05);
        var atMiddle = volume.Value;

        router.Move(new SKPoint(track.Right - 12, y), 0.10);
        router.Up(new SKPoint(track.Right - 12, y), 0.12);

        Assert.True(atPress < 0.2, $"press at the far left should set a low value, got {atPress}");
        Assert.InRange(atMiddle, 0.35, 0.65);            // tracked the finger mid-drag
        Assert.True(volume.Value > 0.9, $"released at the far right, got {volume.Value}");
    }

    [Fact]
    public void TappingABuiltInSlider_StillSetsTheValue()
    {
        // The press alone must set the value (SwiftUI's minimumDistance: 0), so a plain tap still works
        // and no double-apply happens on release.
        var volume = new State<double>(0.0);
        var (bridge, router) = Host(new SliderPage(volume));
        Assert.True(bridge.TryGetFrame("0.1", out var track));

        router.Down(new SKPoint(track.MidX, track.MidY), 0);
        router.Up(new SKPoint(track.MidX, track.MidY), 0.05);

        Assert.InRange(volume.Value, 0.35, 0.65);
    }

    [Fact]
    public void DraggingADiscreteControl_DoesNotRepeatlyFire()
    {
        // A Stepper is discrete: a press-and-drag must not increment once per move event.
        var qty = new State<int>(5);
        var (bridge, router) = Host(new StepperPage(qty));
        Assert.True(bridge.TryGetFrame("0.1", out var stepper));

        router.Down(new SKPoint(stepper.Right - 12, stepper.MidY), 0);
        router.Move(new SKPoint(stepper.Right - 14, stepper.MidY), 0.05);
        router.Move(new SKPoint(stepper.Right - 16, stepper.MidY), 0.10);
        router.Up(new SKPoint(stepper.Right - 16, stepper.MidY), 0.12);

        Assert.Equal(6, qty.Value);   // exactly one increment, from the tap on release
    }

    [Fact]
    public void DraggingAScrollView_PansIt()
    {
        var (bridge, router) = Host(new LongListPage());
        Assert.True(bridge.TryGetFrame("0", out var scroll));

        // Drag upward — content should move up, i.e. the scroll offset grows.
        router.Down(new SKPoint(scroll.MidX, 600), 0);
        router.Move(new SKPoint(scroll.MidX, 500), 0.05);
        router.Move(new SKPoint(scroll.MidX, 400), 0.10);
        router.Up(new SKPoint(scroll.MidX, 400), 0.12);

        var afterUp = bridge.ScrollOffsetOf("0");
        Assert.True(afterUp > 150, $"expected the list to have scrolled ~200px, got {afterUp}");

        // Dragging back down returns toward the top, clamped at 0.
        router.Down(new SKPoint(scroll.MidX, 300), 1.0);
        router.Move(new SKPoint(scroll.MidX, 700), 1.05);
        router.Up(new SKPoint(scroll.MidX, 700), 1.08);
        Assert.Equal(0, bridge.ScrollOffsetOf("0"));
    }

    [Fact]
    public void PanningAScrollView_DoesNotAlsoTapTheRowUnderTheFinger()
    {
        var tapped = new List<string>();
        var (bridge, router) = Host(new LongListPage(tapped));

        router.Down(new SKPoint(200, 600), 0);
        router.Move(new SKPoint(200, 450), 0.05);
        router.Up(new SKPoint(200, 450), 0.08);

        Assert.Empty(tapped);
        GC.KeepAlive(bridge);
    }

    [Fact]
    public void TappingTheBuiltInColorPicker_OpensASwatchPopover_AndPicking_SetsThatColour()
    {
        // It used to blind-cycle to the next palette entry per tap, which is indistinguishable from a
        // broken control: there is no way to *choose* a colour, only to step past the one you wanted.
        var color = new State<string>("#FF3B30");
        var (bridge, router) = Host(new ColorPage(color));
        Assert.True(bridge.TryGetFrame("0.1", out var picker));

        Tap(router, picker.MidX, picker.MidY);
        Assert.Equal("#FF3B30", color.Value);            // opening the popover changes nothing yet

        // The popover is laid out during the overlay paint pass, so render before hit-testing it.
        var image = new SkiaImageHost(bridge);
        image.RenderPng(W, H);

        // Pick the swatch two along on the top row (the palette's third entry).
        Assert.True(bridge.TryGetSwatchCenter("0.1", 2, out var swatch));
        Tap(router, swatch.X, swatch.Y);

        Assert.Equal("#FFCC00", color.Value);
    }

    static void Tap(SkiaPointerRouter router, float x, float y)
    {
        router.Down(new SKPoint(x, y), 0);
        router.Up(new SKPoint(x, y), 0.05);
    }

    static (SkiaBridge, SkiaPointerRouter) Host(View root)
    {
        var bridge = new SkiaBridge();
        var image = new SkiaImageHost(bridge);
        SwiftApp.Run(root, bridge);
        image.RenderPng(W, H);          // gestures hit-test against laid-out frames
        return (bridge, new SkiaPointerRouter(bridge));
    }
}

file sealed class SliderPage(State<double> volume) : View
{
    public override View Body => new VStack(
        new Text("Volume"),
        new Slider(volume)
    );
}

file sealed class StepperPage(State<int> qty) : View
{
    public override View Body => new VStack(
        new Text("Quantity"),
        new Stepper("Quantity:", qty, 0, 10)
    );
}

file sealed class ColorPage(State<string> color) : View
{
    public override View Body => new VStack(
        new Text("Accent"),
        new ColorPicker("Accent color", color)
    );
}

file sealed class LongListPage(List<string>? tapped = null) : View
{
    public override View Body => new ScrollView(
        Enumerable.Range(0, 60)
            .Select(i => (View)new Text($"Row {i}").OnTapGesture(() => tapped?.Add($"row{i}")))
            .ToArray()
    );
}
