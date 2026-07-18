using SkiaSharp;
using SwiftDotNet;
using SwiftDotNet.Sample;

// Headless harness: renders the SHARED ContentView (the same 6-tab tour every backend renders) through
// the Skia self-drawing engine, writing PNGs. Exercises the full loop (tap → Emit → state → diff →
// repaint), scrolling, and the overlays (sheet / alert / menu popover / nav push).

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

float TabX(int i) => (i + 0.5f) * W / 6;
const float BarY = H - 28;
void Tab(int i) { host.RenderPng(W, H); host.Tap(TabX(i), BarY); host.RenderPng(W, H); }
void TapId(string id) { if (bridge.TryGetFrame(id, out var f)) host.Tap(f.MidX, f.MidY); }
void Shot(string name) => host.RenderToFile(Path.Combine(outDir, name + ".png"), W, H);

string[] tabs = { "inputs", "layout", "carousel", "lists", "maps", "nav" };
for (var i = 0; i < tabs.Length; i++) { Tab(i); Shot($"tab{i}_{tabs[i]}"); }

// Scroll: reveal the lower half of the Layout tab.
Tab(1);
host.Scroll(W / 2, 300, 500);
Shot("layout_scrolled");

// Text editing: focus the Name field on the Inputs tab and type — the bound "Hello, {name}!" updates live.
Tab(0);
TapId("0.0.0.2");            // focus the Name TextField
host.Type("Ada Lovelace");
Shot("inputs_typed");

// Overlays on the Nav tab. Node ids: TabView(0) → Tab(0.5) → Alert(0.5.0) → Sheet(0.5.0.0) →
// NavigationStack(0.5.0.0.0) → Form(0.5.0.0.0.0) → [NavLink .0, "Present sheet" .1, "Show alert" .2, Link .3]
Tab(5);
TapId("0.5.0.0.0.0.1"); Shot("nav_sheet");      // present a sheet
host.Tap(W / 2, 40);                             // tap scrim → dismiss
host.RenderPng(W, H);
TapId("0.5.0.0.0.0.2"); Shot("nav_alert");       // show an alert
host.Tap(W / 2, 740);                             // tap scrim below the box → dismiss
host.RenderPng(W, H);
TapId("0.5.0.0.0.0.0"); Shot("nav_pushed");       // push details onto the nav stack

// Menu popover on the Lists tab: Form(0.3.0) → Section(0.3.0.2) → Menu(0.3.0.2.0)
Tab(3);
TapId("0.3.0.2.0"); Shot("lists_menu");

// (The Maps tab already uses the registered MapRenderer — see tab4_maps.png.)
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
