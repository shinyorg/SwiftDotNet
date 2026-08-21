import Foundation

// MARK: - Compact wire model
//
// This is a SUBSET decoder for the compact live wire emitted by SwiftDotNet.LiveWire, not a general
// JSON reader. The main bridge's WireNode cannot be reused: it decodes the verbose core wire
// ({id,type,props,modifiers,children}) that crosses a C ABI in-process, where a few hundred wasted bytes
// per render cost nothing. A Live Activity's whole state rides inside a 4 KB APNs payload instead, so the
// live writer uses single-letter keys, drops the "L" type prefix, and omits empty collections.
//
// Keeping the two decoders separate rather than unifying them is deliberate. A shared decoder would have
// to accept both shapes, which means neither is checkable, and the extension pays for a vocabulary it
// cannot render anyway.

/// A prop or modifier value: string, number or bool. Mirrors the writer's three value kinds exactly.
public struct SDNValue: Decodable, Equatable, Sendable {
    public let string: String?
    public let number: Double?
    public let bool: Bool?

    public init(from decoder: Decoder) throws {
        let c = try decoder.singleValueContainer()
        if let s = try? c.decode(String.self) {
            string = s; number = nil; bool = nil
        } else if let b = try? c.decode(Bool.self) {
            bool = b; string = nil; number = nil
        } else if let n = try? c.decode(Double.self) {
            number = n; string = nil; bool = nil
        } else {
            string = nil; number = nil; bool = nil
        }
    }

    public var text: String { string ?? "" }
    public var double: Double { number ?? 0 }
    public var flag: Bool { bool ?? false }
}

/// One node of a live tree.
public final class SDNNode: Decodable, Sendable {
    /// Node type with the writer's dropped `L` prefix restored is unnecessary here — the interpreter
    /// switches on the short form the wire actually carries ("Text", "VStack", …).
    public let type: String

    /// Present only on nodes the interpreter has to address: buttons and links.
    public let id: String?

    public let props: [String: SDNValue]
    public let modifiers: [[String: SDNValue]]
    public let children: [SDNNode]

    private enum CodingKeys: String, CodingKey {
        case type = "t"
        case id = "i"
        case props = "p"
        case modifiers = "m"
        case children = "c"
    }

    public init(from decoder: Decoder) throws {
        let c = try decoder.container(keyedBy: CodingKeys.self)
        type = try c.decode(String.self, forKey: .type)
        id = try c.decodeIfPresent(String.self, forKey: .id)
        // Absent means empty, not malformed: the writer omits empty collections to save bytes, which is
        // most of where the compaction comes from.
        props = try c.decodeIfPresent([String: SDNValue].self, forKey: .props) ?? [:]
        modifiers = try c.decodeIfPresent([[String: SDNValue]].self, forKey: .modifiers) ?? []
        children = try c.decodeIfPresent([SDNNode].self, forKey: .children) ?? []
    }

    /// Parses one serialized tree. Returns nil rather than throwing: a widget extension that fails to
    /// decode must render a placeholder, never crash — the system relaunches it and it fails again.
    public static func parse(_ json: String) -> SDNNode? {
        guard let data = json.data(using: .utf8) else { return nil }
        return try? JSONDecoder().decode(SDNNode.self, from: data)
    }

    public func prop(_ key: String) -> SDNValue? { props[key] }

    public func string(_ key: String) -> String { props[key]?.text ?? "" }

    public func number(_ key: String, _ fallback: Double = 0) -> Double { props[key]?.number ?? fallback }

    public func flag(_ key: String) -> Bool { props[key]?.bool ?? false }

    /// First modifier of a type, if any. Modifiers are a short ordered list, so a scan beats a dictionary.
    public func modifier(_ type: String) -> [String: SDNValue]? {
        modifiers.first { $0["t"]?.string == type }
    }
}
