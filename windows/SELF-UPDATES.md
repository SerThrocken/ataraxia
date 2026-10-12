# Ataraxia self-updates (Velopack)

Client self-updates run through Velopack (MIT — see DEPENDENCIES.md).
The SDK is wired in `Hermes.Client.WinUI3`:

- `App.xaml.cs`: `VelopackApp.Build().Run()` is the first call in
  `OnLaunched` (install/update/uninstall hooks; no-op in dev builds).
- `Services/VelopackAppUpdater.cs`: `IAppUpdater` over `UpdateManager`
  (check → download → apply-and-restart).
- `Hermes.Client.Core/Updating/UpdateCoordinator.cs`: UI-agnostic state
  machine with honest status strings; unit-tested in
  `Hermes.Client.Core.Tests/Updating/`.

## No feed yet

No update feed is published (no `releases.{channel}.json` anywhere).
`AppUpdateOptions.FeedUrl` is null, and the Updates page labels
self-updates honestly unavailable until a feed exists. **Do NOT invent a
feed URL** — setting one is Thomas's call when the hosting is decided.

## Packaging recipe (when the feed exists)

```cmd
dotnet tool install -g vpk
vpk pack --packId <pack-id> --packVersion <semver2> --packDir <publish-dir> --mainExe Hermes.Client.WinUI3.exe
```

- Keep the `vpk` tool version aligned with the `Velopack` NuGet version
  (currently 1.2.161).
- `--packVersion` is SemVer2 only (`1.2.0`, never `1.2.0.0`).
- **Authenticode:** sign during `vpk pack` via `--signParams` (or
  `--azureTrustedSignFile` for Azure Trusted Signing). Velopack signs its
  own binaries at specific build points — signing the output *after*
  packing breaks the install. Never sign-then-pack by hand.
- Per-user install goes to `%LocalAppData%\<packId>` (no elevation).

## Data survives updates

Velopack replaces the `current\` app dir on every update. Settings
(`%LocalAppData%\Hermes.Client\settings` via `JsonSettingsService`) and
the offline queue (LocalAppData via `FileOutboundMessageQueue`) already
live outside the app dir — updates won't wipe them. Keep it that way:
never store user data next to the exe.

## NOTE FOR WORKER A (tray)

Velopack's install path churns per version:
`%LocalAppData%\<packId>\app-<version>\Hermes.Client.WinUI3.exe`.
The exe path is **not stable** across updates. When wiring the tray icon,
register it with a **fixed GUID** — never keyed off the exe path —
or the icon registration will go stale on every update.
(`DeepLinkService` already re-registers the `ataraxia://` protocol from
`Environment.ProcessPath` on every launch, so protocol activation
self-heals across updates with no extra work.)
