// SwiftDotNet.Maps — Android (MapLibre) renderer.
//
// The native half of the `Map` control on Android. Custom renderers on Android are Kotlin, registered via
// `registerRenderer`, so this is NOT part of the C# NuGet package. Ship it by adding this file to the
// SwiftDotNetComposeBridge sources (rebuilding the AAR) or a companion module the app links, and calling
// `registerSwiftDotNetMapRenderer()` at startup.
//
// It reads the JSON-string props the C# `Map` writes ("camera", "pins", "polylines") and renders a real
// MapLibre map (key-free OSM demo tiles) hosted in an AndroidView, with markers, a polyline, and tap/
// camera events forwarded back to C#.
//
// Requires the MapLibre Android SDK + annotations plugin:
//   implementation("org.maplibre.gl:android-sdk:11.+")
//   implementation("org.maplibre.gl:android-plugin-annotation-v9:3.+")
//
// STATUS: authored for review — not compiled in this environment (needs the Android SDK + AAR rebuild).

package com.swiftdotnet.bridge

import android.view.Gravity
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.runtime.Composable
import androidx.compose.runtime.DisposableEffect
import androidx.compose.runtime.remember
import androidx.compose.ui.Modifier
import androidx.compose.ui.viewinterop.AndroidView
import org.json.JSONArray
import org.json.JSONObject
import org.maplibre.android.MapLibre
import org.maplibre.android.camera.CameraPosition
import org.maplibre.android.geometry.LatLng
import org.maplibre.android.maps.MapView
import org.maplibre.android.maps.MapLibreMap
import org.maplibre.android.plugins.annotation.LineManager
import org.maplibre.android.plugins.annotation.LineOptions
import org.maplibre.android.plugins.annotation.SymbolManager
import org.maplibre.android.plugins.annotation.SymbolOptions

private const val STYLE = "https://demotiles.maplibre.org/style.json" // key-free OSM demo tiles

@Composable
fun SwiftDotNetMap(props: SwiftDotNetProps) {
    val camera = parseObj(props.string("camera"))
    val lat = camera?.optDouble("lat", 0.0) ?: 0.0
    val lng = camera?.optDouble("lng", 0.0) ?: 0.0
    val zoom = camera?.optDouble("zoom", 1.0) ?: 1.0

    // Keep the MapView across recompositions so tiles/state survive; reconcile overlays on each pass.
    val holder = remember { MapHolder() }

    AndroidView(
        modifier = Modifier.fillMaxSize(),
        factory = { ctx ->
            MapLibre.getInstance(ctx)
            MapView(ctx).also { view ->
                holder.view = view
                view.getMapAsync { map ->
                    holder.map = map
                    map.cameraPosition = CameraPosition.Builder().target(LatLng(lat, lng)).zoom(zoom).build()
                    map.setStyle(STYLE) { style ->
                        holder.symbols = SymbolManager(view, map, style)
                        holder.lines = LineManager(view, map, style)
                        applyOverlays(holder, props)
                    }
                    map.addOnMapClickListener { p ->
                        props.emit("tap:${p.latitude},${p.longitude}"); true
                    }
                    map.addOnCameraIdleListener {
                        val c = map.cameraPosition
                        props.emit("camera:${c.target?.latitude},${c.target?.longitude},${c.zoom}")
                    }
                }
            }
        },
        update = { applyOverlays(holder, props) },
    )

    DisposableEffect(Unit) {
        onDispose {
            holder.symbols?.onDestroy(); holder.lines?.onDestroy()
            holder.view?.onDestroy()
        }
    }
}

private class MapHolder {
    var view: MapView? = null
    var map: MapLibreMap? = null
    var symbols: SymbolManager? = null
    var lines: LineManager? = null
}

private fun applyOverlays(holder: MapHolder, props: SwiftDotNetProps) {
    val symbols = holder.symbols ?: return
    val lines = holder.lines ?: return

    // Pins: clear + re-add (small counts in Phase 1; keyed diffing is a later milestone).
    symbols.deleteAll()
    for (pin in parseArr(props.string("pins"))) {
        symbols.create(
            SymbolOptions()
                .withLatLng(LatLng(pin.optDouble("lat"), pin.optDouble("lng")))
                .withTextField(pin.optString("title", ""))
                .withIconImage("marker-15")
        )
    }

    lines.deleteAll()
    for (line in parseArr(props.string("polylines"))) {
        val pts = parseArr(line.optString("points"))
            .map { LatLng(it.optDouble("lat"), it.optDouble("lng")) }
        if (pts.size >= 2) {
            lines.create(LineOptions().withLatLngs(pts).withLineColor(line.optString("color", "#3b82f6"))
                .withLineWidth((line.optDouble("width", 3.0)).toFloat()))
        }
    }
}

private fun parseObj(json: String?): JSONObject? = try { if (json.isNullOrEmpty()) null else JSONObject(json) } catch (_: Exception) { null }
private fun parseArr(json: String?): List<JSONObject> = try {
    if (json.isNullOrEmpty()) emptyList() else JSONArray(json).let { a -> (0 until a.length()).map { a.getJSONObject(it) } }
} catch (_: Exception) { emptyList() }

/** Call once at app startup (after the bridge is initialized) to render `Map` nodes via MapLibre. */
fun registerSwiftDotNetMapRenderer() {
    registerRenderer("Map") { props -> SwiftDotNetMap(props) }
}
