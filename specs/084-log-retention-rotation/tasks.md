---

description: "Task list for feature 084: Execution Log Retention Default & Long-Run Rotation"
---

# Tasks: Execution Log Retention Default & Long-Run Rotation

**Input**: Design documents from `/specs/084-log-retention-rotation/`
**Prerequisites**: [plan.md](./plan.md), [spec.md](./spec.md), [research.md](./research.md), [data-model.md](./data-model.md), [contracts/execution-log-rotation.md](./contracts/execution-log-rotation.md), [quickstart.md](./quickstart.md)

**Tests**: Included — the project constitution (`.specify/memory/constitution.md`, Principle II) requires tests for all executable logic; the feature's Independent Test criteria in spec.md are implemented as these test tasks.

**Organization**: Tasks are grouped by user story (US1 = retention default, US2 = long-run rotation) to enable independent implementation and testing of each.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (US1, US2)

## Path Conventions

Single ASP.NET Core backend (`src/GameBot.Domain`, `src/GameBot.Service`, `tests/`) + React SPA (`src/web-ui/src`) — matches plan.md's Project Structure.

---

## Phase 1: Setup

**Purpose**: Confirm a clean starting point before making changes.

- [X] T001 Confirm baseline is green: run `dotnet build` on the solution and `dotnet test` for `tests/unit/ExecutionLogs`, `tests/unit/Queues`, and `tests/integration/ExecutionLogs` (no code changes in this task; abort and report if baseline is already red, per the constitution's non-negotiable red-build gate).

---

## Phase 2: Foundational

**Purpose**: Shared blocking prerequisites for all user stories.

**⚠️ CRITICAL**: None identified. US1 (retention default) and US2 (rotation) touch disjoint files — US1 is a single default-value change in `src/GameBot.Domain/Logging/ExecutionLogRetentionPolicy.cs`; US2's model/DTO/service/UI changes do not depend on US1. Both stories may start immediately after Phase 1.

**Checkpoint**: Proceed directly to Phase 3.

---

## Phase 3: User Story 1 - Shorter default retention keeps the log list clean (Priority: P1)

**Goal**: A fresh/never-configured deployment defaults execution log retention to 7 days instead of 60, with no other behavior change.

**Independent Test**: Start with no saved retention policy file, read the retention policy, confirm it is 7 days; confirm an already-saved policy value is untouched; confirm explicit overrides still work.

### Tests for User Story 1

- [X] T002 [P] [US1] Add unit test asserting `ExecutionLogRetentionPolicy.Default.RetentionDays == 7` and that `Enabled`/`CleanupIntervalMinutes` defaults are unchanged, in `tests/unit/ExecutionLogs/ExecutionLogRetentionPolicyDefaultsTests.cs` (new file).
- [X] T003 [P] [US1] Extend `tests/integration/ExecutionLogs/ExecutionLogRetentionIntegrationTests.cs` with a case: no saved policy file → `GET /api/execution-logs/retention` returns `retentionDays: 7`; a previously-saved policy (any value, e.g. 45) is preserved unchanged when the service restarts after this change (simulate by writing a policy file with `RetentionDays = 45` before starting the host and asserting the read-back value is still 45, not silently migrated to 7).

### Implementation for User Story 1

- [X] T004 [US1] Change the default in `src/GameBot.Domain/Logging/ExecutionLogRetentionPolicy.cs`: `RetentionDays` auto-property initializer `60` → `7`. No other change (validation, `Default` static property, and `ExecutionLogRetentionPolicyRepository` clamping logic are untouched).

**Checkpoint**: User Story 1 is fully functional and independently testable — run T002/T003 to confirm.

---

## Phase 4: User Story 2 - Long-running queues get their execution history rotated (Priority: P1)

**Goal**: A queue run open for more than 24 continuous hours is split into linked run segments, cut only between sequence firings, each segment carrying a structured link to its neighbor(s); a run that never exceeds 24h is unaffected.

**Independent Test**: Drive a queue run's sequence firings across a simulated >24h span (via the existing injectable `TimeProvider`), inspect the resulting execution log entries, and confirm the segment chain, link fields, and cut-only-between-firings guarantee described in spec.md's Acceptance Scenarios.

### Tests for User Story 2

- [X] T005 [P] [US2] Add unit tests in `tests/unit/ExecutionLogs/ExecutionLogRotationTests.cs` (new file) covering `ExecutionLogService`'s new rotate helper (introduced in T009): closing the old root entry sets `RotatedToExecutionId` and a "rotated, continuation available" summary/status; the new root entry sets `RotatedFromExecutionId` and a "continuation of a prior run" summary; both entries are persisted (via a fake `IExecutionLogRepository`) before the helper returns.
- [X] T006 [P] [US2] Add unit tests in `tests/unit/Queues/QueueExecutionServiceTests.cs` (existing file) covering the rotation-decision integration: using the existing fake `TimeProvider` test seam, (a) a run whose active segment is <24h old never rotates across several firings; (b) a run that crosses the 24h mark rotates exactly once, immediately before the next firing, and that firing's `ExecutionLogContext.RootExecutionId`/`ParentExecutionId` is the **new** root, never the old one; (c) a run crossing multiple 24h periods (simulate 3 days) produces a chain of segments, each linked correctly to its neighbor(s); (d) a run stopped before 24h elapses never rotates; and (e) FR-012 — a queue stopped after having rotated and then restarted begins a fresh, unrotated segment (new root id, both rotation link fields `null`) whose 24h clock starts over, carrying no link back to the previous run's chain.
- [X] T007 [P] [US2] Add an integration test `tests/integration/ExecutionLogs/ExecutionLogRotationIntegrationTests.cs` (new file, following the conventions in `tests/integration/ExecutionLogs/ExecutionLogRetentionIntegrationTests.cs`) that runs a queue through a simulated >24h span via the API/service layer and asserts, via `GET /api/execution-logs/{id}`, that the closed-out segment's `relatedObjects` contains a "Continues in newer run segment" link and the new segment's contains a "Continued from earlier run segment" link, and that retention cleanup can delete the closed-out segment independently without breaking the still-active continuation segment's own retrieval (per FR-013). Also cover FR-014: seed a pre-existing entry file whose JSON lacks the two new rotation fields entirely (as every entry written before this feature does) and assert it still lists and renders its detail correctly with both fields read as `null`.

### Implementation for User Story 2

- [X] T008 [P] [US2] Add `RotatedToExecutionId` and `RotatedFromExecutionId` (`string?`, both default `null`) to `ExecutionLogEntry` in `src/GameBot.Domain/Logging/ExecutionLogModels.cs`.
- [X] T009 [US2] In `src/GameBot.Service/Services/ExecutionLog/ExecutionLogService.cs`, add `Task<string> LogQueueRotateAsync(string oldRootId, string queueId, string queueName, CancellationToken ct = default)`: loads the existing root entry for `oldRootId`, upserts it with `FinalStatus` reflecting closure (as built: `"success"`, terminal — the run's eventual finalize targets the newest segment, so leaving the closed one `"running"` would dangle forever; `Summary` and a trailing `"rotation"` detail item state that rotation occurred and a continuation is available) and `RotatedToExecutionId` set to a newly-generated id; then creates the new root entry (mirroring `LogQueueStartAsync`'s construction) with that same new id, `RotatedFromExecutionId = oldRootId`, and `Summary` stating it continues a prior run; returns the new root id. Depends on T008.
- [X] T010 [P] [US2] Add `RotatedToExecutionId`/`RotatedFromExecutionId` (`string?`) to `ExecutionLogEntryDto` in `src/GameBot.Service/Models/ExecutionLogs.cs`, and map them in whatever existing entry→DTO projection populates `ExecutionLogEntryDto` (locate via `ExecutionLogEntryDto` construction sites in `src/GameBot.Service/Endpoints/ExecutionLogsEndpoints.cs` / `ExecutionLogService.cs`). Depends on T008.
- [X] T011 [US2] In `ExecutionLogService`'s detail-building method (`BuildDetailProjection`, referenced from `src/GameBot.Service/Endpoints/ExecutionLogsEndpoints.cs:194`), add one `RelatedObjectLinkDto` to the projection's `RelatedObjects` when `RotatedToExecutionId`/`RotatedFromExecutionId` is set, per the shapes documented in `contracts/execution-log-rotation.md` (label, `TargetId`, and `IsAvailable`/`UnavailableReason`). As built: `TargetType` is `"execution"` and `IsAvailable` is `true` whenever the id is present — matching the existing "Parent execution" link in the same method, which `BuildDetailProjection` being a pure static over one entry (no repository access) already constrains it to. A target since removed by retention behaves exactly like a deleted parent does today. Depends on T008, T009.
- [X] T012 [US2] In `src/GameBot.Service/Services/QueueExecution/QueueExecutionService.cs`, add a private helper (e.g. `Task<string> RotateIfDueAsync(string currentRootId, ExecutionQueue queue, QueueRunHandle handle, CancellationToken ct)`) that reads the current root entry's age via the existing `IExecutionLogService`/repository read path, and — if open more than 24 hours — calls `LogQueueRotateAsync` (T009), updates `handle.RootExecutionId` to the new id, and returns the new root id (otherwise returns `currentRootId` unchanged). Depends on T009.
- [X] T013 [US2] Wire the helper from T012 into `RunAsync`'s loop in `QueueExecutionService.cs`: replace the closed-over `var rootId = ...` reads at each `RunOneSequenceAsync(...)` call site with `rootId = await RotateIfDueAsync(rootId, queue, handle, ct)` immediately before the call, so the rotation decision runs exactly once per firing boundary and never mid-firing (FR-005/FR-007/FR-009a). Depends on T012.
- [X] T014 [P] [US2] Add `rotatedToExecutionId`/`rotatedFromExecutionId` (optional) to the execution-log entry TypeScript type in `src/web-ui/src/services/executionLogsApi.ts`.
- [X] T015 [US2] Surface the rotation link in the web UI. As built: in `src/web-ui/src/pages/executionLogGrid.ts`'s `projectEntryRow`, append the linked segment id(s) to the row's existing `info` text. The Execution Logs page renders top-level rows from this projection and does not render `relatedObjects` anywhere today, so this is the minimal surfacing that does not amount to the UI redesign the spec puts out of scope; the backend rotation summary already flows into the same column. Depends on T010, T014.
- [X] T016 [P] [US2] Add Jest coverage in `src/web-ui/src/pages/__tests__/executionLogGrid.test.ts` for the T015 rendering (continuation named on a closed segment, previous segment named on a continuation, both named mid-chain, nothing added to a run that never rotated).

**Checkpoint**: User Story 2 is fully functional and independently testable — run T005-T007 to confirm; combined with User Story 1, both spec.md priorities are delivered.

---

## Phase 5: Polish & Cross-Cutting Concerns

- [X] T017 Update `docs/architecture.md`'s execution log domain-model section to describe the 7-day default and the rotation/segment-chain concept (`RotatedToExecutionId`/`RotatedFromExecutionId`), refreshing its "Last reviewed" date, per the constitution's Living Documentation principle.
- [X] T018 Update this feature's `specs/084-log-retention-rotation/spec.md` **Status** line to `Implemented` and add/update the corresponding row in `specs/STATUS.md`, per the constitution's Living Documentation principle (do this only once implementation and tests are complete).
- [X] T019 Run `quickstart.md`'s verification steps end-to-end and note the result in the PR description. Done via their automated equivalents, which is how quickstart.md specifies the rotation scenario should be exercised (fake `TimeProvider` rather than a real 24-hour wait): retention default → `ExecutionLogRetentionPolicyDefaultsTests` + `ExecutionLogRetentionIntegrationTests`; rotation scenario and chain → `QueueExecutionServiceTests` rotation cases + `ExecutionLogRotationTests` + `ExecutionLogRotationIntegrationTests`; UI surfacing → `executionLogGrid.test.ts` rotation cases. A live manual UI walkthrough of a rotated chain needs a queue genuinely running past 24h against an emulator and was not performed.

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies.
- **Foundational (Phase 2)**: Empty — no blocking prerequisites (see above).
- **User Story 1 (Phase 3)**: Depends only on Phase 1. Fully independent of User Story 2.
- **User Story 2 (Phase 4)**: Depends only on Phase 1. Fully independent of User Story 1 (T004 and T008-T016 touch disjoint files).
- **Polish (Phase 5)**: Depends on both User Story 1 and User Story 2 being complete.

### Within User Story 2

- T008 (domain field) before T009 (rotate helper), T010 (DTO field), T011 (detail projection).
- T009 before T011, T012.
- T012 before T013 (wiring into the run loop).
- T010, T011 before T014-T016 (UI needs the fields/links to exist).
- Tests T005-T007 should be written first and observed to fail before T008-T013 land (constitution Principle II / template convention), though because this is new behavior rather than a bug fix, "fail" here means "does not compile / helper does not exist yet" rather than a red assertion against existing behavior.

### Parallel Opportunities

- T002, T003 in parallel (different files) — Phase 3 tests.
- T005, T006, T007 in parallel (different files) — Phase 4 tests.
- T008, T010 in parallel (different files, both additive field changes) once tests are in place.
- T014, T016 in parallel with backend Phase 4 implementation tasks once the DTO shape (T010) is settled.
- Phase 3 (User Story 1) and Phase 4 (User Story 2) can be implemented fully in parallel by different contributors — no shared files.

---

## Parallel Example: User Story 2 tests

```bash
# Launch all three US2 test tasks together (different files, no shared dependency):
Task: "Add unit tests for ExecutionLogService rotate helper in tests/unit/ExecutionLogs/ExecutionLogRotationTests.cs"
Task: "Add rotation-decision unit tests in tests/unit/Queues/QueueExecutionServiceTests.cs"
Task: "Add rotation integration test in tests/integration/ExecutionLogs/ExecutionLogRotationIntegrationTests.cs"
```

---

## Implementation Strategy

### MVP First

Both user stories are P1 in spec.md and are independent, so either can ship alone as a first increment:

1. Complete Phase 1: Setup.
2. Complete Phase 3: User Story 1 (small, low-risk — ships the retention-default win immediately).
3. Complete Phase 4: User Story 2 (the larger rotation mechanism).
4. Complete Phase 5: Polish.

### Incremental Delivery

1. Setup → baseline confirmed green.
2. User Story 1 → test independently → mergeable on its own if desired.
3. User Story 2 → test independently → mergeable on its own if desired.
4. Polish → living docs + status lines updated, quickstart run once both stories are in.

---

## Notes

- [P] tasks = different files, no dependencies.
- [Story] label maps task to US1/US2 for traceability.
- Both user stories are independently completable and testable, and touch disjoint files, so there is no cross-story dependency to protect.
- Commit after each task or logical group (handled by the pipeline's own commit checkpoints, not per-task, given this run is autonomous).
