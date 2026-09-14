# Contract: `POST /api/images/detect`

**Feature**: 085-device-scoped-detect | **Date**: 2026-09-14
**Route constant**: `ApiRoutes.ImageDetect` | **Auth**: bearer token, as for all `/api/*` routes

Measures one reference image against one device's screen. This contract supersedes the previous
behaviour in exactly one respect: **a 200 response now always means a measurement was taken.**

## Request

```jsonc
{
  "referenceImageId": "pns-quit-cancel",  // required
  "threshold": 0.3,                        // optional, 0–1
  "maxResults": 3,                         // optional, 1–100
  "overlap": 0.1,                          // optional, 0–1
  "captureId": "cap_abc123",               // optional — NEW
  "sessionId": "sess_5558"                 // optional — NEW, mutually exclusive with captureId
}
```

### Target selection

| `captureId` | `sessionId` | Screen measured |
|---|---|---|
| — | — | Ambient device context of the current run; else the sole running session; else **error** |
| set | — | That exact stored frame |
| — | set | That session's latest captured frame |
| set | set | **400** `invalid_request` — never guessed between them |

Blank/whitespace values count as omitted.

## Responses

### 200 — measured

Unchanged in shape. An empty `matches` array now carries a strengthened guarantee: a real
measurement ran against a determinate screen and nothing scored at or above `threshold`.

```jsonc
{
  "matches": [
    {
      "templateId": "pns-quit-cancel",
      "score": 0.9431, "confidence": 0.9431,
      "x": 0.42, "y": 0.71, "width": 0.18, "height": 0.06,
      "overlap": 0.1,
      "bbox": { "x": 0.42, "y": 0.71, "width": 0.18, "height": 0.06 }
    }
  ],
  "limitsHit": false
}
```

`limitsHit: true` with an empty `matches` still means the matcher timed out — also a real attempt,
and pre-existing behaviour.

### Error responses

All use this endpoint's existing body shape:

```jsonc
{ "code": "ambiguous_session", "message": "2 device sessions are active; specify sessionId or captureId." }
```

| Status | `code` | Condition |
|--------|--------|-----------|
| 400 | `invalid_request` | Missing `referenceImageId`; `threshold`/`overlap` outside 0–1; `maxResults` outside 1–100; both `captureId` and `sessionId` supplied. |
| 404 | `not_found` | `referenceImageId` names no stored reference image. *(pre-existing)* |
| 404 | `capture_not_found` | `captureId` names no live capture — unknown, or evicted from the 10-entry store. |
| 404 | `session_not_found` | `sessionId` names no known session. |
| 409 | `ambiguous_session` | No target named, several sessions running, no ambient context. **This is the case issue #176 reported as `200 {"matches":[]}`.** |
| 503 | `emulator_unavailable` | No frame obtainable: the named session has no captured frame yet; or no target named and no screen resolvable; or the host has no screen capability registered. |

Codes and statuses are the same vocabulary `GET /api/emulator/screenshot` already uses for these
conditions, so a client's existing handling transfers unchanged.

> **Why `capture_not_found` here but `not_found` on `/api/images/detect-all`.** The sibling route
> returns a plain `not_found` for an unknown `captureId`, and that stays as it is. It can afford the
> generic code because a capture is the *only* thing it resolves. This route resolves two
> independent identifiers — `referenceImageId` and `captureId` — so a single `not_found` would leave
> the caller unable to tell "that image id is wrong" from "that capture expired", which are opposite
> fixes. The more specific code is therefore deliberate here, not drift, and `not_found` is retained
> unchanged for the reference-image case so existing callers keep working.

## Behavioural guarantees

- **G1** — A 200 implies a measurement against a determinate screen. There is no longer any path
  that returns a successful empty result without one. *(FR-009)*
- **G2** — A caller can tell "measured, found nothing" from "could not measure" using the status code
  alone, without knowing how many emulators are running. *(FR-008, SC-003)*
- **G3** — `threshold`, `maxResults` and `overlap` behave identically whether or not a target is
  named, so a low-threshold absence probe returns a graded score on both paths. *(FR-010, SC-006)*
- **G4** — A request that omits both new fields behaves exactly as it did before this feature,
  including when zero sessions are running on a host that serves a fixed screen. *(FR-013, FR-016)*
- **G5** — A named target is never silently substituted. An unresolvable target is a 404, never a
  measurement against a different screen. *(FR-005)*

## Compatibility

**Additive for well-behaved callers.** Both request fields are optional; the success response is
byte-identical.

**One intentional behaviour change**, which is the fix: a caller that previously received
`200 {"matches":[],"limitsHit":false}` while several sessions were running now receives `409`. Any
client treating that empty array as a confident "absent" was already being misled — that is the
production defect in issue #176. Such callers should either name a target (recommended for
absence probes) or treat non-2xx as indeterminate.

## Out of scope

`POST /api/images/detect-all` is unchanged, including its lack of a `threshold` parameter.
