@file:OptIn(ExperimentalMaterial3Api::class, ExperimentalFoundationApi::class)

package com.swiftdotnet.bridge

import android.content.Context
import android.view.View
import android.webkit.WebView
import android.webkit.WebViewClient
import androidx.compose.animation.AnimatedVisibility
import androidx.compose.animation.animateContentSize
import androidx.compose.animation.core.Animatable
import androidx.compose.animation.core.DurationBasedAnimationSpec
import androidx.compose.animation.core.FastOutLinearInEasing
import androidx.compose.animation.core.FastOutSlowInEasing
import androidx.compose.animation.core.FiniteAnimationSpec
import androidx.compose.animation.core.LinearEasing
import androidx.compose.animation.core.LinearOutSlowInEasing
import androidx.compose.animation.core.RepeatMode
import androidx.compose.animation.core.StartOffset
import androidx.compose.animation.core.animateFloat
import androidx.compose.animation.core.animateFloatAsState
import androidx.compose.animation.core.Easing
import androidx.compose.animation.core.KeyframesSpec
import androidx.compose.animation.core.infiniteRepeatable
import androidx.compose.animation.core.keyframes
import androidx.compose.animation.core.rememberInfiniteTransition
import androidx.compose.animation.core.repeatable
import androidx.compose.animation.core.spring
import androidx.compose.animation.core.tween
import androidx.compose.foundation.ExperimentalFoundationApi
import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.clickable
import androidx.compose.foundation.gestures.detectDragGestures
import androidx.compose.foundation.gestures.detectTransformGestures
import androidx.compose.foundation.gestures.detectTapGestures
import androidx.compose.foundation.horizontalScroll
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.pager.HorizontalPager
import androidx.compose.foundation.pager.rememberPagerState
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.verticalScroll
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.alpha
import androidx.compose.ui.input.pointer.pointerInput
import androidx.compose.ui.draw.clip
import androidx.compose.ui.draw.shadow
import androidx.compose.foundation.Image
import androidx.compose.ui.graphics.Brush
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.asImageBitmap
import androidx.compose.ui.layout.ContentScale
import androidx.compose.ui.layout.Layout
import androidx.compose.ui.graphics.TransformOrigin
import androidx.compose.ui.graphics.graphicsLayer
import androidx.compose.ui.graphics.RectangleShape
import androidx.compose.ui.platform.ComposeView
import androidx.compose.ui.platform.LocalDensity
import androidx.compose.ui.platform.LocalLayoutDirection
import androidx.compose.ui.platform.LocalUriHandler
import androidx.compose.ui.viewinterop.AndroidView
import androidx.compose.ui.text.TextStyle
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.input.PasswordVisualTransformation
import androidx.compose.ui.text.input.VisualTransformation
import androidx.compose.ui.text.style.TextDecoration
import androidx.compose.ui.unit.Constraints
import androidx.compose.ui.unit.IntSize
import androidx.compose.ui.unit.constrainHeight
import androidx.compose.ui.unit.constrainWidth
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import kotlin.math.abs
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext
import org.json.JSONArray
import org.json.JSONObject

// MARK: - Bridge core --------------------------------------------------------

interface EventCallback {
    fun onEvent(id: String, value: String?)
}

/** Observable node — props/modifiers/children are snapshot state (Compose analog of iOS @Observable). */
class VNode(
    val id: String,
    type: String,
    props: Map<String, Any?>,
    modifiers: List<Map<String, Any?>>,
    children: List<VNode>,
) {
    var type by mutableStateOf(type)
    var props by mutableStateOf(props)
    var modifiers by mutableStateOf(modifiers)
    var children by mutableStateOf(children)
}

object SwiftDotNetBridge {
    private var eventCallback: EventCallback? = null

    var root by mutableStateOf<VNode?>(null)
        private set

    @JvmStatic fun setEventCallback(cb: EventCallback) { eventCallback = cb }

    @JvmStatic fun emit(id: String, value: String?) { eventCallback?.onEvent(id, value) }

    @JvmStatic
    fun render(json: String) {
        val ops = JSONObject(json).getJSONArray("ops")
        for (i in 0 until ops.length()) {
            val op = ops.getJSONObject(i)
            when (op.getString("op")) {
                "replace" -> root = parseNode(op.getJSONObject("node"))
                "updateProps" -> find(op.getString("id"))?.let {
                    it.props = parseProps(op.getJSONObject("props"))
                    it.modifiers = parseModifiers(op.getJSONArray("modifiers"))
                }
                "setChildren" -> find(op.getString("id"))?.let {
                    it.children = parseChildren(op.getJSONArray("children"))
                }
            }
        }
    }

    @JvmStatic
    fun createHostView(context: Context): View =
        ComposeView(context).apply { setContent { RootHostView() } }

    private fun find(id: String): VNode? {
        val r = root ?: return null
        val parts = id.split(".")
        if (parts.firstOrNull() != r.id) return null
        var node = r
        for (p in parts.drop(1)) {
            val idx = p.toIntOrNull() ?: return null
            if (idx < 0 || idx >= node.children.size) return null
            node = node.children[idx]
        }
        return node
    }

    private fun parseNode(o: JSONObject) = VNode(
        o.getString("id"), o.getString("type"),
        parseProps(o.getJSONObject("props")),
        parseModifiers(o.getJSONArray("modifiers")),
        parseChildren(o.getJSONArray("children")),
    )

    private fun parseChildren(a: JSONArray): List<VNode> =
        List(a.length()) { parseNode(a.getJSONObject(it)) }

    private fun parseProps(o: JSONObject): Map<String, Any?> =
        o.keys().asSequence().associateWith { o.get(it) }

    private fun parseModifiers(a: JSONArray): List<Map<String, Any?>> =
        List(a.length()) { i -> a.getJSONObject(i).let { o -> o.keys().asSequence().associateWith { o.get(it) } } }
}

// MARK: - Value helpers ------------------------------------------------------

private fun numOf(v: Any?): Double? = (v as? Number)?.toDouble()

private fun easingFor(curve: String?) = when (curve) {
    "linear" -> LinearEasing
    "easeIn" -> FastOutLinearInEasing
    "easeOut" -> LinearOutSlowInEasing
    else -> FastOutSlowInEasing
}

// A spring keeps its native feel; the timed curves map to a tween. Generic over the animated value type
// so the same spec drives both `animateFloatAsState` (alpha) and `animateContentSize` (layout size) —
// a FiniteAnimationSpec<Float> is also an AnimationSpec<Float>, so one helper covers both call sites.
private fun <T> animSpec(mod: Map<String, Any?>): FiniteAnimationSpec<T> {
    if ((mod["curve"] as? String) == "spring") return spring()
    return tween(
        durationMillis = ((numOf(mod["duration"]) ?: 0.3) * 1000).toInt(),
        delayMillis = ((numOf(mod["delay"]) ?: 0.0) * 1000).toInt(),
        easing = easingFor(mod["curve"] as? String),
    )
}

// The repeat path needs a *duration-based* spec (`spring` has no duration and can't be repeated), so a
// spring curve degrades to the equivalent tween here. The per-cycle delay is expressed as the repeatable's
// `initialStartOffset` instead of a tween delay so it applies once, not on every iteration.
private fun repeatCycleSpec(mod: Map<String, Any?>): DurationBasedAnimationSpec<Float> = tween(
    durationMillis = (((numOf(mod["duration"]) ?: 0.3) * 1000).toInt()).coerceAtLeast(1),
    easing = easingFor(mod["curve"] as? String),
)

// F4 repeat: a wire `animation` modifier carrying `repeatCount` is self-playing (shimmer/pulse) — it has no
// external trigger, so it drives its own 0→1 fraction on the composition clock. -1 = forever
// (`rememberInfiniteTransition`), otherwise a finite `repeatable` run once on first composition.
// `autoreverse` yo-yos each cycle (RepeatMode.Reverse) instead of snapping back (RepeatMode.Restart).
@Composable
private fun repeatFraction(mod: Map<String, Any?>, repeatCount: Double): Float {
    val mode = if ((mod["autoreverse"] as? String) == "true") RepeatMode.Reverse else RepeatMode.Restart
    val cycle = repeatCycleSpec(mod)
    val offset = StartOffset(((numOf(mod["delay"]) ?: 0.0) * 1000).toInt().coerceAtLeast(0))
    if (repeatCount < 0) {
        val transition = rememberInfiniteTransition(label = "sdnRepeat")
        return transition.animateFloat(
            initialValue = 0f,
            targetValue = 1f,
            animationSpec = infiniteRepeatable(animation = cycle, repeatMode = mode, initialStartOffset = offset),
            label = "sdnRepeatFraction",
        ).value
    }
    val anim = remember(mod) { Animatable(0f) }
    LaunchedEffect(mod) {
        anim.animateTo(
            targetValue = 1f,
            animationSpec = repeatable(
                iterations = repeatCount.toInt().coerceAtLeast(1),
                animation = cycle,
                repeatMode = mode,
                initialStartOffset = offset,
            ),
        )
    }
    return anim.value
}

// The floor of the repeating opacity pulse — mirrors the Web backend's `sdn-pulse` keyframes (1 → .4).
private const val PulseMinAlpha = 0.4f

// MARK: - Keyframe timelines (.Keyframes(k => …)) ----------------------------
//
// A timeline carries the whole shape of the animation on the wire, so unlike the canned pulse above each
// property maps to a real Compose `keyframes` spec — per-segment easing included.

/** One decoded stop: an absolute value at a fraction of the timeline, and the curve it arrives on. */
private data class KFStop(val time: Double, val value: Double, val curve: String?)

/**
 * Parses `prop:t,v[,curve];…|prop:…` — see `SwiftDotNet.KeyframeWire` on the C# side. Malformed segments
 * are skipped rather than thrown on: a bad stop must not take down a render.
 */
private fun parseKeyframeTracks(wire: String): Map<String, List<KFStop>> {
    if (wire.isEmpty()) return emptyMap()
    val tracks = LinkedHashMap<String, List<KFStop>>()
    for (trackSpec in wire.split("|")) {
        val colon = trackSpec.indexOf(':')
        if (colon <= 0) continue
        val stops = trackSpec.substring(colon + 1).split(";").mapNotNull { stopSpec ->
            val parts = stopSpec.split(",")
            val t = parts.getOrNull(0)?.toDoubleOrNull()
            val v = parts.getOrNull(1)?.toDoubleOrNull()
            if (t == null || v == null) null else KFStop(t, v, parts.getOrNull(2))
        }
        if (stops.isNotEmpty()) tracks[trackSpec.substring(0, colon)] = stops
    }
    return tracks
}

/**
 * One property's stops as a Compose `keyframes` spec. Compose's `using` easing applies to the segment
 * *starting* at a keyframe, where the wire records the curve a stop is *arrived* on — so each stop hands
 * its curve to the one before it. `autoreverse` is spelled out as a mirrored return leg, because
 * `RepeatMode.Reverse` would also reverse each segment's easing.
 */
private fun keyframeSpecFor(
    stops: List<KFStop>,
    durationSeconds: Double,
    defaultCurve: String?,
    autoreverse: Boolean,
): KeyframesSpec<Float> {
    val cycleMs = (durationSeconds * 1000).toInt().coerceAtLeast(1)
    return keyframes {
        durationMillis = if (autoreverse) cycleMs * 2 else cycleMs
        for ((i, stop) in stops.withIndex()) {
            val at = (stop.time * cycleMs).toInt().coerceIn(0, durationMillis)
            // The curve leading *out* of this stop is the next stop's.
            val outgoing = stops.getOrNull(i + 1)?.curve ?: defaultCurve
            stop.value.toFloat() at at using easingFor(outgoing)
        }
        if (!autoreverse) return@keyframes
        for (i in stops.indices.reversed()) {
            val at = ((2 - stops[i].time) * cycleMs).toInt().coerceIn(0, durationMillis)
            val outgoing = stops.getOrNull(i - 1)?.curve ?: defaultCurve
            stops[i].value.toFloat() at at using easingFor(outgoing)
        }
    }
}

/**
 * The live value of one track. A repeating timeline rides `rememberInfiniteTransition`; a one-shot runs an
 * `Animatable` that replays whenever the `on:` trigger changes.
 */
@Composable
private fun keyframeTrackValue(mod: Map<String, Any?>, stops: List<KFStop>, autoreverse: Boolean): Float {
    val spec = keyframeSpecFor(
        stops,
        numOf(mod["duration"]) ?: 1.0,
        mod["curve"] as? String,
        autoreverse,
    )
    val from = stops.first().value.toFloat()
    val to = if (autoreverse) from else stops.last().value.toFloat()
    val repeatCount = numOf(mod["repeatCount"])
    val offset = StartOffset(((numOf(mod["delay"]) ?: 0.0) * 1000).toInt().coerceAtLeast(0))

    if (repeatCount != null && repeatCount < 0) {
        val transition = rememberInfiniteTransition(label = "sdnKeyframes")
        return transition.animateFloat(
            initialValue = from,
            targetValue = to,
            // The mirrored return leg is already in the spec, so this only ever restarts.
            animationSpec = infiniteRepeatable(spec, RepeatMode.Restart, offset),
            label = "sdnKeyframeTrack",
        ).value
    }

    val anim = remember(mod) { Animatable(from) }
    LaunchedEffect(mod, mod["trigger"]) {
        anim.snapTo(from)
        if (repeatCount != null && repeatCount > 1) {
            anim.animateTo(to, repeatable(repeatCount.toInt(), spec, RepeatMode.Restart, offset))
        } else {
            anim.animateTo(to, spec)
        }
    }
    return anim.value
}

private fun VNode.s(key: String): String = props[key]?.toString() ?: ""
private fun VNode.n(key: String): Double? = numOf(props[key])
private fun VNode.b(key: String): Boolean = props[key] as? Boolean ?: false

private fun colorFor(token: String?): Color? = when {
    token == null -> null
    token.startsWith("#") -> runCatching {
        val v = token.removePrefix("#").toLong(16)
        Color(0xFF000000 or v)
    }.getOrNull()
    token == "primary" -> Color.Unspecified
    token == "secondary" -> Color(0xFF8E8E93)
    token == "red" -> Color(0xFFFF3B30)
    token == "green" -> Color(0xFF34C759)
    token == "blue" -> Color(0xFF007AFF)
    token == "accentColor" -> Color(0xFF7C4DFF)
    else -> null
}

// F5: parse a Brush wire string into a Compose gradient Brush ("linear:<deg>:<c>@<loc>;…" / "radial:…").
private fun gradientBrushFor(spec: String): Brush? {
    val firstColon = spec.indexOf(':')
    if (firstColon < 0) return null
    val kind = spec.substring(0, firstColon)
    val rest = spec.substring(firstColon + 1)

    fun parseStops(s: String): Array<Pair<Float, Color>>? {
        val items = s.split(';').filter { it.isNotEmpty() }
        if (items.isEmpty()) return null
        return items.map {
            val at = it.lastIndexOf('@')
            if (at < 0) return null
            val color = colorFor(it.substring(0, at)) ?: Color.Transparent
            val loc = it.substring(at + 1).toFloatOrNull() ?: 0f
            loc to color
        }.toTypedArray()
    }

    return when (kind) {
        "linear" -> {
            val secondColon = rest.indexOf(':')
            if (secondColon < 0) return null
            val angle = rest.substring(0, secondColon).toDoubleOrNull() ?: 90.0
            val stops = parseStops(rest.substring(secondColon + 1)) ?: return null
            val rad = angle * Math.PI / 180.0
            // Large finite endpoints approximate the sweep direction (Compose gradients take pixel points).
            val dx = (Math.cos(rad) * 1000).toFloat()
            val dy = (Math.sin(rad) * 1000).toFloat()
            Brush.linearGradient(
                colorStops = stops,
                start = androidx.compose.ui.geometry.Offset(500f - dx / 2, 500f - dy / 2),
                end = androidx.compose.ui.geometry.Offset(500f + dx / 2, 500f + dy / 2),
            )
        }
        "radial" -> {
            val stops = parseStops(rest) ?: return null
            Brush.radialGradient(colorStops = stops)
        }
        else -> null
    }
}

// Process-wide memo of decoded remote images so recomposition (or a list re-scroll) doesn't refetch. Held
// strongly and never evicted — the bridge only shows the handful of URLs the tree references at once.
private val remoteImageCache = java.util.Collections.synchronizedMap(HashMap<String, android.graphics.Bitmap>())

/**
 * Fetches and decodes a remote image off the main thread, keyed by URL. Deliberately dependency-free
 * (`URLConnection` + `BitmapFactory`) rather than pulling Coil into the bridge AAR. Any failure — bad URL,
 * offline, non-image payload — resolves to `null` so the caller shows its placeholder instead of throwing.
 */
@Composable
private fun remoteBitmap(url: String): android.graphics.Bitmap? =
    produceState<android.graphics.Bitmap?>(initialValue = remoteImageCache[url], key1 = url) {
        if (value != null) return@produceState
        value = withContext(Dispatchers.IO) {
            runCatching {
                val conn = java.net.URL(url).openConnection()
                conn.connectTimeout = 15000
                conn.readTimeout = 15000
                conn.getInputStream().use { android.graphics.BitmapFactory.decodeStream(it) }
            }.getOrNull()?.also { remoteImageCache[url] = it }
        }
    }.value

// F3 raster: decode bytes/file/url into a Bitmap and show it; an SF-Symbol name falls back to an emoji glyph.
@Composable
private fun RasterImage(node: VNode) {
    val scale = if (node.s("contentMode") == "fill") ContentScale.Crop else ContentScale.Fit
    val bytesProp = node.s("bytes")
    val fileProp = node.s("file")
    val urlProp = node.s("url")
    val local = remember(bytesProp, fileProp) {
        runCatching {
            when {
                bytesProp.isNotEmpty() -> {
                    val bytes = android.util.Base64.decode(bytesProp, android.util.Base64.DEFAULT)
                    android.graphics.BitmapFactory.decodeByteArray(bytes, 0, bytes.size)
                }
                fileProp.isNotEmpty() -> android.graphics.BitmapFactory.decodeFile(fileProp)
                else -> null
            }
        }.getOrNull()
    }
    // Inline sources win; a URL loads asynchronously and shows the placeholder until it lands (or fails).
    val bitmap = local ?: if (urlProp.isNotEmpty()) remoteBitmap(urlProp) else null
    if (bitmap != null) {
        Image(bitmap = bitmap.asImageBitmap(), contentDescription = null, contentScale = scale)
    } else {
        Text(emojiFor(node.s("system")), fontSize = 22.sp)
    }
}

private fun textStyleFor(token: String?): TextStyle? = when (token) {
    "largeTitle" -> TextStyle(fontSize = 34.sp, fontWeight = FontWeight.Bold)
    "title" -> TextStyle(fontSize = 28.sp)
    "headline" -> TextStyle(fontSize = 17.sp, fontWeight = FontWeight.SemiBold)
    "body" -> TextStyle(fontSize = 17.sp)
    "caption" -> TextStyle(fontSize = 12.sp)
    else -> null
}

/** SF Symbols don't exist on Android; map common ones to emoji, else fall back to the raw name. */
private fun emojiFor(name: String): String = when (name) {
    "star.fill", "star" -> "⭐"
    "heart.fill", "heart" -> "❤️"
    "bell", "bell.fill" -> "🔔"
    "checkmark" -> "✅"
    "slider.horizontal.3" -> "🎚️"
    "square.grid.2x2" -> "▦"
    "rectangle.stack" -> "🗂️"
    "list.bullet" -> "☰"
    "arrow.forward.circle" -> "➡️"
    "textformat" -> "🔤"
    "hand.tap" -> "👆"
    "wand.and.stars" -> "✨"
    "rectangle.3.offgrid" -> "▤"
    "rectangle.portrait" -> "▭"
    "gauge" -> "🎛️"
    "globe" -> "🌐"
    "map" -> "🗺️"
    "chevron.down.circle" -> "⌄"
    "paintbrush" -> "🎨"
    "square.stack" -> "🧱"
    "calendar" -> "📅"
    "bubble.left.and.bubble.right" -> "💬"
    "camera" -> "📷"
    else -> "•"
}

private fun modColor(node: VNode, type: String): Color? =
    node.modifiers.firstOrNull { it["type"] == type }?.get("value")?.let { colorFor(it as? String) }

private fun titleOf(node: VNode): String? =
    node.modifiers.firstOrNull { it["type"] == "navigationTitle" }?.get("value") as? String

private fun boxAlignmentFor(token: String?): Alignment = when (token) {
    "topLeading" -> Alignment.TopStart
    "top" -> Alignment.TopCenter
    "topTrailing" -> Alignment.TopEnd
    "leading" -> Alignment.CenterStart
    "trailing" -> Alignment.CenterEnd
    "bottomLeading" -> Alignment.BottomStart
    "bottom" -> Alignment.BottomCenter
    "bottomTrailing" -> Alignment.BottomEnd
    else -> Alignment.Center
}

private fun columnAlignFor(token: String?): Alignment.Horizontal = when (token) {
    "leading" -> Alignment.Start
    "trailing" -> Alignment.End
    else -> Alignment.CenterHorizontally
}

private fun rowAlignFor(token: String?): Alignment.Vertical = when (token) {
    "top" -> Alignment.Top
    "bottom" -> Alignment.Bottom
    else -> Alignment.CenterVertically
}

/** Reserved event id for safe-area inset reports — mirrors `SwiftDotNet.SafeArea.EventId`. */
private const val SAFE_AREA_EVENT_ID = "\$safeArea"

/**
 * Builds the `WindowInsets` a `safeAreaPadding` / `ignoresSafeArea` modifier refers to: the
 * `safeDrawing` insets (status bar, cutout, navigation bar), unioned with the IME insets when the
 * modifier asks for the keyboard region, then narrowed to the requested edges.
 */
@Composable
private fun safeAreaInsetsFor(mod: Map<String, Any?>): WindowInsets {
    val regions = mod["regions"] as? String ?: "container"
    var insets = when (regions) {
        "keyboard" -> WindowInsets.ime
        "all" -> WindowInsets.safeDrawing.union(WindowInsets.ime)
        else -> WindowInsets.safeDrawing
    }

    val edges = mod["value"] as? String ?: "all"
    if (edges != "all") {
        var sides: WindowInsetsSides? = null
        for (part in edges.split(",")) {
            val side = when (part) {
                "top" -> WindowInsetsSides.Top
                "leading" -> WindowInsetsSides.Start
                "bottom" -> WindowInsetsSides.Bottom
                "trailing" -> WindowInsetsSides.End
                else -> null
            } ?: continue
            sides = sides?.plus(side) ?: side
        }
        // No recognized edge → nothing to inset; an empty WindowInsets keeps the modifier a no-op
        // rather than silently falling back to insetting on all four sides.
        insets = sides?.let { insets.only(it) } ?: WindowInsets(0, 0, 0, 0)
    }
    return insets
}

// MARK: - Modifier application ----------------------------------------------

@Composable
private fun Modified(node: VNode, content: @Composable () -> Unit) {
    if (node.modifiers.isEmpty()) { content(); return }

    var m: Modifier = Modifier
    var textStyle: TextStyle? = null
    var contentColor: Color? = null
    var boxAlignment: Alignment = Alignment.TopStart

    // Phase-1 implicit animation: `animateContentSize` covers frame/layout, and opacity is animated via
    // `animateFloatAsState`. Scale/offset/color animation on Compose is a follow-up (they still snap).
    // A spec carrying `repeatCount` is a self-playing loop instead — see `repeatFraction`.
    val animMod = node.modifiers.firstOrNull { (it["type"] as? String) == "animation" }
    val repeatCount = animMod?.let { numOf(it["repeatCount"]) }
    var targetAlpha: Float? = null

    // A `.Keyframes(…)` timeline drives real per-property values, so where one is present it owns the
    // properties it declares — including alpha, which is why the pulse below stands down for them.
    val kfMod = node.modifiers.firstOrNull { (it["type"] as? String) == "keyframes" }
    val kfWire = kfMod?.get("tracks") as? String ?: ""
    val kfTracks = remember(kfWire) { parseKeyframeTracks(kfWire) }
    val kfAutoreverse = (kfMod?.get("autoreverse") as? String) == "true"

    for (mod in node.modifiers) {
        when (mod["type"]) {
            "padding" -> m = m.padding(
                start = (numOf(mod["leading"]) ?: 0.0).dp,
                top = (numOf(mod["top"]) ?: 0.0).dp,
                end = (numOf(mod["trailing"]) ?: 0.0).dp,
                bottom = (numOf(mod["bottom"]) ?: 0.0).dp,
            )
            "safeAreaPadding" -> m = m.windowInsetsPadding(safeAreaInsetsFor(mod))
            // Compose content is already edge-to-edge, so "ignoring" the safe area is the *absence* of
            // padding. What this must still do is consume the insets, so a descendant that applies
            // safeAreaPadding doesn't re-inset a region this view has deliberately bled into.
            "ignoresSafeArea" -> m = m.consumeWindowInsets(safeAreaInsetsFor(mod))
            "frame" -> {
                numOf(mod["width"])?.let { m = m.width(it.dp) }
                numOf(mod["height"])?.let { m = m.height(it.dp) }
                (mod["alignment"] as? String)?.let { boxAlignment = boxAlignmentFor(it) }
            }
            "align" -> { m = m.fillMaxWidth(); boxAlignment = boxAlignmentFor(mod["value"] as? String) }
            "background" -> {
                val grad = (mod["gradient"] as? String)?.let { gradientBrushFor(it) }
                if (grad != null) m = m.background(grad)
                else colorFor(mod["value"] as? String)?.let { m = m.background(it) }
            }
            "material" -> {
                // F6: still a translucent tint (documented degradation). `Modifier.blur` / RenderEffect
                // (API 31+) blur a composable's *own* content, but `.material()` is a SwiftUI *backdrop*
                // blur — applying either here would smear the node's children instead of what's behind it.
                // Real backdrop blur on Android needs Window.setBackgroundBlurRadius (window-level, and
                // only when the compositor allows cross-window blur), so it can't be done per-node.
                val tint = when (mod["value"] as? String) {
                    "ultraThin" -> 0.55f; "thin" -> 0.65f; "thick" -> 0.85f; else -> 0.75f
                }
                val base = if ((mod["dark"] as? String) == "true") Color(0xFF141416) else Color.White
                m = m.background(base.copy(alpha = tint))
            }
            "cornerRadius" -> m = m.clip(RoundedCornerShape((numOf(mod["radius"]) ?: 0.0).dp))
            "border" -> m = m.border(
                (numOf(mod["width"]) ?: 1.0).dp,
                colorFor(mod["color"] as? String) ?: Color.Gray,
                RoundedCornerShape((numOf(mod["cornerRadius"]) ?: 0.0).dp),
            )
            "shadow" -> {
                val c = colorFor(mod["color"] as? String) ?: Color.Black
                m = m.shadow(elevation = (numOf(mod["radius"]) ?: 4.0).dp, ambientColor = c, spotColor = c)
            }
            "opacity" -> targetAlpha = (numOf(mod["amount"]) ?: 1.0).toFloat()
            "scaleEffect" -> {
                val t = mod["value"] as? String
                val fx = if (t == "leading" || t == "topLeading" || t == "bottomLeading") 0f
                         else if (t == "trailing" || t == "topTrailing" || t == "bottomTrailing") 1f else 0.5f
                val fy = if (t == "top" || t == "topLeading" || t == "topTrailing") 0f
                         else if (t == "bottom" || t == "bottomLeading" || t == "bottomTrailing") 1f else 0.5f
                m = m.graphicsLayer(
                    scaleX = (numOf(mod["x"]) ?: 1.0).toFloat(),
                    scaleY = (numOf(mod["y"]) ?: 1.0).toFloat(),
                    transformOrigin = TransformOrigin(fx, fy),
                )
            }
            "offset" -> m = m.offset(
                x = (numOf(mod["x"]) ?: 0.0).dp,
                y = (numOf(mod["y"]) ?: 0.0).dp,
            )
            "rotation" -> {
                val t = mod["value"] as? String
                val fx = if (t == "leading" || t == "topLeading" || t == "bottomLeading") 0f
                         else if (t == "trailing" || t == "topTrailing" || t == "bottomTrailing") 1f else 0.5f
                val fy = if (t == "top" || t == "topLeading" || t == "topTrailing") 0f
                         else if (t == "bottom" || t == "bottomLeading" || t == "bottomTrailing") 1f else 0.5f
                m = m.graphicsLayer(
                    rotationZ = (numOf(mod["degrees"]) ?: 0.0).toFloat(),
                    transformOrigin = TransformOrigin(fx, fy),
                )
            }
            "disabled" -> if ((mod["value"] as? String) == "true") {
                // Dim + swallow all pointer input for the subtree (Compose has no generic `.disabled()`).
                m = m.alpha(0.5f).pointerInput(Unit) {
                    awaitPointerEventScope { while (true) { awaitPointerEvent().changes.forEach { it.consume() } } }
                }
            }
            "onTapGesture" -> (mod["event"] as? String)?.let { e ->
                val count = (numOf(mod["amount"]) ?: 1.0).toInt()
                m = if (count >= 2)
                    m.pointerInput(e) { detectTapGestures(onDoubleTap = { SwiftDotNetBridge.emit(e, null) }) }
                else
                    m.clickable { SwiftDotNetBridge.emit(e, null) }
            }
            "onLongPress" -> (mod["event"] as? String)?.let { e ->
                m = m.pointerInput(e) { detectTapGestures(onLongPress = { SwiftDotNetBridge.emit(e, null) }) }
            }
            "onSwipe" -> (mod["event"] as? String)?.let { e ->
                val dir = mod["value"] as? String
                m = m.pointerInput(e, dir) {
                    var dx = 0f; var dy = 0f
                    detectDragGestures(
                        onDragStart = { dx = 0f; dy = 0f },
                        onDrag = { _, drag -> dx += drag.x; dy += drag.y },
                        onDragEnd = {
                            val matched = if (abs(dx) > abs(dy))
                                (if (dx < 0) dir == "left" else dir == "right")
                            else
                                (if (dy < 0) dir == "up" else dir == "down")
                            if (matched && (abs(dx) > 40f || abs(dy) > 40f)) SwiftDotNetBridge.emit(e, null)
                        },
                    )
                }
            }
            "onDrag" -> (mod["event"] as? String)?.let { e ->
                // F1 continuous drag → "<phase>;tx,ty;lx,ly;vx,vy". Compose gives per-event position; the
                // cumulative translation is tracked here. Velocity isn't tracked, sent as 0.
                m = m.pointerInput(e) {
                    var tx = 0f; var ty = 0f; var lx = 0f; var ly = 0f
                    detectDragGestures(
                        onDragStart = { pos -> tx = 0f; ty = 0f; lx = pos.x; ly = pos.y
                            SwiftDotNetBridge.emit(e, "b;0,0;$lx,$ly;0,0") },
                        onDrag = { change, drag -> tx += drag.x; ty += drag.y; lx = change.position.x; ly = change.position.y
                            SwiftDotNetBridge.emit(e, "c;$tx,$ty;$lx,$ly;0,0") },
                        onDragEnd = { SwiftDotNetBridge.emit(e, "e;$tx,$ty;$lx,$ly;0,0") },
                    )
                }
            }
            "onMagnify" -> (mod["event"] as? String)?.let { e ->
                m = m.pointerInput(e) {
                    // detectTransformGestures yields the incremental zoom; accumulate to the cumulative
                    // factor (1.0 = unchanged) the C# handler expects.
                    var scale = 1f
                    detectTransformGestures { _, _, zoom, _ ->
                        scale *= zoom
                        SwiftDotNetBridge.emit(e, scale.toString())
                    }
                }
            }
            "font" -> textStyle = textStyleFor(mod["value"] as? String)
            "foregroundColor" -> contentColor = colorFor(mod["value"] as? String)
        }
    }

    // ---- keyframe timeline ---------------------------------------------------
    // Sampled before the pulse/implicit-animation block so it can claim alpha from it. Each track is a
    // separate @Composable animation; the conditionals are stable per node because they key off the wire
    // string, which Compose re-remembers when it changes.
    var kfAlpha: Float? = null
    if (kfMod != null && kfTracks.isNotEmpty()) {
        var scaleX: Float? = null
        var scaleY: Float? = null
        var rotation: Float? = null
        var offsetX: Float? = null
        var offsetY: Float? = null
        var width: Float? = null
        var height: Float? = null

        kfTracks["opacity"]?.let { kfAlpha = keyframeTrackValue(kfMod, it, kfAutoreverse) }
        kfTracks["scale"]?.let {
            val v = keyframeTrackValue(kfMod, it, kfAutoreverse)
            scaleX = v; scaleY = v
        }
        kfTracks["scaleX"]?.let { scaleX = keyframeTrackValue(kfMod, it, kfAutoreverse) }
        kfTracks["scaleY"]?.let { scaleY = keyframeTrackValue(kfMod, it, kfAutoreverse) }
        kfTracks["rotation"]?.let { rotation = keyframeTrackValue(kfMod, it, kfAutoreverse) }
        kfTracks["offsetX"]?.let { offsetX = keyframeTrackValue(kfMod, it, kfAutoreverse) }
        kfTracks["offsetY"]?.let { offsetY = keyframeTrackValue(kfMod, it, kfAutoreverse) }
        kfTracks["width"]?.let { width = keyframeTrackValue(kfMod, it, kfAutoreverse) }
        kfTracks["height"]?.let { height = keyframeTrackValue(kfMod, it, kfAutoreverse) }

        if (scaleX != null || scaleY != null || rotation != null) {
            m = m.graphicsLayer(
                scaleX = scaleX ?: 1f,
                scaleY = scaleY ?: 1f,
                rotationZ = rotation ?: 0f,
            )
        }
        // `offset` is a layout modifier rather than a graphicsLayer translation so it matches SwiftUI's
        // `.offset` (which also doesn't re-measure) without double-applying inside the layer.
        if (offsetX != null || offsetY != null) {
            m = m.offset(x = (offsetX ?: 0f).dp, y = (offsetY ?: 0f).dp)
        }
        width?.let { m = m.width(it.dp) }
        height?.let { m = m.height(it.dp) }
    }

    if (kfAlpha != null) {
        // An opacity track carries an absolute value, so it replaces any static `.Opacity()` rather than
        // scaling it — the same precedence every other backend gives a track.
        m = m.alpha(kfAlpha!!)
    } else if (animMod != null && repeatCount != null) {
        // Self-playing loop: the wire carries no from/to pair, so the cycle fades opacity down to
        // `PulseMinAlpha` and back — the same generic pulse the Web backend's `sdn-pulse` keyframes apply.
        // Any explicit `.opacity()` in the chain scales the pulse rather than being overwritten.
        val base = targetAlpha ?: 1f
        m = m.alpha(base * (1f - (1f - PulseMinAlpha) * repeatFraction(animMod, repeatCount)))
    } else {
        if (animMod != null) m = m.animateContentSize(animationSpec = animSpec(animMod))
        if (targetAlpha != null) {
            val alpha = if (animMod != null)
                animateFloatAsState(targetValue = targetAlpha!!, animationSpec = animSpec(animMod), label = "alpha").value
            else targetAlpha!!
            m = m.alpha(alpha)
        }
    }

    Box(modifier = m, contentAlignment = boxAlignment) {
        val providers = buildList {
            contentColor?.takeIf { it != Color.Unspecified }?.let { add(LocalContentColor provides it) }
            textStyle?.let { add(LocalTextStyle provides it) }
        }
        if (providers.isEmpty()) content()
        else CompositionLocalProvider(*providers.toTypedArray()) { content() }
    }
}

// MARK: - Interpreter --------------------------------------------------------

// MARK: - Custom renderer registry (for native extensions)

/** Props/emit surface handed to a custom renderer registered from native Kotlin. */
class SwiftDotNetProps internal constructor(val id: String, private val node: VNode) {
    fun string(key: String): String? = node.props[key] as? String
    fun number(key: String): Double? = node.props[key] as? Double
    fun bool(key: String): Boolean? = node.props[key] as? Boolean
    fun emit(value: String? = null) = SwiftDotNetBridge.emit(id, value)
}

typealias SwiftDotNetRenderer = @Composable (SwiftDotNetProps) -> Unit

private val customRenderers = mutableMapOf<String, SwiftDotNetRenderer>()

/** Register a Compose renderer for a custom `CustomView.TypeName`. Call from your app's Kotlin. */
fun registerRenderer(type: String, renderer: SwiftDotNetRenderer) {
    customRenderers[type] = renderer
}

@Composable
private fun RootHostView() {
    val root = SwiftDotNetBridge.root
    ReportSafeAreaInsets()
    Box(Modifier.fillMaxSize()) {
        if (root != null) NodeView(root)
        else Text("SwiftDotNet: waiting for first render…", Modifier.align(Alignment.Center))
    }
}

/**
 * Pushes the window's safe-area insets (plus the live IME height) to C# on the reserved `$safeArea`
 * event id, in dp. Reading them here rather than inside a layout node keeps the report layout-neutral —
 * this composable emits nothing into the tree. `LaunchedEffect` is keyed on the payload, so it fires
 * only when a value actually changes, not on every recomposition.
 */
@Composable
private fun ReportSafeAreaInsets() {
    val density = LocalDensity.current
    val safe = WindowInsets.safeDrawing
    val ime = WindowInsets.ime
    val payload = with(density) {
        listOf(
            safe.getTop(density).toDp().value,
            safe.getLeft(density, LocalLayoutDirection.current).toDp().value,
            safe.getBottom(density).toDp().value,
            safe.getRight(density, LocalLayoutDirection.current).toDp().value,
            ime.getBottom(density).toDp().value,
        ).joinToString(";")
    }
    LaunchedEffect(payload) { SwiftDotNetBridge.emit(SAFE_AREA_EVENT_ID, payload) }
}

@Composable
private fun NodeView(node: VNode) = Modified(node) { RawNode(node) }

@Composable
private fun ColumnScope.StackChildren(node: VNode) =
    node.children.forEach { if (it.type == "Spacer") Spacer(Modifier.weight(1f)) else NodeView(it) }

@Composable
private fun RowScope.StackChildren(node: VNode) =
    node.children.forEach { if (it.type == "Spacer") Spacer(Modifier.weight(1f)) else NodeView(it) }

@Composable
private fun RawNode(node: VNode) {
    when (node.type) {
        "Text" -> Text(node.s("text"))
        "Button" -> Button(onClick = { SwiftDotNetBridge.emit(node.id, null) }) { Text(node.s("title")) }
        "Spacer" -> Spacer(Modifier.size(8.dp))
        "Divider" -> HorizontalDivider()

        "VStack" -> Column(
            verticalArrangement = Arrangement.spacedBy((node.n("spacing") ?: 0.0).dp),
            horizontalAlignment = columnAlignFor(node.props["alignment"] as? String),
        ) { StackChildren(node) }
        "HStack" -> Row(
            horizontalArrangement = Arrangement.spacedBy((node.n("spacing") ?: 0.0).dp),
            verticalAlignment = rowAlignFor(node.props["alignment"] as? String),
        ) { StackChildren(node) }
        "ZStack" -> Box(contentAlignment = boxAlignmentFor(node.props["alignment"] as? String)) { node.children.forEach { NodeView(it) } }

        "ScrollView" -> if (node.s("axis") == "horizontal")
            Row(Modifier.horizontalScroll(rememberScrollState()), verticalAlignment = Alignment.CenterVertically) { StackChildren(node) }
        else
            Column(
                Modifier.fillMaxWidth().verticalScroll(rememberScrollState()),
                verticalArrangement = Arrangement.spacedBy(12.dp),
                horizontalAlignment = Alignment.CenterHorizontally,
            ) { node.children.forEach { NodeView(it) } }

        "Grid" -> GridNode(node)
        "AbsoluteLayout" -> AbsoluteLayoutNode(node)
        "List" -> ListNode(node)
        "Form" -> Column(
            Modifier.fillMaxSize()
                .background(MaterialTheme.colorScheme.surfaceContainer)
                .verticalScroll(rememberScrollState())
                .padding(vertical = 12.dp),
            verticalArrangement = Arrangement.spacedBy(24.dp),
        ) { node.children.forEach { NodeView(it) } }
        "Section" -> SectionNode(node)
        "Group" -> Column { node.children.forEach { NodeView(it) } }
        "DisclosureGroup" -> DisclosureGroupNode(node)

        "TabView" -> TabViewNode(node)
        "Tab" -> node.children.firstOrNull()?.let { NodeView(it) }
        "Menu" -> MenuNode(node)

        "TextField" -> FieldNode(node, secure = false)
        "SecureField" -> FieldNode(node, secure = true)
        "TextEditor" -> OutlinedTextField(
            value = node.s("text"), onValueChange = { SwiftDotNetBridge.emit(node.id, it) },
            modifier = Modifier.fillMaxWidth().heightIn(min = 100.dp),
        )
        "Toggle" -> Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.SpaceBetween, verticalAlignment = Alignment.CenterVertically) {
            Text(node.s("label"))
            Switch(checked = node.b("value"), onCheckedChange = { SwiftDotNetBridge.emit(node.id, it.toString()) })
        }
        "Slider" -> Slider(
            value = (node.n("value") ?: 0.0).toFloat(),
            onValueChange = { SwiftDotNetBridge.emit(node.id, it.toString()) },
            valueRange = (node.n("min") ?: 0.0).toFloat()..(node.n("max") ?: 1.0).toFloat(),
        )
        "Stepper" -> StepperNode(node)
        "Picker" -> PickerNode(node)
        "DatePicker" -> DatePickerNode(node)
        "ColorPicker" -> ColorPickerNode(node)

        "NavigationStack" -> NavigationStackNode(node)
        "NavigationLink" -> NavigationLinkNode(node)
        "Sheet" -> SheetNode(node)
        "Alert" -> AlertNode(node)
        "ActionSheet" -> ActionSheetNode(node)

        "WebView" -> WebViewNode(node)
        "Image" -> RasterImage(node)
        "Label" -> Row(verticalAlignment = Alignment.CenterVertically) {
            // Fixed-width icon slot so titles line up down the column however wide the glyph renders.
            Box(Modifier.width(32.dp), contentAlignment = Alignment.Center) {
                Text(emojiFor(node.s("systemImage")), fontSize = 17.sp)
            }
            Spacer(Modifier.width(10.dp))
            Text(node.s("title"), style = textStyleFor("body")!!)
        }
        "ProgressView" -> ProgressNode(node)
        "Gauge" -> Column {
            (node.props["label"] as? String)?.let { Text(it, style = textStyleFor("caption")!!) }
            LinearProgressIndicator(progress = { gaugeFraction(node) }, modifier = Modifier.fillMaxWidth())
        }
        "Link" -> {
            val uri = LocalUriHandler.current
            Text(node.s("title"), color = colorFor("blue")!!,
                textDecoration = TextDecoration.Underline,
                modifier = Modifier.clickable { runCatching { uri.openUri(node.s("url")) } })
        }

        "Rectangle" -> ShapeBox(node, RectangleShape)
        "Circle" -> ShapeBox(node, CircleShape)
        "Capsule" -> ShapeBox(node, RoundedCornerShape(percent = 50))
        "RoundedRectangle" -> ShapeBox(node, RoundedCornerShape((node.n("cornerRadius") ?: 8.0).dp))

        else -> {
            val renderer = customRenderers[node.type]
            if (renderer != null) renderer(SwiftDotNetProps(node.id, node))
            else Text("⚠️ unknown view: ${node.type}", color = colorFor("red")!!)
        }
    }
}

// MARK: - WebView ------------------------------------------------------------

@Composable
private fun WebViewNode(node: VNode) {
    val url = node.props["url"] as? String
    val html = node.props["html"] as? String
    AndroidView(
        factory = { ctx ->
            WebView(ctx).apply {
                webViewClient = WebViewClient()
                @Suppress("SetJavaScriptEnabled")
                settings.javaScriptEnabled = true
            }
        },
        modifier = Modifier.fillMaxWidth().height(300.dp),
        update = { web ->
            val key = url ?: html
            if (web.tag != key) {
                web.tag = key
                when {
                    url != null -> web.loadUrl(url)
                    html != null -> web.loadDataWithBaseURL(null, html, "text/html", "utf-8", null)
                }
            }
        },
    )
}

// MARK: - Layout nodes -------------------------------------------------------

// ---------------------------------------------------------------------------
//  Grid & AbsoluteLayout engines
//
//  Mirrors SwiftDotNet.GridEngine / AbsoluteLayoutBounds on the C# side. Neither container maps onto
//  a stock composable: LazyVerticalGrid sizes columns but has no row span or explicit cell, and Compose
//  has no absolute-positioning container. Both are therefore written as custom `Layout`s, which is
//  where the measure and place passes the DSL needs actually live.
// ---------------------------------------------------------------------------

private enum class TrackKind { AUTO, FIXED, STAR, FLEXIBLE }

/** One column/row definition: points for FIXED, weight for STAR, minimum (+ optional max) for FLEXIBLE. */
private data class Track(val kind: TrackKind, val value: Double, val max: Double? = null)

/**
 * Parses `"fixed:80,star:1,auto,flex:40:inf"`. An absent or unparseable spec yields [fallback] equal
 * star tracks, which is the plain `new Grid(n, …)` shape.
 */
private fun parseTracks(spec: String?, fallback: Int): List<Track> {
    if (spec.isNullOrEmpty()) return List(maxOf(1, fallback)) { Track(TrackKind.STAR, 1.0) }
    return spec.split(",").map { token ->
        val parts = token.split(":")
        when (parts[0]) {
            "fixed" -> Track(TrackKind.FIXED, parts.getOrNull(1)?.toDoubleOrNull() ?: 0.0)
            "star" -> Track(TrackKind.STAR, parts.getOrNull(1)?.toDoubleOrNull() ?: 1.0)
            "flex" -> {
                val upper = parts.getOrNull(2) ?: "inf"
                Track(TrackKind.FLEXIBLE, parts.getOrNull(1)?.toDoubleOrNull() ?: 0.0,
                    if (upper == "inf") null else upper.toDoubleOrNull())
            }
            else -> Track(TrackKind.AUTO, 0.0)
        }
    }
}

private data class Placement(val column: Int, val row: Int, val columnSpan: Int, val rowSpan: Int)

/** Mirror of `GridEngine.Place`: pins claim their cell first, then the rest flow into the first free fit. */
private fun placeGrid(
    columns: Int,
    requests: List<Placement?>,
): Pair<List<Placement>, Int> {
    val cols = maxOf(1, columns)
    val result = arrayOfNulls<Placement>(requests.size)
    val occupied = mutableListOf<BooleanArray>()

    fun ensureRows(through: Int) { while (occupied.size <= through) occupied.add(BooleanArray(cols)) }
    fun isFree(col: Int, row: Int, cs: Int, rs: Int): Boolean {
        for (r in row until row + rs) {
            if (r >= occupied.size) continue
            for (c in col until minOf(col + cs, cols)) if (occupied[r][c]) return false
        }
        return true
    }
    fun occupy(col: Int, row: Int, cs: Int, rs: Int) {
        ensureRows(row + rs - 1)
        for (r in row until row + rs) for (c in col until minOf(col + cs, cols)) occupied[r][c] = true
    }

    requests.forEachIndexed { i, r ->
        // Only a request carrying BOTH an explicit column and row is a pin; span-only requests flow.
        if (r == null || r.column < 0 || r.row < 0) return@forEachIndexed
        val col = r.column.coerceIn(0, cols - 1)
        val cs = r.columnSpan.coerceIn(1, cols - col)
        val rs = maxOf(1, r.rowSpan)
        occupy(col, r.row, cs, rs)
        result[i] = Placement(col, r.row, cs, rs)
    }

    var cursorRow = 0
    var cursorCol = 0
    requests.forEachIndexed { i, r ->
        if (result[i] != null) return@forEachIndexed
        val cs = (r?.columnSpan ?: 1).coerceIn(1, cols)
        val rs = maxOf(1, r?.rowSpan ?: 1)
        while (true) {
            if (cursorCol + cs > cols) { cursorCol = 0; cursorRow++; continue }
            if (!isFree(cursorCol, cursorRow, cs, rs)) { cursorCol++; continue }
            break
        }
        occupy(cursorCol, cursorRow, cs, rs)
        result[i] = Placement(cursorCol, cursorRow, cs, rs)
        cursorCol += cs
    }

    return result.map { it ?: Placement(0, 0, 1, 1) } to occupied.size
}

/**
 * Mirror of `SkiaNode.ResolveTracks`: FIXED takes its points, AUTO/FLEXIBLE take the largest
 * single-track child, STAR splits the remainder by weight, and a spanning child's shortfall grows the
 * last content-sized track it covers.
 */
private fun resolveTracks(
    tracks: List<Track>,
    available: Float,
    gap: Float,
    spans: List<Pair<Int, Int>>,   // (start, span) per child
    sizes: List<Float>,
): FloatArray {
    val n = tracks.size
    val resolved = FloatArray(n)

    fun extent(sizes: FloatArray, start: Int, count: Int): Float {
        var total = 0f
        for (i in start until minOf(start + count, sizes.size)) total += sizes[i]
        return total + gap * maxOf(0, minOf(count, sizes.size - start) - 1)
    }

    spans.forEachIndexed { i, (start, span) ->
        if (span != 1 || start >= n || i >= sizes.size) return@forEachIndexed
        if (tracks[start].kind == TrackKind.AUTO || tracks[start].kind == TrackKind.FLEXIBLE)
            resolved[start] = maxOf(resolved[start], sizes[i])
    }

    var starWeight = 0f
    for (t in 0 until n) when (tracks[t].kind) {
        TrackKind.FIXED -> resolved[t] = tracks[t].value.toFloat()
        TrackKind.FLEXIBLE -> {
            resolved[t] = maxOf(resolved[t], tracks[t].value.toFloat())
            tracks[t].max?.let { resolved[t] = minOf(resolved[t], it.toFloat()) }
        }
        TrackKind.STAR -> { starWeight += tracks[t].value.toFloat(); resolved[t] = 0f }
        TrackKind.AUTO -> {}
    }

    spans.forEachIndexed { i, (start, span) ->
        if (span <= 1 || start >= n || i >= sizes.size) return@forEachIndexed
        // A span crossing a STAR track needs no help — the star pass below already hands it the leftover.
        // Growing a content-sized track here instead would *steal* that leftover, which is what a greedy
        // spanning child (a shape, a raster image) would otherwise do to every star column.
        for (t in start until minOf(start + span, n))
            if (tracks[t].kind == TrackKind.STAR) return@forEachIndexed
        val want = sizes[i]
        val have = extent(resolved, start, span)
        if (want <= have) return@forEachIndexed
        var target = -1
        for (t in start until minOf(start + span, n))
            if (tracks[t].kind == TrackKind.AUTO || tracks[t].kind == TrackKind.FLEXIBLE) target = t
        if (target < 0) return@forEachIndexed
        var grown = resolved[target] + (want - have)
        tracks[target].max?.let { grown = minOf(grown, it.toFloat()) }
        resolved[target] = grown
    }

    if (starWeight > 0f) {
        var used = gap * maxOf(0, n - 1)
        for (t in 0 until n) if (tracks[t].kind != TrackKind.STAR) used += resolved[t]
        val leftover = maxOf(0f, available - used)
        for (t in 0 until n) if (tracks[t].kind == TrackKind.STAR)
            resolved[t] = leftover * tracks[t].value.toFloat() / starWeight
    }

    return resolved
}

/** A child's `gridCell` request; -1 column/row means "flow me". */
private fun VNode.gridCellRequest(): Placement {
    val m = modifiers.firstOrNull { it["type"] == "gridCell" } ?: return Placement(-1, -1, 1, 1)
    return Placement(
        (numOf(m["column"]) ?: -1.0).toInt(),
        (numOf(m["row"]) ?: -1.0).toInt(),
        maxOf(1, (numOf(m["columnSpan"]) ?: 1.0).toInt()),
        maxOf(1, (numOf(m["rowSpan"]) ?: 1.0).toInt()),
    )
}

@Composable
private fun GridNode(node: VNode) {
    val colTracks = parseTracks(node.s("columnTracks").ifEmpty { null }, (node.n("columns") ?: 2.0).toInt())
    val (placements, rowCount) = placeGrid(colTracks.size, node.children.map { it.gridCellRequest() })
    val rowSpec = node.s("rowTracks").ifEmpty { null }?.let { parseTracks(it, rowCount) }
    // Rows default to AUTO — a grid should be as tall as it needs, not as tall as it is offered.
    val rowTracks = List(maxOf(1, rowCount)) { rowSpec?.getOrNull(it) ?: Track(TrackKind.AUTO, 0.0) }

    val density = LocalDensity.current
    val colGapPx = with(density) { (node.n("columnSpacing") ?: node.n("spacing") ?: 8.0).dp.toPx() }
    val rowGapPx = with(density) { (node.n("rowSpacing") ?: node.n("spacing") ?: 8.0).dp.toPx() }
    val align = boxAlignmentFor(node.props["alignment"] as? String)

    // Fixed/Flexible track sizes are declared in DSL points, so convert them to pixels before sizing.
    fun toPx(t: Track) = when (t.kind) {
        TrackKind.FIXED -> Track(t.kind, with(density) { t.value.dp.toPx() }.toDouble())
        TrackKind.FLEXIBLE -> Track(t.kind, with(density) { t.value.dp.toPx() }.toDouble(),
            t.max?.let { with(density) { it.dp.toPx() }.toDouble() })
        else -> t
    }
    val cols = colTracks.map(::toPx)
    val rows = rowTracks.map(::toPx)

    Layout(content = { node.children.forEach { NodeView(it) } }) { measurables, constraints ->
        val colSpans = placements.map { it.column to it.columnSpan }
        val rowSpans = placements.map { it.row to it.rowSpan }

        // Pass 1: natural sizes drive the content-sized columns.
        val natural = measurables.map { it.measure(Constraints()) }
        val availableW = if (constraints.hasBoundedWidth) constraints.maxWidth.toFloat()
                         else natural.sumOf { it.width }.toFloat()
        val colW = resolveTracks(cols, availableW, colGapPx, colSpans, natural.map { it.width.toFloat() })

        fun extent(sizes: FloatArray, start: Int, count: Int, gap: Float): Float {
            var total = 0f
            for (i in start until minOf(start + count, sizes.size)) total += sizes[i]
            return total + gap * maxOf(0, minOf(count, sizes.size - start) - 1)
        }

        // Pass 2: re-measure inside the resolved cell so wrapping text reports its real height.
        val cells = measurables.mapIndexed { i, m ->
            val w = extent(colW, placements[i].column, placements[i].columnSpan, colGapPx).toInt()
            m.measure(Constraints(maxWidth = maxOf(0, w)))
        }
        val availableH = if (constraints.hasBoundedHeight) constraints.maxHeight.toFloat() else Float.MAX_VALUE
        val rowH = resolveTracks(rows, availableH, rowGapPx, rowSpans, cells.map { it.height.toFloat() })

        val totalW = (colW.sum() + colGapPx * maxOf(0, colW.size - 1)).toInt()
        val totalH = (rowH.sum() + rowGapPx * maxOf(0, rowH.size - 1)).toInt()

        layout(constraints.constrainWidth(totalW), constraints.constrainHeight(totalH)) {
            cells.forEachIndexed { i, placeable ->
                val p = placements[i]
                val cellX = extent(colW, 0, p.column, colGapPx) + if (p.column > 0) colGapPx else 0f
                val cellY = extent(rowH, 0, p.row, rowGapPx) + if (p.row > 0) rowGapPx else 0f
                val cellW = extent(colW, p.column, p.columnSpan, colGapPx)
                val cellH = extent(rowH, p.row, p.rowSpan, rowGapPx)
                val offset = align.align(
                    IntSize(placeable.width, placeable.height),
                    IntSize(cellW.toInt(), cellH.toInt()),
                    layoutDirection)
                placeable.place(cellX.toInt() + offset.x, cellY.toInt() + offset.y)
            }
        }
    }
}

/** A child's `layoutBounds`, already converted from DSL points to pixels where it isn't proportional. */
private data class AbsBounds(
    val x: Float, val y: Float,
    val width: Float?, val height: Float?,
    val flags: String,
) {
    val xProportional get() = flags.contains('x')
    val yProportional get() = flags.contains('y')
    val widthProportional get() = flags.contains('w')
    val heightProportional get() = flags.contains('h')
}

@Composable
private fun AbsoluteLayoutNode(node: VNode) {
    val density = LocalDensity.current
    val bounds = node.children.map { child ->
        val m = child.modifiers.firstOrNull { it["type"] == "layoutBounds" }
        if (m == null) null
        else {
            val flags = m["flags"] as? String ?: ""
            fun conv(v: Double?, proportional: Boolean) =
                v?.let { if (proportional) it.toFloat() else with(density) { it.dp.toPx() } }
            AbsBounds(
                conv(numOf(m["x"]) ?: 0.0, flags.contains('x')) ?: 0f,
                conv(numOf(m["y"]) ?: 0.0, flags.contains('y')) ?: 0f,
                conv(numOf(m["width"]), flags.contains('w')),
                conv(numOf(m["height"]), flags.contains('h')),
                flags)
        }
    }

    Layout(content = { node.children.forEach { NodeView(it) } }) { measurables, constraints ->
        // A canvas claims what it is offered — the fractions need something to be a fraction of. When an
        // axis is unbounded, fall back to the far edge of the point-placed children.
        val hostW = if (constraints.hasBoundedWidth) constraints.maxWidth else 0
        val hostH = if (constraints.hasBoundedHeight) constraints.maxHeight else 0

        val placeables = measurables.mapIndexed { i, m ->
            val b = bounds.getOrNull(i)
            val w = b?.width?.let { if (b.widthProportional) it * hostW else it }?.toInt()
            val h = b?.height?.let { if (b.heightProportional) it * hostH else it }?.toInt()
            m.measure(Constraints(
                minWidth = w ?: 0, maxWidth = w ?: Constraints.Infinity,
                minHeight = h ?: 0, maxHeight = h ?: Constraints.Infinity))
        }

        var extentW = 0
        var extentH = 0
        placeables.forEachIndexed { i, p ->
            val b = bounds.getOrNull(i)
            if (b == null || (!b.xProportional && !b.widthProportional)) extentW = maxOf(extentW, ((b?.x ?: 0f).toInt()) + p.width)
            if (b == null || (!b.yProportional && !b.heightProportional)) extentH = maxOf(extentH, ((b?.y ?: 0f).toInt()) + p.height)
        }
        val width = if (constraints.hasBoundedWidth) constraints.maxWidth else extentW
        val height = if (constraints.hasBoundedHeight) constraints.maxHeight else extentH

        layout(width, height) {
            placeables.forEachIndexed { i, p ->
                // No declared bounds: park it at the origin, so a forgotten .LayoutBounds shows up
                // rather than vanishing.
                val b = bounds.getOrNull(i) ?: run { p.place(0, 0); return@forEachIndexed }
                // A proportional position is an anchor across the free space: 0 flush leading, 1 flush
                // trailing, 0.5 centred — the same rule AbsoluteLayoutBounds.Resolve applies in C#.
                val x = if (b.xProportional) (width - p.width) * b.x else b.x
                val y = if (b.yProportional) (height - p.height) * b.y else b.y
                p.place(x.toInt(), y.toInt())
            }
        }
    }
}

@Composable
private fun ListNode(node: VNode) {
    Card(Modifier.fillMaxWidth()) {
        Column {
            node.children.forEachIndexed { i, child ->
                Box(Modifier.padding(horizontal = 16.dp, vertical = 12.dp)) { NodeView(child) }
                if (i < node.children.size - 1) HorizontalDivider()
            }
        }
    }
}

@Composable
private fun DisclosureGroupNode(node: VNode) {
    Column(Modifier.fillMaxWidth()) {
        Row(
            Modifier.fillMaxWidth().clickable { SwiftDotNetBridge.emit(node.id, (!node.b("expanded")).toString()) },
            horizontalArrangement = Arrangement.SpaceBetween,
            verticalAlignment = Alignment.CenterVertically,
        ) {
            Text(node.s("label"))
            Text(if (node.b("expanded")) "▾" else "▸")
        }
        AnimatedVisibility(visible = node.b("expanded")) {
            Column(Modifier.padding(start = 12.dp), verticalArrangement = Arrangement.spacedBy(4.dp)) {
                node.children.forEach { NodeView(it) }
            }
        }
    }
}

@Composable
private fun TabViewNode(node: VNode) {
    if (node.s("style") == "page") {
        val pages = node.children
        val state = rememberPagerState(pageCount = { pages.size })
        Column(Modifier.fillMaxSize()) {
            HorizontalPager(state = state, modifier = Modifier.weight(1f)) { p ->
                pages.getOrNull(p)?.let { NodeView(it) }
            }
            Row(Modifier.fillMaxWidth().padding(12.dp), horizontalArrangement = Arrangement.Center) {
                repeat(pages.size) { i ->
                    Box(Modifier.padding(4.dp).size(8.dp).clip(CircleShape)
                        .background(if (i == state.currentPage) colorFor("blue")!! else colorFor("secondary")!!))
                }
            }
        }
    } else {
        var selected by remember { mutableIntStateOf(0) }
        val tabs = node.children
        Scaffold(bottomBar = {
            NavigationBar {
                tabs.forEachIndexed { i, tab ->
                    NavigationBarItem(
                        selected = selected == i,
                        onClick = { selected = i },
                        icon = { Text(emojiFor(tab.s("systemImage"))) },
                        label = { Text(tab.s("title")) },
                    )
                }
            }
        }) { pad ->
            Box(Modifier.padding(pad).fillMaxSize()) {
                tabs.getOrNull(selected)?.children?.firstOrNull()?.let { NodeView(it) }
            }
        }
    }
}

@Composable
private fun MenuNode(node: VNode) {
    var expanded by remember { mutableStateOf(false) }
    Box {
        TextButton(onClick = { expanded = true }) { Text(node.s("label")) }
        DropdownMenu(expanded = expanded, onDismissRequest = { expanded = false }) {
            node.children.forEach { child ->
                DropdownMenuItem(
                    text = { Text(child.s("title")) },
                    onClick = { expanded = false; SwiftDotNetBridge.emit(child.id, null) },
                )
            }
        }
    }
}

// MARK: - Controls -----------------------------------------------------------

@Composable
private fun FieldNode(node: VNode, secure: Boolean) {
    // F9: map keyboard type + return key + max length from props.
    val keyboardType = when (node.s("keyboard")) {
        "number" -> androidx.compose.ui.text.input.KeyboardType.Number
        "decimal" -> androidx.compose.ui.text.input.KeyboardType.Decimal
        "email" -> androidx.compose.ui.text.input.KeyboardType.Email
        "phone" -> androidx.compose.ui.text.input.KeyboardType.Phone
        "url" -> androidx.compose.ui.text.input.KeyboardType.Uri
        else -> androidx.compose.ui.text.input.KeyboardType.Text
    }
    val imeAction = when (node.s("returnKey")) {
        "done" -> androidx.compose.ui.text.input.ImeAction.Done
        "go" -> androidx.compose.ui.text.input.ImeAction.Go
        "next" -> androidx.compose.ui.text.input.ImeAction.Next
        "search" -> androidx.compose.ui.text.input.ImeAction.Search
        "send" -> androidx.compose.ui.text.input.ImeAction.Send
        else -> androidx.compose.ui.text.input.ImeAction.Default
    }
    val maxLen = node.n("maxLength")?.toInt()
    OutlinedTextField(
        value = node.s("text"),
        onValueChange = { v -> SwiftDotNetBridge.emit(node.id, if (maxLen != null && v.length > maxLen) v.substring(0, maxLen) else v) },
        placeholder = { Text(node.s("placeholder")) },
        singleLine = true,
        keyboardOptions = androidx.compose.foundation.text.KeyboardOptions(keyboardType = keyboardType, imeAction = imeAction),
        visualTransformation = if (secure) PasswordVisualTransformation() else VisualTransformation.None,
        modifier = Modifier.fillMaxWidth(),
    )
}

@Composable
private fun StepperNode(node: VNode) {
    val value = (node.n("value") ?: 0.0).toInt()
    val min = (node.n("min") ?: Int.MIN_VALUE.toDouble()).toInt()
    val max = (node.n("max") ?: Int.MAX_VALUE.toDouble()).toInt()
    Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.SpaceBetween, verticalAlignment = Alignment.CenterVertically) {
        Text("${node.s("label")} $value")
        Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
            OutlinedButton(onClick = { if (value > min) SwiftDotNetBridge.emit(node.id, (value - 1).toString()) }) { Text("−") }
            OutlinedButton(onClick = { if (value < max) SwiftDotNetBridge.emit(node.id, (value + 1).toString()) }) { Text("+") }
        }
    }
}

@Composable
private fun PickerNode(node: VNode) {
    var expanded by remember { mutableStateOf(false) }
    val selected = (node.n("selection") ?: 0.0).toInt()
    val options = node.children.map { it.s("text") }
    Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.SpaceBetween, verticalAlignment = Alignment.CenterVertically) {
        Text(node.s("label"))
        Box {
            TextButton(onClick = { expanded = true }) { Text(options.getOrNull(selected) ?: "") }
            DropdownMenu(expanded = expanded, onDismissRequest = { expanded = false }) {
                options.forEachIndexed { i, opt ->
                    DropdownMenuItem(text = { Text(opt) }, onClick = { expanded = false; SwiftDotNetBridge.emit(node.id, i.toString()) })
                }
            }
        }
    }
}

@Composable
private fun DatePickerNode(node: VNode) {
    var open by remember { mutableStateOf(false) }
    val seconds = (node.n("value") ?: 0.0)
    val millis = (seconds * 1000).toLong()
    val label = java.text.SimpleDateFormat("MMM d, yyyy", java.util.Locale.getDefault()).format(java.util.Date(millis))
    Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.SpaceBetween, verticalAlignment = Alignment.CenterVertically) {
        Text(node.s("label"))
        TextButton(onClick = { open = true }) { Text(label) }
    }
    if (open) {
        val state = rememberDatePickerState(initialSelectedDateMillis = millis)
        DatePickerDialog(
            onDismissRequest = { open = false },
            confirmButton = {
                TextButton(onClick = {
                    open = false
                    state.selectedDateMillis?.let { SwiftDotNetBridge.emit(node.id, (it / 1000).toString()) }
                }) { Text("OK") }
            },
        ) { DatePicker(state = state) }
    }
}

private val ColorCycle = listOf("#FF3B30", "#34C759", "#007AFF", "#FF9500", "#AF52DE")

@Composable
private fun ColorPickerNode(node: VNode) {
    val current = colorFor(node.s("value")) ?: colorFor("accentColor")!!
    Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.SpaceBetween, verticalAlignment = Alignment.CenterVertically) {
        Text(node.s("label"))
        Box(Modifier.size(32.dp).clip(CircleShape).background(current).clickable {
            val idx = ColorCycle.indexOf(node.s("value").uppercase()).let { if (it < 0) 0 else it }
            SwiftDotNetBridge.emit(node.id, ColorCycle[(idx + 1) % ColorCycle.size])
        })
    }
}

// MARK: - Grouped sections ---------------------------------------------------

/**
 * A `Section` as the inset-grouped card SwiftUI draws inside a `Form`/`List`: a muted header above a
 * rounded surface holding the rows, hairline-separated. Material has no single "grouped list" widget,
 * so this is assembled from Surface + HorizontalDivider to match the other backends' shape.
 */
@Composable
private fun SectionNode(node: VNode) {
    Column {
        (node.props["header"] as? String)?.let {
            Text(
                it,
                style = MaterialTheme.typography.labelLarge,
                color = MaterialTheme.colorScheme.onSurfaceVariant,
                modifier = Modifier.padding(start = 32.dp, end = 16.dp, bottom = 8.dp),
            )
        }
        Surface(
            modifier = Modifier.fillMaxWidth().padding(horizontal = 16.dp),
            shape = RoundedCornerShape(16.dp),
            color = MaterialTheme.colorScheme.surfaceContainerLowest,
        ) {
            Column {
                node.children.forEachIndexed { i, child ->
                    // Separators start past the icon slot, the way inset-grouped lists inset them.
                    if (i > 0) HorizontalDivider(
                        Modifier.padding(start = 58.dp),
                        color = MaterialTheme.colorScheme.outlineVariant,
                    )
                    NodeView(child)
                }
            }
        }
    }
}

// MARK: - Navigation & presentation -----------------------------------------

private class NavStack(root: VNode) {
    val screens = mutableStateListOf(root)
    fun push(v: VNode) = screens.add(v)
    fun pop() { if (screens.size > 1) screens.removeAt(screens.size - 1) }
}

private val LocalNavStack = compositionLocalOf<NavStack?> { null }

@Composable
private fun NavigationStackNode(node: VNode) {
    val rootScreen = node.children.firstOrNull() ?: return
    val stack = remember(node.id) { NavStack(rootScreen) }
    CompositionLocalProvider(LocalNavStack provides stack) {
        val current = stack.screens.last()
        Scaffold(topBar = {
            TopAppBar(
                title = { Text(titleOf(current) ?: "") },
                navigationIcon = {
                    if (stack.screens.size > 1) IconButton(onClick = { stack.pop() }) { Text("‹", fontSize = 28.sp) }
                },
            )
        }) { pad -> Box(Modifier.padding(pad).fillMaxSize()) { NodeView(current) } }
    }
}

@Composable
private fun NavigationLinkNode(node: VNode) {
    val nav = LocalNavStack.current
    val label = node.children.getOrNull(0)
    val destination = node.children.getOrNull(1)
    Row(
        Modifier.fillMaxWidth()
            .clickable { destination?.let { nav?.push(it) } }
            .heightIn(min = 52.dp)
            .padding(horizontal = 16.dp, vertical = 8.dp),
        horizontalArrangement = Arrangement.SpaceBetween,
        verticalAlignment = Alignment.CenterVertically,
    ) {
        label?.let { NodeView(it) }
        Text("›", fontSize = 20.sp, color = MaterialTheme.colorScheme.outline)
    }
}

@Composable
private fun SheetNode(node: VNode) {
    node.children.getOrNull(0)?.let { NodeView(it) }
    if (node.b("presented")) {
        val sheetState = rememberModalBottomSheetState()
        ModalBottomSheet(
            onDismissRequest = { SwiftDotNetBridge.emit(node.id, "false") },
            sheetState = sheetState,
        ) { node.children.getOrNull(1)?.let { NodeView(it) } }
    }
}

/** One parsed entry of a dialog's flat `buttons` prop. */
private data class DialogButtonSpec(val label: String, val role: String)

/**
 * Mirrors `DialogButtons.Parse` in Core: `label,role;label,role`, with `\` escaping the delimiters and
 * itself. Malformed entries are skipped; an empty string yields a single "OK" so the dialog is always
 * dismissable.
 */
private fun parseDialogButtons(encoded: String): List<DialogButtonSpec> {
    val parsed = mutableListOf<DialogButtonSpec>()
    val label = StringBuilder()
    val role = StringBuilder()
    var inRole = false
    var escaped = false

    fun flush() {
        if (label.isNotEmpty() || role.isNotEmpty()) parsed.add(DialogButtonSpec(label.toString(), role.toString()))
        label.clear()
        role.clear()
        inRole = false
    }

    for (c in encoded) {
        if (escaped) {
            (if (inRole) role else label).append(c)
            escaped = false
            continue
        }
        when {
            c == '\\' -> escaped = true
            c == ',' && !inRole -> inRole = true
            c == ';' -> flush()
            else -> (if (inRole) role else label).append(c)
        }
    }
    flush()

    return parsed.ifEmpty { listOf(DialogButtonSpec("OK", "cancel")) }
}

@Composable
private fun DialogTextButton(node: VNode, index: Int, button: DialogButtonSpec, modifier: Modifier = Modifier) {
    TextButton(onClick = { SwiftDotNetBridge.emit(node.id, index.toString()) }, modifier = modifier) {
        Text(
            button.label,
            color = if (button.role == "destructive") MaterialTheme.colorScheme.error else Color.Unspecified,
        )
    }
}

@Composable
private fun AlertNode(node: VNode) {
    node.children.getOrNull(0)?.let { NodeView(it) }
    if (node.b("presented")) {
        val buttons = parseDialogButtons(node.s("buttons"))
        // Material3 has two button slots. Up to two buttons map onto them (cancel takes the dismiss
        // slot, which is also what the system back gesture reads as); beyond that they stack inside the
        // confirm slot — Material's own overflow shape for a button row that no longer fits.
        val stacked = buttons.size > 2
        val cancel = buttons.indexOfFirst { it.role == "cancel" }
        val dismiss = if (stacked || buttons.size < 2) -1 else if (cancel >= 0) cancel else 0
        val confirm = if (stacked) -1 else buttons.indices.firstOrNull { it != dismiss } ?: -1

        AlertDialog(
            onDismissRequest = { SwiftDotNetBridge.emit(node.id, "false") },
            confirmButton = {
                if (stacked) {
                    Column(horizontalAlignment = Alignment.End) {
                        buttons.forEachIndexed { i, b -> DialogTextButton(node, i, b) }
                    }
                } else if (confirm >= 0) {
                    DialogTextButton(node, confirm, buttons[confirm])
                }
            },
            dismissButton = if (dismiss >= 0) {
                { DialogTextButton(node, dismiss, buttons[dismiss]) }
            } else null,
            title = { Text(node.s("title")) },
            text = { Text(node.s("message")) },
        )
    }
}

/**
 * An action sheet is a modal bottom sheet on Android — the Material equivalent of iOS's action sheet,
 * and the shape a long option list needs. Options are full-width rows; the cancel-role button is kept in
 * the list rather than detached, because Material sheets dismiss by swipe and don't use a cancel row.
 */
@Composable
private fun ActionSheetNode(node: VNode) {
    node.children.getOrNull(0)?.let { NodeView(it) }
    if (node.b("presented")) {
        val sheetState = rememberModalBottomSheetState()
        ModalBottomSheet(
            onDismissRequest = { SwiftDotNetBridge.emit(node.id, "false") },
            sheetState = sheetState,
        ) {
            Column(Modifier.padding(bottom = 24.dp)) {
                if (node.s("title").isNotEmpty()) {
                    Text(
                        node.s("title"),
                        style = MaterialTheme.typography.titleMedium,
                        modifier = Modifier.padding(horizontal = 24.dp, vertical = 8.dp),
                    )
                }
                if (node.s("message").isNotEmpty()) {
                    Text(
                        node.s("message"),
                        style = MaterialTheme.typography.bodyMedium,
                        modifier = Modifier.padding(horizontal = 24.dp).padding(bottom = 8.dp),
                    )
                }
                parseDialogButtons(node.s("buttons")).forEachIndexed { i, b ->
                    Text(
                        b.label,
                        color = if (b.role == "destructive") MaterialTheme.colorScheme.error else Color.Unspecified,
                        modifier = Modifier
                            .fillMaxWidth()
                            .clickable { SwiftDotNetBridge.emit(node.id, i.toString()) }
                            .padding(horizontal = 24.dp, vertical = 14.dp),
                    )
                }
            }
        }
    }
}

// MARK: - Display ------------------------------------------------------------

@Composable
private fun ProgressNode(node: VNode) {
    val value = node.n("value")
    Column(horizontalAlignment = Alignment.CenterHorizontally, verticalArrangement = Arrangement.spacedBy(4.dp)) {
        (node.props["label"] as? String)?.let { Text(it, style = textStyleFor("caption")!!) }
        if (value != null) LinearProgressIndicator(progress = { value.toFloat() }, modifier = Modifier.fillMaxWidth())
        else CircularProgressIndicator()
    }
}

private fun gaugeFraction(node: VNode): Float {
    val v = node.n("value") ?: 0.0
    val lo = node.n("min") ?: 0.0
    val hi = node.n("max") ?: 1.0
    return if (hi > lo) ((v - lo) / (hi - lo)).toFloat().coerceIn(0f, 1f) else 0f
}

@Composable
private fun ShapeBox(node: VNode, shape: androidx.compose.ui.graphics.Shape) {
    val fill = modColor(node, "foregroundColor") ?: modColor(node, "background") ?: colorFor("secondary")!!
    Box(Modifier.clip(shape).background(fill).fillMaxSize())
}
