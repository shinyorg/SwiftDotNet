import Foundation
#if canImport(WidgetKit)
import WidgetKit
import SwiftUI

// MARK: - The dumb timeline provider
//
// The inversion at the heart of Apple widget support: this provider computes NOTHING. It reads a snapshot
// the app already published into the shared App Group container and hands the pre-rendered trees back to
// WidgetKit.
//
// It has to work this way. A TimelineProvider runs inside the widget extension, and the extension is a
// separate binary with no .NET in it — there is no way to call the app's C# TimelineAsync from here. So
// the app renders every entry for every placed family up front, and this file is the thin reader on the
// far side.
//
// The consequences are real and worth stating where the code is: a widget can only show data the app has
// already computed, and keeping it fresh needs a background trigger in the *app* (BGAppRefreshTask). A
// widget does not refresh itself.

@available(iOS 17.0, macOS 14.0, *)
public struct SDNWidgetEntry: TimelineEntry {
    public let date: Date
    public let json: String?
    public let kind: String

    public init(date: Date, json: String?, kind: String) {
        self.date = date
        self.json = json
        self.kind = kind
    }
}

@available(iOS 17.0, macOS 14.0, *)
public struct SDNTimelineProvider: TimelineProvider {
    private let kind: String
    private let appGroup: String
    private let family: String

    /// - Parameters:
    ///   - kind: the surface id the app publishes under.
    ///   - appGroup: the App Group id, entitled on both the app and this extension.
    ///   - family: which family's trees to read, e.g. "Small". WidgetKit hands the real family to the
    ///     view via the environment, but a provider is constructed per-configuration, so it is passed in.
    public init(kind: String, appGroup: String, family: String = "Medium") {
        self.kind = kind
        self.appGroup = appGroup
        self.family = family
    }

    public func placeholder(in context: Context) -> SDNWidgetEntry {
        SDNWidgetEntry(date: Date(), json: nil, kind: kind)
    }

    public func getSnapshot(in context: Context, completion: @escaping (SDNWidgetEntry) -> Void) {
        completion(currentEntry())
    }

    public func getTimeline(in context: Context, completion: @escaping (Timeline<SDNWidgetEntry>) -> Void) {
        guard let store = SDNSurfaceStore(appGroup: appGroup),
              let snapshot = store.read(kind: kind) else {
            // No snapshot at all means the app has never published — most likely it has not run since the
            // widget was added. A single placeholder entry with a near reload is the right recovery.
            completion(Timeline(entries: [placeholder(in: context)],
                                policy: .after(Date().addingTimeInterval(900))))
            return
        }

        let published = snapshot.entries(family: family)
        let entries = published.isEmpty
            ? [SDNWidgetEntry(date: Date(), json: nil, kind: kind)]
            : published.map { SDNWidgetEntry(date: $0.at, json: nil, kind: kind).with(node: $0.node) }

        let policy: TimelineReloadPolicy = snapshot.refreshAfter
            .map { .after(Date(timeIntervalSince1970: $0)) } ?? .never
        completion(Timeline(entries: entries, policy: policy))
    }

    private func currentEntry() -> SDNWidgetEntry {
        guard let store = SDNSurfaceStore(appGroup: appGroup),
              let snapshot = store.read(kind: kind) else {
            return SDNWidgetEntry(date: Date(), json: nil, kind: kind)
        }

        let now = Date()
        // The same selection rule the managed side implements in WidgetPayload.SelectTree: the latest
        // entry at or before now, falling back to the earliest if the clock is behind them all.
        let entries = snapshot.entries(family: family)
        let current = entries.last { $0.at <= now } ?? entries.first
        return SDNWidgetEntry(date: now, json: nil, kind: kind).with(node: current?.node)
    }
}

@available(iOS 17.0, macOS 14.0, *)
extension SDNWidgetEntry {
    /// Carries a decoded node alongside the entry. WidgetKit requires TimelineEntry to be a value type,
    /// and SDNNode is a reference type, so it rides in a side table keyed by the entry's date rather than
    /// forcing the node to be Codable twice over.
    func with(node: SDNNode?) -> SDNWidgetEntry {
        SDNNodeCache.shared.put(date: date, kind: kind, node: node)
        return self
    }

    /// The tree for this entry, or nil for a placeholder.
    public var node: SDNNode? { SDNNodeCache.shared.get(date: date, kind: kind) }
}

/// Small side table so a decoded tree can ride with a value-type TimelineEntry.
final class SDNNodeCache: @unchecked Sendable {
    static let shared = SDNNodeCache()
    private var storage: [String: SDNNode] = [:]
    private let lock = NSLock()

    func put(date: Date, kind: String, node: SDNNode?) {
        guard let node else { return }
        lock.lock(); defer { lock.unlock() }
        storage[key(date, kind)] = node
        // A provider is asked for a bounded timeline and the extension is short-lived, so this only needs
        // to not grow without limit within one launch.
        if storage.count > 64 { storage.removeAll(keepingCapacity: true) }
    }

    func get(date: Date, kind: String) -> SDNNode? {
        lock.lock(); defer { lock.unlock() }
        return storage[key(date, kind)]
    }

    private func key(_ date: Date, _ kind: String) -> String {
        "\(kind)@\(date.timeIntervalSince1970)"
    }
}

/// The view a widget extension puts in its body: reads the entry's tree and renders it.
@available(iOS 17.0, macOS 14.0, *)
public struct SDNWidgetView: View {
    private let entry: SDNWidgetEntry

    public init(entry: SDNWidgetEntry) { self.entry = entry }

    public var body: some View {
        SDNLiveView(entry.node, kind: entry.kind)
            // Mandatory from iOS 17: a widget without a container background is rejected at build time
            // in Xcode 15+ and renders with an unstyled backdrop if it slips through.
            .containerBackgroundIfAvailable()
    }
}

extension View {
    @ViewBuilder
    func containerBackgroundIfAvailable() -> some View {
        if #available(iOS 17.0, macOS 14.0, *) {
            self.containerBackground(.fill.tertiary, for: .widget)
        } else {
            self
        }
    }
}
#endif
