# Terminal session seam — contract between backends and views

**Owner:** Worker 4 (SSH + multi-connection registry), Phase 4 · 2026-10-12
**Status:** Contract defined; SSH backend implemented on both platforms.
Local-shell/PTY backends are the PTY worker's lane — this document is the
coordination point. There is exactly one terminal renderer
(`TerminalScreen`); views render, backends transport.

## The layers

The PTY worker owns the transport contract and the renderer:

- `Hermes.Client.Core.Terminal.ITerminalBackend` — the transport-level
  contract: `OutputReceived` (raw bytes), `Exited`, `Start` / `WriteInput` /
  `Resize` / `Terminate`, `IDisposable`.
- `Hermes.Client.Core.Terminal.PtySession` — owns one `ITerminalBackend` +
  one `TerminalScreen`; the view binds to the session.
- `Hermes.Client.Core.Terminal.TerminalScreen` — the VT parser + screen
  model. **The one renderer. Nothing else renders.**

Worker 4 owns the cross-platform session contract and the SSH backends:

- Windows: `Hermes.Client.Core.ITerminalSession`
  (`windows/src/Hermes.Client.Core/ITerminalSession.cs`)
- Android: `com.tlg3d.hermes.ssh.TerminalSession`
  (`android/app/src/main/java/com/tlg3d/hermes/ssh/TerminalSession.kt`)

The two interfaces are intentionally parallel — same members, same semantics,
idiomatic to each language.

## The bridge (Windows)

`Hermes.Client.Core.Terminal.TerminalSessionBackend : ITerminalBackend`
adapts any `ITerminalSession` into the PTY worker's backend contract, so an
SSH session renders through the shared `PtySession`/`TerminalScreen`:

```csharp
var ssh = await new SshNetSessionFactory(secrets, trust).ConnectAsync(host);
using var pty = new PtySession(new TerminalSessionBackend(ssh));
pty.Start("", 80, 24); // fileName ignored: the session knows its target
```

Impedance notes: `ITerminalBackend` is synchronous while
`ITerminalSession` is async, so the adapter blocks on the async calls —
call `Start` off the UI thread for network backends (SSH dials).
`Exited` fires with `-1` (unknown): an SSH shell-channel close has no
process exit code, and `PtySession` already uses `-1` for "unknown".

## The contract (`ITerminalSession` / Android `TerminalSession`)

| Member | Semantics |
|---|---|
| `Title` | Display title. SSH: the saved host's name. Local PTY: the shell name. |
| `IsConnected` | Whether the transport is currently up. |
| `DataReceived` | **Raw terminal bytes from the remote end** (VT/ANSI escapes included). The view feeds them verbatim into its terminal emulator. The backend never interprets escape sequences and never renders. |
| `Disconnected` | Fires **exactly once** when the session ends for any reason: remote close, network drop, or `CloseAsync`. After it fires the instance is done — the view unsubscribes and drops its reference. |
| `ConnectAsync` | Establishes the session. SSH backends may throw `SshUnknownHostKeyException` when the server's host key isn't pinned — the UI shows the fingerprint, and on approval pins it via `ISshHostKeyTrust` / `SshHostKeyTrust` and retries. |
| `WriteAsync(data)` | Sends raw input bytes (keystrokes, paste) toward the remote end, verbatim. A UTF-8 string overload exists for convenience. |
| `ResizeAsync(columns, rows)` | Notifies the remote PTY of the view's size **in character cells**. Views call this on layout changes; backends forward it (`ShellStream.ChangeWindowSize` / sshj `changeWindowDimensions`). |
| `CloseAsync` / dispose | Stops the pump, closes the transport, fires `Disconnected`. Idempotent. |

## Rules both sides follow

1. **Views render, backends transport.** The view owns the terminal emulator.
   The backend owns threads, sockets, pumps. The view only calls the async
   methods and subscribes to the events.
2. **Bytes are opaque.** `DataReceived` payloads and `WriteAsync` payloads
   cross the seam untouched. No sniffing, no filtering, no logging of
   session bytes anywhere.
3. **One instance = one session.** Instances are not reused after close.
   Open a second tab → create a second session.
4. **Backends never touch UI threads.** Events may fire on pump threads;
   views marshal to their UI thread (WinUI: `DispatcherQueue`; Android:
   `Dispatchers.Main`).
5. **Secrets never cross the seam.** The session is constructed with a saved
   host record (no secrets); the backend reads keys/passwords from the
   platform secret store (DPAPI / Keystore) at connect time.
6. **Host keys are pinned, not blindly trusted.** First-seen keys raise
   `SshUnknownHostKeyException` with the `SHA256:base64` fingerprint
   (OpenSSH format, no padding — identical on both platforms). A *changed*
   key for a pinned host (`KeyChanged == true` / `keyChanged == true`) is a
   possible MITM: the UI must warn louder than for a first-seen key.

## Implemented backends (Worker 4)

- Windows: `Hermes.Client.Infrastructure.SshNetTerminalSession`
  (SSH.NET `SshClient` + `ShellStream`), built by `SshNetSessionFactory`.
  Host pins: `SettingsSshHostKeyTrust` (JSON blob `ssh-known-hosts`).
- Android: `com.tlg3d.hermes.ssh.SshjTerminalSession`
  (sshj `SSHClient` + `Session.Shell`), built by `SshjSessionFactory`.
  Host pins: `PrefsSshHostKeyTrust` (JSON object in the registry prefs).

Both authenticate with a private key (optional passphrase) or a password,
read from DPAPI / Keystore at connect time. Saved hosts live in
`SshHostRegistry` (Windows, Core) / `SshHostRegistry` (Android, `ssh`
package); the registry blobs contain no secrets.

## For the PTY worker

Local-shell backends (ConPTY on Windows, and whatever the Android lane
uses) implement `ITerminalBackend` directly. SSH sessions arrive through
`TerminalSessionBackend`, so the view code you write against `PtySession`
works unchanged for SSH. If either contract needs a member we didn't
foresee, extend it and note it here.
