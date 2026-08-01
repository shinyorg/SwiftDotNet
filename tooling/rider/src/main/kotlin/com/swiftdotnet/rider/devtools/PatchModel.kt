package com.swiftdotnet.rider.devtools

/**
 * One node of the live view tree, rebuilt on the IDE side from the patch stream.
 */
data class InspectorNode(
    val id: String,
    val type: String,
    val props: Map<String, JsonValue>,
    val modifiers: List<Map<String, JsonValue>>,
    val children: List<InspectorNode>,
) {
    /** What the tree view shows: `VStack #0.1` — the type, then the id that events are keyed by. */
    val label: String get() = buildString {
        append(type)
        props["text"]?.let { append(" \"").append(it.display().take(40)).append('"') }
        append("  #").append(id)
    }

    fun find(id: String): InspectorNode? =
        if (this.id == id) this else children.firstNotNullOfOrNull { it.find(id) }

    fun count(): Int = 1 + children.sumOf { it.count() }
}

/**
 * Applies the patch stream to a tree, the same way every backend does.
 *
 * That is the inspector's whole trick: it is not a Skia feature or an iOS feature. `SwiftApp.Render()`
 * hands one JSON patch to `IBridge`, and SwiftUI, Compose, GTK, WinUI, the DOM and Skia all reconstruct
 * a tree from it. Reconstructing the same tree here means the inspector shows what the *backend* sees,
 * on every platform, from one implementation.
 *
 * The three ops are the ones `TreeDiffer` emits:
 *  * `replace` — a whole new root (what a hot reload always produces, by design);
 *  * `updateProps` — props and modifiers of one node;
 *  * `setChildren` — the child list of one node.
 */
class PatchModel {

    var root: InspectorNode? = null
        private set

    var patchCount: Int = 0
        private set

    /** Ops applied by the last patch, for the inspector's activity line. */
    var lastOps: List<String> = emptyList()
        private set

    fun clear() {
        root = null
        patchCount = 0
        lastOps = emptyList()
    }

    /** Apply one patch document. Returns true when the tree changed. */
    fun apply(patchJson: String): Boolean {
        val document = Json.parseOrNull(patchJson) as? JsonValue.JsonObject ?: return false
        val ops = document.array("ops")
        if (ops.isEmpty()) return false

        val applied = mutableListOf<String>()
        for (op in ops) {
            val obj = op as? JsonValue.JsonObject ?: continue
            when (val kind = obj.string("op")) {
                "replace" -> {
                    root = obj.obj("node")?.let(::toNode)
                    applied += "replace"
                }
                "updateProps" -> {
                    val id = obj.string("id") ?: continue
                    root = root?.let { updateProps(it, id, obj) }
                    applied += "updateProps $id"
                }
                "setChildren" -> {
                    val id = obj.string("id") ?: continue
                    val children = obj.array("children").mapNotNull { (it as? JsonValue.JsonObject)?.let(::toNode) }
                    root = root?.let { setChildren(it, id, children) }
                    applied += "setChildren $id (${children.size})"
                }
                else -> applied += kind.orEmpty()
            }
        }

        patchCount++
        lastOps = applied
        return applied.isNotEmpty()
    }

    private fun toNode(obj: JsonValue.JsonObject): InspectorNode = InspectorNode(
        id = obj.string("id").orEmpty(),
        type = obj.string("type").orEmpty(),
        props = (obj.obj("props"))?.members.orEmpty(),
        modifiers = obj.array("modifiers").mapNotNull { (it as? JsonValue.JsonObject)?.members },
        children = obj.array("children").mapNotNull { (it as? JsonValue.JsonObject)?.let(::toNode) },
    )

    private fun updateProps(node: InspectorNode, id: String, op: JsonValue.JsonObject): InspectorNode =
        if (node.id == id) {
            node.copy(
                props = op.obj("props")?.members.orEmpty(),
                modifiers = op.array("modifiers").mapNotNull { (it as? JsonValue.JsonObject)?.members },
            )
        } else {
            node.copy(children = node.children.map { updateProps(it, id, op) })
        }

    private fun setChildren(node: InspectorNode, id: String, children: List<InspectorNode>): InspectorNode =
        if (node.id == id) node.copy(children = children)
        else node.copy(children = node.children.map { setChildren(it, id, children) })
}
