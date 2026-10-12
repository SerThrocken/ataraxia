# Dependencies — Hermes Android

Every entry verified at its source on 2026-10-11. License gate:
MIT / Apache-2.0 / BSD / ISC only. No OpenAI products.

## Libraries

| Artifact | Version | License | Verified at |
|---|---|---|---|
| `androidx.compose:compose-bom` | 2024.10.01 | Apache-2.0 | https://raw.githubusercontent.com/androidx/androidx/androidx-main/LICENSE.txt |
| `androidx.core:core-ktx` | 1.13.1 | Apache-2.0 | same as above (AndroidX) |
| `androidx.activity:activity-compose` | 1.9.3 | Apache-2.0 | same as above (AndroidX) |
| `androidx.compose.material:material-icons-core` | BOM-managed | Apache-2.0 | same as above (AndroidX) |
| `androidx.compose.material:material-icons-extended` | BOM-managed | Apache-2.0 | same as above (AndroidX) |
| `androidx.compose.material3:material3` | BOM-managed | Apache-2.0 | same as above (AndroidX) |
| `androidx.compose.material3:material3-window-size-class` | BOM-managed | Apache-2.0 | same as above (AndroidX) |
| `androidx.navigation:navigation-compose` | 2.8.5 | Apache-2.0 | same as above (AndroidX) |
| `androidx.lifecycle:lifecycle-viewmodel-compose` | 2.8.7 | Apache-2.0 | same as above (AndroidX) |
| `androidx.security:security-crypto` | 1.1.0-alpha06 | Apache-2.0 | same as above (AndroidX) |
| `androidx.glance:glance-appwidget` | 1.1.1 | Apache-2.0 | AOSP source headers (android.googlesource.com …/glance-appwidget/…/GlanceAppWidget.kt: "Licensed under the Apache License, Version 2.0"); mvnrepository license table ("The Apache Software License, Version 2.0"); stable 1.1.1 (2024-10-16). Pinned exact. 1.2.0 was tried first and rejected by `checkDebugAarMetadata` (2026-10-12): its AAR metadata requires compileSdk 35 + AGP 8.6+, but this project is compileSdk 34 / AGP 8.5.2. |
| `androidx.work:work-runtime-ktx` | 2.9.1 | Apache-2.0 | same as above (AndroidX). 2.9.x pairs with AGP 8.5 / compileSdk 34. |
| `com.squareup.okhttp3:okhttp` | 4.12.0 | Apache-2.0 | https://raw.githubusercontent.com/square/okhttp/master/LICENSE.txt |
| `org.jetbrains.kotlinx:kotlinx-serialization-json` | 1.8.1 | Apache-2.0 | https://raw.githubusercontent.com/Kotlin/kotlinx.serialization/master/LICENSE.txt |

## Test-only dependencies (never shipped in the APK)

| Artifact | Version | License | Verified at |
|---|---|---|---|
| `junit:junit` | 4.13.2 | EPL-1.0 | https://github.com/junit-team/junit4/blob/main/LICENSE (Eclipse Public License 1.0) |

License-gate note: JUnit 4 is EPL-1.0, outside the standing MIT/Apache-2.0/BSD/ISC
gate. It is `testImplementation` scope only — unit tests run on the JVM and no
JUnit classes are packaged into the APK (dex contains no `org.junit` classes).
The Worker C task spec explicitly mandates "JUnit tests with real assertions"
(TDD), which is the explicit override for this test-only dependency. No runtime,
Android, or shipped dependency was added for share-sheet or voice work: both
use platform APIs only (`android.content.Intent`, `android.speech`,
`android.speech.tts` — part of the Android SDK, no license questions).

## Build tooling

| Artifact | Version | License | Verified at |
|---|---|---|---|
| Android Gradle Plugin | 8.5.2 | Apache-2.0 | https://raw.githubusercontent.com/androidx/androidx/androidx-main/LICENSE.txt (AOSP tooling) |
| Kotlin Gradle plugin (+ compose, serialization) | 2.0.21 | Apache-2.0 | https://raw.githubusercontent.com/JetBrains/kotlin/master/license/LICENSE.txt |
| Gradle | 8.10.2 | Apache-2.0 | https://github.com/gradle/gradle/blob/master/LICENSE |

Verification method: fetched each LICENSE file from the project's own
repository and confirmed the "Apache License, Version 2.0" header.
