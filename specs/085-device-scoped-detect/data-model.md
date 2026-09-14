# Phase 1 Data Model: Device-Scoped Image Detection

**Feature**: 085-device-scoped-detect | **Date**: 2026-09-14

No persisted entities change. This feature touches one request DTO and introduces one internal
outcome type. Nothing is written to disk; `CaptureSessionStore` remains an in-memory cache.

## Modified — `DetectRequest`

`src/GameBot.Service/Endpoints/Dto/ImageDetectionsDtos.cs`

| Field | Type | Required | Change | Notes |
|-------|------|----------|--------|-------|
| `referenceImageId` | `string?` | yes | unchanged | Validated as before. |
| `threshold` | `double?` | no | unchanged | 0–1; falls back to configured default. |
| `maxResults` | `int?` | no | unchanged | 1–100; falls back to configured default. |
| `overlap` | `double?` | no | unchanged | 0–1; falls back to configured default. |
| `captureId` | `string?` | no | **new** | Measure against this previously-taken frame. |
| `sessionId` | `string?` | no | **new** | Measure against this session's current screen. |

**Validation rules** (added to `ImageDetectionsValidation.ValidateRequest`):

- `captureId` and `sessionId` are **mutually exclusive**. Both non-blank →
  `invalid_request: captureId and sessionId are mutually exclusive`.
- Blank or whitespace-only values in either field are treated as **absent**, not as malformed. This
  matches how the DTO's other optional values already behave and keeps naive clients that send
  `""` on the existing implicit path.
- No format validation on either identifier: an unknown value is a *resolution* failure (404), not a
  *syntax* failure (400). Distinguishing them keeps the 400/404 split meaningful.

**Backward compatibility**: both fields are optional and absent from every existing caller's payload,
so existing requests deserialize and behave identically (FR-013).

## New — frame resolution outcome (internal)

`src/GameBot.Service/Endpoints/ImageDetectionsEndpoints.cs`

An internal result type returned by the new `ResolveFrameAsync` helper. It exists so that "no frame"
cannot be silently re-flattened into an empty success — the handler must branch on the kind.

| Kind | Carries | Maps to |
|------|---------|---------|
| `Frame` | the screenshot to measure (PNG bytes or `Bitmap`) | proceed to matching |
| `NotFound` | `code`, `message` | `404` |
| `Ambiguous` | `message` | `409` |
| `Unavailable` | `message` | `503` |

Not serialized and not part of the public API surface.

## Referenced, unchanged

| Entity | Where | Role here |
|--------|-------|-----------|
| `CaptureSession` (`Id`, `Png`, `Width`, `Height`, `CreatedAtUtc`) | `Services/CaptureSessionStore.cs` | Resolved by `captureId`. In-memory, max 10 entries, oldest trimmed — so a `captureId` can legitimately expire, which is why FR-005 requires an explicit not-found rather than a fallback. |
| `EmulatorSession` | `GameBot.Domain.Sessions` | Resolved by `sessionId`; also counted (only on the failure path) to classify `409` vs `503`. |
| `DetectResponse` / `MatchResult` | `Dto/ImageDetectionsDtos.cs` | Unchanged shape — explicitly out of scope. |

## Error vocabulary

Reused verbatim from `GET /api/emulator/screenshot` (research R3), carried in this endpoint's own
`{ code, message }` body shape.

| `code` | Status | Raised when |
|--------|--------|-------------|
| `invalid_request` | 400 | Missing `referenceImageId`; out-of-range tuning values; both targets named. |
| `not_found` | 404 | Unknown `referenceImageId` (pre-existing behaviour, unchanged). |
| `capture_not_found` | 404 | `captureId` names no live capture — never existed, or was trimmed. |
| `session_not_found` | 404 | `sessionId` names no known session. |
| `ambiguous_session` | 409 | No target named, several sessions running, no ambient context. |
| `emulator_unavailable` | 503 | No frame obtainable: named session has no captured frame yet; or no target named and no screen resolvable; or no screen capability registered. |

## State transitions

None. Detection is a stateless read; no entity changes state as a result of this feature.
