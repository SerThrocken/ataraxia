# Hermes Android — native Kotlin + Jetpack Compose client

Thin client over the Hermes gateway HTTP API (Acer, port **8642** per
`../SCOPE.md`). All plugins, tools, and skills execute gateway-side; this app
only renders. Package: `com.tlg3d.hermes`.

Status: **scaffold** — UI shell, navigation, network/security contracts, and
placeholder screens. The OkHttp protocol implementation of `GatewayClient`
(incl. SSE streaming and `/api/ws` JSON-RPC) is dropped in once
`api-contract.md` pins the response shapes.

## Layout

```
android/
├── app/                          # single application module
│   └── src/main/java/com/tlg3d/hermes/
│       ├── MainActivity.kt        # single activity, edge-to-edge
│       ├── HermesApp.kt           # Application; owns SecretStore
│       ├── network/               # core-network
│       │   ├── GatewayClient.kt   # interface only — protocol client plugs in later
│       │   ├── GatewayModels.kt   # Session, ChatMessage, ModelInfo, SkillInfo DTOs
│       │   └── GatewayConfig.kt   # baseUrl + API key (key never stored here)
│       ├── security/              # core-security
│       │   ├── SecretStore.kt     # interface
│       │   └── KeystoreSecretStore.kt  # EncryptedSharedPreferences impl
│       └── ui/
│           ├── HermesNav.kt       # Navigation Compose, adaptive phone/foldable/tablet
│           ├── theme/Theme.kt     # Material 3 Expressive, dark/light
│           └── screens/           # Chat, Sessions, Models, Skills, Settings
├── DEPENDENCIES.md                # every dependency: version + verified license
└── local.properties               # sdk.dir (per-machine, gitignored)
```

## Prerequisites

- JDK 17 (`java -version` → 17.x)
- Android SDK with `platform-tools`, `platforms;android-34`, `build-tools;34.0.0`
  (this repo's SDK lives at `~/workspace/android-sdk`)

## Build

```bash
cd ~/workspace/hermes-native-client/android
./gradlew assembleDebug
# APK: app/build/outputs/apk/debug/app-debug.apk
```

Prerequisites: JDK 17+, `ANDROID_HOME` set (or `sdk.dir` in `local.properties`,
already configured for this VM), network access to Google/Maven Central.

### Notes from the Linux build VM (2026-10-11)

The stock invocation above needs three environment workarounds on this VM:

```bash
export JAVA_HOME=/usr/lib/jvm/java-21-openjdk-amd64
export GRADLE_USER_HOME=~/.gradle-hermes
export GRADLE_OPTS="-Djava.net.preferIPv4Stack=true"
./gradlew assembleDebug --no-daemon
```

Why: the platform's OS intent enforces JDK 21 here (not 17 — AGP 8.5.2 runs
fine on 21); the shared `~/.gradle/gradle.properties` proxy breaks Java TLS
(the egress proxy mangles Java's ClientHello — curl works, Java gets reset),
so the build uses an isolated `GRADLE_USER_HOME` with no proxy and the
repaired `/etc/ssl/certs/java/cacerts`; and this VM's IPv6 loopback blackholes
TCP (`localhost` → `::1` times out), so the Gradle client/daemon force IPv4
and the project sets `kotlin.compiler.execution.strategy=in-process`
(`gradle.properties`) to dodge the Kotlin daemon's TCP socket. None of this
applies to a normal dev machine.

## Conventions

- Clean-room original code only. Reference repos (HAM, hermes-friends,
  hermes-mobile-remote-gateway) are ideas/patterns, never copied code.
- No endpoint paths invented: only what `../SCOPE.md` documents. Wire shapes
  come from `api-contract.md`, not from guesses.
- Secrets go to `SecretStore` (Android Keystore). Never in config files.
- License gate: every dependency must be MIT/Apache-2.0/BSD/ISC, verified at
  its source — see DEPENDENCIES.md.
