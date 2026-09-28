namespace SwiftDotNet.AndroidAuto;

using AndroidX.Car.App;
using SwiftDotNet.Graphics;
using AndroidRect = Android.Graphics.Rect;
using GPoint = SwiftDotNet.Graphics.Point;
using GSize = SwiftDotNet.Graphics.Size;

/// <summary>
/// Draws <see cref="CarMapTemplate.Draw"/> onto the Android Auto surface through <see cref="AndroidCanvas"/>
/// — the same renderer-seam adapter the live surfaces use — and forwards surface gestures in
/// device-independent units.
/// </summary>
sealed class CarMapSurfaceRenderer(Action<string>? log) : Java.Lang.Object, ISurfaceCallback
{
    // Font objects are cached per size/weight; one provider per surface keeps that cache warm across frames.
    readonly AndroidFontProvider fonts = new();
    SurfaceContainer? container;

    public CarMapTemplate? Model { get; set; }

    float Density => container is { Dpi: > 0 } c ? c.Dpi / 160f : 1f;

    public void Draw()
    {
        var model = Model;
        var surface = container?.Surface;
        if (model is null || surface is null || !surface.IsValid)
            return;

        Android.Graphics.Canvas? canvas = null;
        try
        {
            canvas = surface.LockCanvas(null);
            if (canvas is null)
                return;

            var density = Density;
            var size = new GSize(container!.Width / density, container.Height / density);
            canvas.Save();
            canvas.Scale(density, density);
            model.Draw(new CarMapDrawContext(new AndroidCanvas(canvas, size.Width, size.Height), fonts, size));
            canvas.Restore();
        }
        catch (Exception ex)
        {
            log?.Invoke($"SwiftDotNet.Car map draw failed: {ex}");
        }
        finally
        {
            if (canvas is not null)
                surface.UnlockCanvasAndPost(canvas);
        }
    }

    public void OnSurfaceAvailable(SurfaceContainer? surfaceContainer)
    {
        container = surfaceContainer;
        Draw();
    }

    public void OnSurfaceDestroyed(SurfaceContainer? surfaceContainer) => container = null;

    public void OnVisibleAreaChanged(AndroidRect? visibleArea) => Draw();

    public void OnStableAreaChanged(AndroidRect? stableArea) => Draw();

    public void OnScroll(float distanceX, float distanceY)
    {
        // Android reports scroll distance (finger movement inverted); the car model reports content translation.
        Model?.OnPan?.Invoke(-distanceX / Density, -distanceY / Density);
    }

    public void OnFling(float velocityX, float velocityY) { }

    public void OnScale(float focusX, float focusY, float scaleFactor)
    {
        // hosts send -1 for a double-tap "zoom in" gesture
        var factor = scaleFactor < 0 ? 2f : scaleFactor;
        Model?.OnZoom?.Invoke(factor, new GPoint(focusX / Density, focusY / Density));
    }

    public void OnClick(float x, float y) => Model?.OnTap?.Invoke(new GPoint(x / Density, y / Density));
}
