import Foundation
import WatchKit
import WatchConnectivity

/// Native services callable from .NET via `WatchHost.CallNative(operation, args)`.
enum SwiftDotNetWatchServices {
    /// Runs a service and returns its JSON result (or nil).
    static func callJSON(_ op: String, _ argsJson: String) -> String? {
        let args = (try? JSONSerialization.jsonObject(with: Data(argsJson.utf8), options: [.fragmentsAllowed])) as? [String: Any] ?? [:]
        let result: Any?
        switch op {
        case "haptic": result = haptic(args)
        case "device": result = device()
        case "prefs.get": result = UserDefaults.standard.string(forKey: args["key"] as? String ?? "")
        case "prefs.set":
            UserDefaults.standard.set(args["value"] as? String, forKey: args["key"] as? String ?? "")
            result = nil
        case "prefs.remove":
            UserDefaults.standard.removeObject(forKey: args["key"] as? String ?? "")
            result = nil
        case "http.send": result = WatchHttp.shared.send(args)
        case "http.cancel": result = WatchHttp.shared.cancel(args["id"] as? String ?? "")
        case "phone.activate": result = WatchPhone.shared.activate()
        case "phone.state": result = WatchPhone.shared.state()
        case "phone.send": result = WatchPhone.shared.send(args)
        case "phone.context": result = WatchPhone.shared.updateContext(args)
        case "phone.transfer": result = WatchPhone.shared.transfer(args)
        default:
            if let custom = SwiftDotNetWatchRegistry.shared.services[op] {
                result = custom(args)
            } else {
                SwiftDotNetWatch.logger.error("Unknown native operation \(op, privacy: .public)")
                result = nil
            }
        }
        guard let result else { return nil }
        guard let data = try? JSONSerialization.data(withJSONObject: result, options: [.fragmentsAllowed]) else { return nil }
        return String(decoding: data, as: UTF8.self)
    }

    static func haptic(_ args: [String: Any]) -> Any? {
        let type: WKHapticType
        switch args["type"] as? String {
        case "DirectionUp": type = .directionUp
        case "DirectionDown": type = .directionDown
        case "Success": type = .success
        case "Failure": type = .failure
        case "Retry": type = .retry
        case "Start": type = .start
        case "Stop": type = .stop
        case "Click": type = .click
        default: type = .notification
        }
        WKInterfaceDevice.current().play(type)
        return nil
    }

    static func device() -> Any? {
        let d = WKInterfaceDevice.current()
        d.isBatteryMonitoringEnabled = true
        let battery: String
        switch d.batteryState {
        case .charging: battery = "charging"
        case .full: battery = "full"
        case .unplugged: battery = "unplugged"
        default: battery = "unknown"
        }
        #if targetEnvironment(simulator)
        let simulator = true
        #else
        let simulator = false
        #endif
        return [
            "name": d.name,
            "model": d.model,
            "systemName": d.systemName,
            "systemVersion": d.systemVersion,
            "screenWidth": d.screenBounds.width,
            "screenHeight": d.screenBounds.height,
            "screenScale": d.screenScale,
            "batteryLevel": d.batteryLevel,
            "batteryState": battery,
            "isSimulator": simulator,
        ] as [String: Any]
    }
}

/// URLSession-backed HTTP for `WatchHttpMessageHandler` (raw sockets are restricted on watch hardware).
final class WatchHttp: @unchecked Sendable {
    static let shared = WatchHttp()
    private let lock = NSLock()
    private var tasks: [String: URLSessionDataTask] = [:]

    func send(_ args: [String: Any]) -> Any? {
        let id = args["id"] as? String ?? UUID().uuidString
        guard let urlString = args["url"] as? String, let url = URL(string: urlString) else {
            SwiftDotNetWatch.raise("http.response", ["id": id, "error": "invalid url"])
            return nil
        }
        var request = URLRequest(url: url, timeoutInterval: args["timeout"] as? Double ?? 60)
        request.httpMethod = args["method"] as? String ?? "GET"
        for (k, v) in args["headers"] as? [String: Any] ?? [:] {
            request.setValue(v as? String, forHTTPHeaderField: k)
        }
        if let b64 = args["body"] as? String {
            request.httpBody = Data(base64Encoded: b64)
        }
        let task = URLSession.shared.dataTask(with: request) { [weak self] data, response, error in
            self?.lock.withLock { _ = self?.tasks.removeValue(forKey: id) }
            if let error {
                SwiftDotNetWatch.raise("http.response", ["id": id, "error": error.localizedDescription])
                return
            }
            let http = response as? HTTPURLResponse
            var headers: [String: String] = [:]
            for (k, v) in http?.allHeaderFields ?? [:] {
                headers["\(k)"] = "\(v)"
            }
            SwiftDotNetWatch.raise("http.response", [
                "id": id,
                "status": http?.statusCode ?? 0,
                "headers": headers,
                "body": (data ?? Data()).base64EncodedString(),
            ] as [String: Any])
        }
        lock.withLock { tasks[id] = task }
        task.resume()
        return nil
    }

    func cancel(_ id: String) -> Any? {
        lock.withLock { tasks.removeValue(forKey: id) }?.cancel()
        return nil
    }
}

/// WatchConnectivity bridge for `PhoneConnection`.
final class WatchPhone: NSObject, WCSessionDelegate, @unchecked Sendable {
    static let shared = WatchPhone()

    func activate() -> Any? {
        guard WCSession.isSupported() else { return nil }
        WCSession.default.delegate = self
        WCSession.default.activate()
        return nil
    }

    func state() -> Any? {
        let supported = WCSession.isSupported()
        return [
            "supported": supported,
            "activated": supported && WCSession.default.activationState == .activated,
            "reachable": supported && WCSession.default.isReachable,
            "companionInstalled": supported && WCSession.default.isCompanionAppInstalled,
        ]
    }

    func send(_ message: [String: Any]) -> Any? {
        guard WCSession.isSupported(), WCSession.default.isReachable else {
            SwiftDotNetWatch.logger.warning("phone.send ignored: phone not reachable")
            return nil
        }
        WCSession.default.sendMessage(message, replyHandler: nil) { error in
            SwiftDotNetWatch.logger.error("phone.send failed: \(error.localizedDescription, privacy: .public)")
        }
        return nil
    }

    func updateContext(_ context: [String: Any]) -> Any? {
        guard WCSession.isSupported() else { return nil }
        do { try WCSession.default.updateApplicationContext(context) } catch {
            SwiftDotNetWatch.logger.error("phone.context failed: \(error.localizedDescription, privacy: .public)")
        }
        return nil
    }

    func transfer(_ userInfo: [String: Any]) -> Any? {
        guard WCSession.isSupported() else { return nil }
        WCSession.default.transferUserInfo(userInfo)
        return nil
    }

    func session(_ session: WCSession, activationDidCompleteWith activationState: WCSessionActivationState, error: Error?) {
        SwiftDotNetWatch.raise("phone.state", ["activated": activationState == .activated])
        // context the phone set while this app wasn't running
        if activationState == .activated, !session.receivedApplicationContext.isEmpty {
            SwiftDotNetWatch.raise("phone.message", session.receivedApplicationContext)
        }
    }

    func session(_ session: WCSession, didReceiveUserInfo userInfo: [String: Any] = [:]) {
        SwiftDotNetWatch.raise("phone.message", userInfo)
    }

    func sessionReachabilityDidChange(_ session: WCSession) {
        SwiftDotNetWatch.raise("phone.state", ["reachable": session.isReachable])
    }

    func session(_ session: WCSession, didReceiveMessage message: [String: Any]) {
        SwiftDotNetWatch.raise("phone.message", message)
    }

    func session(_ session: WCSession, didReceiveApplicationContext applicationContext: [String: Any]) {
        SwiftDotNetWatch.raise("phone.message", applicationContext)
    }
}
