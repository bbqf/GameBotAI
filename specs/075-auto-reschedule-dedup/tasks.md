# Tasks: Deduplicate Self-Rescheduled Sequence Firings

**Feature**: 075-auto-reschedule-dedup
**Input**: Design documents in `specs/075-auto-reschedule-dedup/`
**Prerequisites**: [plan.md](plan.md), [spec.md](spec.md), [research.md](research.md), [data-model.md](data-model.md), [contracts/add-timer-firing-dedup.md](contracts/add-timer-firing-dedup.md), [quickstart.md](quickstart.md)

**Tests**: Included (Constitution II requires a failing test reproducing the bug before the fix; the change is executable logic).

**Organization**: Tasks are grouped by user story. US1 (dedup) is the MVP and delivers the whole feature value; US2 (other mechanisms unchanged) is verified primarily by the existing suite plus a targeted regression assertion.

## Format: `[ID] [P?] [Story?] Description`

- **[P]**: can run in parallel (different files, no incomplete dependency)
- **[Story]**: US1 / US2 for user-story tasks; Setup/Foundational/Polish have no story label

## Terminology

The spec calls the deduped items "auto-rescheduled" / "future self-rescheduled firings"; in code these are exactly the **Timer** self-reschedule firings held in `QueueRunHandle._pendingTimerFirings` (option `SelfRescheduleOption.Timer`). The three terms are interchangeable throughout these tasks (per the spec's Clarifications).

## Path Conventions

Single .NET backend project. Production code under `src/GameBot.Service/Services/QueueExecution/`; tests under `tests/unit/Queues/` and `tests/integration/Queues/`.

---

## Phase 1: Setup

- [ ] T001 Confirm the baseline builds and the Queues suite is green before touching anything: run `dotnet build C:\src\GameBot\GameBot.sln` then `dotnet test C:\src\GameBot\GameBot.sln --filter "FullyQualifiedName~Queues"`. Record the pass count so post-change regressions are detectable.

---

## Phase 2: Foundational (blocking prerequisites)

No shared foundational work is required — the change is confined to one existing method and its tests. Proceed to Phase 3.

---

## Phase 3: User Story 1 — Self-rescheduling sequence never stacks duplicate future firings (Priority: P1) 🎯 MVP

**Goal**: A Timer self-reschedule for a sequence replaces any pending Timer firing already queued for that same sequence in the current run, so at most one future firing per sequence exists (FR-001, FR-002, FR-006, FR-007).

**Independent Test**: Run/queue a Timer self-reschedule for one sequence twice; assert exactly one pending firing remains, targeting the newest time.

### Tests for User Story 1 (write first, expect them to FAIL against current code)

- [ ] T002 [P] [US1] Create new unit test file `tests/unit/Queues/QueueRunHandleTimerFiringTests.cs`. Add a test proving `AddTimerFiring` for `seq-A` at T1 then `seq-A` at T2 leaves `SnapshotPendingTimerFirings()` with a single entry whose `SequenceId == "seq-A"` and `FireAt == T2` (contract C1; FR-002/FR-006). Add a test that a single `AddTimerFiring` yields exactly one entry (FR-007). Use `SelfRescheduleEntry` records with distinct `Id` GUIDs and `Option = SelfRescheduleOption.Timer`. Follow existing test style in `SelfRescheduleCoordinatorTests.cs` (xUnit + FluentAssertions, `#pragma warning disable CA2007, CA1861, CA1859`).
- [ ] T003 [US1] In `tests/unit/Queues/SelfRescheduleCoordinatorTests.cs`, add a test `TwoTimerReschedulesReplacePreviousBySequence`: with a `FakeTimeProvider`, `ScheduleSelf("q1","seq-A",Timer,null,10min)` then `ScheduleSelf("q1","seq-A",Timer,null,30min)`; assert `DrainDueTimerFirings(now+10min)` is empty (old T1 firing gone) and `DrainDueTimerFirings(now+30min)` returns exactly one firing for `seq-A` (FR-002 via the coordinator path).

### Implementation for User Story 1

- [ ] T004 [US1] In `src/GameBot.Service/Services/QueueExecution/QueueRunHandle.cs`, change `AddTimerFiring` (~line 144) to, under `_timerLock`, first `_pendingTimerFirings.RemoveAll(x => string.Equals(x.SequenceId, entry.SequenceId, StringComparison.Ordinal))` then `_pendingTimerFirings.Add(entry)`. Ensure `System` is available for `StringComparison` (file already uses `using System;`). This satisfies contract C1/C4 and keeps the remove+add atomic under the existing lock (C3).
- [ ] T005 [US1] Update the XML doc comment on `AddTimerFiring` to state it is most-recent-wins per `SequenceId` (replaces any pending Timer firing for the same sequence — feature 075), and amend the register block comment at `QueueRunHandle.cs:54-58` so it no longer claims *all four* registers accumulate: call out that the Timer register is now most-recent-wins per sequence while `PendingOncePerRun`/`PendingNextCycleStart` still accumulate and `EveryStepInjections` is idempotent.
- [ ] T006 [US1] Run the US1 tests: `dotnet test C:\src\GameBot\GameBot.sln --filter "FullyQualifiedName~QueueRunHandleTimerFiring|FullyQualifiedName~SelfRescheduleCoordinator"`. Confirm T002/T003 now pass.

**Checkpoint**: US1 delivers the entire feature value — duplicate future self-reschedule firings can no longer form.

---

## Phase 4: User Story 2 — Other scheduling mechanisms keep their existing behavior (Priority: P1)

**Goal**: Prove the dedup is scoped to Timer self-reschedule firings only and did not disturb any other schedule source (FR-003, FR-004, FR-005, FR-008).

**Independent Test**: Exercise the other registers alongside a deduped Timer firing and confirm unchanged behavior.

### Tests for User Story 2

- [ ] T007 [P] [US2] In `tests/unit/Queues/QueueRunHandleTimerFiringTests.cs`, add a test that `AddTimerFiring` for `seq-A` then `seq-B` leaves two entries (one per sequence), and that adding a second `seq-A` firing removes only the `seq-A` entry and leaves `seq-B` untouched and unreordered (contract C2; FR-003).
- [ ] T008 [P] [US2] Add/confirm a regression assertion that the non-Timer registers are untouched by Timer dedup: in `SelfRescheduleCoordinatorTests.cs` verify (or rely on existing `TwoOncePerRunReschedulesAccumulate` and `EveryStepIsIdempotentPerSequence`) that OncePerRun still accumulates two entries and EveryStep stays idempotent. If not already covered by a single assertion, add a focused test interleaving a Timer self-reschedule and a OncePerRun self-reschedule for the same sequence, asserting the OncePerRun queue is unaffected by the Timer dedup (FR-004).

### Implementation / verification for User Story 2

- [ ] T009 [US2] Run the full Queues suite `dotnet test C:\src\GameBot\GameBot.sln --filter "FullyQualifiedName~Queues"` and confirm all pre-existing tests still pass unchanged — `TwoOncePerRunReschedulesAccumulate`, `EveryStepIsIdempotentPerSequence`, the single-firing Timer tests, `QueueMonitorServiceTests` self-reschedule projections, and `SelfRescheduleRunIntegrationTests` (FR-004 regression guard; SC-003). Additionally, in `tests/integration/Queues/SelfRescheduleRunIntegrationTests.cs`, add (or extend an existing) test that a sequence issuing a Timer self-reschedule twice within one run executes on the deduped firing **exactly once** (FR-005: replacing does not add an execution) and that the pending firings are confined to the active run's handle (FR-008: a fresh run starts with no inherited firings). If a run-level harness for this already exists, assert the single-execution count there rather than adding a duplicate test.

**Checkpoint**: Dedup confirmed isolated to the Timer self-reschedule register.

---

## Phase 5: Polish & Cross-Cutting Concerns

- [ ] T010 [P] Living docs: update `docs/architecture.md` where it describes the self-reschedule Timer register — note Timer firings are most-recent-wins per sequence (no duplicate future firings) — and refresh its "Last reviewed" date (Constitution V).
- [ ] T011 [P] Update feature-065 history: set `specs/065-sequence-self-reschedule/spec.md` `**Status**` line to "Implemented (iterated by 075)", and add/adjust the `075-auto-reschedule-dedup` entry in `specs/STATUS.md` to reflect this feature's status; ensure `specs/075-auto-reschedule-dedup/spec.md` carries an accurate `**Status**` line (Constitution V).
- [ ] T012 Full gate: run `dotnet build C:\src\GameBot\GameBot.sln` then `dotnet test C:\src\GameBot\GameBot.sln`. Confirm the whole suite is green before commit (Constitution II Definition of Done). No web-ui change, so the web-ui gate is not exercised.

---

## Dependencies & Execution Order

- **Setup (T001)** → baseline confirmation, before everything.
- **US1 (T002–T006)**: T002/T003 (tests, [P] with each other since different files — but T003 edits the shared coordinator test file, so T002 is the only true [P]) precede T004/T005 (implementation, same file — sequential). T006 verifies after T004/T005.
- **US2 (T007–T009)**: T007/T008 after T004 exists (they assert the new behavior's scoping); T009 after implementation complete.
- **Polish (T010–T012)**: after US1 + US2 green. T010/T011 are [P] (different files). T012 last.

### Story independence

US1 is the MVP and stands alone. US2 is a verification/regression story over the same one-line change; it adds no production code and can be completed immediately after US1.

## Parallel Execution Examples

- Within US1: T002 can be written in parallel with drafting T003 (different files), but both must exist before running T006.
- Within Polish: T010 (`docs/architecture.md`) and T011 (`specs/` status lines) touch different files → run in parallel.

## Implementation Strategy

**MVP = Phase 1 + Phase 3 (US1).** That single `RemoveAll`+`Add` change plus its tests fully resolves the reported duplication. Phase 4 (US2) locks the scope with regression coverage; Phase 5 keeps docs/history honest and runs the full gate. Total: 12 tasks.
