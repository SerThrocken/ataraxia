# Dependencies — license gate record

Verified 2026-10-11. Allowed: MIT / Apache-2.0 / BSD / ISC.
License evidence: the `.nuspec` of the exact version resolved by the
Release build on the build machine (Acer, `C:\Users\tlcro\.nuget\packages\`),
plus the nuget.org package page where noted.

| Package | Version | License | Evidence |
|---|---|---|---|
| `CommunityToolkit.Mvvm` | 8.4.2 | **MIT** | `.nuspec`: `<license type="expression">MIT</license>`; https://www.nuget.org/packages/CommunityToolkit.Mvvm |
| `Markdig` | 1.4.0 | **BSD-2-Clause** | `.nuspec` on build machine (`C:\Users\tlcro\.nuget\packages\markdig\1.4.0\markdig.nuspec`): `<license type="expression">BSD-2-Clause</license>`; https://www.nuget.org/packages/Markdig. Used by `Hermes.Client.Core` for Markdown parsing (chat transcript rendering). |
| `Microsoft.Extensions.DependencyInjection` | 10.0.12 | **MIT** | `.nuspec`: `<license type="expression">MIT</license>`; nuget.org README states "released as open source under the MIT license": https://www.nuget.org/packages/Microsoft.Extensions.DependencyInjection |
| `Microsoft.WindowsAppSDK` | 2.5.1 | **Microsoft Software License Terms (proprietary) — override approved by Thomas 2026-10-11** (see GOAL.md "License override") | Not copyleft, claims nothing over app code, no royalties; WinUI 3 cannot exist without it; alternatives (WPF dated, Avalonia not truly native, raw Win32 impractical) all worse. Override is explicit and on record. |
| `System.Security.Cryptography.ProtectedData` | 10.0.0 | **MIT** | `.nuspec`: `<license type="expression">MIT</license>` (dotnet/runtime); https://www.nuget.org/packages/System.Security.Cryptography.ProtectedData |
| `QRCoder` | 1.8.0 | **MIT** | Verified at source 2026-10-11: official repo README (github.com/codebude/QRCoder, maintained by Shane32) states MIT license with LICENSE.txt link; nuget.org package page states "licensed under the MIT-license"; `.nuspec` on build machine (`C:\Users\tlcro\.nuget\packages\qrcoder\1.8.0\qrcoder.nuspec`): `<license>MIT</license>`. Used by `Hermes.Client.WinUI3` (`PngByteQRCode`) for handoff-QR rendering only. |
| `Community.PowerToys.Run.Plugin.Dependencies` | 0.97.0 | **MIT** | `.nuspec` from nuget.org (downloaded 2026-10-11): `<license type="expression">MIT</license>`; package README states the five DLLs (`Wox.Plugin`, `Wox.Infrastructure`, `PowerToys.Common.UI`, `PowerToys.ManagedCommon`, `PowerToys.Settings.UI.Lib`) are built from microsoft/PowerToys source, which is MIT (github.com/microsoft/PowerToys LICENSE). **Compile-time reference only**: PowerToys loads its own copies at runtime; none of the five DLLs are redistributed with the plugin. Used by `Ataraxia.PowerToysRun` (the PowerToys Run plugin, `net9.0-windows`). |
| `H.NotifyIcon.WinUI` | 2.4.1 | **MIT** | `.nuspec` on build machine (`C:\Users\tlcro\.nuget\packages\h.notifyicon.winui\2.4.1\h.notifyicon.winui.nuspec`): `<license type="expression">MIT</license>`; https://www.nuget.org/packages/H.NotifyIcon.WinUI ; source repo (github.com/HavenDV/H.NotifyIcon) repository details: "License: MIT License (MIT)". Pulls `H.NotifyIcon` (core) 2.4.1, same author/license. Used by `Hermes.Client.WinUI3` (`Services/TrayService.cs`) for the system-tray icon. Implementation note: the 2.4.1 WinUI `TaskbarIcon` wrapper exposes **no** menu property and **no** `ContextMenuThemeMode` (verified against the shipped 2.4.1 XML docs — that property exists only on the unreleased master), so the menu and balloons go through `H.NotifyIcon.Core.TrayIconWithContextMenu` + `PopupMenu`, which renders a native Win32 popup that follows the Windows app theme on its own. Targets `net10.0-windows10.0.17763`; depends on Microsoft.WindowsAppSDK 1.6.x (NuGet resolves to the app's 2.5.1). |
| `Microsoft.Windows.CsWin32` | 0.3.360 | **MIT** | `.nuspec` on build machine (`C:\Users\tlcro\.nuget\packages\microsoft.windows.cswin32\0.3.360\microsoft.windows.cswin32.nuspec`): `<license type="expression">MIT</license>`; https://www.nuget.org/packages/Microsoft.Windows.CsWin32 ; source repo (github.com/microsoft/CsWin32) repository details: "License: MIT License (MIT)". **Source generator only** (`PrivateAssets=all`): emits the Win32 P/Invoke for the global hotkey (`RegisterHotKey`/`UnregisterHotKey`/`SetWindowSubclass`/`RemoveWindowSubclass`/`DefSubclassProc`, from `NativeMethods.txt`) at compile time; ships no runtime assembly. Used by `Hermes.Client.WinUI3` (`Services/GlobalHotkeyService.cs`). |
| `Velopack` | 1.2.161 | **MIT** | Verified at source 2026-10-12 (Worker B, Ataraxia self-updates): (1) repo LICENSE at github.com/velopack/velopack is the MIT license text (Copyright 2021 Caelan Sayler, 2024 Velopack Ltd.); GitHub API reports license key `mit` / SPDX `MIT` / "MIT License". (2) `.nuspec` from nuget.org (flat container, Velopack 1.2.161): `<license type="expression">MIT</license>`, repository url `https://github.com/velopack/velopack`. The WindowsAppSDK proprietary override on record does **not** extend to Velopack — Velopack passes the MIT allowlist on its own merits. Used by `Hermes.Client.WinUI3` (`VelopackApp`, `UpdateManager`) for client self-updates. |

## Test-only (never ship in the app)

| Package | Version | License | Evidence |
|---|---|---|---|
| `xunit` | 2.9.3 | **Apache-2.0** | `.nuspec` on build machine: `<license type="expression">Apache-2.0</license>` |
| `xunit.runner.visualstudio` | 4.0.1 | **Apache-2.0** | `.nuspec` on build machine: `<license type="expression">Apache-2.0</license>` |
| `Microsoft.NET.Test.Sdk` | 18.10.1 | **MIT** | `.nuspec` on build machine: `<license type="expression">MIT</license>` |

## Deferred

- `CommunityToolkit.WinUI.Controls` (named in the task): the package ID
  `CommunityToolkit.WinUI.Controls` returns **404 at nuget.org** — it does not
  exist as an aggregate package (only per-component packages such as
  `CommunityToolkit.WinUI.Controls.Primitives` exist, in the
  `CommunityToolkit.WinUI.Controls.*` namespace). Per the license gate it was
  **not added unverified**. The scaffold uses only stock WinUI 3 controls.
  If a specific component package is needed later, verify its ID and license
  at nuget.org first, then add it here.

## Notes

- No OpenAI products anywhere in the tree.
- All code is clean-room original; reference repos (harness.winui, DevToys,
  PowerToys) were ideas/patterns only.
