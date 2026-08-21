import Foundation

#if canImport(ActivityKit) && os(iOS)
import ActivityKit

// MARK: - ActivityKit attributes
//
// One attributes type serves every activity the app declares. It has to: the widget extension is compiled
// before any C# runs, so it cannot have a per-activity Swift type. The `kind` discriminates, and the
// trees dictionary carries the slots.
//
// ContentState is what rides inside the 4 KB APNs payload, which is why the C# side ships a compact wire
// and validates its size before publishing. Nothing here can enforce that — by the time a payload is too
// large, APNs has already rejected the update with no error visible to the app.

@available(iOS 16.1, *)
public struct SDNActivityAttributes: ActivityAttributes {
    public struct ContentState: Codable, Hashable {
        /// Slot name ("lockScreen", "compactLeading", …) to serialized compact tree.
        public var trees: [String: String]

        public init(trees: [String: String]) { self.trees = trees }

        /// The decoded tree for a slot, or nil when the activity does not fill it.
        public func node(_ slot: String) -> SDNNode? {
            guard let json = trees[slot] else { return nil }
            return SDNNode.parse(json)
        }
    }

    /// The surface id, matching the C# `LiveActivity.Kind`.
    public var kind: String

    public init(kind: String) { self.kind = kind }
}

/// Starts, updates and ends activities on behalf of the managed driver.
@available(iOS 16.1, *)
enum SDNActivityHost {
    /// Live handles, keyed by kind. ActivityKit can enumerate its own, but only per concrete attributes
    /// type and without our kind filter, so a small index avoids scanning on every update.
    nonisolated(unsafe) static var live: [String: Activity<SDNActivityAttributes>] = [:]

    static func start(kind: String, trees: [String: String]) -> String? {
        // A Live Activity can only be requested while the app is in the foreground, and the throw when it
        // is not is the single most common failure in this API. Reported as nil so the managed side can
        // surface it rather than crashing an app that merely backgrounded at the wrong moment.
        do {
            let activity = try Activity.request(
                attributes: SDNActivityAttributes(kind: kind),
                content: .init(state: .init(trees: trees), staleDate: nil),
                pushType: .token)
            live[kind] = activity
            return activity.id
        } catch {
            return nil
        }
    }

    static func update(kind: String, trees: [String: String]) {
        guard let activity = resolve(kind) else { return }
        Task { await activity.update(.init(state: .init(trees: trees), staleDate: nil)) }
    }

    static func end(kind: String, trees: [String: String]?) {
        guard let activity = resolve(kind) else { return }
        let content: ActivityContent<SDNActivityAttributes.ContentState>? =
            trees.map { .init(state: .init(trees: $0), staleDate: nil) }

        Task {
            // .default lets the system keep the final presentation on screen briefly, which is what makes
            // "Delivered" readable instead of the activity vanishing the instant it completes.
            await activity.end(content, dismissalPolicy: .default)
        }
        live.removeValue(forKey: kind)
    }

    /// The APNs push token for an activity, hex-encoded, once the system has issued one.
    static func pushToken(kind: String) async -> String? {
        guard let activity = resolve(kind) else { return nil }
        for await data in activity.pushTokenUpdates {
            return data.map { String(format: "%02x", $0) }.joined()
        }
        return nil
    }

    static func active(kind: String) -> [String] {
        Activity<SDNActivityAttributes>.activities
            .filter { $0.attributes.kind == kind }
            .map(\.id)
    }

    private static func resolve(_ kind: String) -> Activity<SDNActivityAttributes>? {
        if let cached = live[kind] { return cached }
        // The app may have been relaunched since the activity started, in which case our index is empty
        // but the activity is still running — ActivityKit remembers it, so re-adopt rather than no-op.
        let found = Activity<SDNActivityAttributes>.activities.first { $0.attributes.kind == kind }
        if let found { live[kind] = found }
        return found
    }
}
#endif
