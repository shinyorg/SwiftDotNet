package com.swiftdotnet.rider.preview

import com.intellij.openapi.actionSystem.ActionUpdateThread
import com.intellij.openapi.actionSystem.AnAction
import com.intellij.openapi.actionSystem.AnActionEvent
import com.intellij.openapi.project.DumbAware
import com.intellij.openapi.wm.ToolWindowManager

/**
 * Rebuild and reload the preview from anywhere — the editor, a keymap binding, Search Everywhere.
 *
 * The preview reloads by itself when the assembly changes on disk, so this is for the case where the
 * developer wants it *now* rather than after the next build.
 */
class RestartPreviewAction : AnAction(), DumbAware {

    override fun getActionUpdateThread(): ActionUpdateThread = ActionUpdateThread.EDT

    override fun update(e: AnActionEvent) {
        e.presentation.isEnabled = e.project != null
    }

    override fun actionPerformed(e: AnActionEvent) {
        val project = e.project ?: return
        val toolWindow = ToolWindowManager.getInstance(project).getToolWindow(PREVIEW_TOOL_WINDOW) ?: return

        toolWindow.activate {
            val panel = toolWindow.contentManager.contents
                .firstNotNullOfOrNull { it.component as? PreviewPanel }
            panel?.start()
        }
    }

    companion object {
        const val PREVIEW_TOOL_WINDOW = "SwiftDotNet Preview"
    }
}
