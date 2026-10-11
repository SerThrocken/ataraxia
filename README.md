# Ataraxia

**Software that never disturbs you.**

Ataraxia (ἀταραξία — *freedom from disturbance*) is a native client for the
[Hermes](https://github.com/nousresearch/hermes) AI agent gateway. No Electron.
No wrappers. A Windows app built with WinUI 3 and an Android app built with
native Kotlin, both speaking directly to the Hermes gateway's HTTP/WebSocket
API.

## The Ataraxia principle

Every feature is held against one question: *does this disturb the user?*
If it does, it doesn't ship. No jank, no spinners that never resolve, no
plaintext secrets, no 60-second timeouts with no explanation.

## Status

Phase 0 (foundation) in progress. See [SCOPE.md](docs/SCOPE.md) for the full
project scope and [GOAL.md](docs/GOAL.md) for the build rules.

## Layout

- `windows/` — WinUI 3 desktop client (C#, .NET 10, unpackaged single-file exe)
- `android/` — Android client (Kotlin, Jetpack Compose)
- `docs/` — scope, API contract, research

## License

MIT. See [LICENSE](LICENSE).
