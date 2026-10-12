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

## SSH (Worker 4, Phase 4 — 2026-10-12)

| Artifact | Version | License | Verified at |
|---|---|---|---|
| `com.hierynomus:sshj` | 0.40.0 | **Apache-2.0** | (1) The project's own LICENSE file: https://raw.githubusercontent.com/hierynomus/sshj/master/LICENSE — the Apache License, Version 2.0, January 2004 (full text fetched and confirmed). (2) Maven Central POMs for sshj releases declare "The Apache License, Version 2.0": https://central.sonatype.com/artifact/com.hierynomus/sshj/0.37.0 (same project, same license). (3) Corroborated by downstream third-party notices recording SSHJ as Apache-2.0. Used by `com.tlg3d.hermes.ssh.SshjTerminalSession` / `SshjSessionFactory` for SSH terminal sessions behind `TerminalSession` (see TERMINAL-SEAM.md). sshj is the maintained mainstream Java SSH client with an explicit Android configuration (`AndroidConfig`). |
| `com.hierynomus:asn-one` (sshj transitive) | 0.6.0 | **Apache-2.0** | sshj's published POM lists it; its own project (github.com/hierynomus/asn-one) is Apache-2.0 (corroborated by downstream third-party notices). |
| `org.slf4j:slf4j-api` (sshj transitive) | 2.0.x | **MIT** | https://www.slf4j.org/license.html |
| `org.bouncycastle:bcprov-jdk18on`, `bcpkix-jdk18on`, `bcutil-jdk18on` (sshj transitives) | 1.8x | **Bouncy Castle licence (permissive MIT-style adaptation)** | https://www.bouncycastle.org/about/licence.html — the licence is adapted from the MIT X Consortium licence; corroborated by downstream third-party notices recording it as "(MIT)". |
| `net.i2p.crypto:eddsa` (sshj transitive) | 0.3.0 | **CC0-1.0** | Public-domain dedication; no license obligations. |
| `androidx.test:runner`, `androidx.test:core` | 1.6.1 | **Apache-2.0** | AndroidX (same as above). `androidTestImplementation` scope only — the on-device Keystore round-trip test; never in the release APK. |

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

## Investigated, not added (Phase 4: Git integration — 2026-10-12)

Android git is **not shipped**. Verified findings (Worker 2):

- **Eclipse JGit** (`org.eclipse.jgit`) — the only pure-Java git engine —
  is licensed **EDL-1.0** (Eclipse Distribution License), verified at
  https://github.com/eclipse-jgit/jgit/blob/HEAD/NOTICE.md ("made available
  under the terms of the Eclipse Distribution License v1.0", carrying the
  project's own tag "SPDX-License-Identifier: BSD-3-Clause"). EDL-1.0 is not
  literally one of the allowlisted identifiers (MIT / Apache-2.0 / BSD /
  ISC / OFL / CC0); whether Eclipse's BSD-3-Clause SPDX tag satisfies the
  "BSD" entry is a judgment call reserved for Thomas. No Phase 4 exception
  was granted, so JGit was **not added** — it is not shoehorned past the
  gate.
- **git CLI on stock Android**: not present. AOSP's on-device shell toolset
  is toybox and the Android command ecosystem is `am`/`pm`/`cmd`/`dumpsys`/
  `settings`/`input`/`logcat`/`getprop`/`screencap` (audited AOSP research,
  2026-09-22); an app cannot invoke another app's binary, and bundling a
  cross-compiled git binary is unverified provenance and out of scope.
- **Other engines**: libgit2 JNI bindings (git24j) are GPL-2.0 with a
  linking exception — copyleft family, out. Gitblit is Apache-2.0 but is a
  *server* built on top of JGit, not a client engine. No other allowlisted
  pure-Java git engine exists.

If Thomas grants an explicit license decision on EDL-1.0 (or an allowlisted
engine appears), the Android git UI can follow the Windows design:
`IGitRunner`-style seam over JGit calls with the same porcelain-parsing
models. Until then the Android app surfaces no git UI rather than fake it.
