# Ataraxia — PowerToys Run plugin

PowerToys Run plugin for the Ataraxia gateway. Keyword **`ax`**.
Lives outside the main solution on purpose: PowerToys loads plugins from its
own plugins folder, so this project builds and ships on its own
(`net9.0-windows` — verified 2026-10-11: the target machine's PowerToys
0.101.2362.0 runs on .NET 10, and net9.0 loads on .NET 9/10 hosts).

## Usage

| Query | Result |
|---|---|
| `ax` | Recent sessions + "Open Ataraxia" |
| `ax <text>` | Sessions matching `<text>`, then "Ask Ataraxia" |
| `ax !message` or `ax ask message` | Send `message` to a brand-new session |
| `ax open` | Launch the Ataraxia app |

Session results (and their right-click context menu) deep-link into the app
via `ataraxia://session/{id}`. Quick-send creates the session, posts the
message, then opens the app at that session so you see the reply.

## Settings

No API key lives in the plugin. It reuses the shared projects:

- **API key** — read from the same DPAPI secret store the Ataraxia app uses
  (`gateway-api-key` under `%LocalAppData%\Hermes.Client\secrets`). Set it
  once in the app's Settings screen.
- **Gateway URL** — `settings.json` next to `plugin.json` (optional override):
  `{ "gatewayUrl": "http://127.0.0.1:8642" }`. When absent, the plugin uses
  the URL the app saved (`%LocalAppData%\Hermes.Client\settings\connection.json`),
  else the default `http://100.101.250.118:8642`.

## Install

1. `dotnet build src/Ataraxia.PowerToysRun/Ataraxia.PowerToysRun.csproj -c Release -p:Platform=x64`
2. Copy the output folder contents (`Ataraxia.PowerToysRun.dll`,
   `plugin.json`, plus `Hermes.Client.*.dll` and their dependencies) to
   `%LOCALAPPDATA%\Microsoft\PowerToys\PowerToys Run\Plugins\Ataraxia\`.
   Do **not** copy the `Wox.*`/`PowerToys.*` DLLs — the host supplies them.
   Optional: drop `icon.dark.png` / `icon.light.png` into an `Images/`
   subfolder and add `IcoPathDark`/`IcoPathLight` to `plugin.json`
   (no binaries are committed to the repo, so icons ship separately).
3. Restart PowerToys. The plugin reads the key/URL the Ataraxia app saved;
   launch the app once first so the `ataraxia://` protocol is registered.

## Build notes

- Targets `net9.0-windows` so the plugin loads in the .NET 9/10 PowerToys
  hosts (verified: PowerToys 0.101 runs on .NET 10). The compile-time-only
  PowerToys reference DLLs target net9.0; PowerToys loads its own copies at
  runtime (`DynamicLoading` defaults to false, so the default
  `AssemblyLoadContext` unifies our references to the host's assemblies).
- **Version pairing:** the reference package
  (`Community.PowerToys.Run.Plugin.Dependencies`, MIT) must match the
  installed PowerToys release — PowerToys version-stamps `Wox.Plugin.dll`,
  and a mismatch means the host won't recognize the plugin's `IPlugin`.
  Rebuild against the matching package version after a PowerToys update.
- `Hermes.Client.Core` / `Hermes.Client.Infrastructure` are multi-targeted
  (`net9.0` + `net10.0`) so this project can reference them instead of
  duplicating gateway/secret handling.
- Tests: `dotnet test src/Ataraxia.PowerToysRun.Tests -p:Platform=x64`
  (query routing + result mapping; the Wox adapter layer is thin by design).
