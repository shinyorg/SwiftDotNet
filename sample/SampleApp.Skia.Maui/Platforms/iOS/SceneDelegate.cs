using Foundation;

namespace SampleApp.Skia.Maui;

/// <summary>
/// The MAUI window scene. iOS 27 refuses to launch an app built with its SDK that hasn't adopted the UIScene
/// life cycle, so Info.plist declares a scene manifest pointing here. MAUI's own scene delegate does the work.
/// </summary>
[Register("SceneDelegate")]
public class SceneDelegate : MauiUISceneDelegate;
