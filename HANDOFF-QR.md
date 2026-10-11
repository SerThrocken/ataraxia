# Handoff QR — payload contract v1

**Status:** contract stable · **Date:** 2026-10-11 · **Owner (Windows/contract):** worker E ·
**Consumer (Android scanner):** worker F (read-only — do not change this file; propose changes to the director)

Moves a live Hermes session phone ↔ desktop: the Windows app shows a QR, the
Android app scans it, and the phone continues the same gateway session. The QR
is scanned screen-to-camera, in person, by the user's own phone.

## 1. Payload format (exact)

The QR encodes one JSON object, UTF-8, no whitespace padding:

```json
{
  "v": 1,
  "gateway": "http://100.101.250.118:8642",
  "session_id": "api_9f3c2a1b",
  "token": "X7fQ2mZ9vLp4sN8wRt6yU3iO1pA5dF7gH2jK9lQ4x",
  "exp": 1790438520,
  "label": "DESKTOP-OQNIEA6"
}
```

| Field        | Type   | Required | Rules |
|--------------|--------|----------|-------|
| `v`          | int    | yes      | Must be `1`. Bump only with a new contract version. |
| `gateway`    | string | yes      | Absolute `http(s)` URL of the gateway, no trailing slash. The token is bound to this gateway and useless elsewhere. |
| `session_id` | string | yes      | Non-empty. The gateway-side session to continue. |
| `token`      | string | yes      | Single-use pairing token, ≥ 43 base64url chars (256-bit CSPRNG). **This is NOT the gateway API key.** |
| `exp`        | int    | yes      | Unix seconds. Must be in the future and ≤ 15 min out (contract max). Windows mints 2-min tokens. |
| `label`      | string | no       | Friendly source-device name (Windows uses `Environment.MachineName`). ≤ 64 chars. Shown on the phone's confirm screen. |

Validation is shared: `HandoffPayloadCodec.Decode` (Windows, `Hermes.Client.Core`)
rejects wrong version, non-http(s) gateway, empty session id, short token,
expired token, overlong lifetime, and overlong label. **The Android scanner must
enforce the same rules** — reject loudly, never "best-effort" a bad payload.

**The API key is NEVER in the QR.** There is no field for it and the codec has
nothing to serialize it with. This is the load-bearing rule of the design.

## 2. Token flow

```
Windows app                          Android app                        Gateway (8642)
───────────                          ───────────                        ─────────────
1. User opens Hand off
   (session S on gateway G)
2. Mint token T:
   256-bit CSPRNG, base64url,
   TTL 120 s, single-use,
   bound to (G, S). Stored in
   HandoffTokenStore (memory only).
3. QR ← Encode({v:1, G, S, T,
   exp, label}); show + countdown ──► 4. Scan → Decode + validate
                                      (same rules as §1)
                                      Show confirm: label, G, S,
                                      "expires in Ns"
                                      ──► 5. POST /api/handoff/redeem
                                           {token: T,
                                            device_name, device_id}
                                           → 200 {credential, expires_in}
                                           → 404/410/403 (see §4)
6. (desktop: token consumed/      7. Store credential in Android
   expired → QR invalidated)          Keystore (never plaintext).
                                      Open session S on gateway G
                                      with the credential.
8. T is dead after first
   redemption or TTL — a second
   scan of the same QR fails closed.
```

Credential shape (gateway-issued, short-lived, scoped to session S):

```json
POST /api/handoff/redeem
→ 200 {"credential": "<opaque>", "expires_in": 3600, "session_id": "api_9f3c2a1b"}
→ 410 {"error": "token_expired_or_consumed"}
→ 403 {"error": "token_gateway_mismatch"}
```

The credential is a bearer scoped to the one session, not the master
`API_SERVER_KEY`. Its exact minting is a gateway implementation detail; the
contract only requires: opaque, single-session scope, ≤ 1 h lifetime,
revocable.

## 3. Threat model

| Threat | Mitigation |
|--------|------------|
| **Shoulder-surfing / photo of the QR** | 120 s TTL + single-use: a captured QR is a lottery ticket that expires in two minutes and dies on first redemption. The QR page also clears the code the moment it expires. |
| **Token replay** | Consumed atomically on redeem (desktop `HandoffTokenStore.TryConsume` + gateway-side single-use). Second use → 410. |
| **Token guessing** | 256-bit CSPRNG (`RandomNumberGenerator`), base64url. No sequential or time-derived tokens. |
| **API key exposure** | The key never enters the payload, the QR, logs, or screenshots of the flow. The phone receives only a short-lived session-scoped credential. |
| **Token reuse against another gateway** | Token is bound to the `gateway` URL in the payload; redeem validates the binding (403 on mismatch). |
| **Stale QR left on screen** | Countdown + auto-invalidate on expiry; generating a new code revokes the previous token. |
| **Malicious QR (attacker shows the phone a crafted code)** | Scanner validates every field (§1) and shows label + gateway URL + session id on a confirm screen before redeeming. The user sees where they'd connect. |
| **Credential theft on the phone** | Stored in Android Keystore; short lifetime (≤ 1 h); session-scoped so a leak can't touch other sessions or admin routes. |
| **Desktop token store growth** | In-memory only, TTL sweep on every mint/consume; no persistence, no logs. |

Out of scope for v1: device attestation, mutual TLS, biometric confirm on redeem.

## 4. Gateway gap (verified, blocks end-to-end)

`POST /api/handoff/redeem` (and any pairing/device endpoint) **does not exist**
on the 8642 CLI gateway as of 2026-10-11:

- `POST /api/pairing/requests` → **404** (live probe)
- `/v1/capabilities` endpoint table lists no pairing/handoff routes
- The existing `gateway/pairing.py` is DM user-pairing for messaging platforms (Telegram/Discord/…), not device pairing — different flow, not reusable

**What works today:** Windows generates spec-compliant QRs (`HandoffPage` +
`HandoffPayloadCodec` + `HandoffTokenStore`, unit-tested); Android can
implement the scanner + validation + confirm screen against §1–§2 now.

**What's missing:** the gateway redeem endpoint (mint → single-use check →
session-scoped credential). Until it lands, the phone cannot exchange the
token. This is a gateway-side addition for the director to schedule — the
client contract above is written so neither side needs to change when it lands.

## 5. Notes for the Android scanner (worker F)

- Decode with any QR library, then validate exactly per §1 (version, URL
  scheme, token length, expiry window, label length). Reject + show why.
- Confirm screen before redeem: label, gateway host, session id, seconds
  remaining. One tap to redeem.
- On 404 from `/api/handoff/redeem`: show "gateway doesn't support handoff
  yet" (the §4 gap) — do not retry, do not fall back to asking for the API key.
- Never persist the token; keep it in memory only until redeem responds.
- Store the returned credential in Keystore, keyed by gateway URL + session id.
