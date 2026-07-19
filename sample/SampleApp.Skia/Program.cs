using SkiaSharp;
using SwiftDotNet;
using SwiftDotNet.Sample;

// Headless harness: renders the SHARED ContentView (the same MAUI-style flyout every backend renders)
// through the Skia self-drawing engine, writing PNGs. Exercises the full loop (tap → Emit → state → diff →
// repaint), scrolling, and the overlays (sheet / alert / menu popover / nav push).
//
// The flyout is a NavigationStack("0") whose root Form("0.0") holds grouped Sections; each Section row is a
// NavigationLink that pushes a detail page. Row ids are 0.0.{section}.{row}. Tapping a row pushes its page;
// tapping the ‹ Back bar (top-left, within the 44pt nav bar) pops it.

const int W = 440, H = 820;
var outDir = args.Length > 0 ? args[0] : ".";
Directory.CreateDirectory(outDir);

// Animation mode (exclusive — SwiftApp holds a single global root): flip a panel and render frames
// along the spring so you can see height + opacity interpolate rather than snap.
if (args.Length > 1 && args[1] == "anim")
{
    var d = new AnimatedDemo();
    var b = new SkiaBridge();
    var h = new SkiaImageHost(b);
    SwiftApp.Run(d, b);
    h.RenderToFile(Path.Combine(outDir, "anim_collapsed.png"), W, H);
    d.Toggle();                                                        // arm the animation
    h.RenderToFile(Path.Combine(outDir, "anim_t0.png"), W, H);         // t≈0
    h.Advance(0.10); h.RenderToFile(Path.Combine(outDir, "anim_t1.png"), W, H);
    h.Advance(0.12); h.RenderToFile(Path.Combine(outDir, "anim_t2.png"), W, H);
    h.Advance(0.80); h.RenderToFile(Path.Combine(outDir, "anim_settled.png"), W, H);
    Console.WriteLine("wrote animation frames to " + Path.GetFullPath(outDir));
    return;
}

// Register a custom Skia renderer for the "Map" CustomView (from SwiftDotNet.Maps) — proves the
// registry seam: an unregistered type shows ⚠️, a registered one draws itself onto the canvas.
SkiaRenderers.Register("Map", new MapRenderer());

var view = new ContentView();
var bridge = new SkiaBridge();
var host = new SkiaImageHost(bridge);
SwiftApp.Run(view, bridge);

void Render() => host.RenderPng(W, H);
void Shot(string name) => host.RenderToFile(Path.Combine(outDir, name + ".png"), W, H);
void TapId(string id) { if (bridge.TryGetFrame(id, out var f)) host.Tap(f.MidX, f.MidY); }
void Back() { host.Tap(24, 22); Render(); }   // tap the ‹ Back bar (top-left of the 44pt nav bar) → pop

// Push a flyout row's detail page by id, screenshot it, then pop back to the menu.
void Page(string id, string name) { Render(); TapId(id); Shot(name); Back(); }

// The flyout menu itself.
Render();
Shot("flyout_menu");

// A page from each section (rows visible without scrolling: Controls, Interaction, Layout, Media).
Page("0.0.0.1", "page_values");     // Controls → Values & Steppers
Page("0.0.0.2", "page_rating");     // Controls → Rating
Page("0.0.1.0", "page_gestures");   // Interaction → Gestures
Page("0.0.2.0", "page_shapes");     // Layout → Shapes & Grid
Page("0.0.3.0", "page_carousel");   // Media → Carousel
Page("0.0.3.3", "page_maps");       // Media → Maps (uses the registered MapRenderer)

// Text editing on a pushed page: push Controls → Text & Input (0.0.0.0), focus the Name field, type.
// TextInputPage children: Title(0) Greeting(1) TextField(2)… → field id is <destination>.2 = 0.0.0.0.1.2.
Render();
TapId("0.0.0.0");                   // push the page
Render();
TapId("0.0.0.0.1.2");               // focus the Name TextField
host.Type("Ada Lovelace");          // the bound "Hello, {name}!" greeting updates live
Shot("page_text_typed");
Back();

// The lower sections (Data, Styling, Navigation) — all fit on screen, so page straight into them.
Page("0.0.4.0", "page_lists");      // Data → Lists & Selection
Page("0.0.4.1", "page_disclosure"); // Data → Disclosure & Menus
Page("0.0.5.0", "page_styles");     // Styling → Global Styles

// Navigation page: push it and screenshot its Sheets & Alerts form. (Presenting the sheet/alert overlays
// from *within* a pushed page is a real-SwiftUI feature the headless Skia overlay doesn't composite, so we
// stop at the page itself — see the Skia.Mac window app to exercise it interactively.)
Render();
TapId("0.0.6.0"); Shot("page_nav"); // push Sheets & Alerts
Back();                             // pop back to the menu before the next push

// Animation page: push it, arm the spring panel, and settle a few frames so the interpolation is visible.
Render();
TapId("0.0.1.1");                   // Interaction → Animation
Render();
TapId("0.0.1.1.1.1");               // tap the Expand/Collapse button (ScrollView → Title(0), Button(1))
Shot("page_animation_t0");
host.Advance(0.12); Render(); Shot("page_animation_t1");
host.Advance(0.60); Render(); Shot("page_animation_settled");

Console.WriteLine("wrote all screenshots to " + Path.GetFullPath(outDir));

/// <summary>A tiny screen whose panel animates (height + opacity) via .Animation(spring, on: state).</summary>
sealed class AnimatedDemo : View
{
    readonly State<bool> _open = new(false);
    public void Toggle() => _open.Value = !_open.Value;

    public override View Body => new VStack(
        new Text("Animation").Font(Font.Title),
        new Text(_open.Value ? "Panel expanded" : "Panel collapsed").Font(Font.Caption).ForegroundColor(Color.Secondary),
        new VStack(
            new Text("Animated panel — height & opacity spring in.").Font(Font.Caption).Padding(12)
        ).Frame(height: _open.Value ? 130 : 0)
         .Opacity(_open.Value ? 1 : 0)
         .Background(Color.Hex("#EEF0FF")).CornerRadius(10)
         .Animation(Anim.EaseInOut(0.4), on: _open.Value)   // visible ramp for still frames
    ).Spacing(16).Padding(24);
}

/// <summary>A custom Skia renderer for the Map CustomView: draws a stylized map with a grid and a pin.</summary>
sealed class MapRenderer : ISkiaRenderer
{
    public SKSize Measure(SkiaRenderContext ctx, SKSize available) => available; // greedy fill

    public void Paint(SkiaRenderContext ctx, SKCanvas c, SKRect r)
    {
        using var bg = new SKPaint { Color = new SKColor(0xDD, 0xEC, 0xE0), IsAntialias = true };
        c.DrawRoundRect(r, 10, 10, bg);
        var save = c.Save();
        c.ClipRoundRect(new SKRoundRect(r, 10));
        using var grid = new SKPaint { Color = new SKColor(0xB4, 0xCC, 0xBC), StrokeWidth = 1 };
        for (var x = r.Left; x < r.Right; x += 40) c.DrawLine(x, r.Top, x, r.Bottom, grid);
        for (var y = r.Top; y < r.Bottom; y += 40) c.DrawLine(r.Left, y, r.Right, y, grid);
        using var road = new SKPaint { Color = new SKColor(0xFF, 0xFF, 0xFF), StrokeWidth = 8, IsAntialias = true };
        c.DrawLine(r.Left, r.MidY + 30, r.Right, r.MidY - 40, road);
        using var pin = new SKPaint { Color = new SKColor(0xFF, 0x3B, 0x30), IsAntialias = true };
        c.DrawCircle(r.MidX, r.MidY, 9, pin);
        c.RestoreToCount(save);
        using var f = new SKFont(SKTypeface.Default, 13);
        using var label = new SKPaint { Color = SKColors.Black, IsAntialias = true };
        c.DrawText("Custom SkiaRenderer ✓  (registry seam)", r.Left + 12, r.Top + 24, f, label);
    }
}
