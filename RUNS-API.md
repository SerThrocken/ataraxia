# Hermes Gateway — /v1/runs API reference

Verified 2026-10-11 against gateway **0.21.6** on the Acer
(`http://100.101.250.118:8642`, Bearer `API_SERVER_KEY`).
Verified two ways: live HTTP probes from the gateway host, and reads of the
gateway source (`gateway/platforms/api_server_runs.py`,
`gateway/platforms/api_server.py`).

## Endpoint table

| Method & path | Status | Shape |
|---|---|---|
| `POST /v1/runs` | `202` | Request: `{"input": "<text>"}` (`input` may also be a message list; then the last item's `content` is used). Optional: `"session_id"`, `"model"`, `"previous_response_id"`. Optional header `Idempotency-Key` (1–255 visible ASCII chars; same key + same body replays the original response). Response: `{"run_id": "run_<hex>", "status": "started", "replayed": false}`. |
| `GET /v1/runs` (list) | `405` | **No list endpoint exists.** Clients track the run ids they create (or receive via toast deep links) and poll per run. |
| `GET /v1/runs/{run_id}` | `200` | Pollable status: `{"object": "hermes.run", "run_id", "status", "created_at", "updated_at", "session_id", "model", "last_event"?, "output"?, "usage"?, "runtime"?, "completed"?, "partial"?, "interrupted"?, "pending_steer"?}`. `created_at`/`updated_at` are epoch-second floats. `usage`: `{input_tokens, output_tokens, total_tokens, cache_read_tokens, cache_write_tokens}`. `runtime`: `{provider, model, route_source}`. `status` values: `queued`, `running`, `waiting_for_approval`, `stopping`, `completed`, `failed`, `cancelled`. Terminal: `completed` / `failed` / `cancelled`. |
| `GET /v1/runs/{run_id}/events` | `200` SSE | Frames: `: open` comment, then `id: <seq>` + `data: {<json>}` per event, ending with `: stream closed`. The JSON carries `"event"` (name), `"run_id"`, `"timestamp"` (epoch float), `"seq"`, plus event fields. Resume after disconnect: `Last-Event-ID` header or `?last_seq=N`. Event names seen live: `run.steered`, `message.delta` (`delta`), `reasoning.available` (`text`), `message.interim`, `approval.request`, `approval.responded` (`choice`, `resolved`), `run.stopping`, `run.completed` (`output`, `usage`, `runtime`, `completed`, `partial`, `interrupted`, `pending_steer`). |
| `POST /v1/runs/{run_id}/approval` | `200` | Request: `{"choice": "once"\|"session"\|"always"\|"deny", "request_id"?}`. Aliases accepted: `approve`/`approved`/`allow` → `once`. Optional `"all": true` / `"resolve_all": true` resolves every pending request. Success: `{"object": "hermes.run.approval_response", "run_id", "choice", "request_id"?, "resolved": <n>}`. Errors: `404 run_not_found`, `400 invalid_approval_choice`, `409 approval_not_active` (run has no approval session), `409 approval_not_pending`. Keyed by **run id**, never the session id. |
| `POST /v1/runs/{run_id}/steer` | `200` | Request: `{"message": "<text>"}` (`"input"` or `"text"` also accepted). Success: `{"object": "hermes.run.steer", "run_id", "accepted": true}`. `409 run_not_accepting_steer` when the run isn't `running`. |
| `POST /v1/runs/{run_id}/stop` | `200` | No body needed (`{}`). While active: `{"run_id", "status": "stopping"}`. Already terminal: the full run-status object. `409 run_not_active` when the run isn't live in this gateway process. |
| `POST /api/sessions/{id}/fork` | `201` | Optional body `{"title": …}`. Response envelope: `{"object": "hermes.session", "session": {… "parent_session_id": "<source id>" …}}`. Fork trees render from `parent_session_id`. |
| `GET /health/detailed` | `200` | Fleet pulse: `readiness.checks.background_queues.active_api_runs` and `platforms.api_server.metrics.{active_runs, stored_runs}`. No per-run listing — counts only. |

Error envelope (all failures): `{"error": {"message", "type": "invalid_request_error", "param": null, "code": "<code>"}}`.

## Approval flow (shared by Windows + Android)

1. A run (or chat stream) emits an `approval.request` SSE event. Payload:
   `tool_name`, `command` (redacted server-side), `description`/`reason`,
   `message_id`, `request_id`, and `choices` — the exact strings the client
   may send back (subset of `once`/`session`/`always`/`deny`, computed
   server-side from the request's scope).
2. Show the card/toast. Toast buttons: **Approve** → `choice: "once"`,
   **Deny** → `choice: "deny"`, always with the event's `request_id`.
3. `POST /v1/runs/{run_id}/approval {"choice", "request_id"}` → `200`
   `approval_response` with `resolved` count. The run continues.
4. Every toast deep-links back: activation args carry `action` + `runId`
   (+ `requestId`); the app resolves the approval, then opens the run's
   detail view. Args carry ids only — never secrets.

## Steer / stop flow

- **Steer** while `status == "running"`: `POST …/steer {"message"}` →
  `accepted: true`. The text is injected as guidance; a `run.steered` event
  is emitted. Steering a finished run is a `409` — check status first.
- **Stop** is destructive (in-flight work is discarded): confirm in UI,
  then `POST …/stop` → poll until terminal. A stop on an already-terminal
  run just returns its status.

## Notes for client implementations

- Poll with backoff: 2s, doubling on unchanged status, cap 30s; stop at terminal.
- `GET /v1/runs/{id}` 404s with `run_not_found` for unknown ids; terminal
  statuses are eventually forgotten server-side (status TTL), so expect 404s
  for old runs.
- The C# client lives in `windows/src/Hermes.Client.Core/`:
  `Dtos.Runs.cs` (shapes), `GatewayClient.Runs.cs` + `IGatewayClient.Runs.cs`
  (calls), `RunsClientTests.cs` (offline contract tests). Mirror these shapes
  in Kotlin rather than re-deriving them.
