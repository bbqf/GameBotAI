# Implementation Plan: Device-Scoped Image Detection

**Branch**: `085-device-scoped-detect` | **Date**: 2026-09-14 | **Spec**: [spec.md](./spec.md)
**Input**: Feature specification from `/specs/085-device-scoped-detect/spec.md`
**Issue**: [#176](https://github.com/bbqf/GameBotAI/issues/176) (B-009)

## Summary

`POST /api/images/detect` resolves its screen through the singleton `IScreenSource`
(`BackgroundCaptureScreenSource`), which — correctly, since feature 079 — returns `null` when it
cannot determine which device the caller meant. `DetectAsync` then maps that `null` to
`Results.Ok(new DetectResponse { Matches = new(), LimitsHit = false })`: a 200 with an empty match
array, indistinguishable from a real "nothing matched".

The fix is confined to the API boundary:

1. Give `DetectRequest` an optional target — `captureId` (already the currency of
   `/api/images/detect-all`) or `sessionId` — so a caller can state which screen to measure.
2. Stop flattening "cannot determine" into an empty success. Every path that fails to *obtain a
   frame* returns an explicit `{ code, message }` error, reusing the code vocabulary
   `GET /api/emulator/screenshot` already established for this same ambiguity
   (`ambiguous_session`, `emulator_unavailable`, `session_not_found`).

The screen-resolution logic itself is not touched: `BackgroundCaptureScreenSource`'s refusal to
guess is correct and stays exactly as it is. Only the interpretation of its `null` changes.

**The load-bearing constraint** (spec FR-016): ambiguity is detected by *the resolver returning
null*, never by counting sessions up front. In stub/test hosts (`GAMEBOT_USE_ADB=false`) a
`SingleBitmapScreenSource` always yields a frame even with zero sessions running; a pre-emptive
session count would fail every existing contract test while fixing nothing. Session state is
consulted only *after* a null frame, and only to choose which error code to report.

## Technical Context

**Language/Version**: C# / .NET 8
**Primary Dependencies**: ASP.NET Core Minimal APIs, OpenCvSharp (`ITemplateMatcher`), Swashbuckle
**Storage**: N/A — `CaptureSessionStore` is an in-memory, 10-entry, LRU-trimmed capture cache
**Testing**: xUnit + FluentAssertions; `tests/contract` (via `WebApplicationFactory<Program>`) and `tests/unit`
**Target Platform**: Windows (the vision/capture stack is `[SupportedOSPlatform("windows")]`); CI is `windows-latest`
**Project Type**: Web service (single ASP.NET Core host) with a separate React `web-ui`, untouched here
**Performance Goals**: No change to the matching hot path. The added work on the success path is one
nullable-string check; on the failure path, one `ListSessions()` call that only ever runs when the
request is already failing. Target: no measurable change to detect p95.
**Constraints**: No behaviour change for single-session callers (FR-013); no new API vocabulary (FR-012)
**Scale/Scope**: One DTO, one endpoint handler, one Swagger example block, one architecture-doc section, plus tests

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

*NON-NEGOTIABLE*: If `build` or required `test` runs are failing (local or CI), implementation progression is blocked until failures are fixed or a documented maintainer waiver exists.

| Principle | Assessment | Status |
|-----------|-----------|--------|
| **I. Code Quality Discipline** | The change lives in `ImageDetectionsEndpoints.cs`, a file that deliberately uses named static handler methods because the Roslyn taint analyzers scale super-linearly with method body size (see the comment at its head, and the `gamebot-build-time-analyzers` constraint). `DetectAsync` is already near the size where that matters, so the new resolution logic goes into **a separate named static method**, not inline in the handler. No new dependencies. XML-doc comments on the new public-facing behaviour. | PASS |
| **II. Testing Standards** | This is a bug fix, so per the constitution it MUST ship a test that fails before the fix. The multi-session ambiguity case is exactly that test. Coverage: four new contract/unit tests spanning both new request fields and all three error codes (spec FR-015). | PASS |
| **III. UX Consistency** | The whole point of the feature. Error messages are actionable and name the remedy ("specify sessionId or captureId"), matching the existing screenshot endpoint's wording. Codes are reused, not invented. | PASS |
| **IV. Performance** | Hot path unchanged; see Performance Goals. Perf note recorded in the PR description: the only added success-path cost is two null-or-whitespace checks. | PASS |
| **V. Living Documentation (NON-NEGOTIABLE)** | `docs/architecture.md` documents the device-resolution rules and already describes the screenshot endpoint's `409 ambiguous_session`. This change alters the API surface, so that section MUST be updated and "Last reviewed" refreshed. `spec.md` needs a `**Status**:` line and `specs/STATUS.md` a row for 085. Both are explicit tasks. | PASS |

**Gate result: PASS** — no violations, so the Complexity Tracking table is omitted.

## Project Structure

### Documentation (this feature)

```text
specs/085-device-scoped-detect/
├── spec.md              # Feature specification (with Clarifications session)
├── plan.md              # This file
├── research.md          # Phase 0 output
├── data-model.md        # Phase 1 output
├── quickstart.md        # Phase 1 output
├── contracts/
│   └── images-detect.md # Phase 1 output — request/response contract
├── checklists/
│   └── requirements.md  # Spec quality checklist
└── tasks.md             # Phase 2 output (/speckit-tasks)
```

### Source Code (repository root)

```text
src/GameBot.Service/
├── Endpoints/
│   ├── ImageDetectionsEndpoints.cs      # MODIFY — DetectAsync target resolution + explicit errors
│   ├── ImageDetectionsValidation.cs     # MODIFY — reject both-targets-named as malformed
│   └── Dto/
│       └── ImageDetectionsDtos.cs       # MODIFY — DetectRequest gains captureId + sessionId
└── Swagger/
    └── SwaggerConfig.cs                 # MODIFY — request example + error responses (two call sites)

tests/
├── contract/Images/
│   └── DetectImageTests.cs              # MODIFY — existing shape tests must keep passing unchanged
└── unit/
    └── ImageDetectTargetResolutionTests.cs  # NEW — resolution + error-classification cases

docs/
└── architecture.md                      # MODIFY — device-resolution section + Last reviewed date

specs/
└── STATUS.md                            # MODIFY — add the 085 row
```

**Structure Decision**: No new projects or layers. The defect is a boundary-mapping bug in one
handler, and the spec's Out of Scope section explicitly forbids touching the resolver, the
evaluators, and the capture loop. Everything below the endpoint stays as-is; the only new file is a
unit-test file.

## Design

### Request shape

`DetectRequest` gains two optional, mutually exclusive fields:

| Field | Meaning |
|-------|---------|
| `captureId` | Measure against this exact previously-taken frame. Same identifier `/api/images/detect-all` takes, resolved through the same `CaptureSessionStore`. |
| `sessionId` | Measure against this session's current screen. |

Both omitted → today's implicit resolution (ambient device context → sole running session). Both
supplied → `400 invalid_request` (spec Assumptions: never guess). Blank/whitespace → treated as
omitted, matching how the DTO already treats blank optional values.

### Frame acquisition — one method, four outcomes

A new static helper returns a discriminated outcome rather than a bare `Bitmap?`, so the handler
cannot accidentally re-flatten a failure into an empty success:

```
ResolveFrame(req, captures, sessions, screenSourceFactory, screenSource)
  -> Frame(bytes or bitmap)            // measure
  -> NotFound(code, message)           // 404 — named target unknown/expired
  -> Ambiguous(message)                // 409 — several sessions, none named
  -> Unavailable(message)              // 503 — no screen obtainable at all
```

Resolution order:

1. **Both targets named** → rejected earlier, in validation (`400 invalid_request`).
2. **`captureId` named** → `CaptureSessionStore.TryGet`; miss → `404 capture_not_found`
   ("capture not found or expired", matching `DetectAllAsync`'s existing wording).
3. **`sessionId` named** → `ISessionManager.GetSession`; miss → `404 session_not_found`. Hit →
   `IScreenSourceFactory.ForSession(sessionId).GetLatestScreenshot()`; null → `503
   emulator_unavailable` (the session exists but no frame has been captured for it yet).
4. **Neither named** → the singleton `IScreenSource.GetLatestScreenshot()`, exactly as today. Null →
   **and only now** consult `ISessionManager.ListSessions()` purely to classify: more than one
   running session → `409 ambiguous_session`; otherwise → `503 emulator_unavailable`.
5. **`IScreenSource` not registered at all** (non-Windows host) → `503 emulator_unavailable`.

**Service resolution.** `CaptureSessionStore` and `ISessionManager` are registered unconditionally
and may be handler parameters. `IScreenSourceFactory` must **not** be: both screen-source
registrations sit inside the `OperatingSystem.IsWindows()` guard at `GameBotServiceSetup.cs:322` —
the `GAMEBOT_USE_ADB=false` stub branch included — so a non-Windows host has neither, and a required
handler parameter would fail to resolve there. It is resolved optionally through the
`IServiceProvider` the handler already takes, exactly as the current `IScreenSource` lookup does,
with a null factory mapping to `Unavailable`. CI runs `windows-latest` and would not surface this.

Step 4 is where FR-016 lives. The session count is a *diagnostic*, read after the failure is already
established — never a precondition. This is what keeps `GAMEBOT_USE_ADB=false` hosts, which serve a
fixed bitmap with zero sessions, on the success path exactly as before.

### Error body shape

`{ code, message }` — this endpoint's existing convention (`invalid_request`, `not_found`) — carrying
the *code vocabulary* of `GET /api/emulator/screenshot`. The screenshot endpoint spells its own
bodies `{ error, message }`; that difference is deliberate and preserved. Callers of this endpoint
keep parsing `code`; the values they see are ones they already know from the screenshot route.

### What is deliberately not changed

- `BackgroundCaptureScreenSource.ResolveSessionId()` — correct as written.
- `DetectAllAsync` — no threshold, per Out of Scope.
- The `MatchResult` shape and the matching algorithm.
- The existing `not_found` for an unknown `referenceImageId`, which already complies with FR-009.

## Complexity Tracking

Not applicable — Constitution Check passed with no violations.
