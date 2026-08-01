package com.swiftdotnet.rider.run

import com.intellij.execution.configurations.ConfigurationFactory
import com.intellij.execution.configurations.ConfigurationType
import com.intellij.execution.configurations.ConfigurationTypeBase
import com.intellij.execution.configurations.ConfigurationTypeUtil
import com.intellij.execution.configurations.RunConfiguration
import com.intellij.icons.AllIcons
import com.intellij.openapi.project.DumbAware
import com.intellij.openapi.project.Project
import com.jetbrains.rider.run.configurations.RiderConfigurationParametersAware
import com.jetbrains.rider.run.configurations.dotNetExe.DotNetExeConfigurationParameters
import com.jetbrains.rider.run.configurations.dotNetExe.DotNetExeConfigurationType

/**
 * The "SwiftDotNet App" run configuration type.
 *
 * One type with a head picker, rather than a generated configuration per head: a solution has up to nine
 * heads and the set changes with the operating system, so N configurations would be N things to keep in
 * sync. See `plans/rider-plugin-plan.md` Decision 3.
 */
class SwiftDotNetConfigurationType : ConfigurationTypeBase(
    ID,
    "SwiftDotNet App",
    "Run a SwiftDotNet head with hot reload, on whichever backends this OS can build",
    AllIcons.RunConfigurations.Application,
), DumbAware {

    init {
        addFactory(SwiftDotNetConfigurationFactory(this))
    }

    override fun getHelpTopic(): String? = null

    companion object {
        const val ID = "SwiftDotNetApp"

        fun getInstance(): SwiftDotNetConfigurationType =
            ConfigurationTypeUtil.findConfigurationType(SwiftDotNetConfigurationType::class.java)
    }
}

class SwiftDotNetConfigurationFactory(type: ConfigurationType) : ConfigurationFactory(type) {

    override fun getId(): String = SwiftDotNetConfigurationType.ID

    override fun getName(): String = "SwiftDotNet App"

    override fun createTemplateConfiguration(project: Project): RunConfiguration =
        SwiftDotNetRunConfiguration("SwiftDotNet", project, this, defaultParameters(project))

    override fun isEditableInDumbMode(): Boolean = true

    companion object {
        /**
         * Borrows a fully-defaulted parameter object from Rider's own "Executable" configuration rather
         * than calling the sixteen-argument constructor.
         *
         * This is the difference between depending on Rider's *defaults* and depending on the exact
         * order of its constructor arguments — and only one of those two survives an IDE upgrade
         * quietly. The fields that matter are set from the launch plan at execution time anyway.
         */
        fun defaultParameters(project: Project): DotNetExeConfigurationParameters {
            val exeType = ConfigurationTypeUtil.findConfigurationType(DotNetExeConfigurationType::class.java)
            val template = exeType.factory.createTemplateConfiguration(project)

            // Reached through the parameters-aware *interface* rather than by casting to
            // DotNetExeConfiguration. That class implements interfaces from a Rider module this plugin
            // does not depend on, and Kotlin has to resolve every supertype of a type it is asked to
            // name — so naming it drags in a classpath entry for no benefit.
            return (template as RiderConfigurationParametersAware<*>).parameters as DotNetExeConfigurationParameters
        }
    }
}
