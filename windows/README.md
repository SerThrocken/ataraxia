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

## Dependencies

See [DEPENDENCIES.md](DEPENDENCIES.md) — every package's license was verified
at nuget.org (or from the resolved `.nuspec`) before being added. Allowed:
MIT / Apache-2.0 / BSD / ISC.

## Notes

- Clean-room code: reference repos (harness.winui, DevToys, PowerToys) were
  used for ideas/patterns only — nothing copied.
- No OpenAI products anywhere in the dependency tree.
