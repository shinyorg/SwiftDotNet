package com.swiftdotnet.rider.model

/**
 * The backends a project can target. The string values are exactly the values of the
 * `SwiftDotNetPlatform` MSBuild property (see `build/SwiftDotNet.Platform.props`), which is the
 * single source of truth — the plugin does not infer a backend from package references.
 */
enum class Backend(val propertyValue: String, val displayName: String) {
    APPLE("apple", "Apple (SwiftUI)"),
    ANDROID("android", "Android (Jetpack Compose)"),
    GTK("gtk", "Linux (GTK4)"),
    WINDOWS("windows", "Windows (WinUI 3)"),
    WEB("web", "Web (Blazor WebAssembly)"),
    SKIA("skia", "Skia (self-drawing)"),
    SKIA_MAUI("skia-maui", "Skia in a MAUI host");

    companion object {
        fun fromPropertyValue(value: String?): Backend? =
            value?.trim()?.lowercase()?.let { v -> entries.firstOrNull { it.propertyValue == v } }
    }
}

/** The operating systems a developer can be sitting at. */
enum class HostOs {
    MACOS, WINDOWS, LINUX;

    companion object {
        fun current(): HostOs {
            val name = System.getProperty("os.name").orEmpty().lowercase()
            return when {
                name.contains("mac") || name.contains("darwin") -> MACOS
                name.contains("win") -> WINDOWS
                else -> LINUX
            }
        }
    }
}

/**
 * Whether a head can be built here, and if not, why. Unsupported heads are shown greyed with the
 * reason rather than hidden — see `plans/rider-plugin-plan.md` Decision 5. For a framework whose whole
 * pitch is "every platform", the matrix is a teaching surface.
 */
sealed interface Supported {
    data object Yes : Supported
    data class No(val reason: String) : Supported
}

/**
 * One buildable head: a project, a target framework, and the backend it renders with.
 *
 * @param targetFramework the TFM to pass to `-f`, or null for a single-TFM project.
 */
data class Head(
    val projectPath: String,
    val projectName: String,
    val backend: Backend,
    val targetFramework: String? = null,
    val outputType: String = "Exe",
) {
    val id: String get() = if (targetFramework == null) projectName else "$projectName ($targetFramework)"

    val displayName: String get() = "$projectName — ${backend.displayName}"
}

/**
 * The host-OS gate. Mirrors the conditions `sample/SampleApp/SampleApp.csproj` already encodes with
 * `$([MSBuild]::IsOSPlatform(...))`, so the IDE and MSBuild agree on what exists.
 *
 * GTK is a deliberate special case: it is a plain `net10.0` executable, so it *builds* anywhere, and it
 * runs anywhere the GTK4 native libraries are installed (`brew install gtk4`). We therefore allow it
 * everywhere rather than gating it to Linux, and let the runtime failure speak for itself.
 */
object OsGate {

    fun supports(host: HostOs, backend: Backend): Supported = when (backend) {
        Backend.SKIA -> Supported.Yes
        Backend.WEB -> Supported.Yes
        Backend.GTK -> Supported.Yes

        Backend.APPLE, Backend.SKIA_MAUI -> when (host) {
            HostOs.MACOS -> Supported.Yes
            else -> Supported.No("Apple targets need a Mac with Xcode")
        }

        Backend.ANDROID -> Supported.Yes

        Backend.WINDOWS -> when (host) {
            HostOs.WINDOWS -> Supported.Yes
            else -> Supported.No("WinUI 3 / Windows App SDK only build on Windows")
        }
    }

    /**
     * A TFM-level gate, for multi-targeted projects where the project as a whole is supported but an
     * individual framework is not. `net10.0-maccatalyst` is Apple-only even though the project it lives
     * in also produces an Android head.
     */
    fun supports(host: HostOs, targetFramework: String?): Supported {
        val tfm = targetFramework?.lowercase() ?: return Supported.Yes
        val applePlatform = listOf("-ios", "-tvos", "-macos", "-maccatalyst").any { tfm.endsWith(it) }
        return when {
            applePlatform && host != HostOs.MACOS ->
                Supported.No("$targetFramework needs a Mac with Xcode")
            tfm.contains("-windows") && host != HostOs.WINDOWS ->
                Supported.No("$targetFramework only builds on Windows")
            else -> Supported.Yes
        }
    }

    /** Both gates together — a head is runnable only if its backend and its TFM both pass. */
    fun supports(host: HostOs, head: Head): Supported {
        val byBackend = supports(host, head.backend)
        if (byBackend is Supported.No) return byBackend
        return supports(host, head.targetFramework)
    }
}
