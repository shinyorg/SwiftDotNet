package com.swiftdotnet.rider

import com.swiftdotnet.rider.model.Backend
import com.swiftdotnet.rider.mobile.DeviceLister
import com.swiftdotnet.rider.msbuild.HeadDiscovery
import com.swiftdotnet.rider.run.LaunchOptions
import com.swiftdotnet.rider.run.LaunchPlanner
import org.junit.Assert.assertEquals
import org.junit.Assert.assertNotNull
import org.junit.Assert.assertTrue
import org.junit.Assume.assumeTrue
import org.junit.Test
import java.io.File

/**
 * Integration tests that drive the plugin's real code against **this machine**: MSBuild is actually
 * invoked, `simctl` and `adb` are actually run, and the resulting command lines are the ones a developer
 * gets when they press Run.
 *
 * Everything here is `assumeTrue`-guarded, so on a machine without the repository, without Xcode or
 * without the Android SDK the tests skip instead of failing. A green run on a machine with none of those
 * proves nothing — which is why the guards report *why* they skipped.
 *
 * The mobile heads are the reason these exist. The unit tests in [LaunchPlanTest] assert what the planner
 * produces from a synthetic [com.swiftdotnet.rider.model.Head]; these assert that discovery finds the
 * real iOS and Android heads in `sample/SampleApp` in the first place, which is the step that depends on
 * MSBuild evaluation behaving the way `msbuild/SwiftDotNet.Platform.targets` assumes.
 */
class MobileIntegrationTest {

    private val repository: File? by lazy {
        // The tests run from tooling/rider, so the repository root is two levels up.
        generateSequence(File(System.getProperty("user.dir"))) { it.parentFile }
            .firstOrNull { File(it, "SwiftDotNet.slnx").isFile }
    }

    private fun requireRepository(): File {
        val root = repository
        assumeTrue("not running inside the SwiftDotNet repository", root != null)
        return root!!
    }

    // ---- discovery -----------------------------------------------------------------------------

    @Test
    fun `discovery finds the iOS and Android heads in the sample app`() {
        val root = requireRepository()
        val sampleApp = File(root, "sample/SampleApp/SampleApp.csproj")
        assumeTrue("sample/SampleApp is missing", sampleApp.isFile)

        val heads = HeadDiscovery().headsIn(sampleApp)

        // One project, several apps. Which ones exist depends on the host OS — SampleApp.csproj only
        // adds the Apple TFMs on macOS — so Android is the assertion that must hold everywhere.
        val android = heads.firstOrNull { it.targetFramework == "net10.0-android" }
        assertNotNull("no Android head discovered in SampleApp: $heads", android)
        assertEquals(Backend.ANDROID, android!!.backend)

        if (System.getProperty("os.name").lowercase().contains("mac")) {
            val ios = heads.firstOrNull { it.targetFramework == "net10.0-ios" }
            assertNotNull("no iOS head discovered on a Mac: $heads", ios)
            assertEquals(Backend.APPLE, ios!!.backend)

            val tvos = heads.firstOrNull { it.targetFramework == "net10.0-tvos" }
            assertNotNull("no tvOS head discovered on a Mac: $heads", tvos)
            assertEquals(Backend.APPLE, tvos!!.backend)
        }
    }

    @Test
    fun `an Android app head is found even though its OutputType is Library`() {
        // The trap that made the first version of the MSBuild contract wrong: .NET Android builds an app
        // as OutputType=Library and marks it with AndroidApplication=true. Testing OutputType alone hides
        // every Android head, which is exactly the head this goal is about.
        val root = requireRepository()
        val sampleApp = File(root, "sample/SampleApp/SampleApp.csproj")
        assumeTrue("sample/SampleApp is missing", sampleApp.isFile)

        val android = HeadDiscovery().headsIn(sampleApp).firstOrNull { it.backend == Backend.ANDROID }

        assertNotNull(android)
        assertEquals("Library", android!!.outputType)
    }

    @Test
    fun `a shared UI library is not offered as a head`() {
        val root = requireRepository()
        val sharedUi = File(root, "sample/SharedUI/SharedUI.csproj")
        assumeTrue("sample/SharedUI is missing", sharedUi.isFile)

        // SharedUI also targets net10.0-android. If "multi-targets an Android TFM" were the test, this
        // library would show up in the run-configuration dropdown as an app.
        assertTrue(HeadDiscovery().headsIn(sharedUi).isEmpty())
    }

    // ---- devices -------------------------------------------------------------------------------

    @Test
    fun `simctl lists a real iOS simulator`() {
        assumeTrue("not macOS", System.getProperty("os.name").lowercase().contains("mac"))
        val simulators = DeviceLister.appleSimulators("net10.0-ios")
        assumeTrue("no iOS simulators installed", simulators.isNotEmpty())

        val first = simulators.first()
        // A UDID, not a name: this is what gets passed as -p:_DeviceName=:v2:udid=…
        assertEquals("expected a UDID, got '${first.id}'", 4, first.id.count { it == '-' })
        assertTrue(first.name.isNotBlank())
    }

    @Test
    fun `adb lists a running emulator or device`() {
        val devices = DeviceLister.androidDevices()
        assumeTrue("no Android device or emulator attached", devices.isNotEmpty())

        assertTrue(devices.all { it.id.isNotBlank() })
        assertTrue(devices.all { it.booted })
    }

    @Test
    fun `adb is found without ANDROID_HOME or PATH`() {
        // The failure this guards against: a machine with a running emulator, an SDK installed by Android
        // Studio, and neither ANDROID_HOME nor platform-tools on PATH — which is the default state, and
        // which produced an empty device picker before DeviceLister looked in the conventional places.
        assumeTrue(
            "no Android SDK in a conventional location",
            File(System.getProperty("user.home"), "Library/Android/sdk/platform-tools/adb").canExecute() ||
                File(System.getProperty("user.home"), "Android/Sdk/platform-tools/adb").canExecute(),
        )

        assertTrue("adb not resolved to a real path: ${DeviceLister.adb()}", File(DeviceLister.adb()).canExecute())
    }

    // ---- the launch commands -------------------------------------------------------------------

    @Test
    fun `the iOS head plans the command that actually deploys`() {
        val root = requireRepository()
        assumeTrue("not macOS", System.getProperty("os.name").lowercase().contains("mac"))
        val sampleApp = File(root, "sample/SampleApp/SampleApp.csproj")
        assumeTrue("sample/SampleApp is missing", sampleApp.isFile)

        val ios = HeadDiscovery().headsIn(sampleApp).firstOrNull { it.targetFramework == "net10.0-ios" }
        assumeTrue("no iOS head", ios != null)

        val plan = LaunchPlanner.plan(ios!!, LaunchOptions(deviceId = "499AF569-C96C-4E5E-9361-CCEF93410629"))

        // Verified by hand against a booted simulator: this command builds, installs and launches.
        assertTrue(plan.arguments.containsAll(listOf("build", "-t:Run", "-f", "net10.0-ios", "-c", "Debug")))
        assertTrue(plan.arguments.contains("-p:_DeviceName=:v2:udid=499AF569-C96C-4E5E-9361-CCEF93410629"))
        assertTrue(plan.arguments.contains("-p:SwiftDotNetHotReload=true"))
        assertEquals(File(root, "sample/SampleApp").path, plan.workingDirectory)
    }

    @Test
    fun `the Android head plans the command that actually deploys`() {
        val root = requireRepository()
        val sampleApp = File(root, "sample/SampleApp/SampleApp.csproj")
        assumeTrue("sample/SampleApp is missing", sampleApp.isFile)

        val android = HeadDiscovery().headsIn(sampleApp).firstOrNull { it.targetFramework == "net10.0-android" }
        assumeTrue("no Android head", android != null)

        val plan = LaunchPlanner.plan(android!!, LaunchOptions(deviceId = "emulator-5554"))

        assertTrue(plan.arguments.containsAll(listOf("build", "-t:Run", "-f", "net10.0-android")))
        assertTrue(plan.arguments.contains("-p:AdbTarget=-s emulator-5554"))
        // Android does not use the Mono interpreter; asking for it would slow the app down for nothing.
        assertTrue(plan.arguments.none { it.startsWith("-p:SwiftDotNetHotReload") })
    }

    @Test
    fun `the resolved dotnet is a real executable`() {
        val dotnet = com.swiftdotnet.rider.run.SwiftDotNetRunConfiguration.resolveDotNet()

        assertTrue("dotnet not resolved: $dotnet", dotnet == "dotnet" || File(dotnet).canExecute())
    }
}
