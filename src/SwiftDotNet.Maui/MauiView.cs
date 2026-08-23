// This file lives in the SwiftDotNet namespace, where `View` is the *DSL* View — alias MAUI's.
using MauiControl = Microsoft.Maui.Controls.View;

namespace SwiftDotNet;

/// <summary>
/// Embeds a real <see cref="MauiControl">.NET MAUI view</see> inside a SwiftDotNet tree.
///
/// <code>
/// new MauiView(() => new Microsoft.Maui.Controls.DatePicker())
///     .Update(v => ((DatePicker)v).Date = _date.Value)
///     .Size(320, 44)
/// </code>
///
/// <para>It is a <see cref="CustomView"/> like <c>Map</c> and <c>CameraView</c>, so a backend with no
/// renderer for it shows the standard ⚠️ placeholder rather than crashing. Where it *is* supported the
/// control is not drawn by the engine at all: the host floats the real MAUI view above the canvas at this
/// node's frame, through the platform-view seam (<c>SwiftDotNet.Graphics.IPlatformViewHost</c>).</para>
///
/// <para><b>The factory never crosses the wire.</b> Props are JSON scalars — the patch protocol is
/// hand-rolled and reflection-free — so a delegate cannot be serialized into the tree. It travels beside
/// the wire instead, in <see cref="MauiViewRegistry"/>, which works because the host and the DSL share a
/// process on every pure-C# backend.</para>
///
/// <para><b>Identity is the node id, not the object.</b> A <see cref="MauiView"/> is reconstructed on every
/// render pass, so the instance cannot be the identity — the structural node id is, and the host reuses one
/// real control for the lifetime of that id. Give <see cref="Key"/> an explicit value inside a keyed
/// <c>List</c>, where a row's structural position moves but its identity should not.</para>
/// </summary>
public sealed class MauiView : CustomView
{
    /// <summary>The renderer key. Hosts register a platform-view renderer under this name.</summary>
    public const string NodeType = "MauiView";

    readonly Func<MauiControl> _factory;
    Action<MauiControl>? _update;
    Action<string?>? _onEvent;
    string? _key;
    double _w = -1, _h = -1;

    /// <param name="factory">
    /// Creates the control. Called <b>once per identity</b>, not once per render — the host caches what it
    /// builds. Push changing values in through <see cref="Update"/> instead.
    /// </param>
    public MauiView(Func<MauiControl> factory) => _factory = factory ?? throw new ArgumentNullException(nameof(factory));

    /// <summary>
    /// Stable identity across reorders. Defaults to the structural node id, which is correct everywhere
    /// except inside a keyed collection, where a row keeps its identity while its position changes.
    /// </summary>
    public MauiView Key(string key) { _key = key; return this; }

    /// <summary>
    /// Push current values into the live control. Called by the host on every frame it places the view, so
    /// it must be cheap and idempotent — it is the only way state reaches a control the engine does not draw.
    /// </summary>
    public MauiView Update(Action<MauiControl> update) { _update = update; return this; }

    /// <summary>
    /// The control's size in DIPs. <b>Required in practice:</b> layout has to settle before the host can
    /// place anything, so the engine cannot ask a control that does not exist yet how big it wants to be.
    /// Without it the node fills the available width and takes a 120pt height.
    /// </summary>
    public MauiView Size(double width, double height) { _w = width; _h = height; return this; }

    /// <summary>
    /// Receive events the embedded control raises back into C#. The control calls
    /// <c>MauiViewRegistry.Emit(key, value)</c>; this is where the value lands.
    /// </summary>
    public MauiView OnEvent(Action<string?> handler) { _onEvent = handler; return this; }

    protected override string TypeName => NodeType;

    protected override void Configure(CustomNode n)
    {
        var key = _key ?? n.Id;
        MauiViewRegistry.Bind(key, n.Id, _factory, _update);
        n.Prop("key", key);
        // Only emit the size props when they were actually set: an absent w makes the node greedy in width,
        // which is what an embedded control in a form row usually wants.
        if (_w > 0) n.Prop("w", _w);
        if (_h > 0) n.Prop("h", _h);
        if (_onEvent is { } handler) n.OnEvent(handler);
    }
}
