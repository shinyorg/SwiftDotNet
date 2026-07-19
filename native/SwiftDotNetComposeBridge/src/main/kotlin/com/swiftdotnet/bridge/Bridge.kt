@file:OptIn(ExperimentalMaterial3Api::class, ExperimentalFoundationApi::class)

package com.swiftdotnet.bridge

import android.content.Context
import android.view.View
import android.webkit.WebView
import android.webkit.WebViewClient
import androidx.compose.animation.AnimatedVisibility
import androidx.compose.animation.animateContentSize
import androidx.compose.animation.core.FastOutLinearInEasing
import androidx.compose.animation.core.FastOutSlowInEasing
import androidx.compose.animation.core.FiniteAnimationSpec
import androidx.compose.animation.core.LinearEasing
import androidx.compose.animation.core.LinearOutSlowInEasing
import androidx.compose.animation.core.animateFloatAsState
import androidx.compose.animation.core.spring
import androidx.compose.animation.core.tween
import androidx.compose.foundation.ExperimentalFoundationApi
import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.clickable
import androidx.compose.foundation.gestures.detectDragGestures
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
import androidx.compose.ui.graphics.TransformOrigin
import androidx.compose.ui.graphics.graphicsLayer
import androidx.compose.ui.graphics.RectangleShape
import androidx.compose.ui.platform.ComposeView
import androidx.compose.ui.platform.LocalUriHandler
import androidx.compose.ui.viewinterop.AndroidView
import androidx.compose.ui.text.TextStyle
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.input.PasswordVisualTransformation
import androidx.compose.ui.text.input.VisualTransformation
import androidx.compose.ui.text.style.TextDecoration
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import kotlin.math.abs
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

// F3 raster: decode bytes/file into a Bitmap and show it; an SF-Symbol name falls back to an emoji glyph.
// (Remote URLs need an async image loader like Coil, not a bridge dependency — documented gap on Android.)
@Composable
private fun RasterImage(node: VNode) {
    val scale = if (node.s("contentMode") == "fill") ContentScale.Crop else ContentScale.Fit
    val bytesProp = node.s("bytes")
    val fileProp = node.s("file")
    val bitmap = remember(bytesProp, fileProp) {
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
    val animMod = node.modifiers.firstOrNull { (it["type"] as? String) == "animation" }
    var targetAlpha: Float? = null

    for (mod in node.modifiers) {
        when (mod["type"]) {
            "padding" -> m = m.padding(
                start = (numOf(mod["leading"]) ?: 0.0).dp,
                top = (numOf(mod["top"]) ?: 0.0).dp,
                end = (numOf(mod["trailing"]) ?: 0.0).dp,
                bottom = (numOf(mod["bottom"]) ?: 0.0).dp,
            )
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
                // F6: RenderEffect backdrop blur is API-31+ and awkward to wire generically → translucent
                // tint fallback (documented degradation).
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
                    androidx.compose.foundation.gestures.detectTransformGestures { _, _, zoom, _ ->
                        scale *= zoom
                        SwiftDotNetBridge.emit(e, scale.toString())
                    }
                }
            }
            "font" -> textStyle = textStyleFor(mod["value"] as? String)
            "foregroundColor" -> contentColor = colorFor(mod["value"] as? String)
        }
    }

    if (animMod != null) m = m.animateContentSize(animationSpec = animSpec(animMod))
    if (targetAlpha != null) {
        val alpha = if (animMod != null)
            animateFloatAsState(targetValue = targetAlpha!!, animationSpec = animSpec(animMod), label = "alpha").value
        else targetAlpha!!
        m = m.alpha(alpha)
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
    Box(Modifier.fillMaxSize()) {
        if (root != null) NodeView(root)
        else Text("SwiftDotNet: waiting for first render…", Modifier.align(Alignment.Center))
    }
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
        "List" -> ListNode(node)
        "Form" -> Column(Modifier.fillMaxSize().verticalScroll(rememberScrollState()), verticalArrangement = Arrangement.spacedBy(16.dp)) {
            node.children.forEach { NodeView(it) }
        }
        "Section" -> Column(verticalArrangement = Arrangement.spacedBy(6.dp)) {
            (node.props["header"] as? String)?.let { Text(it, style = textStyleFor("headline")!!) }
            node.children.forEach { NodeView(it) }
        }
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

        "WebView" -> WebViewNode(node)
        "Image" -> RasterImage(node)
        "Label" -> Row(horizontalArrangement = Arrangement.spacedBy(6.dp), verticalAlignment = Alignment.CenterVertically) {
            Text(emojiFor(node.s("systemImage"))); Text(node.s("title"))
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

@Composable
private fun GridNode(node: VNode) {
    val cols = (node.n("columns") ?: 2.0).toInt().coerceAtLeast(1)
    val sp = (node.n("spacing") ?: 8.0).dp
    Column(verticalArrangement = Arrangement.spacedBy(sp)) {
        node.children.chunked(cols).forEach { rowItems ->
            Row(horizontalArrangement = Arrangement.spacedBy(sp)) {
                rowItems.forEach { Box(Modifier.weight(1f), contentAlignment = Alignment.Center) { NodeView(it) } }
                repeat(cols - rowItems.size) { Spacer(Modifier.weight(1f)) }
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
        Modifier.fillMaxWidth().clickable { destination?.let { nav?.push(it) } },
        horizontalArrangement = Arrangement.SpaceBetween,
        verticalAlignment = Alignment.CenterVertically,
    ) {
        label?.let { NodeView(it) }
        Text("›")
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

@Composable
private fun AlertNode(node: VNode) {
    node.children.getOrNull(0)?.let { NodeView(it) }
    if (node.b("presented")) {
        AlertDialog(
            onDismissRequest = { SwiftDotNetBridge.emit(node.id, "false") },
            confirmButton = { TextButton(onClick = { SwiftDotNetBridge.emit(node.id, "false") }) { Text("OK") } },
            title = { Text(node.s("title")) },
            text = { Text(node.s("message")) },
        )
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
