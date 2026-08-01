package com.swiftdotnet.rider.mobile

import com.swiftdotnet.rider.model.Head
import java.util.concurrent.TimeUnit

/** A simulator, emulator or physical device the run configuration can target. */
data class Device(val id: String, val name: String, val booted: Boolean) {
    override fun toString(): String = if (booted) "$name (booted)" else name
}

/**
 * Enumerates what is available to deploy to.
 *
 * Only *listing* is done here. Deploying and launching is `dotnet build -t:Run`, which is the SDK's own
 * entry point and already knows about provisioning, the app-bundle path and fast deployment — see
 * [com.swiftdotnet.rider.run.LaunchPlanner]. Re-implementing that with `simctl install` and
 * `simctl launch` would be a second, worse copy of a thing that already works.
 */
object DeviceLister {

    fun devicesFor(head: Head): List<Device> {
        val tfm = head.targetFramework?.lowercase().orEmpty()
        return when {
            tfm.endsWith("-android") -> androidDevices()
            tfm.endsWith("-ios") || tfm.endsWith("-tvos") -> appleSimulators(tfm)
            else -> emptyList()
        }
    }

    /**
     * `xcrun simctl list devices available` — booted devices first, since that is almost always the one
     * meant.
     *
     * Parsed from the plain-text listing rather than `-j` because the JSON form nests devices under
     * runtime keys that change name with every Xcode release, and this shape has been stable for years:
     *
     *     iPhone 16 Pro (0B2E...-...) (Booted)
     */
    fun appleSimulators(targetFramework: String): List<Device> {
        val output = run(listOf("xcrun", "simctl", "list", "devices", "available")) ?: return emptyList()

        val wantsTv = targetFramework.endsWith("-tvos")
        val devices = mutableListOf<Device>()
        var inMatchingRuntime = false

        for (line in output.lineSequence()) {
            val trimmed = line.trim()
            if (trimmed.startsWith("--")) {
                // Runtime header: "-- iOS 18.2 --" / "-- tvOS 18.2 --"
                val runtime = trimmed.trim('-', ' ').lowercase()
                inMatchingRuntime = if (wantsTv) runtime.startsWith("tvos") else runtime.startsWith("ios")
                continue
            }
            if (!inMatchingRuntime) continue

            val open = trimmed.indexOf('(')
            val close = trimmed.indexOf(')', open + 1)
            if (open <= 0 || close < 0) continue

            val name = trimmed.substring(0, open).trim()
            val udid = trimmed.substring(open + 1, close).trim()
            if (udid.count { it == '-' } != 4) continue      // not a UDID; a nested annotation

            devices += Device(udid, name, booted = trimmed.contains("(Booted)"))
        }

        return devices.sortedByDescending { it.booted }
    }

    /** `adb devices -l`, skipping the header and anything not in the `device` state. */
    fun androidDevices(): List<Device> {
        val output = run(listOf(adb(), "devices", "-l")) ?: return emptyList()

        return output.lineSequence()
            .drop(1)
            .mapNotNull { line ->
                val parts = line.trim().split(Regex("\\s+"))
                if (parts.size < 2 || parts[1] != "device") return@mapNotNull null

                // `adb devices -l` appends key:value pairs; `model:Pixel_8` is the readable name.
                val model = parts.drop(2)
                    .firstOrNull { it.startsWith("model:") }
                    ?.removePrefix("model:")
                    ?.replace('_', ' ')

                Device(parts[0], model ?: parts[0], booted = true)
            }
            .toList()
    }

    /**
     * Absolute path to `adb`.
     *
     * The default locations are checked, not just `$ANDROID_HOME` and `PATH`, because the common case on
     * a working Android machine is that **neither is set**: the SDK is installed by Android Studio into
     * `~/Library/Android/sdk` and nothing ever exports a variable or adds `platform-tools` to `PATH`.
     * Relying on the environment produced an empty device list on a machine with a running emulator.
     *
     * The IDE also does not inherit a login shell's environment, so anything set in `.zshrc` is invisible
     * here even when it works in a terminal.
     */
    fun adb(): String {
        val fromEnvironment = listOfNotNull(
            System.getenv("ANDROID_HOME"),
            System.getenv("ANDROID_SDK_ROOT"),
        ).map { java.io.File(it, "platform-tools/adb") }

        val userHome = System.getProperty("user.home").orEmpty()
        val conventional = listOf(
            "$userHome/Library/Android/sdk/platform-tools/adb",      // macOS, Android Studio default
            "$userHome/Android/Sdk/platform-tools/adb",              // Linux, Android Studio default
            "$userHome/AppData/Local/Android/Sdk/platform-tools/adb.exe",
        ).map { java.io.File(it) }

        val onPath = System.getenv("PATH")?.split(java.io.File.pathSeparatorChar).orEmpty()
            .map { java.io.File(it, "adb") }

        return (fromEnvironment + onPath + conventional)
            .firstOrNull { it.canExecute() }
            ?.absolutePath
            ?: "adb"
    }

    private fun run(command: List<String>): String? = try {
        val process = ProcessBuilder(command).redirectErrorStream(true).start()
        val output = process.inputStream.bufferedReader().readText()
        if (process.waitFor(30, TimeUnit.SECONDS) && process.exitValue() == 0) output else null
    } catch (_: Exception) {
        // No Xcode, no Android SDK, or neither on PATH. An empty device list is the right answer; the
        // user can still type an id, and the SDK will pick a default if they do not.
        null
    }
}
