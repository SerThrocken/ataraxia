# Hermes Native Client — Project Scope
**Date:** 2026-10-11 · **Status:** Scoping (pre-build) · **Author:** Prism

Replacing the Electron desktop app with a native WinUI 3 Windows app + native Android (Kotlin) app. Both are thin clients over the Hermes gateway's HTTP/WebSocket API — all plugins, tools, and skills execute gateway-side.

---

## 1. The critical architectural fact

There are **two** HTTP servers on the Acer:

1. **CLI gateway** — port **8642**, 42 routes, Bearer `API_SERVER_KEY` auth. Stable, long-lived, watchdog-supervised. This is what the bots use.
2. **Desktop's spawned backend** — ephemeral port (currently 64898), spawned and supervised *by the Electron app itself*. The renderer never dials the network directly; it sends `hermes:api` IPC to the main process, which proxies to this backend.

**Consequence:** a native client dials HTTP/WebSocket directly — strictly simpler than Electron's IPC-proxy dance. For v1 we target the CLI gateway on 8642 and sidestep the entire backend-supervision problem.

## 2. API surface (verified read-only, 2026-10-11)

### CLI gateway (8642) — the v1 target
- Auth: `Authorization: Bearer <API_SERVER_KEY>` (≥16 chars, no loopback exemption)
- Sessions: `GET/POST/GET/PATCH/DELETE /api/sessions`, `/api/sessions/{id}/messages`, `/fork`
- Chat: `POST /api/sessions/{id}/chat`, `/api/sessions/{id}/chat/stream` (SSE)
- Models: `GET /v1/models`, `/api/model/options`, `POST /api/sessions/{id}/model`
- OpenAI-compatible: `POST /v1/chat/completions`, `/v1/responses`
- Skills: `GET /v1/skills`, `/v1/toolsets`
- Agent runs: `/v1/runs` (+ events SSE, approval, steer, stop; idempotency keys)
- Cron jobs: `/api/jobs` full CRUD + pause/resume/run
- Artifacts: `/v1/artifacts/upload`, `/download/{id}`
- Browser control: `/v1/browser-control/register`, `/ws`
- Health: `/health`, `/v1/health` (no auth)
- Plugins can mount extra routes beyond this table (per installed plugin)

### Desktop serve backend (ephemeral) — full-parity targets
92 `/api/*` paths: audio (TTS/STT/voice-live), config+schema, cron, files/fs, gateway restart, local-models (14), logs, MCP, media, memory, **messaging/platforms**, model info, ops (backup/doctor/security-audit), pairing, profiles, providers, sessions, skills (+hub), status, tools (computer-use, terminal backends, toolsets), webhooks, `/api/ws` (JSON-RPC: `commands.catalog`, `command.resolve`, `command.dispatch`, `prompt.submit`, `skills.manage/reload`, `complete.slash`).

### Skill invocation model (important)
Skill invocation is **not an RPC** — dispatching a skill loads the full SKILL.md into a prompt message; the client sends chat and tools fire inside the turn. **All ~759 skills render generically. Zero per-skill UI work.**

### A2A (port 9900) — not a UI channel
Agent-to-agent JSON-RPC (Linux Foundation A2A v1.0). Currently 403-locked (non-loopback bind, no trusted peers). Mobile should use 8642 REST + `/api/ws`, not A2A.

## 3. IPC-only risk list (no HTTP equivalent)

These are the real calendar costs of full parity — the chat UI is the easy part:

| # | Area | Cost driver |
|---|------|-------------|
| A | Backend supervision | spawn/health-check/recycle gateway child, port claiming, crash recovery — **sidestepped in v1 by targeting 8642** |
| B | Multi-connection registry | local/remote/cloud/SSH connections, SSH dialing |
| C | Terminal PTY | interactive terminal I/O (node-pty equivalent) |
| D | Git | 22 channels: worktrees, branches, diffs, commits, PRs |
| E | Local filesystem | reveal/trash/watch, file dialogs |
| F | Native dialogs/clipboard/shell | open/save, context menus, file-drop |
| G | OS notifications | toast + actions |
| H | Windows/tray/HUD/pet overlay | floating HUD, tray, global hotkeys, quick-entry |
| I | Secrets/OAuth | keychain storage, OAuth browser flows |
| J | Capture | screenshots, mic permission |
| K | Updates | MSIX/App Installer handoff |
| L | Deep links | `hermes://` protocol handlers |
| M | Misc | SSH config reading, battery, VS Code theme proxy |

## 4. Recommended architecture (from GitHub research)

**Windows:** C# / WinUI 3 / Windows App SDK on .NET 10, **unpackaged single-file exe** (zero install friction). MVVM via CommunityToolkit.Mvvm source generators, DI via Microsoft.Extensions.DependencyInjection, WeakReferenceMessenger event bus. Layers: `Hermes.Client.Core` (protocol, no UI) / `.Infrastructure` (SQLite, DPAPI, settings) / `.WinUI3` (views). NavigationView shell, page-per-area with page-level ViewModels. Mica, custom title bar, light/dark/system, toast notifications.

**Chat UX:** messenger shell (slim nav rail | virtualized conversation list | transcript | floating composer), streaming coalesced ~30ms, approval cards (allow once/always/deny), artifact side panel, collapsible reasoning, inline tool cards.

**Settings UX:** one page per area (Connection, Models, Messaging, Skills, Appearance), JSON-backed; connectivity test + auto-filled provider URLs on the connect screen (Tailscale-first).

**Plugin extensibility:** `Hermes.Client.Api` contract (`IChatCommand`, `IToolCardRenderer`, `ISettingsPageProvider`), MEF/dynamic loading from a plugins folder. Client plugins *render*; execution stays server-side.

**Android:** native Kotlin + Jetpack Compose. Keystore-backed per-gateway credentials, foreground service + notifications, adaptive phone/foldable/tablet layouts, share-sheet into composer, voice input. Protocol: 8642 REST + `/api/ws` JSON-RPC.

**Reference implementations (priority):** harness.winui (WinUI agent client) → hermes-mobile-remote-gateway (auth flow) → hermes-friends (wire protocol) → HAM (Android patterns) → DevToys (plugins) → PowerToys (settings) → BigChat (chat starter) → Files (status center).

**Key libraries (all permissive):** CommunityToolkit.Mvvm, CommunityToolkit.WinUI.Controls, Markdig, Microsoft.Extensions.DependencyInjection, EF Core SQLite, WinUIEx, ModelContextProtocol C# SDK.

## 5. Security posture (better than Electron)

- Desktop's safeStorage encryption is currently **OFF** — secrets persist plaintext. Native client uses **Windows DPAPI** / **Android Keystore** from day one.
- 8642 requires Bearer key ≥16 chars; no loopback exemption exists — the native client stores it in DPAPI/Keystore, never in plaintext config.

## 6. Phased plan

**Phase 0 — Foundation (3–5 days)**
- Confirm `API_SERVER_KEY` at-rest location; probe `/v1/capabilities` + `/v1/models` response shapes; verify SSE streaming behavior
- Scaffold both solutions (WinUI 3 + Android), CI, shared API contract doc

**Phase 1 — Usable daily driver (2–3 weeks)**
- Gateway connection (Tailscale-first, DPAPI/Keystore secrets, connectivity test)
- Chat: sessions, SSE streaming, markdown, approval cards, artifacts
- Models page, basic settings, notifications
- GOLD: offline queue (queue while gateway down, send on reconnect), live token/cost meter in status bar, Ctrl+K command palette (fuzzy search skills/sessions/settings/actions)
- Electron import: connections, settings, sessions migrate over — no starting from zero
- Accessibility from day one on every screen: screen reader (AutomationProperties), full keyboard nav, high contrast theme — never retrofitted

**Phase 2 — Full desktop parity + gold (3–4 weeks)**
- Messaging platforms config, skills hub, providers/MCP, cron jobs, profiles
- Terminal backends list, local models, voice I/O, memory/vault
- Plugin rendering surface (`Hermes.Client.Api`)
- GOLD: camera-to-agent (photo into chat), rich toasts with approve/deny actions, side-by-side diff view for agent file changes (per-hunk accept/reject), handoff QR (move live session phone↔desktop), plain-language scheduled prompts (creates cron jobs)
- GOLD: mission control for agent fleets (live subagent status, drill-in, steer/kill), gateway health dashboard (CPU/mem/uptime/restarts/pending-update marker), cost analytics (per-day/provider/session), provider health board (latency/errors/quota), run replay timeline scrubber, session fork tree visualization, full-text session search, skill marketplace (browse/install/update)
- NOTE: verify 8642 exposes pending-update marker, provider quota/latency, skill install endpoints during Phase 2 planning — fall back to serve-backend API or gateway-side additions if missing

**Phase 3 — Native-only wins (2 weeks)**
- System tray, global hotkey quick-entry, toast actions
- Self-updates via Velopack (unpackaged single-exe auto-updates), `hermes://` deep links
- Android: share-sheet, widgets, foldable layouts
- GOLD: voice-first mode, Android home screen widget (gateway status), persistent notification controls (stop/steer/nudge for live runs)
- GOLD: update management (trigger/check gateway updates, pending-update marker), one-tap diagnostics (ops/doctor natively), backup/restore UI, PowerToys Run plugin, taskbar progress for long runs

**Phase 4 — Long tail (as needed)**
- IPC risks B–M: PTY terminal, git integration, HUD/pet overlay, multi-connection SSH registry

## 7. Remaining unknowns

- `API_SERVER_KEY` at-rest persistence (keychain vs env vs config.yaml)
- Starmap + session-import backend endpoints
- Exact `/v1/capabilities` and `/v1/models` response shapes
- Whether desktop caches `commands.catalog` or refetches per render

## 8. Source reports

- API surface: fleet survey 2026-10-11 (read-only, Acer)
- GitHub research: `sandbox://workspace/research/winui-hermes-client-research.md` (13 projects, licenses verified)
