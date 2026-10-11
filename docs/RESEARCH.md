# WinUI 3 Hermes Client — GitHub Research Report
Date: 2026-10-11 | Research for native WinUI 3 desktop client + Android APK for the Hermes agent gateway.

## Projects evaluated

### 1. Harness.WinUI — breezy89757/harness.winui
- URL: https://github.com/breezy89757/harness.winui
- What: Native WinUI 3 + .NET 10 AI agent client (no Electron/Node). Any OpenAI-compatible endpoint, MCP tools, agent with file tools + PowerShell sandbox.
- License: **MIT** (verified via badge + THIRD-PARTY-NOTICES.md)
- Steal-worthy:
  - **Approval cards**: writing files / running commands / changing settings shows "Allow once / Always allow / Deny" confirmation card; read-only lock mode refuses all side effects.
  - **Live artifact panel**: web pages, SVG, Mermaid, Markdown preview in a side panel *while being written*, with version history + Save As.
  - **MCP Apps**: tools shipping their own UI (`io.modelcontextprotocol/ui`) render inline in chat inside an isolated sandboxed frame.
  - **Transparent agent activity**: reasoning, tool args/results, token usage, model name, response time all visible.
- Architecture: CommunityToolkit.Mvvm, Markdig (BSD-2) for markdown, WebView2 for rich rendering, Microsoft Agent Framework. THIRD-PARTY-NOTICES.md is a vetted permissive dependency list.
- Design: native lightweight single-process; chat-first with side panel for artifacts.

### 2. BigChat — Sammy-Lastre/BigChat
- URL: https://github.com/Sammy-Lastre/BigChat
- What: WinUI 3 chat app for LLMs via Ollama + OpenAI-compatible endpoints.
- License: **MIT** (verified on repo page)
- Steal-worthy:
  - Custom Markdown rendering via modified CommunityToolkit `MarkdownTextBlock` — the proven path for chat markdown in WinUI.
  - Fine-grained model controls (temperature, top-p, penalties, restore defaults) in Settings.
  - EF Core + SQLite on-device conversation storage.
- Architecture: BigChat.AppCore / .Infrastructure / .WinUI layering, .NET 10, OllamaSharp.

### 3. ModelPort — vitstudio/modelport
- URL: https://github.com/vitstudio/modelport
- What: Windows-only OpenAI-compatible chat client, pure C#/XAML, ships as a single unpackaged self-contained `ModelPort.exe`.
- License: **UNKNOWN** (no license file visible on repo; 0 stars, new project) — ideas only, no code reuse.
- Steal-worthy:
  - **Messenger-style shell**: 60px nav rail + 318px conversation pane (overlays below 940px width) + focused transcript + floating auto-growing composer.
  - Streaming UI updates throttled to ~32ms to avoid per-token layout churn; virtualized lists.
  - Conversation management: pinning, previews, draft indicators, search debounce, soft-delete + Trash + Undo.
  - Security defaults: DPAPI-protected API keys, AES-256-GCM conversation state, atomic state writes, no plain HTTP except loopback.
  - Pinned toolchain + `Verify-NativeOnly.ps1` in CI proving no web runtime.

### 4. DevToys — DevToys-app/DevToys (+ DevToys.Api)
- URL: https://github.com/DevToys-app/DevToys
- What: "Swiss Army knife for developers" — 30+ tools, extensible.
- License: **MIT** (verified)
- Steal-worthy (plugin architecture — the gold standard):
  - **DevToys.Api NuGet package**: third-party extensions implement `IGuiTool` (+ `IResourceAssemblyIdentifier` for localization); installed as NuGet packages via an extension manager.
  - MEF / dynamic assembly loading for discovery; Smart Detection picks the right tool for clipboard content.
  - Extension docs: https://github.com/devtoys-app/documentation (extension-development guides).
- Design: tools as independent modules with uniform chrome; each tool owns its UI via the API contract.

### 5. PowerToys — microsoft/PowerToys
- URL: https://github.com/microsoft/PowerToys
- What: 30+ Windows utilities suite.
- License: **MIT** (verified)
- Steal-worthy:
  - **Settings UX**: `Settings.UI` WinUI 3 app — one XAML page + page-level ViewModel per module; module toggles, dashboard home, GPO-aware lock icons.
  - **Module/plugin pattern**: each module exports a C interface (`powertoy_create/destroy`, `enable`, `send_config_json`/`get_config`); settings stored as per-module JSON under `%LOCALAPPDATA%`; runner communicates via named-pipe IPC.
  - WinUIEx `WindowEx` + Page-in-Window pattern (Window is not a FrameworkElement in WinUI 3).
- Design: centralized settings dashboard; each utility is independently toggleable.

### 6. Files — files-community/Files
- URL: https://github.com/files-community/Files
- What: Modern WinUI 3 file manager (45k+ stars).
- License: **MIT** (verified via third-party research doc)
- Steal-worthy: MVVM + DI reference implementation at scale; **status center** (running operations list) — perfect pattern for Hermes background tasks/delegated subagents.

### 7. Hermes Mobile Remote Gateway — karem505/hermes-mobile-remote-gateway
- URL: https://github.com/karem505/hermes-mobile-remote-gateway
- What: Independent Flutter Android client for Hermes Agent. APK in releases.
- License: **MIT** (verified)
- Steal-worthy:
  - **Auth flow documented**: password login → session cookie → single-use WebSocket ticket (`/api/auth/ws-ticket`) → JSON-RPC over `/api/ws`.
  - Multiple saved gateways, one-tap switching, per-gateway credentials in platform keychain.
  - Foreground connection service + local completion notifications.
  - **Steer / queue follow-up / stop** on active turns; live panel of background `terminal` processes + subagents with output streaming and kill.
  - Session takeover between devices (`session.takeover`).
  - Works against `hermes serve` over Tailscale.

### 8. hermes-friends — onlyxn/hermes-friends
- URL: https://github.com/onlyxn/hermes-friends
- What: Native Kotlin Android client, WeChat-style.
- License: **MIT** (verified)
- Steal-worthy:
  - **Wire protocol documented**: REST `/api/sessions`, `/api/sessions/{id}/messages`, `/api/files/*`; WebSocket `/api/ws` JSON-RPC: `session.create` / `session.resume` / `prompt.submit` / `image.attach`.
  - Multi-server with isolated credentials; sessions-as-contacts UX; camera/gallery/file upload.
  - "All intelligence lives on the server; the app is a pure bridge" — the right architecture statement.

### 9. HAM (Hermes Agent Mobile) — DmytroBatyuk/Hermes-Agent-Mobile-HAM
- URL: https://github.com/dmytrobatyuk/hermes-agent-mobile-ham
- What: Native Android (Kotlin, gradle.kts) Hermes client. Most polished of the three.
- License: **MIT** (verified)
- Steal-worthy:
  - Token-by-token WebSocket streaming with inline tool activity, code blocks, collapsible reasoning.
  - **Push-style notifications on turn completion / approval needed**, with inline reply + tap-to-open session.
  - Respond to tool approvals / clarify questions / secret prompts from the phone; parked requests survive reconnect.
  - Artifacts + host file browsing rendered natively; Android share-sheet into composer (staged, never auto-sent).
  - Nous OAuth via system-browser PKCE; origin-scoped encrypted credentials (Android Keystore).
  - Adaptive layouts: compact phone → foldable/tablet three-pane (projects | sessions | workspace).
  - Voice via official `/api/audio/…` routes; hands-free mode with barge-in.

### 10. hermes-console — qq5871480/hermes-console
- URL: https://github.com/qq5871480/hermes-console
- What: Single-file Flask self-hosted web console for managing Hermes instances.
- License: **MIT** (stated in upstream issue; verify before reuse)
- Steal-worthy (settings UX ideas):
  - Model settings: provider dropdown with auto-filled base URLs, live model-list fetch, one-click connectivity test.
  - Skills: enable/disable, view SKILL.md, zip upload with path-traversal validation.
  - Backup & restore with pre-snapshot; dashboard with version check + one-click repair.

### Flagged: copyleft / unverifiable (ideas only, NO code reuse)
- **kingkillery/mcpdeskclient** — GPL-3.0. Ideas: MCP config auto-detection (paste config → normalized), MCP server management UI, multi-provider switching.
- **chloemlla/nexai** — GPL-3.0. Ideas: Core/Infrastructure/WinUI3 layering, DPAPI-protected secrets, perf patterns (coalesced streaming ~20fps, in-place chat list reconciliation, narrow rebuild scope).
- **bkaankose/wino-mail** — GPL-3.0. Ideas: MVVM Toolkit + WeakReferenceMessenger event bus, NavigationView mail-style tri-pane, SQLite in publisher cache folder.
- **vitstudio/modelport** — no license file found; treat as all-rights-reserved.
- **happygamergoose/ghostclawui** — license not verified; project paused.

## Synthesis: recommended approach for the Hermes WinUI 3 client

### Opinionated architecture
- **C# / WinUI 3 / Windows App SDK on .NET 10**, unpackaged single-file exe (ModelPort pattern) — zero install friction, matches Thomas's portable preference. x64 + ARM64.
- **MVVM with CommunityToolkit.Mvvm** (source generators — `[ObservableProperty]`, `[RelayCommand]`), the universal pattern across every reference.
- **DI via Microsoft.Extensions.DependencyInjection**; **WeakReferenceMessenger** for decoupled events (Wino Mail pattern).
- **Solution layering**: `Hermes.Client.Core` (protocol client: auth, WS JSON-RPC, REST — no UI dependency) / `Hermes.Client.Infrastructure` (SQLite, DPAPI secrets, settings store) / `Hermes.Client.WinUI3` (views). Mirrors NexAI/MCPDesk.
- **Navigation**: NavigationView shell + Frame, page-per-area with page-level ViewModels (PowerToys Settings.UI pattern). WinUIEx `WindowEx`, Page-in-Window (WinUI 3 Window isn't a FrameworkElement).
- **Markdown**: Markdig (BSD-2) for parsing + CommunityToolkit `MarkdownTextBlock` (BigChat's modified version as reference).
- **Native feel**: Mica backdrop, custom title bar, light/dark/system themes, snap layouts, Windows toast notifications for turn completion / approvals.

### Chat UX (highest priority)
Adopt the ModelPort messenger shell: slim nav rail | conversation list (virtualized, pinned, searchable w/ debounce) | transcript | floating auto-growing composer. Throttle streaming UI to ~30ms. Add Harness.WinUI's approval cards (Allow once / Always allow / Deny + read-only lock) and live artifact side panel; HAM's collapsible reasoning blocks and inline tool-activity cards.

### Settings UX
PowerToys model: one page per area (Connection, Models, Messaging platforms, Skills, Appearance), each with its own ViewModel, JSON-backed. Steal hermes-console's one-click connectivity test + provider dropdown with auto-filled base URLs. Connection screen modeled on HAM (server URL + auth, Tailscale-first).

### Plugin extensibility
DevToys is the template: define a `Hermes.Client.Api` contract package (`IChatCommand`, `IToolCardRenderer`, `ISettingsPageProvider`); discover via MEF/DynamicAssemblyLoading from a plugins folder; sandboxed rendering for tool-provided UI (Harness.WinUI's MCP Apps pattern). Note: Hermes plugins execute server-side (Python) — the client's plugin surface is *rendering* (tool cards, commands, settings pages), not execution.

### Reference implementations (permissive, in priority order)
1. **breezy89757/harness.winui** (MIT) — agent chat UX: approvals, artifacts, MCP Apps.
2. **karem505/hermes-mobile-remote-gateway** (MIT) — Hermes auth/ticket flow + session/takeover semantics.
3. **onlyxn/hermes-friends** (MIT) — documented Hermes wire protocol (REST + WS JSON-RPC).
4. **DmytroBatyuk/Hermes-Agent-Mobile-HAM** (MIT) — native Android patterns: streaming, notifications, approvals, adaptive layouts.
5. **DevToys-app/DevToys** (MIT) — plugin/extension architecture via DevToys.Api.
6. **microsoft/PowerToys** (MIT) — settings dashboard + module pattern.
7. **Sammy-Lastre/BigChat** (MIT) — WinUI chat + markdown rendering starter.
8. **files-community/Files** (MIT) — large-scale WinUI MVVM/DI + status-center pattern.

### Libraries worth pulling in (all permissive)
- CommunityToolkit.Mvvm, CommunityToolkit.WinUI.Controls (MIT)
- Markdig (BSD-2-Clause) for markdown parsing
- Microsoft.Extensions.DependencyInjection (MIT)
- Microsoft.EntityFrameworkCore.Sqlite or raw Microsoft.Data.Sqlite (MIT/Apache-2.0)
- WinUIEx (MIT) for WindowEx niceties
- ModelContextProtocol C# SDK (Apache-2.0) if MCP client support is wanted later

### Android APK
Native Kotlin + Jetpack Compose (per Thomas's PSYCHE native decision). HAM is the primary reference implementation; hermes-friends documents the protocol; hermes-mobile-remote-gateway documents auth. Reuse the protocol knowledge; build fresh native UI. Keystore-backed per-gateway credentials, foreground service + notifications, adaptive phone/foldable layouts.
