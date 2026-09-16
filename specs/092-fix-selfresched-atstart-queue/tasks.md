# Tasks: Self-reschedule bookings keep an AtQueueStart-only queue running

**Input**: Design documents from `specs/092-fix-selfresched-atstart-queue/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, quickstart.md

**Tests**: REQUIRED. FR-008 and constitution II require a failing test for this bug fix before the fix. All tests go in `tests/unit/Queues/QueueExecutionServiceTests.cs`, in a new `// ── Feature 092 …` region after the feature-065 self-reschedule tests. They use the existing helpers `Harness`, `FakeTimeProvider`, `AddQueueWithEntries`, `AtQueueStart(...)`, `h.Coordinator.ScheduleSelf(...)`, `WaitForAsync` and `WaitUntilStoppedAsync`.

**Organization**: grouped by user story. Every test lives in the same file, so tasks within that file are sequential, not [P].

## Phase 1: Setup

- [ ] T001 Build the unit test project and run `QueueExecutionServiceTests` to record a green baseline: `dotnet test C:\src\GameBot\tests\unit\GameBot.UnitTests.csproj --filter "FullyQualifiedName~QueueExecutionServiceTests"`

## Phase 2: Foundational

- [ ] T002 Add a computed `internal bool HasPendingSelfRescheduleWork` property to `QueueRunHandle` in `src/GameBot.Service/Services/QueueExecution/QueueRunHandle.cs`. It returns `HasPendingTimerFirings || !PendingNextCycleStart.IsEmpty || !PendingOncePerRun.IsEmpty || !PendingLiveSchedules.IsEmpty` (EveryStepInjections deliberately excluded, research R-002). Give it an XML doc comment citing feature 092 / issue #198. Match the member visibility used by the neighbouring `HasPendingTimerFirings`.
- [ ] T003 Add unit tests for `HasPendingSelfRescheduleWork` in `tests/unit/Queues/QueueRunHandleTimerFiringTests.cs`. Cover five cases: false on a fresh handle; true for each of a timer firing, a next-cycle-start entry, a once-per-run entry and a live schedule; false when only an EveryStep injection is registered.

**Checkpoint**: helper exists and is tested; engine behaviour unchanged.

## Phase 3: User Story 1 - "Run at start, then reschedule yourself" roster runs indefinitely (Priority: P1) 🎯 MVP

**Goal**: An at-queue-start-only template whose sequences book themselves keeps the queue running and fires each booking when due, with cycle execution off or on.

**Independent Test**: The US1 tests below fail against the current engine and pass after T007.

### Tests (write first, confirm RED)

- [ ] T004 [US1] Add `AtQueueStartOnlyTimerRescheduleKeepsNonCyclingRunAliveAndFires` in `tests/unit/Queues/QueueExecutionServiceTests.cs`. This mirrors SC-001. Setup: `FakeTimeProvider` clock, three entries `AtQueueStart("A")`, `AtQueueStart("B")`, `AtQueueStart("C")`, non-cycling. The handler books the running sequence itself with `SelfRescheduleOption.Timer` +90 s on that sequence's first firing only. Wait until all three have run and `handle.HasPendingTimerFirings` is true, then assert `IsRunning` is true and each ran exactly once. Advance the clock 90 s and wait until stopped. Assert each ran twice and the summary contains "6 sequence(s) executed".
- [ ] T005 [US1] Add `AtQueueStartOnlyTimerRescheduleRepeatsForSeveralGenerations` in `tests/unit/Queues/QueueExecutionServiceTests.cs`. Non-cycling, the handler books `A` +90 s on **every** firing. Loop three times: wait for the pending timer firing, then advance the clock 90 s. After that, assert A ran ≥ 4 times and the queue is still running. Stop it and assert the summary contains "stopped manually" (SC-002).
- [ ] T006 [US1] Add `AtQueueStartOnlyTimerRescheduleFiresInCyclingRun` in `tests/unit/Queues/QueueExecutionServiceTests.cs`. Same as T004 with `cycle: true`: advance the clock, wait until A has run twice, then stop. Assert A ran ≥ 2 times (FR-004).
- [ ] T007 [US1] Run the T003–T006 tests and confirm T004–T006 FAIL for the reported reason (the run stops before the booking fires). Append a one-line "Red run:" note under this task in this tasks.md.

### Implementation

- [ ] T008 [US1] In `src/GameBot.Service/Services/QueueExecution/QueueExecutionService.cs` (`RunAsync`, gate at ~line 386), widen the loop-entry condition to `oncePerRunEntries.Count > 0 || everyStepEntries.Count > 0 || timerEntries.Count > 0 || handle.HasPendingSelfRescheduleWork`. Update the adjacent comment to explain that at-queue-start firings may book work only the loop drains (feature 092, #198). The loop body stays unchanged.
- [ ] T009 [US1] Re-run T003–T006 and confirm GREEN.
- [ ] T010 [US1] Add `AtQueueStartOnlyOncePerRunRescheduleFiresBeforeRunEnds` in `tests/unit/Queues/QueueExecutionServiceTests.cs` (edge case: non-timer bookings). Non-cycling, `AtQueueStart("A")`; A books `R` with `SelfRescheduleOption.OncePerRun` once. Assert the run completes with executed order `A, R` and the summary contains "2 sequence(s) executed".

## Phase 4: User Story 2 - Waiting on a booking behaves like the mixed-template case (Priority: P2)

**Goal**: The booking wait uses the existing idle-pause and poll and stays promptly stoppable.

**Independent Test**: Idle-pause engages for an at-queue-start-only run with a booking far away, and a stop during the wait ends the run as "stopped manually".

- [ ] T011 [US2] Add `AtQueueStartOnlyRescheduleWaitIsStoppableAndNotFailed` in `tests/unit/Queues/QueueExecutionServiceTests.cs`. Non-cycling, A books itself +12 h. Wait until the timer firing is pending, stop, then assert A ran once, `FinalStatus` is "success" and the summary contains "stopped manually" (FR-005, edge case "booking abandoned at stop").
- [ ] T012 [US2] Add `AtQueueStartOnlyRescheduleWaitUsesIdlePause` in `tests/unit/Queues/QueueExecutionServiceTests.cs`, modelled on `IdlePauseEnabledBacksGameOutThenForegroundsBeforeDueSequence`. Setup: `FakeTimeProvider`, `h.EnsureGame.ExecutedCountProvider = () => h.Sequences.Executed.Count`, `AtQueueStart("A")` only, `pauseWhenIdle: true`, `idleThresholdSeconds: 30`; A books itself +10 min on its first firing only. Wait for `h.Sessions.HomeCount >= 1`, then assert `handle.IsIdlePaused` is true and `h.EnsureGame.Calls == 0`. Advance the clock 10 min and wait until stopped. Assert `HomeCount == 1`, `EnsureGame.Calls == 1`, executed order `A, A`, and `FinalStatus` "success".

## Phase 5: User Story 3 - Unchanged completion when nothing is booked (Priority: P3)

**Goal**: Regression guard for FR-006/FR-007.

**Independent Test**: An at-queue-start-only template without bookings completes immediately with an unchanged summary.

- [ ] T013 [US3] Add `AtQueueStartOnlyWithoutReschedulesCompletesImmediately` in `tests/unit/Queues/QueueExecutionServiceTests.cs`. Test it twice, non-cycling and cycling (a `[Theory]` over `cycle`): template `AtQueueStart("A"), AtQueueStart("B")` with no bookings. Assert the run stops on its own with executed order `A, B`, and the summary contains "completed full run: 2 sequence(s) executed" and does NOT contain "across" (no multi-cycle note, since cycles stays 1).
- [ ] T014 [US3] Add `AtQueueStartOnlyEveryStepInjectionDoesNotKeepRunAlive` in `tests/unit/Queues/QueueExecutionServiceTests.cs`. Non-cycling; A registers an `EveryStep` injection for `R`. Assert the run completes on its own (research R-002) and R ran exactly once, from the pre-pass every-step pass.
- [ ] T015 [US3] Run the full `QueueExecutionServiceTests` class and confirm the existing tests are still green, notably `CycleWithEmptyTemplateCompletesWithoutBusyLoop` and the feature-060/065 tests (SC-003).

## Phase 6: Polish & Cross-Cutting

- [ ] T016 [P] Update the "Self-reschedule action" bullet in `docs/architecture.md`: a pending self-reschedule booking (or pending live schedule) keeps a run alive whatever schedule types the template uses, including an at-queue-start-only template (feature 092). Refresh the `_Last reviewed:` line to 2026-09-16 (feature 092).
- [ ] T017 [P] Set `**Status**: Implemented` in `specs/092-fix-selfresched-atstart-queue/spec.md` and append row `| 092 | Self-reschedule Keeps AtQueueStart-only Queue Running | Implemented |` to `specs/STATUS.md`.
- [ ] T018 Run the whole unit test project (`dotnet test C:\src\GameBot\tests\unit\GameBot.UnitTests.csproj`) and a Release build of `src/GameBot.Service`; both must be green with no new analyzer warnings.

## Dependencies & Execution Order

- T001 → T002 → T003 → T004–T006 (write) → T007 (red) → T008 (fix) → T009 (green) → T010 → US2 (T011–T012) → US3 (T013–T015) → Polish (T016–T018).
- US2 and US3 depend on T008 only in the sense that their tests must pass. They are independent of each other.
- T016 and T017 touch different files and can run in parallel.

## Parallel Example

```text
T016 docs/architecture.md   ‖   T017 specs/092.../spec.md + specs/STATUS.md
```

## Implementation Strategy

MVP is Phase 1–3 (T001–T010): it fixes the reported defect with failing-first tests. US2 and US3 add behavioural and regression guards; Polish keeps the living docs honest (constitution V).
