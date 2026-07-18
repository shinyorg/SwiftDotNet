using AppKit;
using CoreGraphics;
using Foundation;
using SwiftDotNet;
using SwiftDotNet.Sample;

namespace MacSample;

[Register("AppDelegate")]
public class AppDelegate : NSApplicationDelegate
{
    NSWindow? _window;
    NSViewController? _hostController;

    public override void DidFinishLaunching(NSNotification notification)
    {
        // The SAME shared ContentView the iOS/Android apps render — here as SwiftUI hosted in AppKit.
        _hostController = SwiftDotNetHost.CreateRootController(new ContentView());

        _window = new NSWindow(
            new CGRect(0, 0, 440, 820),
            NSWindowStyle.Titled | NSWindowStyle.Closable | NSWindowStyle.Resizable | NSWindowStyle.Miniaturizable,
            NSBackingStore.Buffered,
            deferCreation: false)
        {
            Title = "SwiftDotNet",
        };

        // Fill the window with the SwiftUI host view (autoresizing) rather than letting the
        // content controller collapse the window to its intrinsic fitting size.
        var content = _window.ContentView!;
        var host = _hostController.View;
        host.Frame = content.Bounds;
        host.AutoresizingMask = NSViewResizingMask.WidthSizable | NSViewResizingMask.HeightSizable;
        content.AddSubview(host);

        _window.Center();
        _window.MakeKeyAndOrderFront(null);

        NSApplication.SharedApplication.ActivationPolicy = NSApplicationActivationPolicy.Regular;
        NSApplication.SharedApplication.ActivateIgnoringOtherApps(true);
    }

    public override bool ApplicationShouldTerminateAfterLastWindowClosed(NSApplication sender) => true;
}
