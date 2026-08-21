#if canImport(AppIntents)
import AppIntents
import Foundation

// MARK: - The one intent
//
// A single parameterised intent stands in for every button on every live surface. The alternative — a
// generated Swift AppIntent type per button — is impossible here: the buttons are declared in C#, at
// runtime, in a tree the extension has never seen at compile time. So the node id travels as a parameter
// and the routing happens on the managed side.
//
// The conformance is LiveActivityIntent, not AppIntent, and that is the load-bearing detail of the whole
// interactive story. A plain AppIntent's perform() runs inside the widget extension, which contains no
// .NET runtime and therefore has no handler to call. LiveActivityIntent (iOS 17.2+) runs in the APP's
// process instead, in the background, which is where the managed handlers live.
//
// It still writes to the shared mailbox before returning. The app process may be launched fresh to run
// this intent and may not have registered handlers yet, and a durable record is what makes the tap
// survive that race — and survive a relaunch entirely.

@available(iOS 17.2, macOS 14.2, *)
public struct SDNLiveActionIntent: LiveActivityIntent {
    public static var title: LocalizedStringResource = "Surface Action"
    public static var isDiscoverable: Bool = false

    @Parameter(title: "Kind") public var kind: String
    @Parameter(title: "Node") public var nodeId: String

    public init() {}

    public init(kind: String, nodeId: String) {
        self.kind = kind
        self.nodeId = nodeId
    }

    public func perform() async throws -> some IntentResult {
        SDNBridge.recordAction(kind: kind, nodeId: nodeId)
        return .result()
    }
}
#endif
