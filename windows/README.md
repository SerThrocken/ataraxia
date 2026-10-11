# Hermes.Client — WinUI 3 native client (scaffold)

Thin native Windows client for the Hermes gateway (port 8642). All plugins,
tools, and skills execute gateway-side; this app only renders.

Status: **Phase 1 connection flow** — shell, navigation, DI, placeholder pages, plus: Tailscale-first connect screen (first-run + settings), DPAPI-backed API key storage, `GET /health` → `GET /v1/capabilities` connectivity test with precise failure classification, and app-wide connection state in the title bar.
The protocol client (`IGatewayClient`) is an interface; real API calls land
in Phase 1 once `api-contract.md` pins the response shapes.

## Projects

| Project | TFM | Role |
|---|---|---|
| `Hermes.Client.Core` | net10.0 | Protocol abstractions: `IGatewayClient`, DTO records, `GatewayOptions`. No UI. |
| `Hermes.Client.Infrastructure` | net10.0 | `ISecretStore` (DPAPI stub), JSON settings service. |
| `Hermes.Client.WinUI3` | net10.0-windows10.0.26100.0 | Unpackaged WinUI 3 app: NavigationView shell, custom title bar, Mica, placeholder pages (Chat, Sessions, Models, Skills, Settings). |
| `Ataraxia.PowerToysRun` | net9.0-windows | PowerToys Run plugin (keyword `ax`): recent/session search, quick-send to a new session, `ataraxia://` deep links. **Outside the solution** — see `src/Ataraxia.PowerToysRun/README.md`. |

## Prerequisites

- Windows 10 1809+ (11 recommended) — WinUI 3 builds **only on Windows**
- .NET 10 SDK (`dotnet --list-sdks` should show a `10.x` entry)
- No extra workloads: WinUI 3 comes from the `Microsoft.WindowsAppSDK` NuGet package

## Build

```powershell
cd windows
dotnet build Hermes.Client.sln -c Release
```

The WinUI 3 app is **unpackaged** (`WindowsPackageType=None`) and self-contained
(`WindowsAppSDKSelfContained=true`), so the exe runs with no install and no
separate Windows App SDK runtime download.

## Run

```powershell
.\src\Hermes.Client.WinUI3\bin\Release\net10.0-windows10.0.26100.0\win-x64\Hermes.Client.WinUI3.exe
```

Single-file publish (Phase 3 goal):

```powershell
dotnet publish src\Hermes.Client.WinUI3\Hermes.Client.WinUI3.csproj -c Release -p:PublishSingleFile=true
```

## PowerToys Run plugin

`src/Ataraxia.PowerToysRun/` is a PowerToys Run plugin (keyword `ax`) that
lives **outside this solution** — PowerToys loads plugins from its own
plugins folder. It references `Hermes.Client.Core`/`Infrastructure`
(net9.0 builds) for the gateway client and the shared DPAPI secret store,
so the API key is never duplicated. Session results deep-link into the app
via the `ataraxia://session/{id}` protocol, which the WinUI3 app registers
per-user at launch (`Services/DeepLinkService`). Full install/usage notes:
[src/Ataraxia.PowerToysRun/README.md](src/Ataraxia.PowerToysRun/README.md).

```powershell
dotnet build src\Ataraxia.PowerToysRun\Ataraxia.PowerToysRun.csproj -c Release -p:Platform=x64
dotnet test src\Ataraxia.PowerToysRun.Tests -p:Platform=x64
```

## Dependencies

See [DEPENDENCIES.md](DEPENDENCIES.md) — every package's license was verified
at nuget.org (or from the resolved `.nuspec`) before being added. Allowed:
MIT / Apache-2.0 / BSD / ISC.

## Notes

- Clean-room code: reference repos (harness.winui, DevToys, PowerToys) were
  used for ideas/patterns only — nothing copied.
- No OpenAI products anywhere in the dependency tree.
