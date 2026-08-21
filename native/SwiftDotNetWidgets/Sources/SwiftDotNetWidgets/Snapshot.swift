import Foundation

// MARK: - The App Group mailbox
//
// The store both halves of an Apple live surface talk through. The app writes snapshots and reads
// actions; the widget extension reads snapshots and appends actions. They are almost never alive at the
// same time — the user taps a widget, the extension launches, renders and dies, while the app may have
// been suspended for hours — so this is a durable mailbox, not an IPC channel.
//
// The format is delimited rather than JSON on purpose. This file is read at extension launch under a
// tight memory budget, and both sides need to agree on it byte for byte; a line split has no failure
// modes worth reasoning about. It mirrors SwiftDotNet.FileSurfaceChannel exactly:
//
//   line 1:  kind|surface|publishedAt|refreshAfter
//   line n:  variantKey<TAB>tree-json

/// One published surface state, as read back from the shared container.
public struct SDNSnapshot: Sendable {
    public let kind: String
    public let surface: String
    public let publishedAt: Double
    public let refreshAfter: Double?
    public let trees: [String: String]

    /// The tree for a named variant — an activity slot, or a widget's `{family}@{unix-seconds}` key.
    public func tree(_ key: String) -> SDNNode? {
        guard let json = trees[key] else { return nil }
        return SDNNode.parse(json)
    }

    /// Every timeline stamp published for a widget family, ascending.
    public func entries(family: String) -> [(at: Date, node: SDNNode)] {
        let prefix = family + "@"
        return trees.compactMap { key, json -> (at: Date, node: SDNNode)? in
            guard key.hasPrefix(prefix),
                  let seconds = Double(key.dropFirst(prefix.count)),
                  let node = SDNNode.parse(json) else { return nil }
            return (Date(timeIntervalSince1970: seconds), node)
        }
        .sorted { $0.at < $1.at }
    }
}

/// Reads and writes the shared container. Used from the app *and* from the widget extension.
public struct SDNSurfaceStore: Sendable {
    public let containerURL: URL

    /// - Parameter appGroup: the App Group identifier, which must be entitled on **both** targets.
    ///   Getting this wrong fails silently: the extension reads an empty container and renders a
    ///   placeholder forever, with no error anywhere.
    public init?(appGroup: String) {
        guard let url = FileManager.default
            .containerURL(forSecurityApplicationGroupIdentifier: appGroup) else { return nil }
        containerURL = url.appendingPathComponent("swiftdotnet-surfaces", isDirectory: true)
        try? FileManager.default.createDirectory(at: containerURL, withIntermediateDirectories: true)
    }

    public func read(kind: String) -> SDNSnapshot? {
        let url = containerURL.appendingPathComponent(sanitize(kind) + ".surface")
        guard let text = try? String(contentsOf: url, encoding: .utf8) else { return nil }
        return Self.decode(text)
    }

    /// Appends a tap to the mailbox for the app to drain when it next runs.
    public func postAction(kind: String, nodeId: String, value: String? = nil) {
        let line = [
            String(format: "%.3f", Date().timeIntervalSince1970),
            escape(kind),
            escape(nodeId),
            escape(value ?? ""),
        ].joined(separator: "|") + "\n"

        let url = containerURL.appendingPathComponent("actions.log")
        guard let data = line.data(using: .utf8) else { return }

        if let handle = try? FileHandle(forWritingTo: url) {
            defer { try? handle.close() }
            _ = try? handle.seekToEnd()
            try? handle.write(contentsOf: data)
        } else {
            try? data.write(to: url)
        }
    }

    static func decode(_ text: String) -> SDNSnapshot? {
        let lines = text.split(separator: "\n", omittingEmptySubsequences: false)
        guard let head = lines.first else { return nil }

        let parts = head.split(separator: "|", omittingEmptySubsequences: false)
        guard parts.count == 4, let published = Double(parts[2]) else { return nil }

        var trees: [String: String] = [:]
        for line in lines.dropFirst() where !line.isEmpty {
            guard let tab = line.firstIndex(of: "\t") else { continue }
            trees[String(line[line.startIndex..<tab])] = String(line[line.index(after: tab)...])
        }

        return SDNSnapshot(
            kind: String(parts[0]),
            surface: String(parts[1]),
            publishedAt: published,
            refreshAfter: Double(parts[3]),
            trees: trees)
    }

    private func sanitize(_ kind: String) -> String {
        let safe = kind.map { $0.isLetter || $0.isNumber || $0 == "-" || $0 == "_" ? $0 : "_" }
        return safe.isEmpty ? "_" : String(safe)
    }

    private func escape(_ s: String) -> String {
        s.replacingOccurrences(of: "\\", with: "\\\\")
         .replacingOccurrences(of: "|", with: "\\p")
         .replacingOccurrences(of: "\n", with: "\\n")
    }
}
