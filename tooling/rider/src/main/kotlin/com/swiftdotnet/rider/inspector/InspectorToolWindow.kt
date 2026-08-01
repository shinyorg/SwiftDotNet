package com.swiftdotnet.rider.inspector

import com.intellij.openapi.actionSystem.ActionManager
import com.intellij.openapi.actionSystem.ActionUpdateThread
import com.intellij.openapi.actionSystem.AnAction
import com.intellij.openapi.actionSystem.AnActionEvent
import com.intellij.openapi.actionSystem.DefaultActionGroup
import com.intellij.openapi.application.ApplicationManager
import com.intellij.icons.AllIcons
import com.intellij.openapi.project.DumbAware
import com.intellij.openapi.project.Project
import com.intellij.openapi.ui.Splitter
import com.intellij.openapi.wm.ToolWindow
import com.intellij.openapi.wm.ToolWindowFactory
import com.intellij.ui.components.JBLabel
import com.intellij.ui.components.JBScrollPane
import com.intellij.ui.content.ContentFactory
import com.intellij.ui.table.JBTable
import com.intellij.ui.treeStructure.Tree
import com.swiftdotnet.rider.devtools.DevToolsClient
import com.swiftdotnet.rider.devtools.DevToolsProtocol
import com.swiftdotnet.rider.devtools.InspectorNode
import com.swiftdotnet.rider.devtools.PatchModel
import java.awt.BorderLayout
import javax.swing.JComponent
import javax.swing.JPanel
import javax.swing.event.TreeSelectionListener
import javax.swing.table.DefaultTableModel
import javax.swing.tree.DefaultMutableTreeNode
import javax.swing.tree.DefaultTreeModel
import javax.swing.tree.TreePath

/**
 * A live view of the tree the *backend* sees.
 *
 * The inspector is backend-agnostic by construction rather than by effort: every backend renders from
 * the same patch stream, so reconstructing that stream here shows the same tree whether the app under
 * inspection is drawing with SwiftUI, Compose, GTK, WinUI, the DOM or Skia.
 */
class InspectorToolWindowFactory : ToolWindowFactory, DumbAware {
    override fun createToolWindowContent(project: Project, toolWindow: ToolWindow) {
        val panel = InspectorPanel(project)
        val content = ContentFactory.getInstance().createContent(panel, "", false)
        content.setDisposer(panel)
        toolWindow.contentManager.addContent(content)
    }
}

class InspectorPanel(private val project: Project) : JPanel(BorderLayout()), com.intellij.openapi.Disposable {

    private val model = PatchModel()
    private val rootNode = DefaultMutableTreeNode("Not attached")
    private val treeModel = DefaultTreeModel(rootNode)
    private val tree = Tree(treeModel)

    private val propertyModel = object : DefaultTableModel(arrayOf("Property", "Value"), 0) {
        override fun isCellEditable(row: Int, column: Int) = false
    }
    private val propertyTable = JBTable(propertyModel)

    private val status = JBLabel("Not attached. Run a SwiftDotNet configuration with the dev-tools channel on.")

    private var client: DevToolsClient? = null

    init {
        tree.isRootVisible = true
        tree.addTreeSelectionListener(TreeSelectionListener { showProperties() })

        val splitter = Splitter(true, 0.65f).apply {
            firstComponent = JBScrollPane(tree)
            secondComponent = JBScrollPane(propertyTable)
        }

        add(toolbar(), BorderLayout.NORTH)
        add(splitter, BorderLayout.CENTER)
        add(status, BorderLayout.SOUTH)
    }

    private fun toolbar(): JComponent {
        val group = DefaultActionGroup(
            object : AnAction("Attach", "Attach to the running app's dev-tools port", AllIcons.Actions.Execute), DumbAware {
                override fun getActionUpdateThread() = ActionUpdateThread.BGT
                override fun actionPerformed(e: AnActionEvent) = attachToLastRun()
            },
            object : AnAction("Detach", "Stop following the app", AllIcons.Actions.Suspend), DumbAware {
                override fun getActionUpdateThread() = ActionUpdateThread.BGT
                override fun actionPerformed(e: AnActionEvent) = detach()
            },
            object : AnAction("Clear", "Forget the current tree", AllIcons.Actions.GC), DumbAware {
                override fun getActionUpdateThread() = ActionUpdateThread.BGT
                override fun actionPerformed(e: AnActionEvent) {
                    model.clear()
                    refreshTree()
                }
            },
        )
        return ActionManager.getInstance()
            .createActionToolbar("SwiftDotNetInspector", group, true)
            .also { it.targetComponent = this }
            .component
    }

    /** Attach to the port the most recent SwiftDotNet run configuration reported. */
    fun attachToLastRun() {
        val port = com.swiftdotnet.rider.services.SwiftDotNetSession.getInstance(project).devToolsPort
        if (port <= 0) {
            status.text = "No running SwiftDotNet app with the dev-tools channel enabled."
            return
        }
        attach(port)
    }

    fun attach(port: Int) {
        detach()
        status.text = "Attaching to 127.0.0.1:$port…"

        client = DevToolsClient(
            port = port,
            onFrame = ::onFrame,
            onDisconnect = { error ->
                ApplicationManager.getApplication().invokeLater {
                    status.text = error?.let { "Detached: ${it.message}" } ?: "App disconnected."
                }
            },
        ).also { it.start() }
    }

    fun detach() {
        client?.close()
        client = null
    }

    private fun onFrame(frame: com.swiftdotnet.rider.devtools.DevToolsFrame) {
        when (frame.type) {
            DevToolsProtocol.HELLO ->
                ApplicationManager.getApplication().invokeLater { status.text = "Attached — ${frame.text}" }

            DevToolsProtocol.PATCH -> {
                // The payload is "<sequence>\n<patch json>" — the sequence is what makes a dropped or
                // reordered frame visible rather than silently producing a wrong tree.
                val newline = frame.text.indexOf('\n')
                if (newline < 0) return
                val json = frame.text.substring(newline + 1)
                if (model.apply(json))
                    ApplicationManager.getApplication().invokeLater { refreshTree() }
            }

            DevToolsProtocol.EVENT ->
                ApplicationManager.getApplication().invokeLater {
                    status.text = "event ${frame.text.replace('\t', ' ')}"
                }

            DevToolsProtocol.LOG ->
                ApplicationManager.getApplication().invokeLater { status.text = frame.text }
        }
    }

    private fun refreshTree() {
        val expanded = tree.getExpandedDescendants(TreePath(rootNode))?.toList().orEmpty()

        rootNode.removeAllChildren()
        val root = model.root
        if (root == null) {
            rootNode.userObject = "No tree yet"
        } else {
            rootNode.userObject = root.label
            root.children.forEach { rootNode.add(build(it)) }
            status.text = "${root.count()} nodes · ${model.patchCount} patches · ${model.lastOps.take(3).joinToString(", ")}"
        }

        treeModel.reload()
        expanded.forEach { tree.expandPath(it) }
        if (tree.rowCount > 0) tree.expandRow(0)
    }

    private fun build(node: InspectorNode): DefaultMutableTreeNode {
        val treeNode = DefaultMutableTreeNode(NodeHolder(node))
        node.children.forEach { treeNode.add(build(it)) }
        return treeNode
    }

    private fun showProperties() {
        propertyModel.rowCount = 0
        val holder = (tree.lastSelectedPathComponent as? DefaultMutableTreeNode)?.userObject as? NodeHolder ?: return

        propertyModel.addRow(arrayOf("id", holder.node.id))
        propertyModel.addRow(arrayOf("type", holder.node.type))
        holder.node.props.forEach { (key, value) -> propertyModel.addRow(arrayOf(key, value.display())) }
        holder.node.modifiers.forEachIndexed { index, modifier ->
            val type = modifier["type"]?.display() ?: "modifier"
            val rest = modifier.filterKeys { it != "type" }.entries.joinToString(", ") { "${it.key}=${it.value.display()}" }
            propertyModel.addRow(arrayOf("modifier[$index] $type", rest))
        }
    }

    override fun dispose() = detach()

    /** Wrapper so the tree shows [InspectorNode.label] while keeping the node reachable. */
    private class NodeHolder(val node: InspectorNode) {
        override fun toString(): String = node.label
    }
}
