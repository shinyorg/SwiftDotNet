package com.swiftdotnet.rider.services

import com.intellij.openapi.components.Service
import com.intellij.openapi.components.service
import com.intellij.openapi.project.Project

/**
 * Where the most recent launch left its dev-tools port, so the tool windows can attach without the
 * developer copying a number out of the run console.
 *
 * Deliberately just the last one. Multiple SwiftDotNet apps at once is a real scenario (a Skia head and
 * an iOS head side by side), but "attach to the thing I just started" is the case worth making
 * effortless, and a picker for the rare case can come later.
 */
@Service(Service.Level.PROJECT)
class SwiftDotNetSession {

    @Volatile
    var devToolsPort: Int = 0
        private set

    @Volatile
    var description: String = ""
        private set

    fun record(port: Int, description: String) {
        this.devToolsPort = port
        this.description = description
    }

    fun clear() {
        devToolsPort = 0
        description = ""
    }

    companion object {
        fun getInstance(project: Project): SwiftDotNetSession = project.service()
    }
}
