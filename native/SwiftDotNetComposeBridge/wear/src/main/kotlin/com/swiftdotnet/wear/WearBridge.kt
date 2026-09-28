package com.swiftdotnet.wear

import androidx.activity.compose.BackHandler
import androidx.compose.foundation.background
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.pager.VerticalPager
import androidx.compose.foundation.pager.rememberPagerState
import androidx.compose.material3.darkColorScheme
import androidx.compose.runtime.Composable
import androidx.compose.runtime.CompositionLocalProvider
import androidx.compose.runtime.compositionLocalOf
import androidx.compose.runtime.mutableStateListOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.saveable.rememberSaveableStateHolder
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.unit.dp
import androidx.wear.compose.foundation.lazy.ScalingLazyColumn
import androidx.wear.compose.foundation.lazy.ScalingLazyListScope
import androidx.wear.compose.foundation.lazy.rememberScalingLazyListState
import androidx.wear.compose.material.Button
import androidx.wear.compose.material.ButtonDefaults
import androidx.wear.compose.material.Chip
import androidx.wear.compose.material.ChipDefaults
import androidx.wear.compose.material.CircularProgressIndicator
import androidx.wear.compose.material.Icon
import androidx.wear.compose.material.InlineSlider
import androidx.wear.compose.material.InlineSliderDefaults
import androidx.wear.compose.material.ListHeader
import androidx.wear.compose.material.MaterialTheme
import androidx.wear.compose.material.PositionIndicator
import androidx.wear.compose.material.Scaffold
import androidx.wear.compose.material.Switch
import androidx.wear.compose.material.Text
import androidx.wear.compose.material.TimeText
import androidx.wear.compose.material.ToggleChip
import com.swiftdotnet.bridge.LocalRootAlignment
import com.swiftdotnet.bridge.RenderNode
import com.swiftdotnet.bridge.SwiftDotNetBridge
import com.swiftdotnet.bridge.VNode
import com.swiftdotnet.bridge.overrideRenderer
import com.swiftdotnet.bridge.setRootContainer
import com.swiftdotnet.bridge.systemImageGlyph
import androidx.compose.material3.LocalContentColor as M3LocalContentColor
import androidx.compose.material3.MaterialTheme as M3MaterialTheme

/**
 * Compose for Wear OS skin over the SwiftDotNet interpreter.
 *
 * The phone interpreter already renders every node type; on a round 1.3" screen a handful of them are the
 * wrong *shape* (a Material 3 card list, a top app bar, a bottom navigation bar, dropdowns). This module
 * overrides exactly those, through the main bridge's form-factor seam, with their Wear equivalents:
 *
 * | Node | Wear OS |
 * |---|---|
 * | `List` / `Form` / vertical `ScrollView` | `ScalingLazyColumn` (+ rotary scroll, position indicator) |
 * | `Section` (inside a list) | `ListHeader` + its rows, flattened into the column |
 * | `Button` | `Chip` (a list row) |
 * | `Toggle` | `ToggleChip` + `Switch` |
 * | `NavigationStack` / `NavigationLink` | own back stack, system back / swipe-to-dismiss pops, `Chip` row |
 * | `TabView` | `VerticalPager` (watchOS 10 vertical paging) |
 * | `Slider` / `Stepper` | `InlineSlider` / round −/+ buttons |
 * | `Gauge` | `CircularProgressIndicator` |
 *
 * Everything else — text, stacks, shapes, images, modifiers including `.DigitalCrownRotation` — is the
 * phone interpreter unchanged, drawn in a dark Material 3 theme so it reads correctly on a black face.
 * The root is wrapped in a Wear `Scaffold` with `TimeText`.
 */
object SwiftDotNetWear {
    private var installed = false

    /** Install the Wear overrides. Idempotent; call before the first render (the managed host does). */
    @JvmStatic
    fun install() {
        if (installed) return
        installed = true

        setRootContainer { content -> WearRoot(content) }
        overrideRenderer("List") { WearList(it) }
        overrideRenderer("Form") { WearList(it) }
        overrideRenderer("ScrollView") { node ->
            if (node.props["axis"] == "horizontal") WearHorizontalScroll(node) else WearList(node)
        }
        overrideRenderer("Section") { WearSectionStandalone(it) }
        overrideRenderer("Button") { WearButton(it) }
        overrideRenderer("Toggle") { WearToggle(it) }
        overrideRenderer("NavigationStack") { WearNavigationStack(it) }
        overrideRenderer("NavigationLink") { WearNavigationLink(it) }
        overrideRenderer("TabView") { WearPager(it) }
        overrideRenderer("Slider") { WearSlider(it) }
        overrideRenderer("Stepper") { WearStepper(it) }
        overrideRenderer("Gauge") { WearGauge(it) }
    }
}

// MARK: - Helpers -------------------------------------------------------------

private fun VNode.s(key: String): String = props[key]?.toString() ?: ""
private fun VNode.n(key: String): Double? = (props[key] as? Number)?.toDouble()
private fun VNode.b(key: String): Boolean = props[key] as? Boolean ?: false

private fun titleOf(node: VNode): String? =
    node.modifiers.firstOrNull { it["type"] == "navigationTitle" }?.get("value") as? String

/** Node types that lay themselves out as a scrolling column — they render the screen title themselves. */
private val ListLike = setOf("List", "Form", "ScrollView")

private fun isListLike(node: VNode) =
    node.type in ListLike && node.props["axis"] != "horizontal"

/** The id of the node being rendered as a direct list row, so a row-level control can fill the width. */
private val LocalRowId = compositionLocalOf<String?> { null }

/** The current screen's title, consumed by the first list that renders it (as its header). */
private val LocalScreenTitle = compositionLocalOf<String?> { null }

// MARK: - Root ----------------------------------------------------------------

@Composable
private fun WearRoot(content: @Composable () -> Unit) {
    MaterialTheme {
        // Nodes the phone interpreter still draws use Material 3; give them a dark scheme and a light
        // content color so they read on a black watch face instead of defaulting to black-on-black.
        M3MaterialTheme(colorScheme = darkColorScheme()) {
            CompositionLocalProvider(
                M3LocalContentColor provides Color.White,
                LocalRootAlignment provides Alignment.Center,
            ) {
                Scaffold(
                    modifier = Modifier.fillMaxSize().background(Color.Black),
                    timeText = { TimeText() },
                ) { content() }
            }
        }
    }
}

// MARK: - Lists ---------------------------------------------------------------

@Composable
private fun WearList(node: VNode) {
    val state = rememberScalingLazyListState()
    val title = LocalScreenTitle.current
    Scaffold(positionIndicator = { PositionIndicator(scalingLazyListState = state) }) {
        CompositionLocalProvider(LocalScreenTitle provides null) {
            ScalingLazyColumn(
                modifier = Modifier.fillMaxSize(),
                state = state,
                contentPadding = PaddingValues(horizontal = 10.dp, vertical = 24.dp),
                verticalArrangement = Arrangement.spacedBy(4.dp),
            ) {
                if (title != null) item(key = "\$title") { ListHeader { Text(title) } }
                rows(node.children)
            }
        }
    }
}

/** Each child is one row; a `Section` flattens into a header plus its own rows. */
private fun ScalingLazyListScope.rows(children: List<VNode>) {
    for (child in children) {
        if (child.type == "Section") {
            val header = child.props["header"] as? String
            if (header != null) item(key = child.id + "\$h") { ListHeader { Text(header) } }
            rows(child.children)
        } else {
            item(key = child.id) { ListRow(child) }
        }
    }
}

@Composable
private fun ListRow(child: VNode) {
    CompositionLocalProvider(LocalRowId provides child.id) {
        Box(Modifier.fillMaxWidth(), contentAlignment = Alignment.Center) { RenderNode(child) }
    }
}

/** A `Section` outside a list: header over its rows, no scrolling of its own. */
@Composable
private fun WearSectionStandalone(node: VNode) {
    Column(Modifier.fillMaxWidth(), verticalArrangement = Arrangement.spacedBy(4.dp)) {
        (node.props["header"] as? String)?.let { ListHeader { Text(it) } }
        node.children.forEach { RenderNode(it) }
    }
}

@Composable
private fun WearHorizontalScroll(node: VNode) {
    androidx.compose.foundation.lazy.LazyRow(horizontalArrangement = Arrangement.spacedBy(6.dp)) {
        items(node.children.size) { RenderNode(node.children[it]) }
    }
}

// MARK: - Controls ------------------------------------------------------------

@Composable
private fun WearButton(node: VNode) {
    val title = node.s("title")
    val onClick = { SwiftDotNetBridge.emit(node.id, null) }
    if (LocalRowId.current == node.id) {
        Chip(
            onClick = onClick,
            label = { Text(title, maxLines = 2) },
            modifier = Modifier.fillMaxWidth(),
            colors = ChipDefaults.primaryChipColors(),
        )
    } else if (title.length <= 2) {
        // "+", "−", "▶" — a glyph button, drawn as Wear's round button (watchOS draws these as capsules).
        Button(onClick = onClick, modifier = Modifier.size(ButtonDefaults.SmallButtonSize)) { Text(title) }
    } else {
        Chip(onClick = onClick, label = { Text(title, maxLines = 1) }, colors = ChipDefaults.primaryChipColors())
    }
}

@Composable
private fun WearToggle(node: VNode) {
    val checked = node.b("value")
    ToggleChip(
        checked = checked,
        onCheckedChange = { SwiftDotNetBridge.emit(node.id, it.toString()) },
        label = { Text(node.s("label"), maxLines = 2) },
        toggleControl = { Switch(checked = checked) },
        modifier = Modifier.fillMaxWidth(),
    )
}

@Composable
private fun WearSlider(node: VNode) {
    val min = (node.n("min") ?: 0.0).toFloat()
    val max = (node.n("max") ?: 1.0).toFloat()
    val value = (node.n("value") ?: 0.0).toFloat()
    // Ten detents across the range: an InlineSlider is stepped by design (there is no free drag on Wear).
    InlineSlider(
        value = value,
        onValueChange = { SwiftDotNetBridge.emit(node.id, it.toString()) },
        increaseIcon = { Icon(InlineSliderDefaults.Increase, contentDescription = "Increase") },
        decreaseIcon = { Icon(InlineSliderDefaults.Decrease, contentDescription = "Decrease") },
        valueRange = min..max,
        steps = 9,
        segmented = false,
        modifier = Modifier.fillMaxWidth(),
    )
}

@Composable
private fun WearStepper(node: VNode) {
    val value = (node.n("value") ?: 0.0).toInt()
    val min = (node.n("min") ?: Int.MIN_VALUE.toDouble()).toInt()
    val max = (node.n("max") ?: Int.MAX_VALUE.toDouble()).toInt()
    Column(Modifier.fillMaxWidth(), horizontalAlignment = Alignment.CenterHorizontally) {
        Text(node.s("label"), style = MaterialTheme.typography.caption1)
        Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(12.dp)) {
            Button(
                onClick = { if (value > min) SwiftDotNetBridge.emit(node.id, (value - 1).toString()) },
                modifier = Modifier.size(ButtonDefaults.SmallButtonSize),
            ) { Text("−") }
            Text("$value", style = MaterialTheme.typography.title2)
            Button(
                onClick = { if (value < max) SwiftDotNetBridge.emit(node.id, (value + 1).toString()) },
                modifier = Modifier.size(ButtonDefaults.SmallButtonSize),
            ) { Text("+") }
        }
    }
}

@Composable
private fun WearGauge(node: VNode) {
    val v = node.n("value") ?: 0.0
    val lo = node.n("min") ?: 0.0
    val hi = node.n("max") ?: 1.0
    val fraction = if (hi > lo) ((v - lo) / (hi - lo)).toFloat().coerceIn(0f, 1f) else 0f
    Box(Modifier.size(72.dp), contentAlignment = Alignment.Center) {
        CircularProgressIndicator(progress = fraction, modifier = Modifier.fillMaxSize(), strokeWidth = 6.dp)
        (node.props["label"] as? String)?.let {
            Text(it, style = MaterialTheme.typography.caption2, textAlign = TextAlign.Center)
        }
    }
}

// MARK: - Navigation ----------------------------------------------------------

private class WearNavStack(root: VNode) {
    val screens = mutableStateListOf(root)
    fun push(v: VNode) = screens.add(v)
    fun pop() { if (screens.size > 1) screens.removeAt(screens.size - 1) }
}

private val LocalWearNav = compositionLocalOf<WearNavStack?> { null }

/**
 * A Wear back stack. There is no app bar on a watch: system back (the swipe-right gesture on Wear OS, or
 * the hardware back button) pops, and the screen's `.NavigationTitle` becomes the header of its list —
 * or a caption above it when the screen isn't a list.
 */
@Composable
private fun WearNavigationStack(node: VNode) {
    val rootScreen = node.children.firstOrNull() ?: return
    val stack = remember(node.id) { WearNavStack(rootScreen) }
    // The root screen node is re-created on every C# render that touches it; keep the stack's base current.
    if (stack.screens.first() !== rootScreen) stack.screens[0] = rootScreen
    BackHandler(enabled = stack.screens.size > 1) { stack.pop() }

    val current = stack.screens.last()
    val title = titleOf(current)
    // Every screen renders from this one call site, so without per-screen state a pushed list would
    // inherit the previous list's scroll position. Keying saveable state by screen also restores the
    // parent's scroll position on back.
    val saveable = rememberSaveableStateHolder()
    CompositionLocalProvider(LocalWearNav provides stack) {
        saveable.SaveableStateProvider(current.id) { WearScreen(current, title) }
    }
}

@Composable
private fun WearScreen(screen: VNode, title: String?) {
    if (isListLike(screen)) {
        CompositionLocalProvider(LocalScreenTitle provides title) { RenderNode(screen) }
    } else {
        Column(
            Modifier.fillMaxSize().padding(top = 28.dp, start = 12.dp, end = 12.dp),
            horizontalAlignment = Alignment.CenterHorizontally,
            verticalArrangement = Arrangement.spacedBy(6.dp),
        ) {
            title?.let { Text(it, style = MaterialTheme.typography.caption1, color = MaterialTheme.colors.primary) }
            Box(Modifier.fillMaxWidth().weight(1f), contentAlignment = Alignment.Center) { RenderNode(screen) }
        }
    }
}

@Composable
private fun WearNavigationLink(node: VNode) {
    val nav = LocalWearNav.current
    val label = node.children.getOrNull(0)
    val destination = node.children.getOrNull(1)
    Chip(
        onClick = { destination?.let { nav?.push(it) } },
        label = { label?.let { LinkLabel(it) } },
        modifier = Modifier.fillMaxWidth(),
        colors = ChipDefaults.secondaryChipColors(),
    )
}

/** A link's label: plain text and `Label(title, systemImage)` render as chip text; anything else as-is. */
@Composable
private fun LinkLabel(label: VNode) {
    when (label.type) {
        "Text" -> Text(label.s("text"), maxLines = 2)
        "Label" -> Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(8.dp)) {
            Text(systemImageGlyph(label.s("systemImage")))
            Text(label.s("title"), maxLines = 2)
        }
        else -> RenderNode(label)
    }
}

// MARK: - Paging --------------------------------------------------------------

/** watchOS 10 pages vertically; so does this, whatever the `TabView` style. */
@Composable
private fun WearPager(node: VNode) {
    // Tab nodes wrap their content in one child; a `.TabViewStyle(Page)` child is the page itself.
    val pages = node.children.map { if (it.type == "Tab") it.children.firstOrNull() ?: it else it }
    val state = rememberPagerState(pageCount = { pages.size })
    VerticalPager(state = state, modifier = Modifier.fillMaxSize()) { p ->
        Box(Modifier.fillMaxSize(), contentAlignment = Alignment.Center) { pages.getOrNull(p)?.let { RenderNode(it) } }
    }
}
