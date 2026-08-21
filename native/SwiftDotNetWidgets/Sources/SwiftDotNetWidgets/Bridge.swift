import Foundation
#if canImport(WidgetKit)
import WidgetKit
#endif

// MARK: - The C ABI the managed driver calls
//
// Neither WidgetKit nor ActivityKit is bound in .NET for iOS — both are Swift-only frameworks with no
// Objective-C surface to project — so every call from C# lands here. This mirrors the main bridge's
// @_cdecl approach, but with a much smaller surface: there is no tree to patch and no host controller,
// only "publish this state" and "tell me what came back".

public enum SDNBridge {
    /// The App Group id, set once from managed code at startup.
    nonisolated(unsafe) static var appGroup: String = ""

    /// Invoked when a surface tap arrives while the app is running.
    nonisolated(unsafe) static var actionCallback: (@convention(c) (UnsafePointer<CChar>?, UnsafePointer<CChar>?) -> Void)?

    /// Invoked when ActivityKit issues or rotates an activity's APNs push token.
    nonisolated(unsafe) static var pushTokenCallback: (@convention(c) (UnsafePointer<CChar>?, UnsafePointer<CChar>?) -> Void)?

    static var store: SDNSurfaceStore? { SDNSurfaceStore(appGroup: appGroup) }

    /// Records a tap: durably to the mailbox, and immediately to the app if it happens to be running.
    ///
    /// Both, not either. The mailbox alone would delay every tap until the next foreground; the callback
    /// alone would lose taps that arrive while the app is suspended, or that are delivered to a process
    /// launched fresh to run the intent before managed handlers exist.
    public static func recordAction(kind: String, nodeId: String) {
        store?.postAction(kind: kind, nodeId: nodeId)

        if let callback = actionCallback {
            kind.withCString { k in
                nodeId.withCString { n in callback(k, n) }
            }
        }
    }
}

// MARK: - Entry points

@_cdecl("swiftdotnet_live_configure")
public func swiftdotnet_live_configure(_ appGroup: UnsafePointer<CChar>?) {
    SDNBridge.appGroup = appGroup.map { String(cString: $0) } ?? ""
}

@_cdecl("swiftdotnet_live_set_action_callback")
public func swiftdotnet_live_set_action_callback(
    _ callback: @convention(c) (UnsafePointer<CChar>?, UnsafePointer<CChar>?) -> Void
) {
    SDNBridge.actionCallback = callback
}

@_cdecl("swiftdotnet_live_set_push_token_callback")
public func swiftdotnet_live_set_push_token_callback(
    _ callback: @convention(c) (UnsafePointer<CChar>?, UnsafePointer<CChar>?) -> Void
) {
    SDNBridge.pushTokenCallback = callback
}

/// Starts a Live Activity. Returns its id, or null when ActivityKit refuses — most often because the app
/// was not in the foreground, which is a hard requirement for `Activity.request`.
@_cdecl("swiftdotnet_live_start")
public func swiftdotnet_live_start(
    _ kind: UnsafePointer<CChar>?, _ snapshot: UnsafePointer<CChar>?
) -> UnsafeMutablePointer<CChar>? {
    #if canImport(ActivityKit) && os(iOS)
    guard #available(iOS 16.1, *),
          let kindText = kind.map({ String(cString: $0) }),
          let trees = decodeTrees(snapshot) else { return nil }

    guard let id = SDNActivityHost.start(kind: kindText, trees: trees) else { return nil }

    // Push tokens arrive asynchronously and can rotate, so they are reported through a callback rather
    // than returned here. An app that never pushes simply never registers one.
    if SDNBridge.pushTokenCallback != nil {
        Task {
            if let token = await SDNActivityHost.pushToken(kind: kindText) {
                kindText.withCString { k in
                    token.withCString { t in SDNBridge.pushTokenCallback?(k, t) }
                }
            }
        }
    }

    return strdup(id)
    #else
    return nil
    #endif
}

@_cdecl("swiftdotnet_live_update")
public func swiftdotnet_live_update(_ kind: UnsafePointer<CChar>?, _ snapshot: UnsafePointer<CChar>?) {
    #if canImport(ActivityKit) && os(iOS)
    guard #available(iOS 16.1, *),
          let kindText = kind.map({ String(cString: $0) }),
          let trees = decodeTrees(snapshot) else { return }
    SDNActivityHost.update(kind: kindText, trees: trees)
    #endif
}

@_cdecl("swiftdotnet_live_end")
public func swiftdotnet_live_end(_ kind: UnsafePointer<CChar>?, _ snapshot: UnsafePointer<CChar>?) {
    #if canImport(ActivityKit) && os(iOS)
    guard #available(iOS 16.1, *), let kindText = kind.map({ String(cString: $0) }) else { return }
    SDNActivityHost.end(kind: kindText, trees: decodeTrees(snapshot))
    #endif
}

/// Comma-separated ids of every running activity of a kind.
@_cdecl("swiftdotnet_live_active")
public func swiftdotnet_live_active(_ kind: UnsafePointer<CChar>?) -> UnsafeMutablePointer<CChar>? {
    #if canImport(ActivityKit) && os(iOS)
    guard #available(iOS 16.1, *), let kindText = kind.map({ String(cString: $0) }) else { return nil }
    return strdup(SDNActivityHost.active(kind: kindText).joined(separator: ","))
    #else
    return nil
    #endif
}

/// Asks WidgetKit to reload a kind's timelines, or every timeline when `kind` is null.
///
/// A *request*, not a command: the system decides when to actually ask the provider, and it is spending
/// the app's daily refresh budget when it does.
@_cdecl("swiftdotnet_widgets_reload")
public func swiftdotnet_widgets_reload(_ kind: UnsafePointer<CChar>?) {
    #if canImport(WidgetKit)
    if #available(iOS 14.0, macOS 11.0, *) {
        if let kind, let text = String(validatingUTF8: kind), !text.isEmpty {
            WidgetCenter.shared.reloadTimelines(ofKind: text)
        } else {
            WidgetCenter.shared.reloadAllTimelines()
        }
    }
    #endif
}

/// Comma-separated `kind:family` pairs for every widget the user has actually placed.
///
/// Most apps have none. Knowing that up front is what lets the managed side skip rendering entirely
/// rather than publishing into the void on every refresh.
@_cdecl("swiftdotnet_widgets_placements")
public func swiftdotnet_widgets_placements() -> UnsafeMutablePointer<CChar>? {
    #if canImport(WidgetKit)
    guard #available(iOS 14.0, macOS 11.0, *) else { return nil }

    let semaphore = DispatchSemaphore(value: 0)
    var result = ""

    WidgetCenter.shared.getCurrentConfigurations { outcome in
        if case .success(let infos) = outcome {
            result = infos.map { "\($0.kind):\(familyName($0.family))" }.joined(separator: ",")
        }
        semaphore.signal()
    }

    // Bounded wait: this is called from a managed thread that expects a synchronous answer, and an
    // unbounded wait on a system callback is how an app hangs at launch.
    _ = semaphore.wait(timeout: .now() + 2)
    return strdup(result)
    #else
    return nil
    #endif
}

/// Frees a string returned by this bridge.
@_cdecl("swiftdotnet_live_free")
public func swiftdotnet_live_free(_ pointer: UnsafeMutablePointer<CChar>?) {
    free(pointer)
}

// MARK: - Helpers

/// Decodes the delimited snapshot the managed side sends into the slot/variant trees.
private func decodeTrees(_ snapshot: UnsafePointer<CChar>?) -> [String: String]? {
    guard let snapshot else { return nil }
    let text = String(cString: snapshot)
    return SDNSurfaceStore.decode(text)?.trees
}

#if canImport(WidgetKit)
@available(iOS 14.0, macOS 11.0, *)
private func familyName(_ family: WidgetFamily) -> String {
    switch family {
    case .systemSmall: return "Small"
    case .systemMedium: return "Medium"
    case .systemLarge: return "Large"
    case .systemExtraLarge: return "ExtraLarge"
    default:
        // The lock-screen accessories are gated behind availability, so they are matched by description
        // rather than by case — a new family in a future OS then reads as its own name instead of
        // failing to compile.
        let name = String(describing: family)
        if name.contains("Circular") { return "AccessoryCircular" }
        if name.contains("Rectangular") { return "AccessoryRectangular" }
        if name.contains("Inline") { return "AccessoryInline" }
        return "Medium"
    }
}
#endif
