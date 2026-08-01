package com.swiftdotnet.rider.msbuild

import com.swiftdotnet.rider.model.Backend
import com.swiftdotnet.rider.model.Head
import java.io.File
import java.util.concurrent.TimeUnit

/**
 * Finds the SwiftDotNet heads in a solution by *asking MSBuild*, never by inferring from package
 * references.
 *
 * `dotnet msbuild -getProperty:` evaluates a project without running a target and prints the result as
 * JSON, which makes the `SwiftDotNetPlatform` contract in `msbuild/SwiftDotNet.Platform.targets` readable
 * in a few milliseconds. Two things this buys that inference does not:
 *
 *  * `sample/SampleApp.Skia.Mac` targets `net10.0-macos` but draws with Skia, not SwiftUI. Only the
 *    project can say that.
 *  * `sample/SharedUI` also targets `net10.0-android` and is a *library*. Only the project can say that
 *    too — and .NET Android builds apps as `OutputType=Library`, so even OutputType would get it wrong.
 */
class HeadDiscovery(
    private val dotnet: String = "dotnet",
    private val timeoutSeconds: Long = 90,
) {

    /** Every head under [root], skipping the usual noise directories. */
    fun discover(root: File): List<Head> =
        projectFiles(root).flatMap { headsIn(it) }.sortedBy { it.displayName }

    fun projectFiles(root: File): List<File> =
        root.walkTopDown()
            .onEnter { dir ->
                dir.name !in setOf("bin", "obj", "node_modules", ".git", ".gradle", "build")
            }
            .filter { it.isFile && it.extension == "csproj" }
            .toList()

    /**
     * A project can produce several heads — `sample/SampleApp` is one file and up to five apps. Each TFM
     * is evaluated separately, because `SwiftDotNetPlatform` is derived per target framework and the
     * outer evaluation of a multi-targeted project has no `$(TargetFramework)` at all.
     */
    fun headsIn(project: File): List<Head> {
        val outer = evaluate(project, targetFramework = null) ?: return emptyList()

        val frameworks = outer["TargetFrameworks"]
            ?.split(';')
            ?.map(String::trim)
            ?.filter(String::isNotEmpty)
            .orEmpty()

        if (frameworks.isEmpty()) {
            // Single-TFM project: the outer evaluation already has the answer.
            val head = toHead(project, outer, outer["TargetFramework"]?.takeIf { it.isNotBlank() })
            return listOfNotNull(head)
        }

        return frameworks.mapNotNull { tfm ->
            evaluate(project, tfm)?.let { toHead(project, it, tfm) }
        }
    }

    private fun toHead(project: File, properties: Map<String, String>, tfm: String?): Head? {
        if (properties["SwiftDotNetIsAppHead"] != "true") return null
        val backend = Backend.fromPropertyValue(properties["SwiftDotNetPlatform"]) ?: return null

        return Head(
            projectPath = project.absolutePath,
            projectName = project.nameWithoutExtension,
            backend = backend,
            targetFramework = tfm,
            outputType = properties["OutputType"].orEmpty(),
        )
    }

    private fun evaluate(project: File, targetFramework: String?): Map<String, String>? {
        val command = buildList {
            add(dotnet); add("msbuild"); add(project.absolutePath)
            PROPERTIES.forEach { add("-getProperty:$it") }
            targetFramework?.let { add("-p:TargetFramework=$it") }
        }

        return try {
            val process = ProcessBuilder(command)
                .directory(project.parentFile)
                .redirectErrorStream(false)
                .start()

            val output = process.inputStream.bufferedReader().readText()
            if (!process.waitFor(timeoutSeconds, TimeUnit.SECONDS)) {
                process.destroyForcibly()
                return null
            }
            if (process.exitValue() != 0) return null

            parseProperties(output)
        } catch (_: Exception) {
            // A project that cannot even be evaluated (a missing workload, a broken import) is not a
            // head. Reporting it as an error would put a dialog in front of someone who opened a
            // solution containing one unbuildable project.
            null
        }
    }

    companion object {
        private val PROPERTIES = listOf(
            "SwiftDotNetPlatform",
            "SwiftDotNetIsAppHead",
            "TargetFramework",
            "TargetFrameworks",
            "OutputType",
        )

        /**
         * Reads the `{"Properties": {...}}` document `-getProperty` prints. Hand-parsed rather than
         * pulling in a JSON library: the shape is fixed, flat, and string-valued, and MSBuild escapes
         * backslashes in paths — which is the one thing a naive reader gets wrong on Windows.
         */
        fun parseProperties(output: String): Map<String, String> {
            val start = output.indexOf('{')
            if (start < 0) return emptyMap()

            val result = mutableMapOf<String, String>()
            var i = output.indexOf("\"Properties\"", start)
            if (i < 0) return emptyMap()
            i = output.indexOf('{', i)
            if (i < 0) return emptyMap()
            i++

            while (i < output.length) {
                val keyStart = output.indexOf('"', i)
                if (keyStart < 0) break
                val key = readString(output, keyStart) ?: break
                i = key.second

                val colon = output.indexOf(':', i)
                if (colon < 0) break
                val valueStart = output.indexOf('"', colon)
                if (valueStart < 0) break
                val value = readString(output, valueStart) ?: break
                i = value.second

                result[key.first] = value.first

                val next = output.indexOfFirst(i) { it == ',' || it == '}' }
                if (next < 0 || output[next] == '}') break
                i = next + 1
            }
            return result
        }

        /** Reads a JSON string starting at the opening quote; returns the value and the index after it. */
        private fun readString(text: String, openQuote: Int): Pair<String, Int>? {
            val sb = StringBuilder()
            var i = openQuote + 1
            while (i < text.length) {
                when (val c = text[i]) {
                    '\\' -> {
                        if (i + 1 >= text.length) return null
                        when (val escaped = text[i + 1]) {
                            'n' -> sb.append('\n')
                            'r' -> sb.append('\r')
                            't' -> sb.append('\t')
                            'u' -> {
                                if (i + 5 >= text.length) return null
                                sb.append(text.substring(i + 2, i + 6).toInt(16).toChar())
                                i += 4
                            }
                            else -> sb.append(escaped)      // covers \\ and \" and \/
                        }
                        i += 2
                    }
                    '"' -> return sb.toString() to (i + 1)
                    else -> { sb.append(c); i++ }
                }
            }
            return null
        }

        private inline fun String.indexOfFirst(from: Int, predicate: (Char) -> Boolean): Int {
            for (i in from until length) if (predicate(this[i])) return i
            return -1
        }
    }
}
