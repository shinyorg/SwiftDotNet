package com.swiftdotnet.rider.run

import com.intellij.openapi.application.ApplicationManager
import com.intellij.openapi.options.SettingsEditor
import com.intellij.openapi.project.Project
import com.intellij.openapi.ui.ComboBox
import com.intellij.ui.SimpleListCellRenderer
import com.intellij.ui.components.JBCheckBox
import com.intellij.ui.components.JBLabel
import com.intellij.ui.components.JBTextField
import com.intellij.util.ui.FormBuilder
import com.intellij.util.ui.UIUtil
import com.swiftdotnet.rider.model.Head
import com.swiftdotnet.rider.model.HostOs
import com.swiftdotnet.rider.mobile.DeviceLister
import com.swiftdotnet.rider.services.SwiftDotNetHeads
import javax.swing.DefaultComboBoxModel
import javax.swing.JButton
import javax.swing.JComponent
import javax.swing.JPanel

/**
 * The run-configuration editor: pick a head, decide whether to watch, pick a device.
 *
 * The list shows heads this OS **cannot** build as well, greyed and with the reason. Hiding them would
 * be tidier and worse — the platform matrix is the framework's entire pitch, and a Windows developer
 * who cannot find the iOS head should be told why rather than left to conclude it does not exist.
 */
class SwiftDotNetConfigurationEditor(private val project: Project) : SettingsEditor<SwiftDotNetRunConfiguration>() {

    private data class HeadEntry(val head: Head?, val label: String, val enabled: Boolean, val id: String)

    private val headModel = DefaultComboBoxModel<HeadEntry>()
    private val headCombo = ComboBox(headModel).apply {
        renderer = SimpleListCellRenderer.create { label, entry, _ ->
            label.text = entry?.label.orEmpty()
            label.isEnabled = entry?.enabled ?: true
        }
    }

    private val refreshButton = JButton("Refresh")
    private val status = JBLabel(" ")

    private val configurationCombo = ComboBox(DefaultComboBoxModel(arrayOf("Debug", "Release")))
    private val watchCheck = JBCheckBox("Hot reload (run under dotnet watch)", true)
    private val devToolsCheck = JBCheckBox("Attach the inspector and preview (dev-tools channel)", true)
    private val deviceCombo = ComboBox(DefaultComboBoxModel(arrayOf("")))
    private val argumentsField = JBTextField()
    private val propertiesField = JBTextField()

    init {
        headCombo.addActionListener { onHeadChanged() }
        refreshButton.addActionListener { refreshHeads() }

        deviceCombo.isEditable = true
        populateHeads(SwiftDotNetHeads.getInstance(project))
        if (headModel.size == 0) refreshHeads()
    }

    override fun createEditor(): JComponent {
        val headRow = JPanel(java.awt.BorderLayout(6, 0)).apply {
            add(headCombo, java.awt.BorderLayout.CENTER)
            add(refreshButton, java.awt.BorderLayout.EAST)
        }

        return FormBuilder.createFormBuilder()
            .addLabeledComponent("Head:", headRow)
            .addComponentToRightColumn(status)
            .addLabeledComponent("Configuration:", configurationCombo)
            .addComponentToRightColumn(watchCheck)
            .addComponentToRightColumn(devToolsCheck)
            .addLabeledComponent("Device:", deviceCombo)
            .addLabeledComponent("Program arguments:", argumentsField)
            .addLabeledComponent("MSBuild properties:", propertiesField)
            .addComponentToRightColumn(JBLabel("Semicolon-separated, e.g. Foo=Bar;Baz=Qux").apply {
                componentStyle = UIUtil.ComponentStyle.SMALL
            })
            .panel
    }

    override fun resetEditorFrom(configuration: SwiftDotNetRunConfiguration) {
        selectHead(configuration.headId)
        configurationCombo.selectedItem = configuration.buildConfiguration
        watchCheck.isSelected = configuration.watch
        devToolsCheck.isSelected = configuration.attachDevTools
        argumentsField.text = configuration.programArguments
        propertiesField.text = configuration.extraProperties
        deviceCombo.selectedItem = configuration.deviceId
        onHeadChanged()
    }

    override fun applyEditorTo(configuration: SwiftDotNetRunConfiguration) {
        configuration.headId = (headModel.selectedItem as? HeadEntry)?.id.orEmpty()
        configuration.buildConfiguration = configurationCombo.selectedItem as? String ?: "Debug"
        configuration.watch = watchCheck.isSelected
        configuration.attachDevTools = devToolsCheck.isSelected
        configuration.deviceId = (deviceCombo.selectedItem as? String).orEmpty()
        configuration.programArguments = argumentsField.text
        configuration.extraProperties = propertiesField.text
    }

    private fun refreshHeads() {
        refreshButton.isEnabled = false
        status.text = "Discovering heads…"
        val service = SwiftDotNetHeads.getInstance(project)
        service.refresh {
            ApplicationManager.getApplication().invokeLater {
                val selected = (headModel.selectedItem as? HeadEntry)?.id
                populateHeads(service)
                selected?.let(::selectHead)
                refreshButton.isEnabled = true
            }
        }
    }

    private fun populateHeads(service: SwiftDotNetHeads) {
        val host = HostOs.current()
        headModel.removeAllElements()

        service.runnableHeads(host).forEach {
            headModel.addElement(HeadEntry(it, it.displayName, enabled = true, id = it.id))
        }
        service.unavailableHeads(host).forEach { (head, reason) ->
            headModel.addElement(
                HeadEntry(head, "${head.displayName} — unavailable: $reason", enabled = false, id = head.id))
        }

        status.text = when {
            service.lastRefreshFailed -> "Head discovery failed — see the IDE log."
            headModel.size == 0 -> "No heads found. Add <SwiftDotNetPlatform> to an app project."
            else -> "${service.runnableHeads(host).size} runnable on ${host.name.lowercase()}"
        }
    }

    private fun selectHead(id: String) {
        for (i in 0 until headModel.size) {
            if (headModel.getElementAt(i).id == id) {
                headModel.selectedItem = headModel.getElementAt(i)
                return
            }
        }
    }

    /** Only deployed heads need a device, so the picker is populated (and enabled) only for those. */
    private fun onHeadChanged() {
        val head = (headModel.selectedItem as? HeadEntry)?.head
        val needsDevice = head != null && head.backend.isDeployedToADevice(head.targetFramework)
        deviceCombo.isEnabled = needsDevice
        if (!needsDevice || head == null) return

        ApplicationManager.getApplication().executeOnPooledThread {
            val devices = DeviceLister.devicesFor(head)
            ApplicationManager.getApplication().invokeLater {
                val previous = deviceCombo.selectedItem
                deviceCombo.model = DefaultComboBoxModel(
                    (listOf("") + devices.map { it.id }).toTypedArray())
                deviceCombo.selectedItem = previous
            }
        }
    }
}
