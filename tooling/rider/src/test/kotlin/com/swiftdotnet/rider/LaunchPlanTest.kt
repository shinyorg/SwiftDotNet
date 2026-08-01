package com.swiftdotnet.rider

import com.swiftdotnet.rider.model.Backend
import com.swiftdotnet.rider.model.Head
import com.swiftdotnet.rider.model.HostOs
import com.swiftdotnet.rider.model.OsGate
import com.swiftdotnet.rider.model.Supported
import com.swiftdotnet.rider.run.LaunchOptions
import com.swiftdotnet.rider.run.LaunchPlanner
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test
import java.io.File

/**
 * The command lines the plugin builds, and the host-OS gate that decides which heads it offers.
 *
 * These are the two places where being wrong is invisible until someone's simulator does nothing, so
 * they are asserted rather than eyeballed. Both are pure Kotlin — no IDE — which is why they can be.
 */
class LaunchPlanTest {

    private fun head(
        name: String,
        backend: Backend,
        tfm: String? = null,
        path: String = "/repo/sample/$name/$name.csproj",
    ) = Head(projectPath = path, projectName = name, backend = backend, targetFramework = tfm)

    // ---- desktop and web -----------------------------------------------------------------------

    @Test
    fun `skia head runs under dotnet watch by default`() {
        val plan = LaunchPlanner.plan(head("SampleApp.Skia.Silk", Backend.SKIA, "net10.0"), LaunchOptions())

        assertEquals("dotnet", plan.exePath)
        assertEquals(
            listOf(
                "watch", "run",
                "--project", "/repo/sample/SampleApp.Skia.Silk/SampleApp.Skia.Silk.csproj",
                "--non-interactive",
                "-f", "net10.0",
                "-c", "Debug",
            ),
            plan.arguments,
        )
    }

    @Test
    fun `turning watch off gives a plain dotnet run`() {
        val plan = LaunchPlanner.plan(
            head("SampleApp.Gtk", Backend.GTK, "net10.0"),
            LaunchOptions(watch = false),
        )

        assertEquals(listOf("run", "--project"), plan.arguments.take(2))
        assertFalse(plan.arguments.contains("watch"))
    }

    @Test
    fun `working directory is the project folder, not the solution root`() {
        // Microsoft.iOS resolves the .app bundle relative to the current directory; running from the
        // repo root is the documented `MT0069: The app directory does not exist`.
        val plan = LaunchPlanner.plan(head("SampleApp.Web", Backend.WEB, "net10.0"), LaunchOptions())

        assertEquals(File("/repo/sample/SampleApp.Web").path, plan.workingDirectory)
    }

    @Test
    fun `dev tools port is passed as an environment variable`() {
        val plan = LaunchPlanner.plan(
            head("SampleApp.Skia.Silk", Backend.SKIA, "net10.0"),
            LaunchOptions(devToolsPort = 51799),
        )

        assertEquals("51799", plan.environment[LaunchPlanner.DEV_TOOLS_PORT_VARIABLE])
    }

    @Test
    fun `no dev tools port means no variable at all, so the app never listens`() {
        val plan = LaunchPlanner.plan(head("SampleApp.Skia.Silk", Backend.SKIA, "net10.0"), LaunchOptions())

        assertFalse(plan.environment.containsKey(LaunchPlanner.DEV_TOOLS_PORT_VARIABLE))
    }

    // ---- deployed heads ------------------------------------------------------------------------

    @Test
    fun `ios head deploys with dotnet build -t Run and asks for the interpreter`() {
        val plan = LaunchPlanner.plan(
            head("SampleApp", Backend.APPLE, "net10.0-ios"),
            LaunchOptions(deviceId = "ABC-123"),
        )

        assertEquals("dotnet", plan.exePath)
        assertTrue(plan.arguments.containsAll(listOf("build", "-t:Run", "-f", "net10.0-ios")))
        // Without this the SDK hard-errors: "Can't use Hot Reload or 'dotnet watch' unless the
        // interpreter is enabled."
        assertTrue(plan.arguments.contains("-p:${LaunchPlanner.HOT_RELOAD_PROPERTY}=true"))
        assertTrue(plan.arguments.contains("-p:_DeviceName=:v2:udid=ABC-123"))
    }

    @Test
    fun `ios without watch does not force the interpreter`() {
        // The interpreter is opt-in precisely so an ordinary deploy is not silently slowed down.
        val plan = LaunchPlanner.plan(
            head("SampleApp", Backend.APPLE, "net10.0-ios"),
            LaunchOptions(watch = false),
        )

        assertFalse(plan.arguments.any { it.startsWith("-p:${LaunchPlanner.HOT_RELOAD_PROPERTY}") })
    }

    @Test
    fun `android head deploys and uses the adb target flag`() {
        val plan = LaunchPlanner.plan(
            head("SampleApp", Backend.ANDROID, "net10.0-android"),
            LaunchOptions(deviceId = "emulator-5554"),
        )

        assertTrue(plan.arguments.contains("-t:Run"))
        assertTrue(plan.arguments.contains("-p:AdbTarget=-s emulator-5554"))
        // Android does not need the interpreter; asking for it would just slow the app down.
        assertFalse(plan.arguments.any { it.startsWith("-p:${LaunchPlanner.HOT_RELOAD_PROPERTY}") })
    }

    @Test
    fun `macos head runs locally rather than deploying`() {
        // A net10.0-macos app is an Apple head that still just runs on this machine.
        val plan = LaunchPlanner.plan(head("SampleApp", Backend.APPLE, "net10.0-macos"), LaunchOptions())

        assertTrue(plan.arguments.contains("watch"))
        assertFalse(plan.arguments.contains("-t:Run"))
    }

    @Test
    fun `mac catalyst head runs locally too`() {
        val plan = LaunchPlanner.plan(
            head("SampleApp.Skia.Maui", Backend.SKIA_MAUI, "net10.0-maccatalyst"),
            LaunchOptions(),
        )

        assertFalse(plan.arguments.contains("-t:Run"))
    }

    @Test
    fun `maui ios head deploys like any other ios head`() {
        val plan = LaunchPlanner.plan(
            head("SampleApp.Skia.Maui", Backend.SKIA_MAUI, "net10.0-ios"),
            LaunchOptions(),
        )

        assertTrue(plan.arguments.contains("-t:Run"))
    }

    @Test
    fun `extra msbuild properties are passed through`() {
        val plan = LaunchPlanner.plan(
            head("SampleApp.Gtk", Backend.GTK, "net10.0"),
            LaunchOptions(extraMsBuildProperties = mapOf("Foo" to "Bar")),
        )

        assertTrue(plan.arguments.contains("-p:Foo=Bar"))
    }

    @Test
    fun `program arguments go after a bare double dash`() {
        val plan = LaunchPlanner.plan(
            head("SampleApp.Skia.Silk", Backend.SKIA, "net10.0"),
            LaunchOptions(programArguments = "--verbose --seed 7"),
        )

        val separator = plan.arguments.indexOf("--")
        assertTrue("expected a -- separator", separator > 0)
        assertEquals(listOf("--verbose", "--seed", "7"), plan.arguments.drop(separator + 1))
    }

    // ---- the OS gate ---------------------------------------------------------------------------

    @Test
    fun `apple heads are offered only on a mac`() {
        assertEquals(Supported.Yes, OsGate.supports(HostOs.MACOS, Backend.APPLE))
        assertTrue(OsGate.supports(HostOs.WINDOWS, Backend.APPLE) is Supported.No)
        assertTrue(OsGate.supports(HostOs.LINUX, Backend.APPLE) is Supported.No)
    }

    @Test
    fun `winui heads are offered only on windows`() {
        assertEquals(Supported.Yes, OsGate.supports(HostOs.WINDOWS, Backend.WINDOWS))
        assertTrue(OsGate.supports(HostOs.MACOS, Backend.WINDOWS) is Supported.No)
    }

    @Test
    fun `skia web and android are offered everywhere`() {
        for (host in HostOs.entries) {
            assertEquals(Supported.Yes, OsGate.supports(host, Backend.SKIA))
            assertEquals(Supported.Yes, OsGate.supports(host, Backend.WEB))
            assertEquals(Supported.Yes, OsGate.supports(host, Backend.ANDROID))
        }
    }

    @Test
    fun `gtk is offered everywhere because it is a plain net10 executable`() {
        // It builds anywhere and runs wherever the GTK4 native libraries are installed
        // (`brew install gtk4`). Gating it to Linux would hide a head that works on a Mac.
        for (host in HostOs.entries)
            assertEquals(Supported.Yes, OsGate.supports(host, Backend.GTK))
    }

    @Test
    fun `an apple tfm is gated even when its project also builds elsewhere`() {
        // sample/SampleApp is one project with android, ios, tvos, macos and windows heads. The gate has
        // to work per TFM or a Windows developer is offered an iOS build from a project that is
        // otherwise perfectly buildable there.
        val iosHead = head("SampleApp", Backend.APPLE, "net10.0-ios")
        val androidHead = head("SampleApp", Backend.ANDROID, "net10.0-android")

        assertTrue(OsGate.supports(HostOs.WINDOWS, iosHead) is Supported.No)
        assertEquals(Supported.Yes, OsGate.supports(HostOs.WINDOWS, androidHead))
    }

    @Test
    fun `the reason a head is unavailable is worth reading`() {
        val reason = OsGate.supports(HostOs.LINUX, head("SampleApp", Backend.APPLE, "net10.0-tvos"))

        assertTrue(reason is Supported.No)
        assertTrue((reason as Supported.No).reason.contains("Mac"))
    }
}
