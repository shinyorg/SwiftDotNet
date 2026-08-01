package com.swiftdotnet.rider.doctor

import com.intellij.openapi.application.ApplicationStarter
import com.swiftdotnet.rider.mobile.DeviceLister
import com.swiftdotnet.rider.model.HostOs
import com.swiftdotnet.rider.model.OsGate
import com.swiftdotnet.rider.model.Supported
import com.swiftdotnet.rider.msbuild.HeadDiscovery
import com.swiftdotnet.rider.run.LaunchOptions
import com.swiftdotnet.rider.run.LaunchPlanner
import com.swiftdotnet.rider.run.SwiftDotNetRunConfiguration
import com.swiftdotnet.rider.run.isDeployedToADevice
import java.io.File
import kotlin.system.exitProcess

/**
 * `swiftdotnet-doctor <solution-dir>` — headless.
 *
 * Runs the plugin's own discovery, host-OS gate, device listing and launch planning **inside the IDE
 * process**, and prints what a developer would get if they pressed Run. Two jobs:
 *
 *  * **Diagnosis.** "Why isn't my iOS head in the list?" is answerable without screenshots or guesswork.
 *  * **Verification.** The plugin's classes are exercised in the real IDE runtime, on a machine with real
 *    simulators and emulators, without anyone clicking anything. Unit tests cannot prove the plugin
 *    *loads*; this can.
 *
 * ```
 * gradle runIde -Pdoctor
 * ```
 */
class SwiftDotNetDoctor : ApplicationStarter {

    /**
     * The command name comes from the `id` attribute of the `appStarter` extension in `plugin.xml`;
     * recent platform versions dropped the `commandName` property from this interface.
     */
    override fun main(args: List<String>) {
        val root = args.drop(1).firstOrNull()?.let(::File) ?: File(".")
        val out = StringBuilder()

        fun line(text: String = "") = out.appendLine(text)

        line("SwiftDotNet doctor")
        line("=".repeat(72))
        line("solution     : ${root.absolutePath}")
        line("host os      : ${HostOs.current()}")
        line("dotnet       : ${SwiftDotNetRunConfiguration.resolveDotNet()}")
        line("adb          : ${DeviceLister.adb()}")
        line()

        if (!root.isDirectory) {
            line("ERROR: '$root' is not a directory.")
            print(out)
            exitProcess(2)
        }

        val heads = try {
            HeadDiscovery().discover(root)
        } catch (ex: Exception) {
            line("ERROR: head discovery failed: ${ex.message}")
            print(out)
            exitProcess(2)
        }

        line("heads (${heads.size})")
        line("-".repeat(72))
        if (heads.isEmpty())
            line("  none — add <SwiftDotNetPlatform> to an app project, or check that it builds.")

        val host = HostOs.current()
        var runnable = 0
        for (head in heads) {
            when (val support = OsGate.supports(host, head)) {
                is Supported.Yes -> {
                    runnable++
                    line("  ✓ ${head.id}")
                    line("      backend  : ${head.backend.displayName}")

                    val devices =
                        if (head.backend.isDeployedToADevice(head.targetFramework)) DeviceLister.devicesFor(head)
                        else emptyList()

                    if (head.backend.isDeployedToADevice(head.targetFramework)) {
                        line("      devices  : " + if (devices.isEmpty()) "none attached" else
                            devices.joinToString(", ") { "${it.name} (${it.id})" })
                    }

                    val plan = LaunchPlanner.plan(
                        head,
                        LaunchOptions(deviceId = devices.firstOrNull()?.id, devToolsPort = 51799),
                        dotnet = SwiftDotNetRunConfiguration.resolveDotNet(),
                    )
                    line("      run      : ${plan.commandLine}")
                    line("      cwd      : ${plan.workingDirectory}")
                }
                is Supported.No -> line("  · ${head.id} — unavailable: ${support.reason}")
            }
        }

        line()
        line("$runnable of ${heads.size} head(s) runnable on ${host.name.lowercase()}")
        print(out)

        // Non-zero when there is nothing to run, so this is usable as a check in CI.
        exitProcess(if (runnable == 0) 1 else 0)
    }
}
