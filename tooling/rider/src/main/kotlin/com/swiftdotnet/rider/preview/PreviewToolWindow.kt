package com.swiftdotnet.rider.preview

import com.intellij.icons.AllIcons
import com.intellij.openapi.Disposable
import com.intellij.openapi.actionSystem.ActionManager
import com.intellij.openapi.actionSystem.ActionUpdateThread
import com.intellij.openapi.actionSystem.AnAction
import com.intellij.openapi.actionSystem.AnActionEvent
import com.intellij.openapi.actionSystem.DefaultActionGroup
import com.intellij.openapi.actionSystem.ToggleAction
import com.intellij.openapi.application.ApplicationManager
import com.intellij.openapi.project.DumbAware
import com.intellij.openapi.project.Project
import com.intellij.openapi.wm.ToolWindow
import com.intellij.openapi.wm.ToolWindowFactory
import com.intellij.ui.components.JBLabel
import com.intellij.ui.content.ContentFactory
import com.swiftdotnet.rider.devtools.DevToolsClient
import com.swiftdotnet.rider.devtools.DevToolsFrame
import com.swiftdotnet.rider.devtools.DevToolsProtocol
import java.awt.BorderLayout
import java.awt.Dimension
import java.awt.Graphics
import java.awt.event.ComponentAdapter
import java.awt.event.ComponentEvent
import java.awt.event.MouseAdapter
import java.awt.event.MouseEvent
import java.awt.event.MouseWheelEvent
import java.awt.image.BufferedImage
import javax.imageio.ImageIO
import javax.swing.JComponent
import javax.swing.JPanel
import javax.swing.SwingUtilities

/**
 * The SwiftUI-preview equivalent: your views, rendered and interactive, inside the IDE.
 *
 * The renderer is the Skia backend running headlessly in a separate process
 * (`src/SwiftDotNet.Preview.Host`), streaming PNG frames over the dev-tools socket and taking clicks,
 * scrolls and keystrokes back. Two consequences worth being honest about:
 *
 *  * It is a **Skia** preview, not an iOS one. Skia is the most test-verified backend and the only one
 *    that draws itself, which makes it the right choice — but a control that renders as a native
 *    UISwitch on iOS is drawn by Skia here.
 *  * Because the host reloads the assembly rather than patching methods, **every** edit applies,
 *    including the rude ones .NET hot reload refuses (a new type, a changed signature). The trade is
 *    that state resets — which is what a preview does anyway.
 */
class PreviewToolWindowFactory : ToolWindowFactory, DumbAware {
    override fun createToolWindowContent(project: Project, toolWindow: ToolWindow) {
        val panel = PreviewPanel(project)
        val content = ContentFactory.getInstance().createContent(panel, "", false)
        content.setDisposer(panel)
        toolWindow.contentManager.addContent(content)
    }
}

class PreviewPanel(private val project: Project) : JPanel(BorderLayout()), Disposable {

    private val canvas = FrameCanvas()
    private val status = JBLabel("Not started. Use ▶ to launch the preview host.")

    private var launcher: PreviewLauncher? = null
    private var client: DevToolsClient? = null
    private var dark = false

    init {
        add(toolbar(), BorderLayout.NORTH)
        add(canvas, BorderLayout.CENTER)
        add(status, BorderLayout.SOUTH)

        canvas.onInput = { command -> client?.send(DevToolsProtocol.INPUT, command) }

        addComponentListener(object : ComponentAdapter() {
            override fun componentResized(e: ComponentEvent) {
                val size = canvas.size
                if (size.width > 0 && size.height > 0)
                    client?.send(DevToolsProtocol.RESIZE, "${size.width} ${size.height}")
            }
        })
    }

    private fun toolbar(): JComponent {
        val group = DefaultActionGroup(
            object : AnAction("Start Preview", "Launch the Skia preview host", AllIcons.Actions.Execute), DumbAware {
                override fun getActionUpdateThread() = ActionUpdateThread.BGT
                override fun actionPerformed(e: AnActionEvent) = start()
            },
            object : AnAction("Stop", "Stop the preview host", AllIcons.Actions.Suspend), DumbAware {
                override fun getActionUpdateThread() = ActionUpdateThread.BGT
                override fun actionPerformed(e: AnActionEvent) = stop()
            },
            object : AnAction("Reload", "Reload the view assembly", AllIcons.Actions.Refresh), DumbAware {
                override fun getActionUpdateThread() = ActionUpdateThread.BGT
                override fun actionPerformed(e: AnActionEvent) {
                    client?.send(DevToolsProtocol.RELOAD)
                }
            },
            object : ToggleAction("Dark Appearance", "Preview in dark mode", AllIcons.Actions.ToggleSoftWrap), DumbAware {
                override fun getActionUpdateThread() = ActionUpdateThread.EDT
                override fun isSelected(e: AnActionEvent) = dark
                override fun setSelected(e: AnActionEvent, state: Boolean) {
                    dark = state
                    client?.send(DevToolsProtocol.THEME, if (state) "dark" else "light")
                }
            },
        )
        return ActionManager.getInstance()
            .createActionToolbar("SwiftDotNetPreview", group, true)
            .also { it.targetComponent = this }
            .component
    }

    fun start() {
        stop()
        status.text = "Starting the preview host…"

        ApplicationManager.getApplication().executeOnPooledThread {
            val started = try {
                PreviewLauncher(project).start()
            } catch (ex: Exception) {
                ApplicationManager.getApplication().invokeLater {
                    status.text = "Could not start the preview: ${ex.message}"
                }
                return@executeOnPooledThread
            }

            launcher = started
            client = DevToolsClient(
                port = started.port,
                onFrame = ::onFrame,
                onDisconnect = { error ->
                    ApplicationManager.getApplication().invokeLater {
                        status.text = error?.let { "Preview disconnected: ${it.message}" } ?: "Preview stopped."
                    }
                },
            ).also { it.start() }

            ApplicationManager.getApplication().invokeLater {
                status.text = "Preview running on port ${started.port} — ${started.description}"
                val size = canvas.size
                if (size.width > 0 && size.height > 0)
                    client?.send(DevToolsProtocol.RESIZE, "${size.width} ${size.height}")
            }
        }
    }

    fun stop() {
        client?.close()
        client = null
        launcher?.stop()
        launcher = null
    }

    private fun onFrame(frame: DevToolsFrame) {
        when (frame.type) {
            DevToolsProtocol.FRAME -> {
                // Decoded off the EDT — a 50 KB PNG per frame on the UI thread is a stuttering IDE.
                val image = try {
                    ImageIO.read(frame.payload.inputStream())
                } catch (_: Exception) {
                    null
                } ?: return
                SwingUtilities.invokeLater { canvas.show(image) }
            }

            DevToolsProtocol.LOG ->
                ApplicationManager.getApplication().invokeLater {
                    status.text = frame.text.removePrefix("error ").let {
                        if (frame.text.startsWith("error ")) "Preview error: $it" else it
                    }
                }
        }
    }

    override fun dispose() = stop()
}

/** Draws the streamed frame and turns mouse input into dev-tools commands. */
private class FrameCanvas : JPanel() {

    var onInput: (String) -> Unit = {}
    private var image: BufferedImage? = null

    init {
        preferredSize = Dimension(390, 844)
        isFocusable = true

        addMouseListener(object : MouseAdapter() {
            override fun mouseClicked(e: MouseEvent) {
                requestFocusInWindow()
                onInput("tap ${e.x} ${e.y}")
            }
        })

        addMouseWheelListener { e: MouseWheelEvent ->
            // Positive wheel rotation scrolls content down, which the engine expresses as a negative dy.
            onInput("scroll ${e.x} ${e.y} ${-e.wheelRotation * 40}")
        }
    }

    fun show(next: BufferedImage) {
        image = next
        repaint()
    }

    override fun paintComponent(g: Graphics) {
        super.paintComponent(g)
        val current = image ?: return
        // Drawn 1:1 at the top-left; the host is told the panel size on every resize, so the frame it
        // sends already matches. Scaling here instead would blur text and lie about layout.
        g.drawImage(current, 0, 0, null)
    }
}
