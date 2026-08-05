import SwiftUI
import Observation
#if canImport(UIKit)
import UIKit
#elseif canImport(AppKit)
import AppKit
#endif
// WebKit is unavailable on tvOS; WebView falls back to a placeholder there.
#if canImport(WebKit)
import WebKit
#endif

// MARK: - Wire model (decoded from the C# patch protocol)

struct PropValue: Decodable, Equatable {
    let string: String?
    let number: Double?
    let bool: Bool?

    init(from decoder: Decoder) throws {
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
}

struct ModifierData: Decodable, Equatable {
    let type: String
    let value: String?
    let width: Double?
    let height: Double?
    let radius: Double?
    let amount: Double?
    let event: String?
    let top: Double?
    let leading: Double?
    let bottom: Double?
    let trailing: Double?
    let alignment: String?
    let cornerRadius: Double?
    let color: String?
    let x: Double?
    let y: Double?
    let curve: String?
    let duration: Double?
    let delay: Double?
    let trigger: String?
    let stiffness: Double?
    let damping: Double?
    let gradient: String?      // F5: Brush wire string on a `background` modifier
    let degrees: Double?       // F4: rotation
    let repeatCount: Double?   // F4: looping animation (-1 = forever)
    let autoreverse: String?   // F4: "true"/"false"
    let regions: String?       // safe area: "container" / "keyboard" / "all"
    let column: Double?        // gridCell: explicit column / row, and the spans
    let row: Double?
    let columnSpan: Double?
    let rowSpan: Double?
    let flags: String?         // layoutBounds: which of "xywh" are proportional
}

final class WireNode: Decodable {
    let id: String
    let type: String
    let props: [String: PropValue]
    let modifiers: [ModifierData]
    let children: [WireNode]
}

// MARK: - Patch protocol (mirror of SwiftDotNet.TreeDiffer)

struct Patch: Decodable { let ops: [PatchOp] }

struct PatchOp: Decodable {
    let op: String
    let id: String?
    let node: WireNode?
    let props: [String: PropValue]?
    let modifiers: [ModifierData]?
    let children: [WireNode]?
}

// MARK: - Observable view tree

@Observable
final class VNode: Identifiable {
    let id: String
    var type: String
    var props: [String: PropValue]
    var modifiers: [ModifierData]
    var children: [VNode]

    init(_ w: WireNode) {
        id = w.id
        type = w.type
        props = w.props
        modifiers = w.modifiers
        children = w.children.map(VNode.init)
    }

    /// The identity SwiftUI's ForEach diffs by: a keyed List row's stable `key` (so it recycles/animates
    /// across inserts and reorders), falling back to the structural id for everything else.
    var identity: String { props["key"]?.string ?? id }
}

@Observable
final class RenderStore {
    static let shared = RenderStore()
    var root: VNode?
    private init() {}

    func apply(_ patch: Patch) {
        for op in patch.ops {
            switch op.op {
            case "replace":
                if let node = op.node { root = VNode(node) }
            case "updateProps":
                if let id = op.id, let node = find(id) {
                    node.props = op.props ?? [:]
                    node.modifiers = op.modifiers ?? []
                }
            case "setChildren":
                if let id = op.id, let node = find(id) {
                    node.children = (op.children ?? []).map(VNode.init)
                }
            default:
                break
            }
        }
    }

    private func find(_ id: String) -> VNode? {
        guard let root else { return nil }
        let parts = id.split(separator: ".").map(String.init)
        guard parts.first == root.id else { return nil }
        var node = root
        for part in parts.dropFirst() {
            guard let idx = Int(part), idx >= 0, idx < node.children.count else { return nil }
            node = node.children[idx]
        }
        return node
    }
}

// MARK: - Token → SwiftUI mapping

private func fontFor(_ token: String?) -> SwiftUI.Font? {
    switch token {
    case "largeTitle": return .largeTitle
    case "title":      return .title
    case "headline":   return .headline
    case "body":       return .body
    case "caption":    return .caption
    default:           return nil
    }
}

extension SwiftUI.Color {
    init?(hexString: String) {
        var s = hexString.trimmingCharacters(in: .whitespaces)
        if s.hasPrefix("#") { s.removeFirst() }
        guard s.count == 6, let v = Int(s, radix: 16) else { return nil }
        self.init(.sRGB,
                  red: Double((v >> 16) & 0xff) / 255,
                  green: Double((v >> 8) & 0xff) / 255,
                  blue: Double(v & 0xff) / 255)
    }
}

private func colorFor(_ token: String?) -> SwiftUI.Color? {
    guard let token else { return nil }
    if token.hasPrefix("#") { return Color(hexString: token) }
    switch token {
    case "primary":     return .primary
    case "secondary":   return .secondary
    case "red":         return .red
    case "green":       return .green
    case "blue":        return .blue
    case "accentColor": return .accentColor
    default:            return nil
    }
}

// Cross-platform bitmap type so the raster Image path compiles on UIKit (iOS/tvOS) and AppKit (macOS).
#if canImport(UIKit)
typealias PlatformImage = UIKit.UIImage
extension SwiftUI.Image { init(platformImage: UIImage) { self.init(uiImage: platformImage) } }
#elseif canImport(AppKit)
typealias PlatformImage = AppKit.NSImage
extension SwiftUI.Image { init(platformImage: NSImage) { self.init(nsImage: platformImage) } }
#endif

// F5: parse a Brush wire string ("linear:<deg>:<c>@<loc>;…" / "radial:<c>@<loc>;…") into a SwiftUI gradient.
private func gradientFor(_ spec: String) -> AnyShapeStyle? {
    let firstColon = spec.firstIndex(of: ":")
    guard let firstColon else { return nil }
    let kind = String(spec[spec.startIndex..<firstColon])
    let rest = String(spec[spec.index(after: firstColon)...])

    func parseStops(_ s: String) -> [Gradient.Stop]? {
        let items = s.split(separator: ";")
        guard !items.isEmpty else { return nil }
        var stops: [Gradient.Stop] = []
        for item in items {
            guard let at = item.lastIndex(of: "@") else { return nil }
            let colorTok = String(item[item.startIndex..<at])
            let loc = Double(item[item.index(after: at)...]) ?? 0
            stops.append(.init(color: colorFor(colorTok) ?? .clear, location: loc))
        }
        return stops
    }

    if kind == "linear" {
        guard let secondColon = rest.firstIndex(of: ":") else { return nil }
        let angle = Double(rest[rest.startIndex..<secondColon]) ?? 90
        guard let stops = parseStops(String(rest[rest.index(after: secondColon)...])) else { return nil }
        // Map angle (0° = leading→trailing, 90° = top→bottom) onto SwiftUI's unit points.
        let rad = angle * .pi / 180
        let sx = 0.5 - cos(rad) * 0.5, sy = 0.5 - sin(rad) * 0.5
        let ex = 0.5 + cos(rad) * 0.5, ey = 0.5 + sin(rad) * 0.5
        return AnyShapeStyle(LinearGradient(gradient: Gradient(stops: stops),
                                            startPoint: UnitPoint(x: sx, y: sy),
                                            endPoint: UnitPoint(x: ex, y: ey)))
    }
    if kind == "radial" {
        guard let stops = parseStops(rest) else { return nil }
        return AnyShapeStyle(RadialGradient(gradient: Gradient(stops: stops),
                                            center: .center, startRadius: 0, endRadius: 200))
    }
    return nil
}

private func hexString(from color: SwiftUI.Color) -> String {
    #if canImport(UIKit)
    let ui = UIColor(color)
    var r: CGFloat = 0, g: CGFloat = 0, b: CGFloat = 0, a: CGFloat = 0
    ui.getRed(&r, green: &g, blue: &b, alpha: &a)
    #else
    let ns = NSColor(color).usingColorSpace(.sRGB) ?? .black
    let r = ns.redComponent, g = ns.greenComponent, b = ns.blueComponent
    #endif
    return String(format: "#%02X%02X%02X", Int(r * 255), Int(g * 255), Int(b * 255))
}

private func alignmentFor(_ token: String?) -> SwiftUI.Alignment {
    switch token {
    case "topLeading": return .topLeading
    case "top": return .top
    case "topTrailing": return .topTrailing
    case "leading": return .leading
    case "trailing": return .trailing
    case "bottomLeading": return .bottomLeading
    case "bottom": return .bottom
    case "bottomTrailing": return .bottomTrailing
    default: return .center
    }
}

private func unitPointFor(_ token: String?) -> UnitPoint {
    switch token {
    case "topLeading": return .topLeading
    case "top": return .top
    case "topTrailing": return .topTrailing
    case "leading": return .leading
    case "trailing": return .trailing
    case "bottomLeading": return .bottomLeading
    case "bottom": return .bottom
    case "bottomTrailing": return .bottomTrailing
    default: return .center
    }
}

private func hAlignFor(_ token: String?) -> SwiftUI.HorizontalAlignment {
    switch token { case "leading": return .leading; case "trailing": return .trailing; default: return .center }
}

private func vAlignFor(_ token: String?) -> SwiftUI.VerticalAlignment {
    switch token { case "top": return .top; case "bottom": return .bottom; default: return .center }
}

/// Safe area: the comma-joined edge tokens the C# `Edge` flags serialize to ("all", or e.g. "top,bottom").
private func edgeSetFor(_ token: String?) -> SwiftUI.Edge.Set {
    guard let token, token != "all" else { return .all }
    var set: SwiftUI.Edge.Set = []
    for part in token.split(separator: ",") {
        switch part {
        case "top": set.insert(.top)
        case "leading": set.insert(.leading)
        case "bottom": set.insert(.bottom)
        case "trailing": set.insert(.trailing)
        default: break
        }
    }
    return set
}

private func safeAreaRegionsFor(_ token: String?) -> SafeAreaRegions {
    switch token {
    case "keyboard": return .keyboard
    case "all": return .all
    default: return .container
    }
}

private func animationFor(_ m: ModifierData) -> SwiftUI.Animation {
    let dur = m.duration ?? 0.3
    let base: SwiftUI.Animation
    switch m.curve {
    case "linear": base = .linear(duration: dur)
    case "easeIn": base = .easeIn(duration: dur)
    case "easeOut": base = .easeOut(duration: dur)
    case "spring": base = .interpolatingSpring(stiffness: m.stiffness ?? 170, damping: m.damping ?? 26)
    default: base = .easeInOut(duration: dur)
    }
    let delay = m.delay ?? 0
    return delay > 0 ? base.delay(delay) : base
}

private func applyModifiers(_ view: some View, _ mods: [ModifierData]) -> AnyView {
    var out = AnyView(view)
    for m in mods {
        switch m.type {
        case "padding":
            out = AnyView(out.padding(EdgeInsets(
                top: CGFloat(m.top ?? 0), leading: CGFloat(m.leading ?? 0),
                bottom: CGFloat(m.bottom ?? 0), trailing: CGFloat(m.trailing ?? 0))))
        // Safe area is a device-window concept, so the C# API is annotated iOS/Android-only and the
        // handlers are compiled in only for iOS — macOS/tvOS fall through to `default: break`.
        #if os(iOS)
        case "safeAreaPadding":
            // SwiftUI's `.safeAreaPadding` takes edges only; the keyboard region is already handled by
            // SwiftUI's automatic keyboard avoidance, so `regions` is informational here.
            out = AnyView(out.safeAreaPadding(edgeSetFor(m.value)))
        case "ignoresSafeArea":
            out = AnyView(out.ignoresSafeArea(safeAreaRegionsFor(m.regions), edges: edgeSetFor(m.value)))
        #endif
        case "font":
            out = AnyView(out.font(fontFor(m.value)))
        case "foregroundColor":
            out = AnyView(out.foregroundColor(colorFor(m.value)))
        case "background":
            if let spec = m.gradient, let g = gradientFor(spec) {
                out = AnyView(out.background(g))
            } else {
                out = AnyView(out.background(colorFor(m.value) ?? .clear))
            }
        case "material":
            // F6: real SwiftUI frosted-glass material (adapts to light/dark automatically).
            let material: Material
            switch m.value {
            case "ultraThin": material = .ultraThinMaterial
            case "thin":      material = .thinMaterial
            case "thick":     material = .thickMaterial
            default:          material = .regularMaterial
            }
            out = AnyView(out.background(material))
        case "frame":
            out = AnyView(out.frame(width: m.width.map { CGFloat($0) },
                                    height: m.height.map { CGFloat($0) },
                                    alignment: alignmentFor(m.alignment)))
        case "align":
            out = AnyView(out.frame(maxWidth: .infinity, alignment: alignmentFor(m.value)))
        case "cornerRadius":
            out = AnyView(out.cornerRadius(CGFloat(m.radius ?? 0)))
        case "border":
            out = AnyView(out.overlay(
                RoundedRectangle(cornerRadius: CGFloat(m.cornerRadius ?? 0))
                    .stroke(colorFor(m.color) ?? .primary, lineWidth: CGFloat(m.width ?? 1))))
        case "shadow":
            out = AnyView(out.shadow(color: colorFor(m.color) ?? Color.black.opacity(0.33),
                                     radius: CGFloat(m.radius ?? 4),
                                     x: CGFloat(m.x ?? 0), y: CGFloat(m.y ?? 0)))
        case "opacity":
            out = AnyView(out.opacity(m.amount ?? 1))
        case "scaleEffect":
            out = AnyView(out.scaleEffect(x: CGFloat(m.x ?? 1), y: CGFloat(m.y ?? 1), anchor: unitPointFor(m.value)))
        case "offset":
            out = AnyView(out.offset(x: CGFloat(m.x ?? 0), y: CGFloat(m.y ?? 0)))
        case "rotation":
            out = AnyView(out.rotationEffect(.degrees(m.degrees ?? 0), anchor: unitPointFor(m.value)))
        case "animation":
            if let rc = m.repeatCount {
                // Self-playing repeat (shimmer/pulse): a repeatForever/repeatCount animation keyed to a
                // constant so it starts on appear rather than waiting for an external trigger.
                let autorev = m.autoreverse == "true"
                let base = animationFor(m)
                let anim = rc < 0 ? base.repeatForever(autoreverses: autorev)
                                  : base.repeatCount(Int(rc), autoreverses: autorev)
                out = AnyView(out.animation(anim, value: true))
            } else {
                // Re-arms whenever the trigger (stringified `on:` value) changes; SwiftUI interpolates the
                // animatable modifiers applied earlier in the chain to their new values.
                out = AnyView(out.animation(animationFor(m), value: m.trigger ?? ""))
            }
        case "disabled":
            out = AnyView(out.disabled(m.value == "true"))
        case "navigationTitle":
            out = AnyView(out.navigationTitle(m.value ?? ""))
        case "onTapGesture":
            let event = m.event
            let count = max(1, Int(m.amount ?? 1))
            out = AnyView(out.onTapGesture(count: count) { if let event { emitEvent(event) } })
        case "onLongPress":
            let event = m.event
            out = AnyView(out.onLongPressGesture(minimumDuration: m.amount ?? 0.5) {
                if let event { emitEvent(event) }
            })
        case "onSwipe":
            // Pointer drag/pinch gestures don't exist on tvOS (no touch surface) — skip there.
            #if !os(tvOS)
            let event = m.event
            let direction = m.value
            out = AnyView(out.gesture(DragGesture(minimumDistance: 20).onEnded { g in
                guard let event, let direction else { return }
                let dx = g.translation.width, dy = g.translation.height
                let matched: Bool
                if abs(dx) > abs(dy) {
                    matched = dx < 0 ? direction == "left" : direction == "right"
                } else {
                    matched = dy < 0 ? direction == "up" : direction == "down"
                }
                if matched { emitEvent(event) }
            }))
            #endif
        case "onDrag":
            // F1 continuous drag → "<phase>;tx,ty;lx,ly;vx,vy". SwiftUI's DragGesture has no began phase,
            // so the first onChanged is reported as begin via a per-gesture flag. (tvOS has no drag.)
            #if !os(tvOS)
            let event = m.event
            let minDist = m.amount ?? 0
            out = AnyView(out.modifier(DragEmitter(event: event, minimumDistance: minDist)))
            #endif
        case "onMagnify":
            #if !os(tvOS)
            let event = m.event
            out = AnyView(out.gesture(MagnificationGesture().onChanged { scale in
                if let event { emitEvent(event, String(format: "%f", scale)) }
            }.onEnded { scale in
                if let event { emitEvent(event, String(format: "%f", scale)) }
            }))
            #endif
        default:
            break
        }
    }
    return out
}

// F1: bridges SwiftUI's phaseless DragGesture to the began/changed/ended grammar the C# side parses.
// tvOS has no DragGesture, so the whole helper is compiled out there (the onDrag case is guarded too).
#if !os(tvOS)
private struct DragEmitter: ViewModifier {
    let event: String?
    let minimumDistance: Double
    @State private var active = false

    func body(content: Content) -> some View {
        content.gesture(DragGesture(minimumDistance: minimumDistance)
            .onChanged { g in
                guard let event else { return }
                let phase = active ? "c" : "b"
                active = true
                emitEvent(event, grammar(phase, g.translation, g.location, g.velocity))
            }
            .onEnded { g in
                guard let event else { return }
                emitEvent(event, grammar("e", g.translation, g.location, g.velocity))
                active = false
            })
    }

    private func grammar(_ phase: String, _ t: CGSize, _ l: CGPoint, _ v: CGSize) -> String {
        String(format: "%@;%f,%f;%f,%f;%f,%f", phase, t.width, t.height, l.x, l.y, v.width, v.height)
    }
}
#endif

// MARK: - Interpreter: VNode tree → real SwiftUI

struct NodeView: View {
    let node: VNode

    var body: some View {
        applyModifiers(baseView, node.modifiers)
    }

    @ViewBuilder
    private var baseView: some View {
        switch node.type {
        case "Text":
            SwiftUI.Text(str("text"))
        case "Button":
            SwiftUI.Button(str("title")) { emitEvent(node.id) }
        case "VStack":
            SwiftUI.VStack(alignment: hAlignFor(node.props["alignment"]?.string), spacing: spacing) { childViews }
        case "HStack":
            SwiftUI.HStack(alignment: vAlignFor(node.props["alignment"]?.string), spacing: spacing) { childViews }
        case "ZStack":
            SwiftUI.ZStack(alignment: alignmentFor(node.props["alignment"]?.string)) { childViews }
        case "Spacer":
            SwiftUI.Spacer()
        case "Divider":
            SwiftUI.Divider()
        case "ScrollView":
            scrollView
        case "Grid":
            gridView
        case "AbsoluteLayout":
            absoluteLayoutView
        case "List":
            listContainer
        case "Form":
            SwiftUI.Form { childViews }
        case "Group":
            SwiftUI.Group { childViews }
        case "Section":
            sectionView
        case "TabView":
            if node.props["selectedIndex"] != nil {
                SelectableTabView(node: node)
            } else {
                tabView
            }
        case "Tab":
            tabContent
        case "Menu":
            SwiftUI.Menu(str("label")) { childViews }
        case "TextField":
            TextFieldNode(node: node)
        case "SecureField":
            SecureFieldNode(node: node)
        case "TextEditor":
            TextEditorNode(node: node)
        case "Toggle":
            ToggleNode(node: node)
        case "Slider":
            SliderNode(node: node)
        case "Stepper":
            StepperNode(node: node)
        case "Picker":
            PickerNode(node: node)
        case "DatePicker":
            DatePickerNode(node: node)
        case "ColorPicker":
            ColorPickerNode(node: node)
        case "DisclosureGroup":
            DisclosureGroupNode(node: node)
        case "NavigationStack":
            NavigationStack { childAt(0) }
        case "NavigationLink":
            NavigationLink { childAt(1) } label: { childAt(0) }
        case "Sheet":
            SheetNode(node: node)
        case "Alert":
            AlertNode(node: node)
        case "WebView":
            WebViewNode(node: node)
        case "Image":
            rasterImage
        case "Label":
            SwiftUI.Label(str("title"), systemImage: str("systemImage"))
        case "ProgressView":
            progressView
        case "Gauge":
            gaugeView
        case "Link":
            linkView
        case "Rectangle":
            SwiftUI.Rectangle()
        case "Circle":
            SwiftUI.Circle()
        case "Capsule":
            SwiftUI.Capsule()
        case "RoundedRectangle":
            SwiftUI.RoundedRectangle(cornerRadius: num("cornerRadius") ?? 8)
        default:
            if let renderer = g_customRenderers[node.type] {
                renderer(SwiftDotNetProps(id: node.id, node: node))
            } else {
                SwiftUI.Text("⚠️ unknown view: \(node.type)").foregroundColor(.red)
            }
        }
    }

    // Helpers ------------------------------------------------------------

    private func str(_ key: String) -> String { node.props[key]?.string ?? "" }
    private func num(_ key: String) -> Double? { node.props[key]?.number }
    private var spacing: CGFloat? { node.props["spacing"]?.number.map { CGFloat($0) } }
    private func has(_ key: String) -> Bool { !(node.props[key]?.string ?? "").isEmpty }

    // F3 raster: a real image from url / file / bytes, or an SF Symbol fallback. contentMode fit=fit, fill=fill.
    @ViewBuilder
    private var rasterImage: some View {
        let mode: ContentMode = str("contentMode") == "fill" ? .fill : .fit
        if has("url"), let url = URL(string: str("url")) {
            AsyncImage(url: url) { img in img.resizable().aspectRatio(contentMode: mode) }
                placeholder: { ProgressView() }
        } else if has("bytes"), let data = Data(base64Encoded: str("bytes")), let ui = PlatformImage(data: data) {
            SwiftUI.Image(platformImage: ui).resizable().aspectRatio(contentMode: mode)
        } else if has("file"), let ui = PlatformImage(contentsOfFile: str("file")) {
            SwiftUI.Image(platformImage: ui).resizable().aspectRatio(contentMode: mode)
        } else {
            SwiftUI.Image(systemName: str("system"))
        }
    }

    @ViewBuilder
    private var childViews: some View {
        ForEach(node.children, id: \.identity) { NodeView(node: $0) }
    }

    // List honours .Columns(n) (grid) and .Horizontal() (axis); otherwise a native virtualizing List.
    @ViewBuilder
    private var listContainer: some View {
        if node.props["layout"]?.string == "grid" {
            let cols = Int(node.props["columns"]?.number ?? 2)
            SwiftUI.ScrollView {
                SwiftUI.LazyVGrid(columns: Array(repeating: GridItem(.flexible()), count: max(1, cols)), spacing: 8) { childViews }
                    .padding(.horizontal)
            }
        } else if node.props["axis"]?.string == "horizontal" {
            SwiftUI.ScrollView(.horizontal) {
                SwiftUI.LazyHStack(spacing: 8) { childViews }.padding(.horizontal)
            }
        } else if node.props["selectionMode"]?.string != nil {
            SwiftUI.List {
                ForEach(node.children, id: \.identity) { child in
                    NodeView(node: child)
                        .listRowBackground(child.props["selected"]?.bool == true ? Color.accentColor.opacity(0.15) : nil)
                        .contentShape(Rectangle())
                        .onTapGesture {
                            if let key = child.props["key"]?.string { emitEvent(node.id, key) }
                        }
                }
            }
        } else if node.props["refreshable"]?.bool == true {
            SwiftUI.List { childViews }
                .refreshable { emitEvent(node.id, "refresh") }
        } else {
            SwiftUI.List { childViews }
        }
    }

    @ViewBuilder
    private func childAt(_ i: Int) -> some View {
        if i < node.children.count { NodeView(node: node.children[i]) }
    }

    @ViewBuilder
    private var scrollView: some View {
        if node.props["axis"]?.string == "horizontal" {
            SwiftUI.ScrollView(.horizontal) { SwiftUI.HStack(spacing: spacing) { childViews } }
        } else {
            SwiftUI.ScrollView(.vertical) { SwiftUI.VStack(spacing: spacing) { childViews } }
        }
    }

    /// Grid lowers to SDNGridLayout, not LazyVGrid: the DSL's tracks, cell spans and explicit
    /// `.GridCell` pins have no LazyVGrid equivalent. Placement is resolved here (where the whole child
    /// list is in hand) and handed to each subview as a layout value.
    private var gridView: some View {
        let tracks = SDNTrack.parse(node.props["columnTracks"]?.string, fallback: Int(num("columns") ?? 2))
        let requests = node.children.map { child -> (column: Int?, row: Int?, columnSpan: Int, rowSpan: Int) in
            guard let m = child.modifiers.first(where: { $0.type == "gridCell" }) else {
                return (nil, nil, 1, 1)
            }
            return (m.column.map(Int.init), m.row.map(Int.init),
                    max(1, Int(m.columnSpan ?? 1)), max(1, Int(m.rowSpan ?? 1)))
        }
        let placed = sdnPlaceGrid(columns: tracks.count, requests: requests)
        let rowSpec = node.props["rowTracks"]?.string.map {
            SDNTrack.parse($0, fallback: placed.rowCount)
        }

        return SDNGridLayout(
            columnTracks: tracks,
            rowSpec: rowSpec,
            rowCount: placed.rowCount,
            columnSpacing: CGFloat(num("columnSpacing") ?? num("spacing") ?? 8),
            rowSpacing: CGFloat(num("rowSpacing") ?? num("spacing") ?? 8),
            alignment: alignmentFor(node.props["alignment"]?.string)
        ) {
            ForEach(Array(node.children.enumerated()), id: \.element.identity) { index, child in
                NodeView(node: child)
                    .layoutValue(key: SDNGridPlacementKey.self, value: placed.placements[index])
            }
        }
    }

    /// AbsoluteLayout lowers to SDNAbsoluteLayout; each child's declared rect rides along as a layout value.
    private var absoluteLayoutView: some View {
        SDNAbsoluteLayout {
            ForEach(node.children, id: \.identity) { child in
                NodeView(node: child)
                    .layoutValue(key: SDNAbsoluteBoundsKey.self, value: child.modifiers
                        .first { $0.type == "layoutBounds" }
                        .map { m in
                            SDNAbsoluteBounds(x: m.x ?? 0, y: m.y ?? 0,
                                              width: m.width, height: m.height,
                                              flags: m.flags ?? "")
                        })
            }
        }
    }

    @ViewBuilder
    private var sectionView: some View {
        if let header = node.props["header"]?.string {
            SwiftUI.Section { childViews } header: { SwiftUI.Text(header) }
        } else {
            SwiftUI.Section { childViews }
        }
    }

    @ViewBuilder
    private var tabView: some View {
        #if os(macOS) || os(tvOS)
        // PageTabViewStyle is unavailable on macOS/tvOS — fall back to a standard TabView.
        SwiftUI.TabView { childViews }
        #else
        if node.props["style"]?.string == "page" {
            SwiftUI.TabView { childViews }
                .tabViewStyle(.page(indexDisplayMode: .always))
                .indexViewStyle(.page(backgroundDisplayMode: .always))
        } else {
            SwiftUI.TabView { childViews }
        }
        #endif
    }

    @ViewBuilder
    private var tabContent: some View {
        childAt(0)
            .tabItem { SwiftUI.Label(str("title"), systemImage: str("systemImage")) }
    }

    @ViewBuilder
    private var progressView: some View {
        if let value = num("value") {
            if let label = node.props["label"]?.string {
                SwiftUI.ProgressView(value: value) { SwiftUI.Text(label) }
            } else {
                SwiftUI.ProgressView(value: value)
            }
        } else if let label = node.props["label"]?.string {
            SwiftUI.ProgressView(label)
        } else {
            SwiftUI.ProgressView()
        }
    }

    @ViewBuilder
    private var gaugeView: some View {
        // Gauge is unavailable on tvOS — fall back to a labeled ProgressView.
        #if os(tvOS)
        let lo = num("min") ?? 0, hi = num("max") ?? 1
        let frac = hi > lo ? ((num("value") ?? 0) - lo) / (hi - lo) : 0
        SwiftUI.VStack {
            SwiftUI.Text(node.props["label"]?.string ?? "")
            SwiftUI.ProgressView(value: max(0, min(1, frac)))
        }
        #else
        SwiftUI.Gauge(value: num("value") ?? 0, in: (num("min") ?? 0)...(num("max") ?? 1)) {
            SwiftUI.Text(node.props["label"]?.string ?? "")
        }
        #endif
    }

    @ViewBuilder
    private var linkView: some View {
        if let url = URL(string: str("url")) {
            SwiftUI.Link(str("title"), destination: url)
        } else {
            SwiftUI.Text(str("title"))
        }
    }
}

// MARK: - Controlled components (two-way binding bridged to C# state)

// F9: apply keyboard type (iOS) + return/submit label from the node props to a text field.
private struct TextInputTraits: ViewModifier {
    let node: VNode
    func body(content: Content) -> some View {
        var v = AnyView(content)
        #if os(iOS)
        if let kb = node.props["keyboard"]?.string {
            let type: UIKeyboardType
            switch kb {
            case "number":  type = .numberPad
            case "decimal": type = .decimalPad
            case "email":   type = .emailAddress
            case "phone":   type = .phonePad
            case "url":     type = .URL
            default:        type = .default
            }
            v = AnyView(v.keyboardType(type))
        }
        #endif
        if let rk = node.props["returnKey"]?.string {
            let label: SubmitLabel
            switch rk {
            case "done":   label = .done
            case "go":     label = .go
            case "next":   label = .next
            case "search": label = .search
            case "send":   label = .send
            default:       label = .return
            }
            v = AnyView(v.submitLabel(label))
        }
        return v
    }
}

struct TextFieldNode: View {
    let node: VNode
    @State private var text = ""
    var body: some View {
        TextField(node.props["placeholder"]?.string ?? "", text: $text)
            #if !os(tvOS)
            .textFieldStyle(.roundedBorder)
            #endif
            .modifier(TextInputTraits(node: node))
            .onAppear { text = node.props["text"]?.string ?? "" }
            .onChange(of: text) { _, v in emitEvent(node.id, v) }
            .onChange(of: node.props["text"]?.string ?? "") { _, incoming in
                if incoming != text { text = incoming }
            }
    }
}

struct SecureFieldNode: View {
    let node: VNode
    @State private var text = ""
    var body: some View {
        SecureField(node.props["placeholder"]?.string ?? "", text: $text)
            #if !os(tvOS)
            .textFieldStyle(.roundedBorder)
            #endif
            .onAppear { text = node.props["text"]?.string ?? "" }
            .onChange(of: text) { _, v in emitEvent(node.id, v) }
            .onChange(of: node.props["text"]?.string ?? "") { _, incoming in
                if incoming != text { text = incoming }
            }
    }
}

struct TextEditorNode: View {
    let node: VNode
    @State private var text = ""
    var body: some View {
        // TextEditor is unavailable on tvOS — fall back to a TextField.
        #if os(tvOS)
        TextField("", text: $text)
            .onAppear { text = node.props["text"]?.string ?? "" }
            .onChange(of: text) { _, v in emitEvent(node.id, v) }
            .onChange(of: node.props["text"]?.string ?? "") { _, incoming in
                if incoming != text { text = incoming }
            }
        #else
        TextEditor(text: $text)
            .frame(minHeight: 100)
            .overlay(RoundedRectangle(cornerRadius: 6).stroke(.secondary.opacity(0.3)))
            .onAppear { text = node.props["text"]?.string ?? "" }
            .onChange(of: text) { _, v in emitEvent(node.id, v) }
            .onChange(of: node.props["text"]?.string ?? "") { _, incoming in
                if incoming != text { text = incoming }
            }
        #endif
    }
}

struct ToggleNode: View {
    let node: VNode
    @State private var isOn = false
    var body: some View {
        Toggle(node.props["label"]?.string ?? "", isOn: $isOn)
            .onAppear { isOn = node.props["value"]?.bool ?? false }
            .onChange(of: isOn) { _, v in emitEvent(node.id, v ? "true" : "false") }
            .onChange(of: node.props["value"]?.bool ?? false) { _, incoming in
                if incoming != isOn { isOn = incoming }
            }
    }
}

/// A TabView with a two-way selected-index binding (SwiftDotNet `TabView.SelectedIndex`). Mirrors the
/// bound index into SwiftUI's own selection and reports user swipes/taps back to C#.
struct SelectableTabView: View {
    let node: VNode
    @State private var selection = 0

    private var boundIndex: Int { Int(node.props["selectedIndex"]?.number ?? 0) }

    var body: some View {
        tabViewBody
            .onAppear { selection = boundIndex }
            .onChange(of: selection) { _, v in emitEvent(node.id, String(v)) }
            .onChange(of: boundIndex) { _, incoming in
                if incoming != selection { selection = incoming }
            }
    }

    @ViewBuilder
    private var tabViewBody: some View {
        let content = SwiftUI.TabView(selection: $selection) {
            ForEach(Array(node.children.enumerated()), id: \.offset) { idx, child in
                NodeView(node: child).tag(idx)
            }
        }
        #if os(macOS) || os(tvOS)
        content   // PageTabViewStyle is unavailable on macOS/tvOS
        #else
        if node.props["style"]?.string == "page" {
            if node.props["pageIndicator"]?.bool == false {
                content.tabViewStyle(.page(indexDisplayMode: .never))
            } else {
                content.tabViewStyle(.page(indexDisplayMode: .always))
                    .indexViewStyle(.page(backgroundDisplayMode: .always))
            }
        } else {
            content
        }
        #endif
    }
}

struct SliderNode: View {
    let node: VNode
    @State private var value = 0.0
    var body: some View {
        // Slider is unavailable on tvOS (no pointer/touch) — show the value.
        #if os(tvOS)
        SwiftUI.Text(String(format: "%.2f", node.props["value"]?.number ?? 0))
        #else
        let lo = node.props["min"]?.number ?? 0
        let hi = node.props["max"]?.number ?? 1
        Slider(value: $value, in: lo...max(lo + 0.0001, hi))
            .onAppear { value = node.props["value"]?.number ?? lo }
            .onChange(of: value) { _, v in emitEvent(node.id, String(v)) }
            .onChange(of: node.props["value"]?.number ?? lo) { _, incoming in
                if abs(incoming - value) > 0.0001 { value = incoming }
            }
        #endif
    }
}

struct StepperNode: View {
    let node: VNode
    @State private var value = 0
    var body: some View {
        let lo = Int(node.props["min"]?.number ?? -1e9)
        let hi = Int(node.props["max"]?.number ?? 1e9)
        // Stepper is unavailable on tvOS — use focusable −/+ buttons (still fully functional).
        #if os(tvOS)
        let current = Int(node.props["value"]?.number ?? 0)
        HStack {
            SwiftUI.Text("\(node.props["label"]?.string ?? "") \(current)")
            SwiftUI.Button("−") { if current > lo { emitEvent(node.id, String(current - 1)) } }
            SwiftUI.Button("+") { if current < hi { emitEvent(node.id, String(current + 1)) } }
        }
        #else
        Stepper("\(node.props["label"]?.string ?? "") \(value)", value: $value, in: lo...max(lo, hi))
            .onAppear { value = Int(node.props["value"]?.number ?? 0) }
            .onChange(of: value) { _, v in emitEvent(node.id, String(v)) }
            .onChange(of: Int(node.props["value"]?.number ?? 0)) { _, incoming in
                if incoming != value { value = incoming }
            }
        #endif
    }
}

struct PickerNode: View {
    let node: VNode
    @State private var selection = 0
    var body: some View {
        Picker(node.props["label"]?.string ?? "", selection: $selection) {
            ForEach(Array(node.children.enumerated()), id: \.offset) { idx, child in
                SwiftUI.Text(child.props["text"]?.string ?? "").tag(idx)
            }
        }
        .onAppear { selection = Int(node.props["selection"]?.number ?? 0) }
        .onChange(of: selection) { _, v in emitEvent(node.id, String(v)) }
        .onChange(of: Int(node.props["selection"]?.number ?? 0)) { _, incoming in
            if incoming != selection { selection = incoming }
        }
    }
}

struct DatePickerNode: View {
    let node: VNode
    @State private var date = Date()
    var body: some View {
        // DatePicker is unavailable on tvOS — show the formatted date.
        #if os(tvOS)
        let d = Date(timeIntervalSince1970: node.props["value"]?.number ?? 0)
        SwiftUI.Text("\(node.props["label"]?.string ?? ""): \(d.formatted(date: .abbreviated, time: .omitted))")
        #else
        DatePicker(node.props["label"]?.string ?? "", selection: $date)
            .onAppear { date = Date(timeIntervalSince1970: node.props["value"]?.number ?? 0) }
            .onChange(of: date) { _, v in emitEvent(node.id, String(v.timeIntervalSince1970)) }
            .onChange(of: node.props["value"]?.number ?? 0) { _, incoming in
                let d = Date(timeIntervalSince1970: incoming)
                if abs(d.timeIntervalSince(date)) > 1 { date = d }
            }
        #endif
    }
}

struct ColorPickerNode: View {
    let node: VNode
    @State private var color = Color.accentColor
    var body: some View {
        // ColorPicker is unavailable on tvOS — show a swatch.
        #if os(tvOS)
        HStack {
            SwiftUI.Text(node.props["label"]?.string ?? "")
            RoundedRectangle(cornerRadius: 4)
                .fill(Color(hexString: node.props["value"]?.string ?? "") ?? .accentColor)
                .frame(width: 48, height: 28)
        }
        #else
        ColorPicker(node.props["label"]?.string ?? "", selection: $color)
            .onAppear { color = Color(hexString: node.props["value"]?.string ?? "") ?? .accentColor }
            .onChange(of: color) { _, v in emitEvent(node.id, hexString(from: v)) }
        #endif
    }
}

struct DisclosureGroupNode: View {
    let node: VNode
    @State private var expanded = false
    var body: some View {
        // DisclosureGroup is unavailable on tvOS — use a focusable header button + conditional content.
        #if os(tvOS)
        let isOpen = node.props["expanded"]?.bool ?? false
        VStack(alignment: .leading) {
            SwiftUI.Button((isOpen ? "▾ " : "▸ ") + (node.props["label"]?.string ?? "")) {
                emitEvent(node.id, isOpen ? "false" : "true")
            }
            if isOpen {
                ForEach(node.children) { NodeView(node: $0) }
            }
        }
        #else
        DisclosureGroup(node.props["label"]?.string ?? "", isExpanded: $expanded) {
            ForEach(node.children) { NodeView(node: $0) }
        }
        .onAppear { expanded = node.props["expanded"]?.bool ?? false }
        .onChange(of: expanded) { _, v in emitEvent(node.id, v ? "true" : "false") }
        .onChange(of: node.props["expanded"]?.bool ?? false) { _, incoming in
            if incoming != expanded { expanded = incoming }
        }
        #endif
    }
}

struct SheetNode: View {
    let node: VNode
    @State private var presented = false
    var body: some View {
        bodyView
            .sheet(isPresented: $presented) {
                if node.children.count > 1 { NodeView(node: node.children[1]) }
            }
            .onAppear { presented = node.props["presented"]?.bool ?? false }
            .onChange(of: presented) { _, v in emitEvent(node.id, v ? "true" : "false") }
            .onChange(of: node.props["presented"]?.bool ?? false) { _, incoming in
                if incoming != presented { presented = incoming }
            }
    }
    @ViewBuilder private var bodyView: some View {
        if !node.children.isEmpty { NodeView(node: node.children[0]) }
    }
}

struct AlertNode: View {
    let node: VNode
    @State private var presented = false
    var body: some View {
        bodyView
            .alert(node.props["title"]?.string ?? "", isPresented: $presented) {
                Button("OK", role: .cancel) { }
            } message: {
                SwiftUI.Text(node.props["message"]?.string ?? "")
            }
            .onAppear { presented = node.props["presented"]?.bool ?? false }
            .onChange(of: presented) { _, v in emitEvent(node.id, v ? "true" : "false") }
            .onChange(of: node.props["presented"]?.bool ?? false) { _, incoming in
                if incoming != presented { presented = incoming }
            }
    }
    @ViewBuilder private var bodyView: some View {
        if !node.children.isEmpty { NodeView(node: node.children[0]) }
    }
}

// MARK: - WebView (native web engine)

struct WebViewNode: View {
    let node: VNode
    var body: some View {
        #if canImport(WebKit)
        WebEngineView(node: node)
        #else
        // tvOS has no web engine.
        SwiftUI.VStack(spacing: 8) {
            SwiftUI.Image(systemName: "globe")
            SwiftUI.Text("Web content is unavailable on this platform.")
        }
        .foregroundColor(.secondary)
        #endif
    }
}

#if canImport(WebKit)
/// Tracks what a web view last loaded so an unchanged patch doesn't force a reload.
final class WebViewCoordinator {
    var loadedKey: String?
}

private func loadWeb(_ webView: WKWebView, _ node: VNode, _ coord: WebViewCoordinator) {
    let url = node.props["url"]?.string
    let html = node.props["html"]?.string
    let key = url ?? html ?? ""
    if coord.loadedKey == key { return }
    coord.loadedKey = key
    if let url, let u = URL(string: url) {
        webView.load(URLRequest(url: u))
    } else if let html {
        webView.loadHTMLString(html, baseURL: nil)
    }
}

#if canImport(UIKit)
struct WebEngineView: UIViewRepresentable {
    let node: VNode
    func makeCoordinator() -> WebViewCoordinator { WebViewCoordinator() }
    func makeUIView(context: Context) -> WKWebView { WKWebView() }
    func updateUIView(_ webView: WKWebView, context: Context) {
        loadWeb(webView, node, context.coordinator)
    }
}
#elseif canImport(AppKit)
struct WebEngineView: NSViewRepresentable {
    let node: VNode
    func makeCoordinator() -> WebViewCoordinator { WebViewCoordinator() }
    func makeNSView(context: Context) -> WKWebView { WKWebView() }
    func updateNSView(_ webView: WKWebView, context: Context) {
        loadWeb(webView, node, context.coordinator)
    }
}
#endif
#endif

struct RootHostView: View {
    var store = RenderStore.shared
    var body: some View {
        content
        #if os(iOS)
            // Layout-neutral: an empty overlay that only listens for notifications and reports the
            // *window's* insets to C#. Deliberately not a GeometryReader around the root — that would
            // re-align and re-inset every existing app's layout.
            .overlay(SafeAreaReporter().frame(width: 0, height: 0))
        #endif
    }

    @ViewBuilder
    private var content: some View {
        if let root = store.root {
            NodeView(node: root)
        } else {
            SwiftUI.Text("SwiftDotNet: waiting for first render…").foregroundColor(.secondary)
        }
    }
}

#if os(iOS)
/// Pushes the window's safe-area insets (plus the live keyboard height) to C# on the reserved
/// `$safeArea` event id. The insets come from the key window rather than a `GeometryReader`, so the
/// reporter contributes nothing to layout; the keyboard height comes from UIKit's keyboard-frame
/// notifications, which SwiftUI has no first-class equivalent for.
private struct SafeAreaReporter: View {
    @State private var keyboard: CGFloat = 0
    @State private var lastPayload: String = ""

    var body: some View {
        Color.clear
            .onAppear { report() }
            // Insets change on rotation, on scene resize (iPad multitasking), and when the status bar
            // does. `didChangeStatusBarFrame` catches the last of those; the other two land here too.
            .onReceive(NotificationCenter.default.publisher(for: UIDevice.orientationDidChangeNotification)) { _ in
                // The window's insets update after the rotation commits, so read on the next runloop turn.
                DispatchQueue.main.async { report() }
            }
            .onReceive(NotificationCenter.default.publisher(for: UIApplication.didBecomeActiveNotification)) { _ in report() }
            .onReceive(NotificationCenter.default.publisher(for: UIResponder.keyboardWillChangeFrameNotification)) { note in
                guard let frame = note.userInfo?[UIResponder.keyboardFrameEndUserInfoKey] as? CGRect,
                      let window = UIWindow.keyWindow else { keyboard = 0; report(); return }
                keyboard = max(0, window.bounds.height - frame.origin.y)
                report()
            }
            .onReceive(NotificationCenter.default.publisher(for: UIResponder.keyboardWillHideNotification)) { _ in
                keyboard = 0
                report()
            }
    }

    private func report() {
        let insets = UIWindow.keyWindow?.safeAreaInsets ?? .zero
        // UIKit reports physical left/right; the wire is leading/trailing. They coincide in LTR, which is
        // the mapping the rest of the bridge already assumes for alignment tokens.
        let payload = String(
            format: "%f;%f;%f;%f;%f",
            insets.top, insets.left, insets.bottom, insets.right, keyboard)
        guard payload != lastPayload else { return }
        lastPayload = payload
        emitEvent(SAFE_AREA_EVENT_ID, payload)
    }
}

private extension UIWindow {
    /// The active key window (`UIApplication.keyWindow` is deprecated and wrong in multi-scene setups).
    static var keyWindow: UIWindow? {
        UIApplication.shared.connectedScenes
            .compactMap { $0 as? UIWindowScene }
            .flatMap(\.windows)
            .first(where: \.isKeyWindow)
    }
}
#endif

/// Reserved event id for safe-area inset reports — mirrors `SwiftDotNet.SafeArea.EventId`. Node ids are
/// structural paths rooted at "0", so a `$` prefix can never collide with one.
let SAFE_AREA_EVENT_ID = "$safeArea"


// MARK: - Grid & AbsoluteLayout engines
//
// These mirror SwiftDotNet.GridEngine / SwiftDotNet.AbsoluteLayoutBounds on the C# side, because
// neither container maps onto anything SwiftUI ships. LazyVGrid can size columns but has no concept of
// a cell span or an explicit cell; SwiftUI's own Grid has gridCellColumns but no row span and no
// per-column sizing; and there is no absolute-positioning container at all. The `Layout` protocol
// (iOS 16 / macOS 13 / tvOS 16, all below this bridge's deployment targets) gives us the measure and
// place passes needed to implement both properly instead of approximating them.

/// One column/row definition, parsed from the wire's compact track token.
struct SDNTrack {
    enum Kind { case auto, fixed, star, flexible }
    var kind: Kind
    var value: Double        // points (fixed) · weight (star) · minimum (flexible)
    var maximum: Double?     // flexible only; nil is unbounded

    /// Parses `"fixed:80,star:1,auto,flex:40:inf"`. An absent or unparseable spec yields `fallback`
    /// equal star tracks, which is the plain `new Grid(n, …)` shape.
    static func parse(_ spec: String?, fallback: Int) -> [SDNTrack] {
        guard let spec, !spec.isEmpty else {
            return Array(repeating: SDNTrack(kind: .star, value: 1, maximum: nil), count: max(1, fallback))
        }
        return spec.split(separator: ",").map { token in
            let parts = token.split(separator: ":", omittingEmptySubsequences: false).map(String.init)
            func part(_ i: Int) -> Double? { parts.count > i ? Double(parts[i]) : nil }
            switch parts.first ?? "auto" {
            case "fixed": return SDNTrack(kind: .fixed, value: part(1) ?? 0, maximum: nil)
            case "star": return SDNTrack(kind: .star, value: part(1) ?? 1, maximum: nil)
            case "flex":
                let upper = parts.count > 2 ? parts[2] : "inf"
                return SDNTrack(kind: .flexible, value: part(1) ?? 0, maximum: upper == "inf" ? nil : Double(upper))
            default: return SDNTrack(kind: .auto, value: 0, maximum: nil)
            }
        }
    }
}

struct SDNGridPlacement: Equatable {
    var column = 0
    var row = 0
    var columnSpan = 1
    var rowSpan = 1
}

/// Mirror of `GridEngine.Place`: pinned children claim their cell first, then the rest flow into the
/// first free cell their whole span fits in.
func sdnPlaceGrid(columns: Int, requests: [(column: Int?, row: Int?, columnSpan: Int, rowSpan: Int)])
    -> (placements: [SDNGridPlacement], rowCount: Int) {
    let columns = max(1, columns)
    var result = [SDNGridPlacement](repeating: SDNGridPlacement(), count: requests.count)
    var pinned = [Bool](repeating: false, count: requests.count)
    var occupied: [[Bool]] = []

    func ensureRows(through row: Int) {
        while occupied.count <= row { occupied.append([Bool](repeating: false, count: columns)) }
    }
    func isFree(_ col: Int, _ row: Int, _ colSpan: Int, _ rowSpan: Int) -> Bool {
        for r in row..<(row + rowSpan) where r < occupied.count {
            for c in col..<min(col + colSpan, columns) where occupied[r][c] { return false }
        }
        return true
    }
    func occupy(_ col: Int, _ row: Int, _ colSpan: Int, _ rowSpan: Int) {
        ensureRows(through: row + rowSpan - 1)
        for r in row..<(row + rowSpan) {
            for c in col..<min(col + colSpan, columns) { occupied[r][c] = true }
        }
    }

    for (i, r) in requests.enumerated() {
        guard let c = r.column, let row = r.row else { continue }
        let col = min(max(c, 0), columns - 1)
        let cs = min(max(r.columnSpan, 1), columns - col)
        let rs = max(r.rowSpan, 1)
        occupy(col, max(row, 0), cs, rs)
        result[i] = SDNGridPlacement(column: col, row: max(row, 0), columnSpan: cs, rowSpan: rs)
        pinned[i] = true
    }

    var cursorRow = 0
    var cursorCol = 0
    for (i, r) in requests.enumerated() where !pinned[i] {
        let cs = min(max(r.columnSpan, 1), columns)
        let rs = max(r.rowSpan, 1)
        while true {
            if cursorCol + cs > columns { cursorCol = 0; cursorRow += 1; continue }
            if !isFree(cursorCol, cursorRow, cs, rs) { cursorCol += 1; continue }
            break
        }
        occupy(cursorCol, cursorRow, cs, rs)
        result[i] = SDNGridPlacement(column: cursorCol, row: cursorRow, columnSpan: cs, rowSpan: rs)
        cursorCol += cs
    }

    return (result, occupied.count)
}

/// Mirror of `SkiaNode.ResolveTracks`: Fixed takes its points, Auto/Flexible take the largest
/// single-track child, Star splits the remainder by weight, and a spanning child's shortfall grows the
/// last content-sized track it covers.
func sdnResolveTracks(_ tracks: [SDNTrack], available: CGFloat, gap: CGFloat,
                      spans: [(start: Int, span: Int)], sizes: [CGFloat]) -> [CGFloat] {
    let n = tracks.count
    var resolved = [CGFloat](repeating: 0, count: n)

    for (i, s) in spans.enumerated() where s.span == 1 && s.start < n && i < sizes.count {
        if tracks[s.start].kind == .auto || tracks[s.start].kind == .flexible {
            resolved[s.start] = max(resolved[s.start], sizes[i])
        }
    }

    var starWeight: CGFloat = 0
    for t in 0..<n {
        switch tracks[t].kind {
        case .fixed:
            resolved[t] = CGFloat(tracks[t].value)
        case .flexible:
            resolved[t] = max(resolved[t], CGFloat(tracks[t].value))
            if let m = tracks[t].maximum { resolved[t] = min(resolved[t], CGFloat(m)) }
        case .star:
            starWeight += CGFloat(tracks[t].value)
            resolved[t] = 0
        case .auto:
            break
        }
    }

    func extent(_ sizes: [CGFloat], _ start: Int, _ count: Int) -> CGFloat {
        var total: CGFloat = 0
        for i in start..<min(start + count, sizes.count) { total += sizes[i] }
        return total + gap * CGFloat(max(0, min(count, sizes.count - start) - 1))
    }

    for (i, s) in spans.enumerated() where s.span > 1 && s.start < n && i < sizes.count {
        // A span crossing a Star track needs no help — the star pass below already hands it the leftover.
        // Growing a content-sized track here instead would *steal* that leftover, which is what a greedy
        // spanning child (a shape, a raster image) would otherwise do to every star column.
        var hasStar = false
        for t in s.start..<min(s.start + s.span, n) where tracks[t].kind == .star { hasStar = true }
        guard !hasStar else { continue }

        let want = sizes[i]
        let have = extent(resolved, s.start, s.span)
        guard want > have else { continue }
        var target = -1
        for t in s.start..<min(s.start + s.span, n)
        where tracks[t].kind == .auto || tracks[t].kind == .flexible { target = t }
        guard target >= 0 else { continue }
        var grown = resolved[target] + (want - have)
        if let cap = tracks[target].maximum { grown = min(grown, CGFloat(cap)) }
        resolved[target] = grown
    }

    if starWeight > 0 {
        var used = gap * CGFloat(max(0, n - 1))
        for t in 0..<n where tracks[t].kind != .star { used += resolved[t] }
        let leftover = max(0, available - used)
        for t in 0..<n where tracks[t].kind == .star {
            resolved[t] = leftover * CGFloat(tracks[t].value) / starWeight
        }
    }

    return resolved
}

private struct SDNGridPlacementKey: LayoutValueKey {
    static let defaultValue = SDNGridPlacement()
}

private struct SDNAbsoluteBoundsKey: LayoutValueKey {
    static let defaultValue: SDNAbsoluteBounds? = nil
}

struct SDNAbsoluteBounds {
    var x: Double = 0
    var y: Double = 0
    var width: Double?
    var height: Double?
    var flags: String = ""

    var xProportional: Bool { flags.contains("x") }
    var yProportional: Bool { flags.contains("y") }
    var widthProportional: Bool { flags.contains("w") }
    var heightProportional: Bool { flags.contains("h") }
}

/// The `Grid` node's layout. Two measure passes: natural sizes size the content-driven columns, then
/// every child is re-proposed at its final cell width so wrapping text reports the height it will
/// actually draw at.
struct SDNGridLayout: Layout {
    var columnTracks: [SDNTrack]
    var rowSpec: [SDNTrack]?
    var rowCount: Int
    var columnSpacing: CGFloat
    var rowSpacing: CGFloat
    var alignment: Alignment

    private func rowTracks() -> [SDNTrack] {
        // Rows default to Auto — a grid should be as tall as it needs, not as tall as it is offered.
        (0..<max(1, rowCount)).map { i in
            if let spec = rowSpec, i < spec.count { return spec[i] }
            return SDNTrack(kind: .auto, value: 0, maximum: nil)
        }
    }

    private func extent(_ sizes: [CGFloat], _ start: Int, _ count: Int, _ gap: CGFloat) -> CGFloat {
        var total: CGFloat = 0
        for i in start..<min(start + count, sizes.count) { total += sizes[i] }
        return total + gap * CGFloat(max(0, min(count, sizes.count - start) - 1))
    }

    private func resolve(_ subviews: Subviews, proposal: ProposedViewSize)
        -> (columns: [CGFloat], rows: [CGFloat], cells: [CGSize], placements: [SDNGridPlacement]) {
        let placements = subviews.map { $0[SDNGridPlacementKey.self] }
        let colSpans = placements.map { (start: $0.column, span: $0.columnSpan) }
        let rowSpans = placements.map { (start: $0.row, span: $0.rowSpan) }

        let natural = subviews.map { $0.sizeThatFits(.unspecified) }
        let availableWidth = proposal.width ?? natural.reduce(0) { $0 + $1.width }
        let columns = sdnResolveTracks(columnTracks, available: availableWidth, gap: columnSpacing,
                                       spans: colSpans, sizes: natural.map(\.width))

        var cells: [CGSize] = []
        for (i, subview) in subviews.enumerated() {
            let w = extent(columns, placements[i].column, placements[i].columnSpan, columnSpacing)
            cells.append(subview.sizeThatFits(ProposedViewSize(width: w, height: nil)))
        }

        let rows = sdnResolveTracks(rowTracks(), available: proposal.height ?? .infinity, gap: rowSpacing,
                                    spans: rowSpans, sizes: cells.map(\.height))
        return (columns, rows, cells, placements)
    }

    func sizeThatFits(proposal: ProposedViewSize, subviews: Subviews, cache: inout ()) -> CGSize {
        let r = resolve(subviews, proposal: proposal)
        return CGSize(
            width: r.columns.reduce(0, +) + columnSpacing * CGFloat(max(0, r.columns.count - 1)),
            height: r.rows.reduce(0, +) + rowSpacing * CGFloat(max(0, r.rows.count - 1)))
    }

    func placeSubviews(in bounds: CGRect, proposal: ProposedViewSize, subviews: Subviews, cache: inout ()) {
        let r = resolve(subviews, proposal: ProposedViewSize(width: bounds.width, height: bounds.height))
        for (i, subview) in subviews.enumerated() {
            let p = r.placements[i]
            let cellX = bounds.minX + extent(r.columns, 0, p.column, columnSpacing)
                + (p.column > 0 ? columnSpacing : 0)
            let cellY = bounds.minY + extent(r.rows, 0, p.row, rowSpacing)
                + (p.row > 0 ? rowSpacing : 0)
            let cellW = extent(r.columns, p.column, p.columnSpan, columnSpacing)
            let cellH = extent(r.rows, p.row, p.rowSpan, rowSpacing)

            let w = min(r.cells[i].width, cellW)
            let h = min(r.cells[i].height, cellH)
            let x = cellX + (cellW - w) * unit(alignment.horizontal)
            let y = cellY + (cellH - h) * unit(alignment.vertical)
            subview.place(at: CGPoint(x: x, y: y), anchor: .topLeading,
                          proposal: ProposedViewSize(width: w, height: h))
        }
    }

    private func unit(_ a: HorizontalAlignment) -> CGFloat {
        a == .leading ? 0 : a == .trailing ? 1 : 0.5
    }

    private func unit(_ a: VerticalAlignment) -> CGFloat {
        a == .top ? 0 : a == .bottom ? 1 : 0.5
    }
}

/// The `AbsoluteLayout` node's layout: a canvas that claims what it is offered (fractions need
/// something to be a fraction of) and places each child at the rect its `.LayoutBounds` declared.
struct SDNAbsoluteLayout: Layout {
    func sizeThatFits(proposal: ProposedViewSize, subviews: Subviews, cache: inout ()) -> CGSize {
        // Unbounded on an axis: fall back to the far edge of the children placed in points, so an
        // AbsoluteLayout inside a scrolling stack isn't measured away to nothing.
        var extentW: CGFloat = 0
        var extentH: CGFloat = 0
        for subview in subviews {
            guard let b = subview[SDNAbsoluteBoundsKey.self] else { continue }
            let natural = subview.sizeThatFits(.unspecified)
            if !b.xProportional && !b.widthProportional {
                extentW = max(extentW, CGFloat(b.x) + CGFloat(b.width ?? Double(natural.width)))
            }
            if !b.yProportional && !b.heightProportional {
                extentH = max(extentH, CGFloat(b.y) + CGFloat(b.height ?? Double(natural.height)))
            }
        }
        return CGSize(width: proposal.width ?? extentW, height: proposal.height ?? extentH)
    }

    func placeSubviews(in bounds: CGRect, proposal: ProposedViewSize, subviews: Subviews, cache: inout ()) {
        for subview in subviews {
            let natural = subview.sizeThatFits(.unspecified)
            guard let b = subview[SDNAbsoluteBoundsKey.self] else {
                // No declared bounds: park it at the origin at its natural size, so a forgotten
                // .LayoutBounds shows up rather than vanishing.
                subview.place(at: CGPoint(x: bounds.minX, y: bounds.minY), anchor: .topLeading,
                              proposal: ProposedViewSize(natural))
                continue
            }

            let w: CGFloat = b.width.map { b.widthProportional ? bounds.width * CGFloat($0) : CGFloat($0) }
                ?? natural.width
            let h: CGFloat = b.height.map { b.heightProportional ? bounds.height * CGFloat($0) : CGFloat($0) }
                ?? natural.height
            // A proportional position is an anchor across the free space: 0 flush leading, 1 flush
            // trailing, 0.5 centred. Matches AbsoluteLayoutBounds.Resolve on the C# side.
            let x = b.xProportional ? (bounds.width - w) * CGFloat(b.x) : CGFloat(b.x)
            let y = b.yProportional ? (bounds.height - h) * CGFloat(b.y) : CGFloat(b.y)

            subview.place(at: CGPoint(x: bounds.minX + x, y: bounds.minY + y), anchor: .topLeading,
                          proposal: ProposedViewSize(width: w, height: h))
        }
    }
}

// MARK: - C ABI bridge (P/Invoke target from C#)

// MARK: - Custom renderer registry (for native extensions)

/// Props/emit surface handed to a custom renderer registered from native Swift.
public struct SwiftDotNetProps {
    public let id: String
    let node: VNode
    public func string(_ key: String) -> String? { node.props[key]?.string }
    public func number(_ key: String) -> Double? { node.props[key]?.number }
    public func bool(_ key: String) -> Bool? { node.props[key]?.bool }
    public func emit(_ value: String? = nil) { emitEvent(id, value) }
}

public typealias SwiftDotNetRenderer = (SwiftDotNetProps) -> AnyView
private var g_customRenderers: [String: SwiftDotNetRenderer] = [:]

/// Register a SwiftUI renderer for a custom `CustomView.TypeName`. Call from your app's Swift.
public func swiftDotNetRegisterRenderer(_ type: String, _ renderer: @escaping SwiftDotNetRenderer) {
    g_customRenderers[type] = renderer
}

public typealias EventCallback = @convention(c) (UnsafePointer<CChar>, UnsafePointer<CChar>?) -> Void
private var g_eventCallback: EventCallback?

func emitEvent(_ id: String, _ value: String? = nil) {
    id.withCString { idC in
        if let value {
            value.withCString { valC in g_eventCallback?(idC, valC) }
        } else {
            g_eventCallback?(idC, nil)
        }
    }
}

@_cdecl("swiftdotnet_set_event_callback")
public func swiftdotnet_set_event_callback(_ callback: @escaping EventCallback) {
    g_eventCallback = callback
}

@_cdecl("swiftdotnet_render")
public func swiftdotnet_render(_ json: UnsafePointer<CChar>) {
    let text = String(cString: json)
    guard let data = text.data(using: .utf8) else { return }
    do {
        let patch = try JSONDecoder().decode(Patch.self, from: data)
        if Thread.isMainThread {
            RenderStore.shared.apply(patch)
        } else {
            DispatchQueue.main.async { RenderStore.shared.apply(patch) }
        }
    } catch {
        NSLog("[SwiftDotNet] patch decode error: \(error)")
    }
}

@_cdecl("swiftdotnet_make_host_controller")
public func swiftdotnet_make_host_controller() -> UnsafeMutableRawPointer {
    #if canImport(UIKit)
    let controller = UIHostingController(rootView: RootHostView())
    #else
    let controller = NSHostingController(rootView: RootHostView())
    #endif
    return Unmanaged.passRetained(controller).toOpaque()
}
