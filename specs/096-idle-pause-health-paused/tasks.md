# Tasks: Report Idle Pause in queue health

**Input**: Design documents from `specs/096-idle-pause-health-paused/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/queue-health.md, quickstart.md

**Tests**: Required. This is a bug fix (constitution II: failing test first), and spec FR-011 asks for a contract/integration test.

## Format: `[ID] [P?] [Story] Description`

## Phase 1: Setup

No setup is needed: existing projects, no new dependencies.

---

## Phase 2: Foundational (blocking)

**Purpose**: The idle-pause register records its start instant. Both US1 and US2 depend on it.

- [ ] T001 Write failing unit tests in new file `tests/unit/Queues/QueueRunHandlePauseTests.cs` covering: (a) `EnterIdlePause(resumeAt, at)` sets `IdlePausedAt = at` and `IdlePausedUntil = resumeAt`; (b) a second `EnterIdlePause(laterResume, laterAt)` updates `IdlePausedUntil` but keeps the first `IdlePausedAt`; (c) `ClearIdlePause()` nulls both; (d) after a clear, a new `EnterIdlePause` records the new `at`
- [ ] T002 In `src/GameBot.Service/Services/QueueExecution/QueueRunHandle.cs`, add `_idlePausedAt` guarded by `_idleLock`, a public `IdlePausedAt` getter (XML doc), change `EnterIdlePause(DateTimeOffset resumeAt)` to `EnterIdlePause(DateTimeOffset resumeAt, DateTimeOffset at)` setting `_idlePausedAt` only when it is null, and make `ClearIdlePause()` null it too
- [ ] T003 Update both `handle.EnterIdlePause(...)` calls in `IdlePauseHoldAsync` in `src/GameBot.Service/Services/QueueExecution/QueueExecutionService.cs` to pass `_timeProvider.GetLocalNow()` (the entry call and the per-tick `due` update)
- [ ] T004 [P] Update the three `handle.EnterIdlePause(...)` call sites in `tests/unit/Queues/QueueMonitorServiceTests.cs` (lines ~453, ~476, ~601) to the new signature, passing `Now`
- [ ] T005 Extend `CyclingScheduledOnlyQueueEntersIdlePauseWhileWaiting` in `tests/unit/Queues/QueueExecutionServiceTests.cs` to assert `paused.IdlePausedAt` is not null, is at or after `FakeStart`, and is not after `FakeStart + 10 min`, and that after the pause ends `IdlePausedAt` is null again

**Checkpoint**: `dotnet build` passes, and T001 plus the monitor and execution-service unit tests pass.

---

## Phase 3: User Story 1 - Health reports an idle pause (Priority: P1) 🎯 MVP

**Goal**: `GET /api/queues/{id}` reports `paused: true`, `pausedAt`, `pauseReason` and `pauseKind: "idle"` during an idle pause, and null fields once it ends.

**Independent Test**: The integration test registers an idle-paused run handle and reads `health` and `/monitor`.

### Tests for User Story 1

- [ ] T006 [P] [US1] Add failing unit tests to `tests/unit/Queues/QueueRunHandlePauseTests.cs` for `SnapshotPause()`: not paused → `(false, null, null, null)`; idle-paused → `(true, IdlePausedAt, "idle pause: resumes at HH:mm", "idle")` with `HH:mm` from the current `IdlePausedUntil`; after `ClearIdlePause` → not paused again
- [ ] T007 [P] [US1] Create failing integration test `tests/integration/Queues/QueueHealthIdlePauseTests.cs` (follow the WebApplicationFactory/host setup and JSON helpers used in `tests/integration/Queues/QueueFailurePolicyRunTests.cs`). Create a queue via `POST /api/queues`; resolve `IQueueRunRegistry` and `IQueueRuntimeStore` from `app.Services`; `TryAdd` a hand-built `QueueRunHandle { QueueId = id, Cts = new() }`, call `EnterIdlePause(resumeAt, pausedAt)` and `SetStatus(id, Running)`. Assert that `GET /api/queues/{id}` `health` has `paused == true`, `pausedAt` equal to the given instant, `pauseReason` starting with `idle pause: resumes at`, and `pauseKind == "idle"`, and that `GET /api/queues/{id}/monitor` `current.scheduleKind == "IdlePause"` at the same time. Then `ClearIdlePause()` and assert `paused == false` with null `pausedAt`/`pauseReason`/`pauseKind` and no `IdlePause` current item. In `finally`, remove the handle and set the status back to Stopped

### Implementation for User Story 1

- [ ] T008 [US1] In `src/GameBot.Service/Services/QueueExecution/QueueRunHandle.cs` (or a sibling file `QueuePauseSnapshot.cs` in the same folder), add `internal readonly record struct QueuePauseSnapshot(bool Paused, DateTimeOffset? PausedAt, string? Reason, string? Kind)` and `internal static class QueuePauseKinds { Idle = "idle"; FailurePolicy = "failurePolicy"; }` with XML docs, plus `public QueuePauseSnapshot SnapshotPause()` on the handle: read the policy register under `_policyLock` first (if paused, return its values with kind FailurePolicy); otherwise read `_idlePausedUntil`/`_idlePausedAt` under `_idleLock` and return the idle values, with the reason formatted `string.Format(CultureInfo.InvariantCulture, "idle pause: resumes at {0:HH:mm}", until)`; otherwise return the default not-paused snapshot
- [ ] T009 [US1] Add `public string? PauseKind { get; set; }` to `src/GameBot.Service/Contracts/Queues/QueueHealthResponse.cs`
- [ ] T010 [US1] In `ProjectHealth` in `src/GameBot.Service/Endpoints/QueuesEndpoints.cs`, replace the `Paused`/`PausedAt`/`PauseReason` assignments with values from one `handle.SnapshotPause()` call, set `PauseKind`, and replace the stale "Note PolicyPausedAt, not IdlePausedUntil" comment with one explaining the combined snapshot and `pauseKind`

**Checkpoint**: T001, T006 and T007 pass.

---

## Phase 4: User Story 2 - The two pauses stay distinguishable (Priority: P2)

**Goal**: A failure-policy pause keeps its exact values, adds `pauseKind: "failurePolicy"`, and wins over an idle pause. `/resume` still does not end an idle pause.

**Independent Test**: The failure-policy integration test plus precedence unit and integration cases.

- [ ] T011 [P] [US2] Add unit tests to `tests/unit/Queues/QueueRunHandlePauseTests.cs`: policy-paused → `(true, PolicyPausedAt, PauseReason, "failurePolicy")`; both policy- and idle-paused → the policy values win; idle-paused with `ResumeFromPolicyPause()` returning false leaves the idle snapshot unchanged
- [ ] T012 [P] [US2] Extend `PausePolicyParksTheRunAndResumeRestartsIt` in `tests/integration/Queues/QueueFailurePolicyRunTests.cs` to assert `pauseKind == "failurePolicy"` while parked, and that `pauseKind` is null after a resume
- [ ] T013 [US2] Add a case to `tests/integration/Queues/QueueHealthIdlePauseTests.cs`: with the handle idle-paused, `POST /api/queues/{id}/resume` returns 200 with `resumed: false`, and a following `GET` still reports `pauseKind == "idle"` and `failurePolicyTripped == false`

**Checkpoint**: All pause tests pass, and the existing failure-policy integration tests pass unchanged apart from the added assertion.

---

## Phase 5: User Story 3 - The schema says what the pause fields mean (Priority: P3)

**Goal**: The published API description explains the widened fields.

- [ ] T014 [P] [US3] Create failing contract test `tests/contract/Queues/QueueHealthOpenApiTests.cs`, modelled on `tests/contract/Sequences/SequenceTimeLimitOpenApiTests.cs`. It fetches `/swagger/v1/swagger.json` and asserts that `components.schemas.QueueHealthResponse.properties`: `paused.description` contains both "idle" and "failure policy"; `pausedAt` and `pauseReason` have non-empty descriptions; `pauseKind.description` mentions `idle` and `failurePolicy`; and `pauseKind.enum` holds exactly those two values. It also asserts that the `/api/queues/{id}` GET operation description contains "pauseKind"
- [ ] T015 [US3] Create `src/GameBot.Service/Swagger/QueueHealthSchemaFilter.cs` (`internal sealed class : ISchemaFilter`, same shape as `SequenceTimeLimitSchemaFilter`). For `context.Type == typeof(QueueHealthResponse)` it sets the descriptions of `paused`, `pausedAt`, `pauseReason` and `pauseKind` per `contracts/queue-health.md` "Field descriptions", and sets `pauseKind`'s `Enum` to `OpenApiString("idle")`/`OpenApiString("failurePolicy")` from `QueuePauseKinds`. Register it with `options.SchemaFilter<QueueHealthSchemaFilter>()` in `src/GameBot.Service/GameBotServiceSetup.cs`. Append to the GET `/api/queues/{id}` `operation.Description` in `src/GameBot.Service/Swagger/SwaggerConfig.cs` (~line 508) a sentence saying `health.paused` covers both idle and failure-policy pauses, told apart by `health.pauseKind`. Also rewrite the XML docs on `Paused`/`PausedAt`/`PauseReason`/`PauseKind` in `src/GameBot.Service/Contracts/Queues/QueueHealthResponse.cs` to the same meaning

**Checkpoint**: The swagger document shows `pauseKind` and the updated descriptions.

---

## Phase 6: Polish & Cross-Cutting Concerns

- [ ] T016 [P] Update the feature 087 `health` field list and the idle-pause (feature 073) paragraph in `docs/architecture.md` to say that `health.paused`/`pausedAt`/`pauseReason` cover both pauses and that `pauseKind` distinguishes them; refresh the "Last reviewed" date
- [ ] T017 [P] Add a CHANGELOG entry in `CHANGELOG.md` (Fixed: `health.paused` now reports idle pauses; added `health.pauseKind`; issue #199)
- [ ] T018 [P] Set `**Status**: Implemented` in `specs/096-idle-pause-health-paused/spec.md`, add a 096 row to `specs/STATUS.md`, and mark `specs/087-queue-failure-policy/spec.md` Status as iterated by 096 (update its STATUS.md row to match)
- [ ] T019 Run `dotnet build` for the solution and the unit, integration and contract test suites from `quickstart.md`, and fix any failure before marking complete

---

## Dependencies & Execution Order

- Phase 2 (T001–T005) blocks everything. T002 must precede T003/T004/T005; T001 is written first and fails.
- US1 (T006–T010) depends on Phase 2. The tests T006/T007 are written before T008–T010.
- US2 (T011–T013) depends on T008 (`SnapshotPause`) and T009/T010 (`pauseKind` on the wire).
- US3 (T014–T015) depends on T008 (`QueuePauseKinds`) and T009 (`PauseKind` property). T014 is written first and fails.
- Polish (T016–T019) runs after all stories; T019 comes last.

## Parallel Opportunities

- T004 and T005 in parallel after T002/T003.
- T006 and T007 in parallel.
- T011 and T012 in parallel.
- T016, T017 and T018 in parallel.

## Implementation Strategy

MVP = Phase 2 + US1: `health` reports an idle pause. US2 locks in the failure-policy regression guard, US3 publishes the meaning, and Polish keeps the living docs honest. Single PR.
