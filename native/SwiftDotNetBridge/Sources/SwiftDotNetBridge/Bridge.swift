import SwiftUI
import Observation
#if canImport(UIKit)
import UIKit
#elseif canImport(AppKit)
import AppKit
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

private func hAlignFor(_ token: String?) -> SwiftUI.HorizontalAlignment {
    switch token { case "leading": return .leading; case "trailing": return .trailing; default: return .center }
}

private func vAlignFor(_ token: String?) -> SwiftUI.VerticalAlignment {
    switch token { case "top": return .top; case "bottom": return .bottom; default: return .center }
}

private func applyModifiers(_ view: some View, _ mods: [ModifierData]) -> AnyView {
    var out = AnyView(view)
    for m in mods {
        switch m.type {
        case "padding":
            out = AnyView(out.padding(EdgeInsets(
                top: CGFloat(m.top ?? 0), leading: CGFloat(m.leading ?? 0),
                bottom: CGFloat(m.bottom ?? 0), trailing: CGFloat(m.trailing ?? 0))))
        case "font":
            out = AnyView(out.font(fontFor(m.value)))
        case "foregroundColor":
            out = AnyView(out.foregroundColor(colorFor(m.value)))
        case "background":
            out = AnyView(out.background(colorFor(m.value) ?? .clear))
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
        case "navigationTitle":
            out = AnyView(out.navigationTitle(m.value ?? ""))
        case "onTapGesture":
            let event = m.event
            out = AnyView(out.onTapGesture { if let event { emitEvent(event) } })
        default:
            break
        }
    }
    return out
}

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
        case "List":
            SwiftUI.List { childViews }
        case "Form":
            SwiftUI.Form { childViews }
        case "Group":
            SwiftUI.Group { childViews }
        case "Section":
            sectionView
        case "TabView":
            tabView
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
        case "Image":
            SwiftUI.Image(systemName: str("system"))
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

    @ViewBuilder
    private var childViews: some View {
        ForEach(node.children) { NodeView(node: $0) }
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

    private var gridView: some View {
        let cols = Int(num("columns") ?? 2)
        let items = Array(repeating: GridItem(.flexible(), spacing: spacing), count: max(1, cols))
        return SwiftUI.LazyVGrid(columns: items, spacing: spacing) { childViews }
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

struct TextFieldNode: View {
    let node: VNode
    @State private var text = ""
    var body: some View {
        TextField(node.props["placeholder"]?.string ?? "", text: $text)
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

struct RootHostView: View {
    var store = RenderStore.shared
    var body: some View {
        if let root = store.root {
            NodeView(node: root)
        } else {
            SwiftUI.Text("SwiftDotNet: waiting for first render…").foregroundColor(.secondary)
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
