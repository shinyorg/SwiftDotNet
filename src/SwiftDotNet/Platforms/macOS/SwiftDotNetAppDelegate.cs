using AppKit;
using CoreGraphics;
using Foundation;

namespace SwiftDotNet;

/// <summary>
/// Reusable macOS (AppKit) application delegate that hosts a SwiftDotNet root view as SwiftUI. Subclass it,
/// export the ObjC name, and return your root — the window creation and host-view sizing (including the
/// fix that keeps the window from collapsing to its intrinsic size) are done for you:
/// <code>
/// [Register("AppDelegate")]
/// public sealed class AppDelegate : SwiftDotNetAppDelegate
/// {
///     protected override View CreateRoot() => new ContentView();
/// }
/// </code>
/// </summary>
public abstract class SwiftDotNetAppDelegate : NSApplicationDelegate
{
    NSWindow? _window;

    /// <summary>Return the root view for the app. Called once during launch.</summary>
    protected abstract View CreateRoot();

    /// <summary>Initial content size of the host window. Override to change it.</summary>
    protected virtual CGSize InitialSize => new(440, 820);

    /// <summary>Window title. Override to change it.</summary>
    protected virtual string WindowTitle => "SwiftDotNet";

    public override void DidFinishLaunching(NSNotification notification)
    {
        var host = SwiftDotNetHost.CreateRootController(CreateRoot());

        _window = new NSWindow(
            new CGRect(CGPoint.Empty, InitialSize),
            NSWindowStyle.Titled | NSWindowStyle.Closable | NSWindowStyle.Resizable | NSWindowStyle.Miniaturizable,
            NSBackingStore.Buffered,
            deferCreation: false)
        {
            Title = WindowTitle,
        };

        // Fill the window with the SwiftUI host view (autoresizing) rather than letting the content
        // controller collapse the window to its intrinsic fitting size.
        var content = _window.ContentView!;
        var view = host.View;
        view.Frame = content.Bounds;
        view.AutoresizingMask = NSViewResizingMask.WidthSizable | NSViewResizingMask.HeightSizable;
        content.AddSubview(view);

        _window.Center();
        _window.MakeKeyAndOrderFront(null);

        NSApplication.SharedApplication.ActivationPolicy = NSApplicationActivationPolicy.Regular;
        NSApplication.SharedApplication.ActivateIgnoringOtherApps(true);
    }

    public override bool ApplicationShouldTerminateAfterLastWindowClosed(NSApplication sender) => true;
}
