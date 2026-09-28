pluginManagement {
    repositories {
        google()
        mavenCentral()
        gradlePluginPortal()
    }
}
dependencyResolutionManagement {
    repositories {
        google()
        mavenCentral()
    }
}
rootProject.name = "SwiftDotNetComposeBridge"

// Wear OS companion: Compose-for-Wear overrides for the built-in node types. A separate .aar so phone apps
// never carry the androidx.wear.compose graph — see plans/wear-os-plan.md.
include(":wear")
