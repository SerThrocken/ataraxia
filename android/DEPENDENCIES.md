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
| `com.squareup.okhttp3:okhttp` | 4.12.0 | Apache-2.0 | https://raw.githubusercontent.com/square/okhttp/master/LICENSE.txt |
| `org.jetbrains.kotlinx:kotlinx-serialization-json` | 1.8.1 | Apache-2.0 | https://raw.githubusercontent.com/Kotlin/kotlinx.serialization/master/LICENSE.txt |

## Build tooling

| Artifact | Version | License | Verified at |
|---|---|---|---|
| Android Gradle Plugin | 8.5.2 | Apache-2.0 | https://raw.githubusercontent.com/androidx/androidx/androidx-main/LICENSE.txt (AOSP tooling) |
| Kotlin Gradle plugin (+ compose, serialization) | 2.0.21 | Apache-2.0 | https://raw.githubusercontent.com/JetBrains/kotlin/master/license/LICENSE.txt |
| Gradle | 8.10.2 | Apache-2.0 | https://github.com/gradle/gradle/blob/master/LICENSE |

Verification method: fetched each LICENSE file from the project's own
repository and confirmed the "Apache License, Version 2.0" header.
