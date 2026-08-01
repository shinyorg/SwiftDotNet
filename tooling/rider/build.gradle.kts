import org.jetbrains.intellij.platform.gradle.TestFrameworkType

plugins {
    kotlin("jvm") version "2.3.21"
    id("org.jetbrains.intellij.platform") version "2.18.1"
}

group = "com.swiftdotnet"
version = providers.gradleProperty("pluginVersion").get()

repositories {
    mavenCentral()
    intellijPlatform {
        defaultRepositories()
    }
}

dependencies {
    intellijPlatform {
        // useInstaller = false: the Rider distribution published to the IntelliJ repository, rather than
        // the .dmg/.exe installer — the only form that resolves on every host OS.
        rider(providers.gradleProperty("riderVersion")) {
            useInstaller = false
        }
        testFramework(TestFrameworkType.Platform)
    }

    implementation(kotlin("stdlib"))
    testImplementation(kotlin("test"))
    testImplementation("junit:junit:4.13.2")
}

intellijPlatform {
    // The bytecode instrumenter exists to wire up GUI Designer .form files and @NotNull assertions.
    // Every panel here is built in Kotlin, so there is nothing to instrument — and leaving it on makes
    // the build depend on a JDK layout it does not otherwise need.
    instrumentCode = false

    pluginConfiguration {
        id = "com.swiftdotnet.rider"
        name = "SwiftDotNet"
        version = providers.gradleProperty("pluginVersion")
        vendor {
            name = "SwiftDotNet"
        }
        ideaVersion {
            sinceBuild = providers.gradleProperty("pluginSinceBuild")
            untilBuild = providers.gradleProperty("pluginUntilBuild")
        }
    }

    // The plugin verifier is wired up but not run as part of `build` — it downloads IDEs.
    pluginVerification {
        ides {
            recommended()
        }
    }
}

kotlin {
    jvmToolchain(21)
}

// `gradle runIde` opens the sandbox IDE on this repository, so the plugin has real heads to discover
// rather than an empty project.
//
// `gradle runIde -Pdoctor` instead runs the headless doctor over the same repository — the plugin's
// discovery, OS gate, device listing and launch planning, executed inside the real IDE, with no window
// and nothing to click. That is what makes "does the plugin work?" answerable in CI.
tasks.named<org.jetbrains.intellij.platform.gradle.tasks.RunIdeTask>("runIde") {
    val repository = rootProject.projectDir.parentFile.parentFile.absolutePath
    val doctor = providers.gradleProperty("doctor").isPresent

    argumentProviders.add(CommandLineArgumentProvider {
        if (doctor) listOf("swiftdotnet-doctor", repository) else listOf(repository)
    })
}

tasks.test {
    useJUnit()
}
