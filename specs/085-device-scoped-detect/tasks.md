---

description: "Task list for feature 085 — Device-Scoped Image Detection"
---

# Tasks: Device-Scoped Image Detection

**Input**: Design documents from `/specs/085-device-scoped-detect/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/images-detect.md
**Issue**: [#176](https://github.com/bbqf/GameBotAI/issues/176) (B-009)

**Tests**: Test tasks ARE included. Spec **FR-015** requires them explicitly, and constitution
principle II requires a bug fix to ship a test that reproduces the issue *before* the fix.

**Organization**: Grouped by user story so each is independently implementable and testable.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (US1, US2, US3)
- Exact file paths are included in every task

## Path Conventions

Single ASP.NET Core service at repository root: `src/GameBot.Service/`, `tests/contract/`,
`tests/unit/`. Paths below are repo-relative, per plan.md's Source Code layout.

---

## Phase 1: Setup

**Purpose**: Establish a known-green baseline. Constitution: a red build/test state is a hard stop,
so a pre-existing failure must be identified now rather than misattributed to this feature later.

- [x] T001 Build the solution and run the existing detect tests to confirm a green baseline, recording the result: `dotnet build GameBot.sln` then `dotnet test tests/contract/GameBot.ContractTests.csproj --filter "FullyQualifiedName~DetectImage"`

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: The request surface and the outcome type every story branches on.

**⚠️ CRITICAL**: No user story work can begin until this phase is complete.

- [x] T002 Add optional `CaptureId` and `SessionId` properties to `DetectRequest` with `[JsonPropertyName]` attributes `captureId` and `sessionId`, in `src/GameBot.Service/Endpoints/Dto/ImageDetectionsDtos.cs`
- [x] T003 Add a mutual-exclusion rule to `ImageDetectionsValidation.ValidateRequest` in `src/GameBot.Service/Endpoints/ImageDetectionsValidation.cs`: when both `CaptureId` and `SessionId` are non-blank, return `(false, "invalid_request: captureId and sessionId are mutually exclusive")`. Whitespace-only values MUST count as absent, per data-model.md.
- [x] T004 Define the internal frame-resolution outcome type (`Frame` / `NotFound(code, message)` / `Ambiguous(message)` / `Unavailable(message)`) as a small private sealed type in `src/GameBot.Service/Endpoints/ImageDetectionsEndpoints.cs`, per data-model.md "New — frame resolution outcome"
- [x] T005 Add a named static `ResolveFrame` method in `src/GameBot.Service/Endpoints/ImageDetectionsEndpoints.cs` returning that outcome type, and give `DetectAsync` access to the services it needs. Keep it a **named static method, not a lambda or inline block** — the file's header comment explains that the CA3xxx taint analyzers scale super-linearly with method body size. **Service resolution rule**: `CaptureSessionStore` and `ISessionManager` may be handler parameters (both are registered unconditionally — `CaptureSessionStore` at `GameBotServiceSetup.cs:318`), but `IScreenSourceFactory` MUST be resolved optionally through the `IServiceProvider sp` parameter `DetectAsync` already takes, exactly as the existing `IScreenSource` lookup does. Both screen-source registrations live inside the `OperatingSystem.IsWindows()` guard at `GameBotServiceSetup.cs:322` — the test-stub branch included — so a non-Windows host has neither, and a required handler parameter would fail to resolve there. CI runs `windows-latest` and would not catch it.

**Checkpoint**: Solution compiles; `ResolveFrame` exists and is called, behaviour still unchanged.

---

## Phase 3: User Story 1 — Ambiguity is reported, never disguised as a negative result (Priority: P1) 🎯 MVP

**Goal**: `/api/images/detect` stops returning `200 {"matches":[]}` when it could not determine which
screen to measure. This is the defect in issue #176.

**Independent test**: With two sessions running and no target named, the response is an explicit
error, not a successful empty result.

### Tests for User Story 1 (write first — they must fail before the fix)

- [x] T006 [P] [US1] Create `tests/unit/ImageDetectTargetResolutionTests.cs` and add a failing test: with a screen source that returns `null` and a session manager reporting **two** running sessions, `ResolveFrame` yields `Ambiguous` (→ 409 `ambiguous_session`), not a frame and not an empty success
- [x] T007 [P] [US1] In `tests/unit/ImageDetectTargetResolutionTests.cs`, add a failing test: screen source returns `null` and **zero** sessions are running → `Unavailable` (→ 503 `emulator_unavailable`)

### Implementation for User Story 1

- [x] T008 [US1] In `ResolveFrame` in `src/GameBot.Service/Endpoints/ImageDetectionsEndpoints.cs`, implement the no-target path: call the singleton `IScreenSource.GetLatestScreenshot()` exactly as today; on a non-null frame return `Frame`. **Only when it returns null**, call `ISessionManager.ListSessions()` to classify — more than one running session → `Ambiguous`, otherwise → `Unavailable`. The session count MUST NOT be consulted before requesting the frame (spec **FR-016**): stub hosts serve a fixed screen with zero sessions, and a pre-emptive count would fail every existing contract test.
- [x] T009 [US1] In `ResolveFrame`, map a missing `IScreenSource` registration (`sp.GetService(...)` returns null on non-Windows hosts) to `Unavailable` instead of the current empty-200 return, in `src/GameBot.Service/Endpoints/ImageDetectionsEndpoints.cs`
- [x] T010 [US1] In `DetectAsync` in `src/GameBot.Service/Endpoints/ImageDetectionsEndpoints.cs`, replace both `Results.Ok(new DetectResponse { Matches = new(), LimitsHit = false })` early returns with a branch on the `ResolveFrame` outcome: `Ambiguous` → `Results.Json(new { code = "ambiguous_session", message = $"{n} device sessions are active; specify sessionId or captureId." }, statusCode: StatusCodes.Status409Conflict)`; `Unavailable` → `Results.Json(new { code = "emulator_unavailable", message = "..." }, statusCode: StatusCodes.Status503ServiceUnavailable)`. Use this endpoint's `{ code, message }` body shape with the screenshot endpoint's code vocabulary, per contracts/images-detect.md.
- [x] T011 [US1] Add a structured log entry for each refusal alongside the existing `LogDetectInvalid` / `LogDetectNotFound` helpers in `src/GameBot.Service/Endpoints/ImageDetectionsEndpoints.cs`, routing any interpolated value through the existing `SanitizeForLog` helper
- [x] T012 [US1] Run the unit tests and confirm T006 and T007 now pass: `dotnet test tests/unit/GameBot.UnitTests.csproj --filter "FullyQualifiedName~ImageDetectTargetResolution"`

**Checkpoint**: The silent-failure defect is fixed. Multi-emulator callers now fail loudly rather
than receiving a fabricated absence.

---

## Phase 4: User Story 2 — A caller can name the screen to measure (Priority: P1)

**Goal**: Restore a working absence probe for multi-emulator setups by letting the caller state which
screen to measure. Ships together with US1 — US1 alone turns a wrong answer into a dead end.

**Independent test**: With two sessions running, a request naming one target returns real match
scores for that device.

### Tests for User Story 2

- [x] T013 [P] [US2] In `tests/unit/ImageDetectTargetResolutionTests.cs`, add tests for the `captureId` path: a known capture yields `Frame` (even with several sessions running), and an unknown or trimmed capture yields `NotFound` with code `capture_not_found`
- [x] T014 [P] [US2] In `tests/unit/ImageDetectTargetResolutionTests.cs`, add tests for the `sessionId` path: an unknown session yields `NotFound` with code `session_not_found`; a known session whose latest frame is null yields `Unavailable`
- [x] T015 [P] [US2] Add a contract test to `tests/contract/Images/DetectImageTests.cs` asserting that supplying both `captureId` and `sessionId` returns `400` with code `invalid_request`
- [x] T016 [P] [US2] Add a contract test to `tests/contract/Images/DetectImageTests.cs` asserting that a request naming an unknown `captureId` returns `404` with code `capture_not_found` — proving it never falls back to another screen (FR-005, guarantee G5)
- [x] T016a [P] [US2] Add a contract test to `tests/contract/Images/DetectImageTests.cs` for the graded-score guarantee (**SC-006**, SC-001, FR-010): against a named target, a deliberately low `threshold` (e.g. 0.2) returns a match whose `score` is below the configured default gate of 0.86, rather than an empty match set. This is the measurement the whole feature exists to protect — an absence probe must be able to tell "absent" from "present but weak".
- [x] T016b [P] [US2] Add a contract test to `tests/contract/Images/DetectImageTests.cs` asserting that blank/whitespace `captureId` and `sessionId` values are treated as **absent**, not malformed, so a naive client sending `""` stays on the working implicit path (data-model.md validation rules, FR-013)

### Implementation for User Story 2

- [x] T017 [US2] In `ResolveFrame` in `src/GameBot.Service/Endpoints/ImageDetectionsEndpoints.cs`, implement the `captureId` branch: resolve via `CaptureSessionStore.TryGet`; a miss returns `NotFound("capture_not_found", "capture not found or expired")`, reusing `DetectAllAsync`'s existing wording; a hit decodes `capture.Png` to the frame to measure
- [x] T018 [US2] In `ResolveFrame`, implement the `sessionId` branch: `ISessionManager.GetSession(sessionId)` returning null → `NotFound("session_not_found", ...)`; otherwise `IScreenSourceFactory.ForSession(sessionId).GetLatestScreenshot()`, with a null frame → `Unavailable`, in `src/GameBot.Service/Endpoints/ImageDetectionsEndpoints.cs`
- [x] T019 [US2] Ensure the named-target branches take precedence over ambient device context and over the single-session fallback, and that `threshold`, `maxResults` and `overlap` are applied identically on the named and unnamed paths (FR-004, FR-010), in `src/GameBot.Service/Endpoints/ImageDetectionsEndpoints.cs`
- [x] T020 [US2] Map `NotFound` outcomes to `Results.Json(new { code, message }, statusCode: StatusCodes.Status404NotFound)` in `DetectAsync` in `src/GameBot.Service/Endpoints/ImageDetectionsEndpoints.cs`
- [x] T021 [US2] Guard frame decoding so an undecodable capture returns an explicit failure rather than an empty match set (FR-008), mirroring `DetectAllAsync`'s existing decode guard, in `src/GameBot.Service/Endpoints/ImageDetectionsEndpoints.cs`
- [x] T022 [US2] Run the new unit and contract tests and confirm they pass: `dotnet test tests/unit/GameBot.UnitTests.csproj --filter "FullyQualifiedName~ImageDetectTargetResolution"` and `dotnet test tests/contract/GameBot.ContractTests.csproj --filter "FullyQualifiedName~DetectImage"`

**Checkpoint**: Both P1 stories complete — the fix is whole. A multi-emulator observer has a sound
absence probe again.

---

## Phase 5: User Story 3 — Single-emulator callers are unaffected (Priority: P2)

**Goal**: Prove the compatibility guarantee. This phase is verification, not new behaviour — if it
needs production code changes, something in Phase 3 or 4 was done wrong.

**Independent test**: The pre-existing detect tests pass unmodified.

- [x] T023 [US3] Run the full pre-existing contract suite for this endpoint **without editing the assertions**, confirming `DetectUsesDefaultsAndReturnsMatchesShape` and `DetectAllowsParameterOverrides` still pass: `dotnet test tests/contract/GameBot.ContractTests.csproj --filter "FullyQualifiedName~DetectImage"`. These run with `GAMEBOT_USE_ADB=false` and **zero** sessions, so they are the live guard on FR-016 — if they fail, the ambiguity check is counting sessions before requesting a frame.
- [x] T024 [P] [US3] Add a contract test to `tests/contract/Images/DetectImageTests.cs` covering the ambient/single-session path explicitly: a request omitting both new fields returns `200` with the unchanged response shape (FR-013, guarantee G4)
- [x] T025 [US3] Run the whole unit and contract suites to confirm nothing outside this endpoint regressed: `dotnet test GameBot.sln`

**Checkpoint**: Compatibility demonstrated by tests rather than asserted in prose.

---

## Phase 6: Polish & Cross-Cutting Concerns

**Purpose**: API documentation and the constitution's Living Documentation gate (principle V,
NON-NEGOTIABLE).

- [x] T026 [P] Update the `ImageDetectRequest()` Swagger example in `src/GameBot.Service/Swagger/SwaggerConfig.cs` (~line 1229) to include `captureId`, and document the `400` / `404` / `409` / `503` responses for `ApiRoutes.ImageDetect`. **Both call sites** must be updated — the detect branch appears twice (~line 379 and ~line 615).
- [x] T027 [P] Add a bullet to the device-resolution list in `docs/architecture.md` (~line 256, beside the existing `GET /api/emulator/screenshot` `409 ambiguous_session` bullet) describing the new detect behaviour, so the two endpoints read as one rule
- [x] T028 Refresh the `_Last reviewed:_` line at the top of `docs/architecture.md` (line 13) to `2026-09-14 (feature 085 device-scoped image detection)`
- [x] T029 [P] Set the `**Status**:` line in `specs/085-device-scoped-detect/spec.md` to `Implemented`
- [x] T030 [P] Add the row `| 085 | Device-Scoped Image Detection | Implemented |` to the table in `specs/STATUS.md`
- [x] T031 Leave `specs/openapi.json` untouched and confirm that is still correct: it is a point-in-time snapshot last updated by feature 051, which features 080–084 all left alone. The living API surface is the runtime Swagger document (T026) plus `docs/architecture.md` (T027). Updating a stale snapshot for this one route would misrepresent it as current. If this file has since become generated on build, update it instead.
- [x] T032 Run the full build and test suite one final time and confirm it is green before commit, per the constitution's release-blocker rule: `dotnet build GameBot.sln` then `dotnet test GameBot.sln`
- [x] T034 **(discovered during implementation)** Update `tests/integration/ImageDetectionsEndpointTests.cs`: `DetectReturnsEmptyWhenNoScreenshot` asserted the exact behaviour this feature removes — an unobtainable screenshot answered with `200` and an empty match array. It encodes the defect rather than a feature, so per spec Clarifications Q2 it is rewritten as `DetectFailsExplicitlyWhenNoScreenshot`, asserting `503 emulator_unavailable`. This is the only pre-existing test whose assertions changed; the two detect contract tests passed untouched.
- [x] T033 Confirm the constitution's principle II coverage bar (≥80% line, ≥70% branch **for the touched area**) is met for `ImageDetectionsEndpoints.cs` and `ImageDetectionsValidation.cs`, using the repo's existing coverage collection. If collection is not wired up for a targeted run, record in the PR description which branches of `ResolveFrame` each test exercises — all four outcomes (`Frame`, `NotFound`, `Ambiguous`, `Unavailable`) must be covered by T006, T007, T013, T014.

---

## Dependencies

```text
Phase 1 (T001)
   └─> Phase 2 Foundational (T002-T005)   [BLOCKS every story]
          ├─> Phase 3 US1 (T006-T012)     P1
          │      └─> Phase 4 US2 (T013-T022, incl. T016a/T016b)  P1  [shares ResolveFrame + DetectAsync]
          │             └─> Phase 5 US3 (T023-T025)  P2  [verifies 3+4]
          └─────────────> Phase 6 Polish (T026-T033)
```

**Story independence**: US1 is independently shippable and alone removes the silent failure. US2
depends on US1 only because both edit `ResolveFrame` and `DetectAsync` — not conceptually — so they
are sequenced to avoid conflicting edits to the same two methods. US3 is pure verification of both.

## Parallel Execution Opportunities

Tasks marked `[P]` touch different files or independent test cases.

- **Phase 3 tests**: T006 and T007 together (same new file, independent cases).
- **Phase 4 tests**: T013–T016b together — T013/T014 in the unit file, T015/T016/T016a/T016b in the contract file.
- **Phase 6**: T026 (Swagger), T027 (architecture), T029 (spec status), T030 (STATUS.md) are four
  different files and can all run together. T028 must follow T027 (same file); T032 then T033 run last.

**Not parallelizable**: T008–T011 and T017–T021 all edit `ImageDetectionsEndpoints.cs`.

## Implementation Strategy

**MVP = Phase 1 + Phase 2 + Phase 3 (US1).** That alone closes the safety hole in issue #176: the
watchdog stops receiving a fabricated `0.0`.

**Ship-together recommendation**: US1 and US2 land in the same PR. US1 alone converts a silent wrong
answer into a hard failure with no remedy for the multi-emulator setup that reported the bug; US2 is
what gives those callers a working probe again.

**The one trap to watch**: spec FR-016. The intuitive implementation — check `ListSessions().Count >
1` first, then fetch the frame — reads correctly and passes review, but the existing contract tests
run with zero sessions against a stub screen source, so that ordering turns them red while fixing
nothing real. T008 and T023 exist to catch this.
