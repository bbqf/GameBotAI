# Tasks: Queue Cycle Observability

**Input**: Design documents from `/specs/086-queue-cycle-observability/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/queue-cycles.md
**Issue**: [#180](https://github.com/bbqf/GameBotAI/issues/180)

**Tests**: Included. The constitution requires unit coverage of core logic and integration coverage of
externally visible contracts, and the spec's SC-003 names a condition that must be demonstrably
detectable.

**Organization**: Grouped by user story. US1 (health) is a viable standalone MVP; US2 (cycle records)
builds on the same ledger; US3 is regression protection that constrains both.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: US1 / US2 / US3, or shared phases

## Path Conventions

Single repository. Service code under `src/GameBot.Service/`, tests under `tests/unit/`
(`GameBot.UnitTests.csproj`) and `tests/contract/` (`GameBot.ContractTests.csproj`).

---

## Phase 1: Foundational (Blocking Prerequisites)

**Purpose**: The ledger every story reads from. No user story can begin until this is done.

**⚠️ CRITICAL**: US1 and US2 both project the same ledger; it must exist and be correct first.

- [x] T001 Create `src/GameBot.Service/Services/QueueExecution/QueueCycleLedger.cs` with the records
      from data-model.md: `QueueCycleRecord` (Ordinal, StartedAt, CompletedAt, Succeeded, Entries),
      `QueueCycleEntryOutcome` (SequenceId, Succeeded), and `QueueCycleHealth` (CyclesCompleted,
      LastCycleStartedAt, LastCycleCompletedAt, LastCycleSucceeded, ConsecutiveFailedCycles,
      CurrentEntryIndex). All `internal sealed record`. XML docs on each.
- [x] T002 Implement the `QueueCycleLedger` class in the same file: one private lock, a private
      `OpenCycle` holder, a `Queue<QueueCycleRecord>` of completed cycles, and the counters.
      `const int MaxRetainedCycles = 50`.
- [x] T003 Implement the mutators — `EnsureOpen(now)` (idempotent: no-op when a cycle is already
      open), `RecordEntry(sequenceId, succeeded)` (no-op when none is open),
      `SetCurrentEntryIndex(index)` / `ClearCurrentEntryIndex()`, `CompleteOpen(now)` (derive
      `Succeeded` = no entry failed, assign the 1-based ordinal, update
      `CyclesCompleted`/`ConsecutiveFailedCycles`, trim the ring to 50; no-op when none is open), and
      `RecordEmptyCycle(now)`. Every mutator returns `void` and must not throw for a caller-actionable
      reason — this is what makes the ledger a pure observer (plan, FR-020).
- [x] T004 Implement the readers — `SnapshotHealth()` and `SnapshotCycles(limit)` (newest first, at
      most `limit`) — each copying under the lock so no reader holds it while the run loop needs it,
      mirroring `QueueRunHandle.SnapshotPendingTimerFirings()`.
- [x] T005 Add a `public QueueCycleLedger Cycles { get; } = new();` to
      `src/GameBot.Service/Services/QueueExecution/QueueRunHandle.cs`, in the observability region
      alongside the existing current-sequence and idle-pause state, with an XML doc pointing at this
      feature.

**Checkpoint**: The ledger compiles and is reachable from a run handle; nothing observes it yet.

---

## Phase 2: Wire the run loop (Blocking Prerequisites)

**Purpose**: Populate the ledger from the engine. Shared by US1 and US2 — neither has data without it.

**⚠️ CONSTRAINT**: `ExecuteRunAsync` in `QueueExecutionService.cs` is a very large method and the
build-time Roslyn taint analyzers scale super-linearly with method body size (plan R5). Each edit
below MUST be a single call with no new locals, no branching and no lambdas.

- [x] T006 In `src/GameBot.Service/Services/QueueExecution/QueueExecutionService.cs`, call
      `handle.Cycles.EnsureOpen(_timeProvider.GetLocalNow())` at the top of each `do { … } while (true)`
      iteration, next to the existing `everyStepRanThisIteration = false;`.
- [x] T007 In the same file, call `handle.Cycles.RecordEntry(<sequenceId>, <ok>)` after **every**
      `RunOneSequenceAsync` result — the at-queue-start drain, the live-schedule drain, the
      self-reschedule timer drain, the once-per-run roster loop, and the once-per-run reschedule drain
      — immediately beside each existing `executed++ / if (!ok) failed++` pair.
- [x] T008 In the same file, call `handle.Cycles.CompleteOpen(_timeProvider.GetLocalNow())` at the
      existing `cycles++` (immediately after `schedule.MarkOncePerRunPassDone()`), so a cycle is
      published exactly when the engine counts one.
- [x] T009 In the same file, bracket the roster-pass entry execution with
      `handle.Cycles.SetCurrentEntryIndex(entryIndex)` before `RunOneSequenceAsync` and
      `handle.Cycles.ClearCurrentEntryIndex()` after the per-entry every-step pass, so
      `currentEntryIndex` is populated only within the roster pass (spec Clarifications).
- [x] T010 In the same file, call `handle.Cycles.RecordEmptyCycle(_timeProvider.GetLocalNow())` in the
      empty-template branch that sets `cycles = 1`, so an idle-but-alive queue still reports one
      completed cycle (spec Edge Cases).
- [x] T011 Verify by inspection that `RunEveryStepPassAsync` records its firings through T007's call
      sites (it runs sequences via the same helper), and that no ledger call sits on a path that can
      throw into the run loop.

**Checkpoint**: A run populates the ledger. Verified by the US1 tests below.

---

## Phase 3: User Story 1 — Tell a working queue from a stuck one (Priority: P1) 🎯 MVP

**Goal**: `GET /api/queues/{id}` reports live health for a running queue (FR-001–FR-009).

**Independent Test**: Start a cycling queue, read it while running, watch the health advance; make
every cycle fail and watch `consecutiveFailedCycles` climb — without stopping it.

- [x] T012 [P] [US1] Create `src/GameBot.Service/Contracts/Queues/QueueHealthResponse.cs` with the
      fields in data-model.md — `runStartedAt` (FR-001), `cyclesCompleted`, `lastCycleStartedAt`,
      `lastCycleCompletedAt`, `lastCycleStatus`, `consecutiveFailedCycles`, `currentEntryIndex`,
      `currentSequenceId` — with `lastCycleStatus` as `"success"`/`"failure"` (not a boolean) to match
      the execution log's existing status vocabulary.
- [x] T013 [US1] Add a nullable `QueueHealthResponse? Health` to
      `src/GameBot.Service/Contracts/Queues/QueueDetailResponse.cs`, documented as null when the queue
      is not running.
- [x] T014 [US1] In `src/GameBot.Service/Endpoints/QueuesEndpoints.cs`, add a static
      `ProjectHealth(QueueRunHandle)` helper beside the existing `ProjectMonitor`, mapping
      `SnapshotHealth()` plus the handle's existing `CurrentSequenceId` and `RunStartedAt`.
- [x] T015 [US1] Add a shared static `TryGetLiveRun(id, runtime, registry, out handle)` helper in
      `QueuesEndpoints.cs` implementing the FR-008a liveness gate — a run is live iff
      `runtime.GetStatus(id) == QueueExecutionStatus.Running` **and** `registry.TryGet` yields a
      handle. Both read paths MUST use this one helper so they cannot drift apart (research R8).
- [x] T015a [US1] Populate `Health` in `BuildDetailAsync` via `TryGetLiveRun`, leaving it **null**
      otherwise (FR-008). Add `IQueueRunRegistry` to the `GET {id}` handler's parameters.
- [x] T016 [P] [US1] Create `tests/unit/QueueCycleLedgerTests.cs` covering: ordinal is 1-based and
      keeps counting past the ring bound; `EnsureOpen` is idempotent; outcome is failure iff an entry
      failed; an entry-less cycle succeeds; `ConsecutiveFailedCycles` climbs then resets on success;
      the ring retains only the newest 50; an open cycle that never completes is never published;
      `RecordEntry` with no open cycle is a no-op.
- [x] T017 [P] [US1] Create `tests/contract/Queues/QueueCyclesApiContractTests.cs` with the health
      cases: a not-running queue returns `health: null`; a running queue before its first completed
      cycle returns `cyclesCompleted: 0` with the three `lastCycle*` fields null together.
- [x] T018 [US1] Add the SC-003 regression test to that file: a run whose cycles all fail reports a
      climbing `consecutiveFailedCycles` and `lastCycleStatus: "failure"` **while `status` is still
      `Running`** — the 2026-09-12 condition, now detectable.

**Checkpoint**: US1 is independently shippable. A stuck queue is distinguishable from a healthy one.

---

## Phase 4: User Story 2 — Diagnose why cycles fail (Priority: P1)

**Goal**: `GET /api/queues/{id}/cycles?limit=n` returns recent cycles with per-entry outcomes while
the queue runs (FR-010–FR-016).

**Independent Test**: Let several cycles complete, request the cycles mid-run, confirm per-entry
outcomes appear newest-first and the limit is honoured.

- [x] T019 [P] [US2] Create `src/GameBot.Service/Contracts/Queues/QueueCyclesResponse.cs` containing
      `QueueCyclesResponse` (QueueId, Running, Cycles), `QueueCycleResponse` (Ordinal, StartedAt,
      CompletedAt, Status, Entries) and `QueueCycleEntryResponse` (SequenceId, SequenceName, Status),
      per data-model.md.
- [x] T020 [US2] In `QueuesEndpoints.cs`, add a static `ProjectCycles(...)` helper that maps ledger
      records to the DTOs and resolves each `SequenceName` from `ISequenceRepository` at projection
      time (null when the sequence no longer exists), keeping the endpoint lambda thin.
- [x] T021 [US2] Register `group.MapGet("{id}/cycles", …).WithName("GetQueueCycles")` immediately
      after the `{id}/monitor` route, carrying a comment that states the shared contract decision
      (200 + `running:false` rather than 404/409 for a known, stopped queue; safe to poll).
- [x] T022 [US2] Implement the handler: 404 for an unknown queue id; `running:false` with an empty
      list when `TryGetLiveRun` (T015) reports no live run; otherwise the newest cycles, newest first.
      Use the same helper as US1 — do not re-derive liveness here (FR-008a).
- [x] T023 [US2] Implement `limit` handling — default 20, clamped to 1–50, out-of-range clamped rather
      than rejected (FR-014).
- [x] T024 [P] [US2] Extend `tests/contract/Queues/QueueCyclesApiContractTests.cs` with the cycles
      cases: completed cycles carry ordinal, both instants, status and per-entry outcomes; ordering is
      newest-first; an unknown queue is 404; a never-run queue is 200 with an empty list; `limit=0`,
      `limit=-1` and `limit=9999` are all clamped and none returns an error.
- [x] T025 [P] [US2] Add a ledger unit test asserting `SnapshotCycles(limit)` returns at most `limit`
      records, newest first, and that ordinals survive ring eviction.

**Checkpoint**: An operator can identify the failing entry from the cycles alone (SC-004).

---

## Phase 5: User Story 3 — No regressions (Priority: P2)

**Goal**: Existing consumers and non-cycling queues are unaffected (FR-017–FR-020).

**Independent Test**: The existing queue and execution-log suites pass unchanged.

- [x] T026 [US3] Add a contract test asserting a non-cycling queue reports exactly one completed cycle
      for its single pass, and that its trailing timer-poll iterations add no further cycles (FR-019).
- [x] T027 [US3] Add a contract assertion that the terminating execution-log record's status and
      summary text for a queue run are unchanged by this feature (FR-018).
- [x] T028 [US3] Run the existing `tests/contract/Queues/QueuesApiContractTests.cs`,
      `QueueLiveScheduleApiContractTests.cs` and `QueueConcurrencyContractTests.cs` unchanged and
      confirm they pass — no existing field renamed or removed (FR-017).
- [x] T028a [US3] Add a contract test for the FR-008a gate: a queue reporting `status: "Stopped"`
      never carries a non-null `health`, and `GET {id}` and `GET {id}/cycles` agree about whether the
      run is in progress.
- [x] T028b [US3] Add a contract test for the concurrent-queues edge case: two queues running on
      different devices each report their own `health` and their own cycles, with no cross-talk
      (spec Edge Cases).

**Checkpoint**: All three stories complete and mutually consistent.

---

## Phase 6: Documentation and Polish

**Purpose**: Constitution V (NON-NEGOTIABLE) and the quality gates.

- [x] T029 [P] Update `src/GameBot.Service/Swagger/SwaggerConfig.cs` to document the `{id}/cycles`
      route (including the `limit` parameter and its clamping) and the additive `health` block.
- [x] T030 Update `docs/architecture.md`: add the `health` block and the `{id}/cycles` route to the
      queue API surface, note that cycle history is run-scoped and does not survive a restart, and
      **refresh the "Last reviewed" date**.
- [x] T031 [P] Set `**Status**: Implemented` in `specs/086-queue-cycle-observability/spec.md` and add
      the 086 row to `specs/STATUS.md`. Feature 072 (live monitor) is complemented, not superseded —
      leave its status alone.
- [x] T032 Build the solution and run both test projects; confirm zero warnings introduced and all
      tests green before committing (constitution: "an evaluation of test builds and runs results is
      necessary before each commit").
- [x] T033 Verify the quickstart flow in `quickstart.md` reads correctly against the implemented
      routes (field names, clamping behaviour, null health).

---

## Dependencies

- **Phase 1 (T001–T005)** blocks everything.
- **Phase 2 (T006–T011)** blocks US1 and US2 — without it the ledger stays empty.
- **US1 (Phase 3)** and **US2 (Phase 4)** are independent of each other once Phase 2 is done and may
  proceed in either order, with one exception: **T015 (the shared liveness helper) blocks T022**, since
  US2 must use the same gate rather than re-deriving it. Both stories touch `QueuesEndpoints.cs`, so
  their endpoint tasks (T014/T015/T015a and T020–T023) should not be done concurrently by different
  agents.
- **US3 (Phase 5)** needs US1 and US2 in place to assert they changed nothing else.
- **Phase 6** last.

## Parallel Opportunities

- T012 and T019 (separate new DTO files) can be written in parallel.
- T016 (unit tests) is independent of all endpoint work and can start as soon as Phase 1 lands.
- T029 and T031 are independent files and can be done together.

## Implementation Strategy

Land Phase 1 + Phase 2 + US1 first: that alone closes the detection gap that cost 44 hours, and is a
complete, shippable increment. US2 then converts detection into diagnosis. Keep every run-loop edit to
a single call — the analyzer cost, not style, is the reason.
