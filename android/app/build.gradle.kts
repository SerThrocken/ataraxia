plugins {
    id("com.android.application")
    id("org.jetbrains.kotlin.android")
    id("org.jetbrains.kotlin.plugin.compose")
    id("org.jetbrains.kotlin.plugin.serialization")
}

android {
    namespace = "com.tlg3d.hermes"
    compileSdk = 34

    defaultConfig {
        applicationId = "com.tlg3d.hermes"
        minSdk = 26
        targetSdk = 34
        versionCode = 1
        versionName = "0.1.0"
    }

    buildTypes {
        release {
            isMinifyEnabled = false
        }
    }

    buildFeatures {
        compose = true
    }

    compileOptions {
        sourceCompatibility = JavaVersion.VERSION_17
        targetCompatibility = JavaVersion.VERSION_17
    }
    kotlinOptions {
        jvmTarget = "17"
    }
}

dependencies {
    val composeBom = platform("androidx.compose:compose-bom:2024.10.01")
    implementation(composeBom)
    androidTestImplementation(composeBom)

    implementation("androidx.core:core-ktx:1.13.1")
    implementation("androidx.activity:activity-compose:1.9.3")
    implementation("androidx.compose.ui:ui")
    implementation("androidx.compose.ui:ui-graphics")
    implementation("androidx.compose.ui:ui-tooling-preview")
    implementation("androidx.compose.material:material-icons-core")
    implementation("androidx.compose.material:material-icons-extended")
    implementation("androidx.compose.material3:material3")
    implementation("androidx.compose.material3:material3-window-size-class")
    implementation("androidx.navigation:navigation-compose:2.8.5")
    implementation("androidx.lifecycle:lifecycle-viewmodel-compose:2.8.7")

    implementation("com.squareup.okhttp3:okhttp:4.12.0")
    implementation("org.jetbrains.kotlinx:kotlinx-serialization-json:1.8.1")
    implementation("androidx.security:security-crypto:1.1.0-alpha06")

    // Worker D: home-screen widget (Glance) + periodic refresh (WorkManager).
    // Pinned exact versions — see DEPENDENCIES.md for source-verified licenses.
    // 1.1.1, not 1.2.0: glance 1.2.0's AAR metadata requires compileSdk 35 +
    // AGP 8.6+ (checkDebugAarMetadata failure, 2026-10-12); this project is
    // compileSdk 34 / AGP 8.5.2.
    implementation("androidx.glance:glance-appwidget:1.1.1")
    implementation("androidx.work:work-runtime-ktx:2.9.1")

    debugImplementation("androidx.compose.ui:ui-tooling")
    debugImplementation("androidx.compose.ui:ui-test-manifest")

    // JUnit 4 for JVM unit tests. EPL-1.0, TEST SCOPE ONLY — never packaged
    // into the APK (verify: no junit classes in the built dex). The task spec
    // mandates "JUnit tests with real assertions" (TDD), which is the
    // explicit license-gate override for this test-only dependency.
    // See DEPENDENCIES.md.
    testImplementation("junit:junit:4.13.2")
}

// Live-gateway protocol verification with the real OkHttpGatewayClient.
// Debug-only (src/debug); never in release. Needs HERMES_TEST_API_KEY and
// TS_PROXY in the environment. The key is used transiently and never printed.
tasks.register<JavaExec>("verifyGatewayLive") {
    group = "verification"
    description = "Runs the live gateway protocol check (debug classes, JVM)."
    dependsOn("assembleDebug")
    val debugClasses = layout.buildDirectory.dir("tmp/kotlin-classes/debug")
    classpath = files(debugClasses, configurations.named("debugRuntimeClasspath"))
    mainClass.set("com.tlg3d.hermes.verify.VerifyGatewayKt")
}

// Offline checks for run controls + gateway widget (Worker D).
// Debug-only (src/debug); never in release. No network, no Android runtime —
// pure JVM. JUnit 4/5 are EPL-licensed and fail the project's license gate,
// so these are plain Kotlin check() assertions with an exact pass count.
tasks.register<JavaExec>("verifyRunChecks") {
    group = "verification"
    description = "Runs the offline run-control/widget checks (debug classes, JVM)."
    dependsOn("assembleDebug")
    val debugClasses = layout.buildDirectory.dir("tmp/kotlin-classes/debug")
    classpath = files(debugClasses, configurations.named("debugRuntimeClasspath"))
    mainClass.set("com.tlg3d.hermes.verify.RunChecksKt")
}
