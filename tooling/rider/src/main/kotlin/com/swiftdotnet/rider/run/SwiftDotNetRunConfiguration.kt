package com.swiftdotnet.rider.run

import com.intellij.execution.Executor
import com.intellij.execution.configurations.ConfigurationFactory
import com.intellij.execution.configurations.RunConfiguration
import com.intellij.execution.configurations.RunConfigurationOptions
import com.intellij.execution.configurations.RunProfileState
import com.intellij.execution.configurations.RuntimeConfigurationError
import com.intellij.execution.runners.ExecutionEnvironment
import com.intellij.openapi.options.SettingsEditor
import com.intellij.openapi.project.Project
import com.intellij.openapi.util.JDOMExternalizerUtil
import com.intellij.util.execution.ParametersListUtil
import com.jetbrains.rider.debugger.IRiderDebuggable
import com.jetbrains.rider.run.configurations.RiderAsyncRunConfiguration
import com.jetbrains.rider.run.configurations.dotNetExe.DotNetExeConfigurationParameters
import com.jetbrains.rider.run.configurations.dotNetExe.DotNetExeExecutorFactory
import com.swiftdotnet.rider.model.HostOs
import com.swiftdotnet.rider.model.OsGate
import com.swiftdotnet.rider.model.Supported
import com.swiftdotnet.rider.services.SwiftDotNetHeads
import com.swiftdotnet.rider.services.SwiftDotNetSession
import org.jdom.Element
import org.jetbrains.concurrency.Promise
import java.io.File

/**
 * A head, plus how to launch it.
 *
 * The configuration extends Rider's own async configuration and hands it a
 * [DotNetExeExecutorFactory]. That one decision is what makes **Run, Debug and hot reload all work
 * without this plugin containing a debugger, a launcher, or a reload mechanism** — Rider's runners
 * accept the state that factory produces, and `DotNetHotReloadConfigurationExecutorExtension` is already
 * wired into that path. See `plans/rider-plugin-plan.md` §2.
 *
 * What the plugin *does* contribute is everything above the launch: which heads exist, which of them
 * this operating system can build, and the exact command line each one needs.
 */
class SwiftDotNetRunConfiguration(
    name: String,
    project: Project,
    factory: ConfigurationFactory,
    val parameters: DotNetExeConfigurationParameters,
) : RiderAsyncRunConfiguration(
    name,
    project,
    factory,
    { p -> SwiftDotNetConfigurationEditor(p) },
    DotNetExeExecutorFactory(parameters),
), IRiderDebuggable {

    /** Id of the selected head — `SampleApp (net10.0-ios)`. Stored by id so it survives a rediscovery. */
    var headId: String = ""

    var buildConfiguration: String = "Debug"
    var watch: Boolean = true
    var attachDevTools: Boolean = true
    var deviceId: String = ""
    var programArguments: String = ""
    var extraProperties: String = ""

    /** The port the dev-tools channel bound to on the last launch, for the tool windows to attach to. */
    @Volatile
    var lastDevToolsPort: Int = 0
        private set

    override fun getStateAsync(executor: Executor, environment: ExecutionEnvironment): Promise<RunProfileState> {
        applyLaunchPlan()
        return super.getStateAsync(executor, environment)
    }

    /**
     * Turn the head plus the options into the command line Rider will execute.
     *
     * Done here rather than in the editor because the answer depends on things that can change between
     * editing and launching — which device is booted, whether a free port is still free.
     */
    fun applyLaunchPlan() {
        val head = resolveHead() ?: throw RuntimeConfigurationError("No SwiftDotNet head selected.")

        val port = if (attachDevTools) freePort() else null
        lastDevToolsPort = port ?: 0

        val plan = LaunchPlanner.plan(
            head,
            LaunchOptions(
                configuration = buildConfiguration,
                watch = watch,
                deviceId = deviceId.takeIf { it.isNotBlank() },
                devToolsPort = port,
                extraMsBuildProperties = parseProperties(extraProperties),
                programArguments = programArguments,
            ),
            dotnet = resolveDotNet(),
        )

        parameters.exePath = plan.exePath
        parameters.programParameters = ParametersListUtil.join(plan.arguments)
        parameters.workingDirectory = plan.workingDirectory
        parameters.envs = plan.environment
        parameters.isPassParentEnvs = true

        // `dotnet watch` and `dotnet build -t:Run` both launch the app as a *child* process, so without
        // this the debugger would attach to the build driver and never reach a breakpoint in a view.
        parameters.autoAttachToChildren = true

        // Remembered so the Inspector and Preview tool windows can attach with one click instead of
        // making the developer read a port number out of the run console.
        port?.let { SwiftDotNetSession.getInstance(project).record(it, head.displayName) }
    }

    fun resolveHead() = SwiftDotNetHeads.getInstance(project).findById(headId)

    override fun checkConfiguration() {
        val heads = SwiftDotNetHeads.getInstance(project)
        if (headId.isBlank())
            throw RuntimeConfigurationError("Select a SwiftDotNet head to run.")

        val head = heads.findById(headId)
            ?: throw RuntimeConfigurationError(
                "Head '$headId' was not found. Press Refresh to rediscover the heads in this solution.")

        // Better to say "this needs a Mac" in the configuration dialog than to let MSBuild say
        // something longer and less clear two minutes into a build.
        (OsGate.supports(HostOs.current(), head) as? Supported.No)?.let {
            throw RuntimeConfigurationError("${head.displayName} cannot be built here: ${it.reason}")
        }
    }

    override fun getConfigurationEditor(): SettingsEditor<out RunConfiguration> =
        SwiftDotNetConfigurationEditor(project)

    // ---- persistence ---------------------------------------------------------------------------

    override fun writeExternal(element: Element) {
        super.writeExternal(element)
        JDOMExternalizerUtil.writeField(element, HEAD, headId)
        JDOMExternalizerUtil.writeField(element, CONFIGURATION, buildConfiguration)
        JDOMExternalizerUtil.writeField(element, WATCH, watch.toString())
        JDOMExternalizerUtil.writeField(element, DEV_TOOLS, attachDevTools.toString())
        JDOMExternalizerUtil.writeField(element, DEVICE, deviceId)
        JDOMExternalizerUtil.writeField(element, ARGUMENTS, programArguments)
        JDOMExternalizerUtil.writeField(element, PROPERTIES, extraProperties)
    }

    override fun readExternal(element: Element) {
        super.readExternal(element)
        headId = JDOMExternalizerUtil.readField(element, HEAD).orEmpty()
        buildConfiguration = JDOMExternalizerUtil.readField(element, CONFIGURATION) ?: "Debug"
        watch = JDOMExternalizerUtil.readField(element, WATCH)?.toBoolean() ?: true
        attachDevTools = JDOMExternalizerUtil.readField(element, DEV_TOOLS)?.toBoolean() ?: true
        deviceId = JDOMExternalizerUtil.readField(element, DEVICE).orEmpty()
        programArguments = JDOMExternalizerUtil.readField(element, ARGUMENTS).orEmpty()
        extraProperties = JDOMExternalizerUtil.readField(element, PROPERTIES).orEmpty()
    }

    override fun clone(): RunConfiguration {
        val copy = super.clone() as SwiftDotNetRunConfiguration
        copy.headId = headId
        copy.buildConfiguration = buildConfiguration
        copy.watch = watch
        copy.attachDevTools = attachDevTools
        copy.deviceId = deviceId
        copy.programArguments = programArguments
        copy.extraProperties = extraProperties
        return copy
    }

    companion object {
        private const val HEAD = "SwiftDotNetHead"
        private const val CONFIGURATION = "SwiftDotNetConfiguration"
        private const val WATCH = "SwiftDotNetWatch"
        private const val DEV_TOOLS = "SwiftDotNetDevTools"
        private const val DEVICE = "SwiftDotNetDevice"
        private const val ARGUMENTS = "SwiftDotNetArguments"
        private const val PROPERTIES = "SwiftDotNetProperties"

        /** `Foo=Bar;Baz=Qux` → a property map. Blank entries are dropped rather than passed as `-p:=`. */
        fun parseProperties(text: String): Map<String, String> =
            text.split(';', '\n')
                .mapNotNull { entry ->
                    val separator = entry.indexOf('=')
                    if (separator <= 0) null
                    else entry.substring(0, separator).trim() to entry.substring(separator + 1).trim()
                }
                .filter { it.first.isNotEmpty() }
                .toMap()

        /**
         * A free loopback port for the dev-tools channel.
         *
         * Racy by nature — something else can take the port between the probe and the app binding it —
         * but the app treats a bind failure as "dev tools off" rather than a startup error, so the worst
         * case is a tool window that does not attach.
         */
        fun freePort(): Int = try {
            java.net.ServerSocket(0).use { it.localPort }
        } catch (_: Exception) {
            0
        }

        /**
         * Absolute path to the `dotnet` binary. Rider launches the configuration without a shell, so
         * PATH lookup has to happen here rather than being assumed.
         */
        fun resolveDotNet(): String {
            System.getenv("DOTNET_HOST_PATH")?.let { if (File(it).canExecute()) return it }

            val executable = if (System.getProperty("os.name").lowercase().contains("win")) "dotnet.exe" else "dotnet"
            System.getenv("PATH")?.split(File.pathSeparatorChar)?.forEach { dir ->
                val candidate = File(dir, executable)
                if (candidate.canExecute()) return candidate.absolutePath
            }
            return executable
        }
    }
}

/** Rider requires an options class for the configuration's persisted state container. */
class SwiftDotNetRunConfigurationOptions : RunConfigurationOptions()
