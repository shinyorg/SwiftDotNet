package com.swiftdotnet.rider.preview

import com.intellij.openapi.project.Project
import com.swiftdotnet.rider.run.SwiftDotNetRunConfiguration
import java.io.File
import java.util.concurrent.TimeUnit

/**
 * Starts `SwiftDotNet.Preview.Host` against the view assembly of the project being edited.
 *
 * Finding the assembly is the whole job, and it is done by *building* the shared UI project rather than
 * guessing at a path under `bin/`: the preview must show the code as it is now, and a stale assembly
 * from three edits ago is worse than no preview at all.
 */
class PreviewLauncher(private val project: Project) {

    var port: Int = 0
        private set

    var description: String = ""
        private set

    private var process: Process? = null

    fun start(): PreviewLauncher {
        val root = project.basePath?.let(::File)
            ?: throw IllegalStateException("This project has no base path.")

        val hostProject = findHostProject(root)
            ?: throw IllegalStateException(
                "Could not find src/SwiftDotNet.Preview.Host in this solution.")

        val viewProject = findViewProject(root)
            ?: throw IllegalStateException(
                "Could not find a shared UI project to preview. Expected a net10.0 library referencing SwiftDotNet.")

        val assembly = buildAndLocate(viewProject)
            ?: throw IllegalStateException("`dotnet build` produced no assembly for ${viewProject.name}.")

        port = freePort()
        description = viewProject.nameWithoutExtension

        val command = listOf(
            SwiftDotNetRunConfiguration.resolveDotNet(), "run",
            "--project", hostProject.absolutePath,
            "--",
            "--assembly", assembly.absolutePath,
            "--port", port.toString(),
        )

        process = ProcessBuilder(command)
            .directory(root)
            .redirectErrorStream(true)
            .start()

        return this
    }

    fun stop() {
        process?.destroy()
        // The host polls a stop flag every 16 ms, so a graceful exit is quick; the deadline is only
        // here so a wedged process cannot leak.
        if (process?.waitFor(3, TimeUnit.SECONDS) == false) process?.destroyForcibly()
        process = null
    }

    private fun findHostProject(root: File): File? =
        File(root, "src/SwiftDotNet.Preview.Host/SwiftDotNet.Preview.Host.csproj").takeIf { it.isFile }

    /**
     * The project holding the views. Prefers one that looks like shared UI, then any plain `net10.0`
     * library that references SwiftDotNet — the preview host loads a net10.0 assembly, so a head
     * targeting `net10.0-ios` is not a candidate even though it contains views.
     */
    private fun findViewProject(root: File): File? {
        val candidates = root.walkTopDown()
            .onEnter { it.name !in setOf("bin", "obj", ".git", "node_modules", "build", "tooling") }
            .filter { it.isFile && it.extension == "csproj" }
            .toList()

        return candidates.firstOrNull { it.nameWithoutExtension.equals("SharedUI", ignoreCase = true) }
            ?: candidates.firstOrNull {
                val text = it.readText()
                text.contains("SwiftDotNet") && text.contains("net10.0") && !text.contains("<OutputType>Exe")
            }
    }

    private fun buildAndLocate(project: File): File? {
        val build = ProcessBuilder(
            SwiftDotNetRunConfiguration.resolveDotNet(), "build", project.absolutePath,
            "-f", "net10.0", "-v", "quiet", "--nologo",
        ).directory(project.parentFile).redirectErrorStream(true).start()

        build.inputStream.bufferedReader().readText()
        if (!build.waitFor(5, TimeUnit.MINUTES)) {
            build.destroyForcibly()
            return null
        }
        if (build.exitValue() != 0) return null

        val assemblyName = "${project.nameWithoutExtension}.dll"
        return File(project.parentFile, "bin/Debug/net10.0/$assemblyName").takeIf { it.isFile }
    }

    private fun freePort(): Int = java.net.ServerSocket(0).use { it.localPort }
}
