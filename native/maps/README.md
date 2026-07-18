# SwiftDotNet.Maps — native renderers (Apple / Android)

The `Map` control's C# half lives in **`src/SwiftDotNet.Maps`** (view + data types + JSON-string prop
serialization). Its **renderers** are per-platform:

| Platform | Renderer | Where it lives | Buildable on this repo's Mac? |
|----------|----------|----------------|-------------------------------|
| **Web** | `SwiftDotNet.Maps.Web` (MapLibre GL JS) | C# NuGet — `src/SwiftDotNet.Maps.Web` | ✅ yes (built + verified) |
| **Apple** (iOS/macOS/tvOS) | `MapRenderer.swift` (MapKit) | this folder — Swift source | ❌ needs an Apple SDK + bridge build |
| **Android** | `MapRenderer.kt` (MapLibre) | this folder — Kotlin source | ❌ needs the Android SDK + AAR rebuild |

Custom renderers on Apple/Android are **native code** registered from the app (Swift
`swiftDotNetRegisterRenderer` / Kotlin `registerRenderer`), so — unlike Web — they can't ship as a pure-C#
package. These two files are **authored and reviewed but not compiled in this environment.**

## Apple (MapKit)

1. Add `MapRenderer.swift` to your app's Swift bridge sources (the same target that builds
   `SwiftDotNetBridge`), or a companion Swift package linked into the app.
2. Call `registerSwiftDotNetMapRenderer()` at startup, after the bridge is initialized.
3. Requires **iOS 17 / macOS 14** (SwiftUI `Map` content builder). No API key — MapKit is free on Apple.

## Android (MapLibre)

1. Add `MapRenderer.kt` to the `SwiftDotNetComposeBridge` module (rebuild the AAR) or a linked module.
2. Gradle dependencies:
   ```kotlin
   implementation("org.maplibre.gl:android-sdk:11.+")
   implementation("org.maplibre.gl:android-plugin-annotation-v9:3.+")
   ```
3. Call `registerSwiftDotNetMapRenderer()` at startup. Key-free OSM demo tiles by default.

## Web (for reference)

Register from C# at app startup:
```csharp
SwiftDotNet.MapsWeb.UseMapLibre();
```
and load MapLibre GL JS + CSS in the host page (`index.html`):
```html
<link href="https://unpkg.com/maplibre-gl@4/dist/maplibre-gl.css" rel="stylesheet" />
<script src="https://unpkg.com/maplibre-gl@4/dist/maplibre-gl.js"></script>
```

## Wire contract (all renderers)

The C# `Map` writes three JSON-string props and consumes one prefixed event value — see
`src/SwiftDotNet.Maps/MapJson.cs` and `Map.cs`:

- props: `camera` = `{lat,lng,zoom}`, `pins` = `[{lat,lng,title?,tint?,draggable,id}]`,
  `polylines` = `[{color?,width,points:[{lat,lng}]}]`
- events (emit back): `tap:<lat>,<lng>` · `pinTap:<id>` · `pinDrag:<id>,<lat>,<lng>` · `camera:<lat>,<lng>,<zoom>`
