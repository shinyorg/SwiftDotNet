// SwiftDotNet.Maps — Apple (MapKit) renderer.
//
// This is the native half of the `Map` control on iOS/macOS/tvOS. It is NOT part of the C# NuGet package
// (custom renderers on Apple are Swift, registered via `swiftDotNetRegisterRenderer`). Ship it by adding
// this file to the app's Swift bridge sources (or a companion Swift package linked into the app) and
// calling `registerSwiftDotNetMapRenderer()` from your app startup, alongside the SwiftDotNetBridge.
//
// It reads the JSON-string props the C# `Map` writes ("camera", "pins", "polylines") — the Phase-1
// "JSON-string prop" wire shortcut — and renders a real MapKit map with markers and a polyline overlay.
// Requires iOS 17 / macOS 14 for the SwiftUI MapContentBuilder API.
//
// STATUS: authored for review — not compiled in this environment (needs an Apple SDK + the bridge build).

import SwiftUI
import MapKit
import SwiftDotNetBridge   // companion module: pulls in swiftDotNetRegisterRenderer + SwiftDotNetProps

// MARK: - Wire model (mirror of SwiftDotNet.Maps MapJson)

private struct WireCoord: Decodable { let lat: Double; let lng: Double }
private struct WireCamera: Decodable { let lat: Double; let lng: Double; let zoom: Double }
private struct WirePin: Decodable { let lat: Double; let lng: Double; let title: String?; let tint: String?; let id: String }
private struct WirePolyline: Decodable { let color: String?; let width: Double?; let points: [WireCoord] }

private func decode<T: Decodable>(_ type: T.Type, _ json: String?) -> T? {
    guard let data = json?.data(using: .utf8) else { return nil }
    return try? JSONDecoder().decode(T.self, from: data)
}

private func colorFor(_ token: String?) -> Color {
    switch token {
    case "red": return .red
    case "green": return .green
    case "blue": return .blue
    case "accentColor": return .accentColor
    case "secondary": return .secondary
    case "primary": return .primary
    case let hex? where hex.hasPrefix("#"): return Color(hex: hex) ?? .red
    default: return .red
    }
}

// MARK: - The map view

@available(iOS 17.0, macOS 14.0, *)
struct SwiftDotNetMapView: View {
    let props: SwiftDotNetProps

    @State private var position: MapCameraPosition = .automatic

    private var pins: [WirePin] { decode([WirePin].self, props.string("pins")) ?? [] }
    private var polylines: [WirePolyline] { decode([WirePolyline].self, props.string("polylines")) ?? [] }

    var body: some View {
        MapReader { proxy in
            Map(position: $position) {
                ForEach(pins, id: \.id) { pin in
                    Marker(pin.title ?? "", coordinate: CLLocationCoordinate2D(latitude: pin.lat, longitude: pin.lng))
                        .tint(colorFor(pin.tint))
                }
                ForEach(Array(polylines.enumerated()), id: \.offset) { _, line in
                    MapPolyline(coordinates: line.points.map { CLLocationCoordinate2D(latitude: $0.lat, longitude: $0.lng) })
                        .stroke(colorFor(line.color), lineWidth: line.width ?? 3)
                }
            }
            .gesture(SpatialTapGesture().onEnded { event in
                // SpatialTapGesture reports the tap location (plain .onTapGesture does not); convert it to a
                // coordinate via the MapReader proxy and forward to C# (Map.OnTap).
                if let coord = proxy.convert(event.location, from: .local) {
                    props.emit("tap:\(coord.latitude),\(coord.longitude)")
                }
            })
            .onMapCameraChange(frequency: .onEnd) { context in
                let c = context.camera.centerCoordinate
                // MKMapCamera has no zoom; approximate from the region span so C# gets a usable value.
                let zoom = zoomFromSpan(context.region.span)
                props.emit("camera:\(c.latitude),\(c.longitude),\(zoom)")
            }
            .onAppear {
                if let cam = decode(WireCamera.self, props.string("camera")) {
                    position = .region(MKCoordinateRegion(
                        center: CLLocationCoordinate2D(latitude: cam.lat, longitude: cam.lng),
                        span: spanFromZoom(cam.zoom)))
                }
            }
        }
    }
}

private func spanFromZoom(_ zoom: Double) -> MKCoordinateSpan {
    // 360° across the world at zoom 0, halving each level — the standard web-mercator zoom convention.
    let degrees = 360.0 / pow(2.0, zoom)
    return MKCoordinateSpan(latitudeDelta: degrees, longitudeDelta: degrees)
}

private func zoomFromSpan(_ span: MKCoordinateSpan) -> Double {
    max(0, log2(360.0 / max(span.longitudeDelta, 0.0001)))
}

private extension Color {
    init?(hex: String) {
        var s = hex; s.removeFirst()
        guard let v = Int(s, radix: 16), s.count == 6 else { return nil }
        self = Color(red: Double((v >> 16) & 0xFF) / 255, green: Double((v >> 8) & 0xFF) / 255, blue: Double(v & 0xFF) / 255)
    }
}

// MARK: - Registration

/// Call once at app startup (after loading the SwiftDotNetBridge) to render `Map` nodes via MapKit.
public func registerSwiftDotNetMapRenderer() {
    if #available(iOS 17.0, macOS 14.0, *) {
        swiftDotNetRegisterRenderer("Map") { props in AnyView(SwiftDotNetMapView(props: props)) }
    }
}

/// C entry point so managed code can register the MapKit renderer at startup (resolved via dlsym /
/// "__Internal", like the bridge's own exports).
@_cdecl("swiftdotnet_register_maps")
public func swiftdotnet_register_maps() {
    registerSwiftDotNetMapRenderer()
}
