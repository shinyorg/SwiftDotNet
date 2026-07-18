using System.Globalization;
using SwiftDotNet;
using SwiftDotNet.Sample;

// SDN_TEST=1 verifies custom controls headlessly (composite Rating + a custom native primitive via the
// GtkRenderers registry); otherwise launches the GTK GUI with the shared ContentView (which includes the
// composite Rating control).
if (Environment.GetEnvironmentVariable("SDN_TEST") == "1")
    return CustomControlTest.Run();

return SwiftDotNetHost.Run(new ContentView());

/// <summary>
/// A CUSTOM NATIVE PRIMITIVE (not a composition) — emits type "NativeRating"; a GTK renderer registered
/// via GtkRenderers turns it into a real Gtk.Scale. Renders as ⚠️ on backends with no renderer registered.
/// </summary>
sealed class NativeRating : CustomView
{
    readonly State<int> _value;
    public NativeRating(State<int> value) => _value = value;

    protected override string TypeName => "NativeRating";
    protected override void Configure(CustomNode node)
    {
        node.Prop("value", _value.Value);
        node.OnEvent(v => _value.Value = (int)double.Parse(v ?? "0", CultureInfo.InvariantCulture));
    }
}

sealed class CustomDemo : View
{
    public readonly State<int> Composite = new(3);
    public readonly State<int> Native = new(2);

    public override View Body => new VStack(
        new Rating(Composite),      // composite custom control (SharedUI/Rating.cs) — pure C#, works everywhere
        new NativeRating(Native)    // custom native primitive — rendered by the registered GtkRenderer
    ).Spacing(8);
}

static class CustomControlTest
{
    public static int Run()
    {
        Gtk.Module.Initialize();

        // Register a native GTK renderer for the "NativeRating" custom primitive — no interpreter fork.
        GtkRenderers.Register("NativeRating", ctx =>
        {
            var scale = Gtk.Scale.NewWithRange(Gtk.Orientation.Horizontal, 0, 5, 1);
            scale.SetValue(ctx.Number("value") ?? 0);
            scale.OnValueChanged += (_, _) => ctx.Emit(((int)scale.GetValue()).ToString(CultureInfo.InvariantCulture));
            return scale;
        });

        var demo = new CustomDemo();
        var bridge = new GtkBridge();
        SwiftApp.Run(demo, bridge);

        Console.WriteLine("widget tree: " + Dump(bridge.Host));
        Console.WriteLine($"contains Gtk.Scale (custom renderer used): {Contains(bridge.Host, "Scale")}");

        // Fire the custom primitive's event as its GTK signal would → C# OnEvent → State updates.
        bridge.Emit("0.1", "5"); // NativeRating is child 1 of the root VStack
        Console.WriteLine($"after emit('0.1','5'): Native.Value = {demo.Native.Value}  (expected 5)");
        return 0;
    }

    static bool Contains(Gtk.Widget root, string typeName)
    {
        for (var w = root.GetFirstChild(); w is not null; w = w.GetNextSibling())
            if (w.GetType().Name == typeName || Contains(w, typeName)) return true;
        return false;
    }

    static string Dump(Gtk.Widget root)
    {
        var sb = new System.Text.StringBuilder();
        void Walk(Gtk.Widget? w)
        {
            for (; w is not null; w = w.GetNextSibling())
            {
                sb.Append(w switch
                {
                    Gtk.Label l => $"[Label '{l.GetText()}']",
                    Gtk.Button b => $"[Button '{b.GetLabel()}']",
                    Gtk.Scale => "[Scale]",
                    _ => $"[{w.GetType().Name}(",
                });
                if (w is not Gtk.Label and not Gtk.Button and not Gtk.Scale) { Walk(w.GetFirstChild()); sb.Append(")]"); }
            }
        }
        Walk(root.GetFirstChild());
        return sb.ToString();
    }
}
