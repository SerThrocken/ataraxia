# Chat UI — Phase 1 worker plan (worker: chat-ui)

Binding: GOAL.md rules (ponytail-full, TDD, verify-before-done, clean-room, license gate, close test windows).
Contract: `~/workspace/goals/native-hermes-clients-winui-3-android/files/api-contract.md`.
Coordination: connection-flow worker owns the concrete client; chat UI coordinates ONLY via
`IGatewayClient` / `GatewayClientBase` / DTOs in `Hermes.Client.Core`. No HTTP/auth duplication.

## Wire shapes (verified 2026-10-11, read-only probes of Acer gateway source — no guessing)
- Sessions: `GET /api/sessions?limit=&offset=` → `{object,data[],limit,offset,has_more}`;
  item: `id,title,preview,message_count,model,last_active` (epoch float), `started_at,ended_at,pinned,archived`.
- Messages: `GET /api/sessions/{id}/messages` → `{object,session_id,data[],pagination}`;
  item keys: `id,session_id,role,content,tool_call_id,tool_calls,tool_name,timestamp,token_count,finish_reason,reasoning,reasoning_content,display_kind`.
- Stream: `POST /api/sessions/{id}/chat/stream` body `{"message":"..."}`; frames
  `event: <name>\n` + `data: <json>\n\n`; `: keepalive` comments; envelope
  `session_id,run_id,seq,ts` on every payload; terminal `event: done` then close.
  Events: run.started, message.started, assistant.delta (append per message_id),
  tool.progress (tool_name `_thinking` = collapsible reasoning), tool.started/completed/failed,
  assistant.commentary, approval.request, assistant.completed, run.completed/cancelled/failed, error, done.
- Approval: `approval.request` data = approval_data (redacted `command`) + envelope + `choices`
  (subset of ["once","session","always","deny"]). Resolve: `POST /v1/runs/{run_id}/approval`
  body `{"choice":"once|session|always|deny"}` (+ optional `request_id`) → keyed by **run_id**.
- Artifacts: NO artifact SSE events exist in the gateway (verified by source grep).
  Detection = scan turn text for `/v1/artifacts/download/<id>`; download =
  `GET /v1/artifacts/download/{id}` → raw bytes, headers `X-Artifact-Sha256`,
  `X-Artifact-Id`, `Content-Disposition: attachment; filename="..."`; **one-shot** (2nd GET 404s).
- Errors: 401 JSON `gateway_auth_failed`; 404 plain-text (non-JSON) on unknown routes;
  429 `rate_limit_exceeded` + `Retry-After: 1`.

## Tasks
1. [Core] `Dtos.cs`: replace placeholders with contract shapes (Session, SessionPage,
   ChatMessage, ApprovalRequest, ApprovalResolution, ArtifactDownload).
2. [Core] `SseParser` + `SseEvent` record (envelope fields; skips keepalive comments).
3. [Core] Extend `IGatewayClient`: `GetSessionsPageAsync(limit,offset)`,
   `ResolveApprovalAsync(runId,choice)`, `DownloadArtifactAsync(artifactId)`,
   `StreamChatEventsAsync(sessionId,message)` (typed). Keep existing abstract members untouched.
4. [Core] `GatewayClientBase`: CONCRETE virtual implementations of the 4 new members
   (protected `Http` + auth already in base = no duplication). Concrete
   `GatewayClient : GatewayClientBase` implementing the legacy abstract members in terms
   of the new ones (unblocks DI + smoke test; connection worker can extend/replace).
5. [Core.Tests] NEW xUnit project (Apache-2.0): TDD red→green for SseParser frames
   (keepalive, multi-data lines, envelope), session/message DTO deserialization from
   contract-shaped JSON, relative-time formatting.
6. [WinUI3] Add Markdig (verify BSD-2-Clause in .nuspec on Acer; DEPENDENCIES.md row).
7. [Core] `MarkdownBlocks` model + `MarkdownParser` (Markdig AST → own block/inline model);
   tests in Core.Tests. [WinUI3] `MarkdownXaml` (blocks → RichTextBlock). Clean-room.
8. [WinUI3] `SessionsViewModel`: list, refresh, load-more (offset/has_more), relative time,
   tap→open chat. `SessionsPage.xaml`: header + refresh btn, ListView template
   (title/preview/meta), Load more button.
9. [WinUI3] `ChatViewModel`: OpenSessionAsync (history), SendCommand (typed SSE switch:
   run_id capture, delta append per message_id, _thinking reasoning, tool cards,
   commentary, approval card, terminal/error/done), artifact scan+download+pane.
   `ChatPage.xaml`: transcript ListView (user bubble / assistant RichTextBlock via MarkdownXaml /
   reasoning Expander / tool status / approval card / error), input row, artifact side pane.
10. [WinUI3] `App.xaml.cs`: register `IGatewayClient` → `GatewayClient` (HttpClient +
    GatewayOptions; key comes from ISecretStore at runtime — connection worker owns UX).
    `MainWindow`: `NavigateToChat(sessionId)` helper.
11. [Verify] `dotnet build -c Release` on Acer via `~/.ssh/acer.sh` — 0 warnings.
    Check Markdig nuspec license on Acer.
12. [Verify] Runtime smoke harness (console, on Acer, key from on-box .env into process
    memory only, never printed): list sessions (parse check), stream one short prompt
    (delta order + run.completed/done), delete the probe session after. ChatViewModel
    stream logic covered by scripted-event unit test. Close everything after.

## Out of scope (not asked)
New-session UI, message pagination UI, steer/stop buttons, skills/models pages, settings UX,
WebSocket paths, /v1/skills (broken server-side), non-streaming chat UI.
