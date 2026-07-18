@file:OptIn(ExperimentalMaterial3Api::class, ExperimentalFoundationApi::class)

package com.swiftdotnet.bridge

import android.content.Context
import android.view.View
import androidx.compose.animation.AnimatedVisibility
import androidx.compose.foundation.ExperimentalFoundationApi
import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.clickable
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
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.RectangleShape
import androidx.compose.ui.platform.ComposeView
import androidx.compose.ui.platform.LocalUriHandler
import androidx.compose.ui.text.TextStyle
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.input.PasswordVisualTransformation
import androidx.compose.ui.text.input.VisualTransformation
import androidx.compose.ui.text.style.TextDecoration
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
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
            "background" -> colorFor(mod["value"] as? String)?.let { m = m.background(it) }
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
            "opacity" -> m = m.alpha((numOf(mod["amount"]) ?: 1.0).toFloat())
            "disabled" -> if ((mod["value"] as? String) == "true") {
                // Dim + swallow all pointer input for the subtree (Compose has no generic `.disabled()`).
                m = m.alpha(0.5f).pointerInput(Unit) {
                    awaitPointerEventScope { while (true) { awaitPointerEvent().changes.forEach { it.consume() } } }
                }
            }
            "onTapGesture" -> (mod["event"] as? String)?.let { e -> m = m.clickable { SwiftDotNetBridge.emit(e, null) } }
            "font" -> textStyle = textStyleFor(mod["value"] as? String)
            "foregroundColor" -> contentColor = colorFor(mod["value"] as? String)
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

        "Image" -> Text(emojiFor(node.s("system")), fontSize = 22.sp)
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
    OutlinedTextField(
        value = node.s("text"),
        onValueChange = { SwiftDotNetBridge.emit(node.id, it) },
        placeholder = { Text(node.s("placeholder")) },
        singleLine = true,
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
