#if SWIFTDOTNET_WATCH_LIVE
import Foundation
import Network
import SwiftUI

/// Live mode: the .NET app runs on the Mac (under a debugger, with Hot Reload) and this shell only renders.
/// Connects to the Mac over loopback TCP and keeps retrying, so either side can restart independently.
/// Frames: 4-byte big-endian length + UTF-8 JSON object. Protocol: src/SwiftDotNet.Watch/Live/LiveHost.cs.
final class LiveConnection: ObservableObject, @unchecked Sendable {
    static let shared = LiveConnection()

    @Published private(set) var connected = false
    /// Why the .NET side isn't serving this shell (e.g. another session owns it).
    @Published private(set) var message: String?

    let port: UInt16 = {
        if let env = ProcessInfo.processInfo.environment["SWIFTDOTNET_WATCH_LIVE_PORT"], let p = UInt16(env) { return p }
        if let p = Bundle.main.object(forInfoDictionaryKey: "SwiftDotNetWatchLivePort") as? Int { return UInt16(p) }
        return 50000
    }()

    private let queue = DispatchQueue(label: "swiftdotnet.watch.live")
    private var connection: NWConnection? // queue-confined
    private var rejected = false          // queue-confined

    func start() {
        queue.async { self.connect() }
    }

    /// A renderer UI event (the same payload the in-process host hands to `swiftdotnet_watch_event`).
    func sendEvent(_ id: String, _ value: String?) {
        send(["op": "event", "id": id, "value": value ?? NSNull()])
    }

    /// Sends a message to .NET. Safe from any thread; dropped while disconnected.
    func send(_ message: [String: Any]) {
        let frame = Self.frame(message)
        queue.async {
            self.connection?.send(content: frame, completion: .contentProcessed { _ in })
        }
    }

    private func connect() {
        let c = NWConnection(host: "127.0.0.1", port: NWEndpoint.Port(rawValue: port)!, using: .tcp)
        connection = c
        c.stateUpdateHandler = { [weak self] state in
            guard let self else { return }
            switch state {
            case .ready:
                SwiftDotNetWatch.logger.info("live: connected to .NET on port \(self.port)")
                c.send(content: Self.frame(["op": "hello", "client": "watch"]), completion: .contentProcessed { _ in })
                DispatchQueue.main.async {
                    self.connected = true
                    self.message = nil
                }
                self.receive(c)
            case .waiting:
                // refused (.NET isn't running yet): NWConnection only retries on network changes, so retry ourselves
                c.cancel()
            case .failed, .cancelled:
                self.closed(c)
            default:
                break
            }
        }
        c.start(queue: queue)
    }

    private func closed(_ c: NWConnection) {
        guard connection === c else { return }
        connection = nil
        DispatchQueue.main.async { self.connected = false }
        // a rejected shell backs off so it doesn't hammer a session that belongs to another client
        queue.asyncAfter(deadline: .now() + (rejected ? 3 : 1)) { self.connect() }
    }

    private func receive(_ c: NWConnection) {
        c.receive(minimumIncompleteLength: 4, maximumLength: 4) { [weak self] header, _, _, error in
            guard let self, let header, header.count == 4, error == nil else { c.cancel(); return }
            let length = Int(header.withUnsafeBytes { $0.loadUnaligned(as: UInt32.self) }.bigEndian)
            c.receive(minimumIncompleteLength: length, maximumLength: length) { body, _, _, error in
                guard let body, body.count == length, error == nil else { c.cancel(); return }
                self.rejected = false
                if self.handle(body) == false {
                    self.rejected = true
                    c.cancel()
                    return
                }
                self.receive(c)
            }
        }
    }

    /// Returns false when .NET turned this shell away.
    private func handle(_ body: Data) -> Bool {
        guard let msg = (try? JSONSerialization.jsonObject(with: body)) as? [String: Any] else { return true }
        switch msg["op"] as? String {
        case "reject":
            let reason = msg["reason"] as? String ?? "Rejected by .NET."
            SwiftDotNetWatch.logger.info("live: \(reason, privacy: .public)")
            DispatchQueue.main.async { self.message = reason }
            return false
        case "render":
            // A TreeDiffer patch, exactly what the in-process host passes to swiftdotnet_render.
            let patch = msg["patch"] as? String ?? "{\"ops\":[]}"
            DispatchQueue.main.async {
                patch.withCString { swiftdotnet_render($0) }
            }
        case "call":
            let id = (msg["id"] as? NSNumber)?.intValue ?? 0
            let name = msg["name"] as? String ?? ""
            let args = msg["args"] as? String ?? "null"
            DispatchQueue.main.async {
                let result = SwiftDotNetWatchServices.callJSON(name, args)
                self.send(["op": "result", "id": id, "result": result ?? NSNull()])
            }
        default:
            break
        }
        return true
    }

    private static func frame(_ message: [String: Any]) -> Data {
        let body = (try? JSONSerialization.data(withJSONObject: message)) ?? Data("{}".utf8)
        var frame = Data(count: 4)
        frame.withUnsafeMutableBytes { $0.storeBytes(of: UInt32(body.count).bigEndian, as: UInt32.self) }
        frame.append(body)
        return frame
    }
}
#endif
