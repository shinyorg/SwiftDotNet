package com.swiftdotnet.rider.run

import com.swiftdotnet.rider.model.Backend
import com.swiftdotnet.rider.model.Head
import java.io.File

/**
 * How a head is launched, per backend. Deliberately pure — no IDE types — so the interesting decisions
 * are unit-testable without an IDE, and so a wrong command line is a failing test rather than a report
 * from a user with a simulator.
 *
 * The whole point of this class is that the incantations in `docs/getting-started.md` stop being
 * something a developer has to remember.
 */
data class LaunchPlan(
    val exePath: String,
    val arguments: List<String>,
    val workingDirectory: String,
    val environment: Map<String, String>,
    /** Human-readable summary for the run console's first line. */
    val description: String,
) {
    val commandLine: String get() = (listOf(exePath) + arguments).joinToString(" ")
}

/** What the user chose in the run-configuration editor. */
data class LaunchOptions(
    val configuration: String = "Debug",
    val watch: Boolean = true,
    /** Simulator/emulator/device id, when the backend needs one. */
    val deviceId: String? = null,
    /** Port for the dev-tools channel; null leaves it off. */
    val devToolsPort: Int? = null,
    val extraMsBuildProperties: Map<String, String> = emptyMap(),
    val programArguments: String = "",
)

object LaunchPlanner {

    /** Set on iOS/tvOS builds to opt into the Mono interpreter, which hot reload requires there. */
    const val HOT_RELOAD_PROPERTY = "SwiftDotNetHotReload"

    const val DEV_TOOLS_PORT_VARIABLE = "SWIFTDOTNET_DEVTOOLS_PORT"

    fun plan(head: Head, options: LaunchOptions, dotnet: String = "dotnet"): LaunchPlan {
        val projectFile = File(head.projectPath)

        // Run from the project directory, not the solution root. Microsoft.iOS resolves the .app bundle
        // relative to the *current* directory, and getting this wrong is the documented `MT0069: The app
        // directory ... does not exist` in docs/hot-reload.md.
        val workingDirectory = projectFile.parent ?: "."

        val environment = buildMap {
            options.devToolsPort?.let { put(DEV_TOOLS_PORT_VARIABLE, it.toString()) }
        }

        return when {
            head.backend.isDeployedToADevice(head.targetFramework) ->
                deployPlan(head, options, dotnet, workingDirectory, environment)

            options.watch ->
                watchPlan(head, options, dotnet, workingDirectory, environment)

            else ->
                runPlan(head, options, dotnet, workingDirectory, environment)
        }
    }

    /**
     * Desktop and web heads: `dotnet run`, with `dotnet watch run` when hot reload is wanted.
     *
     * There is nothing SwiftDotNet-specific here, and that is the point — hot reload on these backends
     * is stock .NET, so the plugin's contribution is knowing which project to point at rather than any
     * machinery of its own.
     */
    private fun runPlan(
        head: Head,
        options: LaunchOptions,
        dotnet: String,
        workingDirectory: String,
        environment: Map<String, String>,
    ): LaunchPlan {
        val args = buildList {
            add("run")
            add("--project"); add(head.projectPath)
            addAll(commonBuildArgs(head, options))
            if (options.programArguments.isNotBlank()) {
                add("--")
                addAll(options.programArguments.split(' ').filter { it.isNotBlank() })
            }
        }
        return LaunchPlan(dotnet, args, workingDirectory, environment, "run ${head.displayName}")
    }

    private fun watchPlan(
        head: Head,
        options: LaunchOptions,
        dotnet: String,
        workingDirectory: String,
        environment: Map<String, String>,
    ): LaunchPlan {
        val args = buildList {
            add("watch")
            add("run")
            add("--project"); add(head.projectPath)
            // Without this, a rude edit stops to ask whether to restart, on a console the IDE may not be
            // showing. "Yes, restart" is the only useful answer in an IDE, so it is the answer given.
            add("--non-interactive")
            addAll(commonBuildArgs(head, options))
            if (options.programArguments.isNotBlank()) {
                add("--")
                addAll(options.programArguments.split(' ').filter { it.isNotBlank() })
            }
        }
        return LaunchPlan(dotnet, args, workingDirectory, environment, "watch ${head.displayName}")
    }

    /**
     * Simulator, emulator and device heads: `dotnet build -t:Run`.
     *
     * This is the .NET Android / Microsoft.iOS entry point, and using it rather than orchestrating
     * `simctl install` + `simctl launch` (or `adb install` + `am start`) by hand is a deliberate choice:
     * the SDK targets already know about provisioning, the app-bundle path, fast deployment and the
     * device selector, and every one of those is a thing to get wrong twice.
     */
    private fun deployPlan(
        head: Head,
        options: LaunchOptions,
        dotnet: String,
        workingDirectory: String,
        environment: Map<String, String>,
    ): LaunchPlan {
        val args = buildList {
            add("build")
            add(head.projectPath)
            add("-t:Run")
            addAll(commonBuildArgs(head, options))

            if (head.needsInterpreterForHotReload() && options.watch)
                add("-p:$HOT_RELOAD_PROPERTY=true")

            options.deviceId?.let { device ->
                // The two SDKs spell "which device" completely differently.
                if (head.isApple()) add("-p:_DeviceName=:v2:udid=$device")
                else add("-p:AdbTarget=-s $device")
            }
        }
        return LaunchPlan(dotnet, args, workingDirectory, environment, "deploy ${head.displayName}")
    }

    private fun commonBuildArgs(head: Head, options: LaunchOptions): List<String> = buildList {
        head.targetFramework?.let { add("-f"); add(it) }
        add("-c"); add(options.configuration)
        options.extraMsBuildProperties.forEach { (key, value) -> add("-p:$key=$value") }
    }
}

/** iOS, tvOS and Android heads are deployed to something; the rest just run. */
internal fun Backend.isDeployedToADevice(targetFramework: String?): Boolean {
    val tfm = targetFramework?.lowercase().orEmpty()
    return when (this) {
        Backend.ANDROID -> true
        Backend.APPLE, Backend.SKIA_MAUI ->
            // macOS and Mac Catalyst produce an app that runs on *this* machine, so they are ordinary
            // `dotnet run` heads even though they are Apple platforms.
            tfm.endsWith("-ios") || tfm.endsWith("-tvos") || tfm.endsWith("-android")
        else -> false
    }
}

internal fun Head.isApple(): Boolean {
    val tfm = targetFramework?.lowercase().orEmpty()
    return tfm.endsWith("-ios") || tfm.endsWith("-tvos") ||
        tfm.endsWith("-macos") || tfm.endsWith("-maccatalyst")
}

/**
 * iOS and tvOS refuse to hot reload without the Mono interpreter — the SDK hard-errors with
 * "Can't use Hot Reload or 'dotnet watch' unless the interpreter is enabled". Android and macOS do not
 * need it, and turning it on there would slow the app down for nothing.
 */
internal fun Head.needsInterpreterForHotReload(): Boolean {
    val tfm = targetFramework?.lowercase().orEmpty()
    return tfm.endsWith("-ios") || tfm.endsWith("-tvos")
}
