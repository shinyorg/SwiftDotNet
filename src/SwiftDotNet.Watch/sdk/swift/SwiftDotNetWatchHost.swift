import Foundation
import SwiftUI
import os

/// Wires the SwiftDotNetBridge renderer to .NET. In-process (NativeAOT linked into this app) by default;
/// with SWIFTDOTNET_WATCH_LIVE the app is a thin shell and .NET runs on the Mac, reached through
/// `LiveConnection`. Either way the renderer is untouched: patches arrive at `swiftdotnet_render`, and
/// UI events leave through the callback installed with `swiftdotnet_set_event_callback`.
enum SwiftDotNetWatch {
    static let logger = Logger(subsystem: Bundle.main.bundleIdentifier ?? "swiftdotnet.watch", category: "dotnet")
    private static var started = false

    static func start() {
        guard !started else { return }
        started = true
        #if SWIFTDOTNET_WATCH_LIVE
        swiftdotnet_set_event_callback { id, value in
            LiveConnection.shared.sendEvent(String(cString: id), value.map { String(cString: $0) })
        }
        LiveConnection.shared.start()
        #else
        swiftdotnet_set_event_callback { id, value in swiftdotnet_watch_event(id, value) }
        var callbacks = SwiftDotNetWatchCallbacks(
            render: { json in
                if let json { swiftdotnet_render(json) }
            },
            request_drain: {
                DispatchQueue.main.async { swiftdotnet_watch_drain() }
            },
            log: { level, message in
                SwiftDotNetWatch.log(Int(level), message.map { String(cString: $0) } ?? "")
            },
            call: { op, args in
                let operation = op.map { String(cString: $0) } ?? ""
                let json = args.map { String(cString: $0) } ?? "null"
                return SwiftDotNetWatchServices.callJSON(operation, json).map { strdup($0) }
            }
        )
        swiftdotnet_watch_register_callbacks(&callbacks)
        swiftdotnet_watch_bootstrap()
        #endif
    }

    static func phase(_ phase: ScenePhase) {
        let value: Int32
        switch phase {
        case .active: value = 0
        case .inactive: value = 1
        case .background: value = 2
        @unknown default: return
        }
        #if SWIFTDOTNET_WATCH_LIVE
        LiveConnection.shared.send(["op": "phase", "phase": Int(value)])
        #else
        swiftdotnet_watch_phase(value)
        #endif
    }

    /// Raise an event in .NET (`WatchHost.OnNativeEvent`). Safe from any thread.
    static func raise(_ name: String, _ payload: Any?) {
        let data = (try? JSONSerialization.data(withJSONObject: payload ?? NSNull(), options: [.fragmentsAllowed])) ?? Data("null".utf8)
        let json = String(decoding: data, as: UTF8.self)
        #if SWIFTDOTNET_WATCH_LIVE
        LiveConnection.shared.send(["op": "nativeEvent", "name": name, "json": json])
        #else
        DispatchQueue.main.async { swiftdotnet_watch_native_event(name, json) }
        #endif
    }

    static func log(_ level: Int, _ text: String) {
        switch level {
        case 0: logger.debug("\(text, privacy: .public)")
        case 1: logger.info("\(text, privacy: .public)")
        case 2: logger.warning("\(text, privacy: .public)")
        default: logger.error("\(text, privacy: .public)")
        }
        print("[dotnet] \(text)")
    }
}

/// Extension point for app-specific Swift (`<WatchSwift>` items in the .csproj). Define
/// `func swiftDotNetWatchConfigure(_ registry: SwiftDotNetWatchRegistry)` in one of your Swift files to
/// register native services (`WatchHost.CallNative("op", args)` in C#) and SwiftUI renderers for your
/// `CustomView` types.
final class SwiftDotNetWatchRegistry: @unchecked Sendable {
    static let shared = SwiftDotNetWatchRegistry()

    typealias Service = (_ args: [String: Any]) -> Any?

    // populated once at startup, before .NET starts; read-only afterwards
    private(set) var services: [String: Service] = [:]

    func service(_ name: String, _ handler: @escaping Service) { services[name] = handler }

    /// Same registry the iOS/macOS/tvOS bridge uses: renders `CustomView` nodes whose TypeName is `type`.
    func renderer(_ type: String, _ renderer: @escaping SwiftDotNetRenderer) { swiftDotNetRegisterRenderer(type, renderer) }
}
