plugins {
    id("com.android.library")
    id("org.jetbrains.kotlin.android")
    id("org.jetbrains.kotlin.plugin.compose")
}

// → wear/build/outputs/aar/wear-release.aar, copied to build/SwiftDotNetWearBridge.aar and bound by
// src/SwiftDotNet.Wear. The Compose/Wear versions must match what that project pulls from NuGet
// (Xamarin.AndroidX.Wear.Compose.* 1.6.2.1 → Compose 1.11.x), since the runtime classes come from there.
android {
    namespace = "com.swiftdotnet.wear"
    compileSdk = 36
    buildToolsVersion = "36.1.0"

    defaultConfig {
        // Wear OS 3 (API 30) is the floor for Compose for Wear OS.
        minSdk = 30
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
    // The interpreter itself: compile against it, never bundle it — the app already carries the main .aar.
    compileOnly(project(":"))
    implementation("androidx.wear.compose:compose-foundation:1.6.2")
    implementation("androidx.wear.compose:compose-material:1.6.2")
    implementation("androidx.compose.material3:material3:1.4.0")
    implementation("androidx.activity:activity-compose:1.13.0")
}
