import SwiftUI

// MARK: - The widget-subset SwiftUI interpreter
//
// This is the Apple half of the live vocabulary: it reconstructs real SwiftUI from a decoded SDNNode
// tree, using ONLY what a WidgetKit-archived view hierarchy is allowed to contain.
//
// It is a separate interpreter from the main bridge rather than a mode of it, because the two render
// under opposite constraints. The main bridge builds an @Observable mirror that C# mutates with patches
// over a C ABI; here there is no C#, no patch, and no process of ours running at all — the system
// launches the extension, evaluates the body once, archives the result, and kills us. The vocabulary is
// also narrower: no ScrollView, List, TextField, Picker, Slider, TabView, WebView or Map, no gesture, and
// no on-demand animation. Everything below is inside that subset.

@available(iOS 16.0, macOS 13.0, watchOS 9.0, *)
public struct SDNLiveView: View {
    private let node: SDNNode?
    private let kind: String

    /// Renders a decoded tree.
    public init(_ node: SDNNode?, kind: String = "") {
        self.node = node
        self.kind = kind
    }

    /// Renders a serialized tree, parsing it here. Convenience for a provider that holds JSON.
    public init(json: String?, kind: String = "") {
        self.node = json.flatMap(SDNNode.parse)
        self.kind = kind
    }

    public var body: some View {
        if let node {
            SDNLiveView.render(node, kind: kind)
        } else {
            // A missing or unparseable tree must still occupy the slot. An empty view would make the
            // whole surface collapse, which reads as a bug in the app rather than in the payload.
            EmptyView()
        }
    }

    @ViewBuilder
    static func render(_ node: SDNNode, kind: String) -> some View {
        AnyView(modified(base(node, kind: kind), node))
    }

    // MARK: Node types

    @ViewBuilder
    private static func base(_ node: SDNNode, kind: String) -> some View {
        switch node.type {
        case "Text":
            Text(node.string("text"))

        case "Timer":
            // The single most valuable node on these surfaces: SwiftUI renders a timer interval that
            // ticks on its own, so a running countdown costs nothing against the activity update budget.
            timer(node)

        case "Date":
            date(node)

        case "Image":
            // Widget extensions have their own bundle and cannot see the app's asset catalogue, so a
            // live image name is always a system symbol. Arbitrary pixels travel as a Bitmap node.
            Image(systemName: node.string("name"))

        case "Bitmap":
            bitmap(node)

        case "VStack":
            VStack(alignment: hAlignment(node), spacing: spacing(node)) {
                children(node, kind: kind)
            }

        case "HStack":
            HStack(alignment: vAlignment(node), spacing: spacing(node)) {
                children(node, kind: kind)
            }

        case "ZStack":
            ZStack(alignment: zAlignment(node)) {
                children(node, kind: kind)
            }

        case "Spacer":
            Spacer()

        case "Divider":
            Divider()

        case "Progress":
            if node.flag("indeterminate") {
                ProgressView()
            } else {
                ProgressView(value: node.number("value"), total: 1)
            }

        case "Gauge":
            gauge(node)

        case "Shape":
            shape(node)

        case "Button":
            button(node, kind: kind)

        case "Link":
            link(node, kind: kind)

        default:
            EmptyView()
        }
    }

    @ViewBuilder
    private static func children(_ node: SDNNode, kind: String) -> some View {
        // Indexed rather than keyed: a live tree is rebuilt whole on every publish and never diffed, so
        // there is no identity to preserve and positional ids are both sufficient and stable.
        ForEach(Array(node.children.enumerated()), id: \.offset) { _, child in
            render(child, kind: kind)
        }
    }

    @ViewBuilder
    private static func timer(_ node: SDNNode) -> some View {
        let target = Date(timeIntervalSince1970: node.number("target"))
        let countsDown = node.flag("countsDown")
        let range = countsDown ? Date()...target : target...Date()

        if range.lowerBound < range.upperBound {
            Text(timerInterval: range, countsDown: countsDown)
        } else {
            // A degenerate range crashes Text(timerInterval:), and a target in the past is entirely
            // normal — a delivery that arrived, a timer that finished.
            Text(countsDown ? "0:00" : "")
        }
    }

    @ViewBuilder
    private static func date(_ node: SDNNode) -> some View {
        let when = Date(timeIntervalSince1970: node.number("date"))
        switch node.string("style") {
        case "date": Text(when, style: .date)
        case "relative": Text(when, style: .relative)
        case "offset": Text(when, style: .offset)
        default: Text(when, style: .time)
        }
    }

    @ViewBuilder
    private static func bitmap(_ node: SDNNode) -> some View {
        if let data = Data(base64Encoded: node.string("png")), let image = SDNPlatformImage(data: data) {
            SDNImageView(image: image).resizable().scaledToFit()
        } else {
            EmptyView()
        }
    }

    @ViewBuilder
    private static func gauge(_ node: SDNNode) -> some View {
        let value = node.number("value")
        let lower = node.number("min", 0)
        let upper = node.number("max", 1)

        // An inverted or empty range traps inside Gauge, and min == max is a perfectly ordinary state
        // for a gauge whose bounds are computed from data.
        if upper > lower {
            Gauge(value: min(max(value, lower), upper), in: lower...upper) { EmptyView() }
        } else {
            ProgressView(value: value, total: max(upper, 0.0001))
        }
    }

    @ViewBuilder
    private static func shape(_ node: SDNNode) -> some View {
        switch node.string("shape") {
        case "rounded": RoundedRectangle(cornerRadius: node.number("radius", 8))
        case "capsule": Capsule()
        case "circle": Circle()
        default: Rectangle()
        }
    }

    @ViewBuilder
    private static func button(_ node: SDNNode, kind: String) -> some View {
        #if canImport(AppIntents)
        if #available(iOS 17.2, macOS 14.2, *), let id = node.id {
            // LiveActivityIntent, NOT a plain AppIntent. A plain AppIntent's perform() runs inside this
            // extension, where there is no .NET and therefore no handler to reach; LiveActivityIntent
            // runs in the APP's process, which is the only reason an interactive button can work at all.
            Button(intent: SDNLiveActionIntent(kind: kind, nodeId: id)) {
                Text(node.string("title"))
            }
        } else {
            Text(node.string("title"))
        }
        #else
        Text(node.string("title"))
        #endif
    }

    @ViewBuilder
    private static func link(_ node: SDNNode, kind: String) -> some View {
        if let url = URL(string: node.string("url")), let child = node.children.first {
            Link(destination: url) { render(child, kind: kind) }
        } else if let child = node.children.first {
            render(child, kind: kind)
        } else {
            EmptyView()
        }
    }

    // MARK: Modifiers

    private static func modified(_ view: some View, _ node: SDNNode) -> some View {
        var result = AnyView(view)

        for mod in node.modifiers {
            guard let type = mod["t"]?.string else { continue }

            switch type {
            case "font":
                result = AnyView(result.font(font(mod["value"]?.text ?? "body")))

            case "bold":
                if mod["value"]?.flag == true { result = AnyView(result.fontWeight(.bold)) }

            case "foregroundColor":
                result = AnyView(result.foregroundStyle(color(mod["value"]?.text ?? "primary")))

            case "background":
                if let gradient = mod["gradient"]?.string {
                    result = AnyView(result.background(brush(gradient)))
                } else {
                    result = AnyView(result.background(color(mod["value"]?.text ?? "clear")))
                }

            case "padding":
                result = AnyView(result.padding(EdgeInsets(
                    top: mod["top"]?.double ?? 0,
                    leading: mod["leading"]?.double ?? 0,
                    bottom: mod["bottom"]?.double ?? 0,
                    trailing: mod["trailing"]?.double ?? 0)))

            case "frame":
                result = AnyView(result.frame(
                    width: mod["width"]?.number.map { CGFloat($0) },
                    height: mod["height"]?.number.map { CGFloat($0) }))

            case "cornerRadius":
                result = AnyView(result.clipShape(
                    RoundedRectangle(cornerRadius: mod["value"]?.double ?? 0)))

            case "opacity":
                result = AnyView(result.opacity(mod["value"]?.double ?? 1))

            case "tint":
                result = AnyView(result.tint(color(mod["value"]?.text ?? "accentColor")))

            case "lineLimit":
                result = AnyView(result.lineLimit(Int(mod["value"]?.double ?? 1)))

            case "a11yLabel":
                result = AnyView(result.accessibilityLabel(Text(mod["value"]?.text ?? "")))

            case "tapUrl":
                if let url = URL(string: mod["value"]?.text ?? "") {
                    result = AnyView(result.widgetURLIfAvailable(url))
                }

            default:
                break
            }
        }

        return result
    }

    // MARK: Token resolution
    //
    // The same semantic tokens the core DSL uses, resolved here against SwiftUI's own palette rather than
    // an app Theme — a lock screen has no app environment to cascade from.

    static func font(_ token: String) -> Font {
        switch token {
        case "largeTitle": return .largeTitle
        case "title": return .title
        case "headline": return .headline
        case "caption": return .caption
        default: return .body
        }
    }

    static func color(_ token: String) -> Color {
        if token.hasPrefix("#") { return hex(token) }
        switch token {
        case "primary": return .primary
        case "secondary": return .secondary
        case "red": return .red
        case "green": return .green
        case "blue": return .blue
        case "accentColor": return .accentColor
        case "clear": return .clear
        default: return .primary
        }
    }

    static func hex(_ token: String) -> Color {
        var hex = token.dropFirst()
        if hex.count == 3 { hex = Substring(hex.map { "\($0)\($0)" }.joined()) }
        guard let value = UInt32(hex, radix: 16) else { return .primary }

        if hex.count == 8 {
            return Color(.sRGB,
                         red: Double((value >> 16) & 0xFF) / 255,
                         green: Double((value >> 8) & 0xFF) / 255,
                         blue: Double(value & 0xFF) / 255,
                         opacity: Double((value >> 24) & 0xFF) / 255)
        }
        return Color(.sRGB,
                     red: Double((value >> 16) & 0xFF) / 255,
                     green: Double((value >> 8) & 0xFF) / 255,
                     blue: Double(value & 0xFF) / 255,
                     opacity: 1)
    }

    /// Parses the core Brush wire grammar, shared verbatim with every other backend:
    ///   linear:<angleDeg>:<color>@<loc>;…   |   radial:<color>@<loc>;…
    static func brush(_ spec: String) -> AnyShapeStyle {
        let parts = spec.split(separator: ":", omittingEmptySubsequences: false).map(String.init)
        guard let kind = parts.first else { return AnyShapeStyle(Color.clear) }

        let stopSpec = parts.last ?? ""
        var stops: [Gradient.Stop] = []
        for piece in stopSpec.split(separator: ";") {
            let halves = piece.split(separator: "@", maxSplits: 1)
            let c = color(String(halves[0]))
            let location = halves.count > 1 ? Double(halves[1]) ?? 0 : 0
            stops.append(.init(color: c, location: location))
        }
        if stops.isEmpty { return AnyShapeStyle(Color.clear) }

        if kind == "radial" {
            return AnyShapeStyle(RadialGradient(
                gradient: Gradient(stops: stops), center: .center, startRadius: 0, endRadius: 100))
        }

        // 0 degrees is left-to-right, 90 is top-to-bottom, matching the core Brush contract.
        let degrees = parts.count > 1 ? Double(parts[1]) ?? 90 : 90
        let radians = degrees * .pi / 180
        let dx = cos(radians) / 2
        let dy = sin(radians) / 2
        return AnyShapeStyle(LinearGradient(
            gradient: Gradient(stops: stops),
            startPoint: UnitPoint(x: 0.5 - dx, y: 0.5 - dy),
            endPoint: UnitPoint(x: 0.5 + dx, y: 0.5 + dy)))
    }

    // MARK: Alignment

    private static func spacing(_ node: SDNNode) -> CGFloat? {
        node.prop("spacing").map { CGFloat($0.double) }
    }

    private static func hAlignment(_ node: SDNNode) -> HorizontalAlignment {
        switch node.string("alignment") {
        case "leading": return .leading
        case "trailing": return .trailing
        default: return .center
        }
    }

    private static func vAlignment(_ node: SDNNode) -> VerticalAlignment {
        switch node.string("alignment") {
        case "top": return .top
        case "bottom": return .bottom
        default: return .center
        }
    }

    private static func zAlignment(_ node: SDNNode) -> Alignment {
        switch node.string("alignment") {
        case "leading": return .leading
        case "trailing": return .trailing
        case "top": return .top
        case "bottom": return .bottom
        default: return .center
        }
    }
}

// MARK: - Platform image shims
//
// UIImage on iOS, NSImage on macOS. Aliased rather than #if'd at each use site so the interpreter above
// reads the same on both.

#if canImport(UIKit)
import UIKit
public typealias SDNPlatformImage = UIImage
@available(iOS 16.0, *)
func SDNImageView(image: SDNPlatformImage) -> Image { Image(uiImage: image) }
#elseif canImport(AppKit)
import AppKit
public typealias SDNPlatformImage = NSImage
@available(macOS 13.0, *)
func SDNImageView(image: SDNPlatformImage) -> Image { Image(nsImage: image) }
#endif

extension View {
    /// `widgetURL` exists only inside WidgetKit, so a tree rendered anywhere else keeps the modifier as
    /// a no-op instead of failing to compile.
    @ViewBuilder
    func widgetURLIfAvailable(_ url: URL) -> some View {
        #if canImport(WidgetKit) && !os(watchOS)
        if #available(iOS 16.0, macOS 13.0, *) {
            self.widgetURL(url)
        } else {
            self
        }
        #else
        self
        #endif
    }
}
