plugins {
    id("com.android.library") version "8.11.1"
    id("org.jetbrains.kotlin.android") version "2.1.0"
    id("org.jetbrains.kotlin.plugin.compose") version "2.1.0"
}

android {
    namespace = "com.swiftdotnet.bridge"
    compileSdk = 36
    buildToolsVersion = "36.1.0"

    defaultConfig {
        minSdk = 24
    }

    buildFeatures {
        compose = true
    }

    compileOptions {
        sourceCompatibility = JavaVersion.VERSION_17
        targetCompatibility = JavaVersion.VERSION_17
    }
}

kotlin {
    compilerOptions {
        jvmTarget.set(org.jetbrains.kotlin.gradle.dsl.JvmTarget.JVM_17)
    }
}

dependencies {
    // BOM 2026.09.00 = Compose UI/Foundation 1.12.1 + Material3 1.4.0 — the versions SwiftDotNet.csproj takes
    // from NuGet (the runtime classes come from there, not from this .aar). 1.12 is forced by the
    // material3-adaptive 1.3.0 binding, which requires Compose UI >= 1.12.0.
    implementation(platform("androidx.compose:compose-bom:2026.09.00"))
    implementation("androidx.compose.ui:ui")
    implementation("androidx.compose.foundation:foundation")
    implementation("androidx.compose.material3:material3")
    implementation("androidx.activity:activity-compose:1.9.3")
    // Foldables: WindowManager for the posture reporter, material3-adaptive for the native TwoPaneView.
    // Must match Xamarin.AndroidX.Window 1.5.1.x / Xamarin.AndroidX.Compose.Material3.Adaptive.Layout 1.3.0.
    implementation("androidx.window:window:1.5.1")
    implementation("androidx.compose.material3.adaptive:adaptive-layout:1.3.0")
}
