# Tasks: A cycling queue with nothing due waits instead of spinning empty cycles

**Input**: Design documents from `specs/093-fix-empty-cycle-spin/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, quickstart.md

**Tests**: Required. FR-010 and constitution II ask for failing tests first for a bug fix.

**Organization**: Tasks are grouped by user story. US1 and US2 share the same loop change (T005), so US2 depends on the Foundational phase and US1.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: The user story the task belongs to (US1, US2, US3)

## Phase 1: Setup

- [X] T001 Confirm the baseline: run `dotnet test C:\src\GameBot\tests\unit\GameBot.UnitTests.csproj --filter "FullyQualifiedName~QueueExecutionServiceTests|FullyQualifiedName~QueueCycleLedgerTests"` on the branch before any change and record that it is green (constitution gate)

---

## Phase 2: Foundational (blocking prerequisites)

- [X] T002 [P] Add failing tests for a new `DiscardOpenIfEmpty()` in tests/unit/QueueCycleLedgerTests.cs. Test 1: after `EnsureOpen(t0)` with no entries, `DiscardOpenIfEmpty()` then `EnsureOpen(t1)` then `CompleteOpen(t2)` publishes one cycle with `StartedAt == t1` and `CyclesCompleted == 1`. Test 2: an open cycle with a recorded entry is NOT discarded (a later `CompleteOpen` keeps the original `StartedAt` and the entry). Test 3: `DiscardOpenIfEmpty()` with no open cycle is a no-op and never changes `CyclesCompleted` or `ConsecutiveFailedCycles`
- [X] T003 Implement `public void DiscardOpenIfEmpty()` in src/GameBot.Service/Services/QueueExecution/QueueCycleLedger.cs. Under `_lock`, set `_open = null` only when `_open is { Entries.Count: 0 }`. Add an XML doc comment stating it is an observer-only mutator used by a cycling run's empty iteration, so a later cycle's `StartedAt` excludes idle waiting (feature 093, FR-007). Run T002 green

**Checkpoint**: the ledger helper is available.

---

## Phase 3: User Story 1 - A cycling scheduled-only queue sits quietly between firings (Priority: P1) 🎯 MVP

**Goal**: a cycling run whose iteration ran no sequence counts no cycle and waits for the next due firing instead of looping at once.

**Independent Test**: a cycling template with one at-queue-start entry and a relative timer on a frozen clock keeps `CyclesCompleted == 0` while waiting, stays Running, fires the timer after the clock advances, then counts exactly one cycle.

### Tests for User Story 1 (write first, confirm RED)

- [X] T004 [US1] Add failing tests to tests/unit/Queues/QueueExecutionServiceTests.cs in a new `// ── feature 093 (#200): cycling run with nothing due waits ──` region. Use the existing `Harness(clock)`, `AddQueueWithEntries(..., cycle: true)`, `AtQueueStart(...)`, `TimerEntry`/relative-timer helper (check how `NonCyclicRunWaitsForRelativeTimerThenFiresAndCompletes` builds a relative timer), `WaitForAsync` and `h.Registry.TryGet`. (a) `CyclingScheduledOnlyQueueDoesNotCountEmptyCycles`: template `AtQueueStart("S")` + relative timer `T` at +10 min, frozen `FakeTimeProvider(FakeStart)`. After start, wait until `S` executed, then `await Task.Delay(500)`, and assert `handle.Cycles.SnapshotHealth().CyclesCompleted == 0` and `h.Service.IsRunning("q1")`. Stop in the finally or at the end. (b) `CyclingScheduledOnlyQueueFiresTimerAndCountsOneCycle`: same template. Advance the clock by 10 min, then `WaitForAsync(() => CyclesCompleted >= 1)`, settle ~300 ms, and assert `T` executed once, `CyclesCompleted == 1` and that `lastCycleStartedAt >= FakeStart + 10 min` (the waiting time is excluded, FR-007). Stop. (c) `CyclingQueueWaitingOnDistantBookingIsStoppableWithoutSpinning`: cycling template `AtQueueStart("S")` whose handler books a `SelfRescheduleOption.Timer` +12 h on its first run via `h.Coordinator.ScheduleSelf` (idle pause off). Assert `CyclesCompleted == 0` after 500 ms and the run is still Running. Then `StopAsync` and assert `Log.Summary` contains "stopped manually" (FR-008). Run the tests and confirm (a), (b) and (c) FAIL on the current code (the cycle count is huge)

### Implementation for User Story 1

- [X] T005 [US1] In src/GameBot.Service/Services/QueueExecution/QueueExecutionService.cs `RunAsync` loop: (1) right after `handle.Cycles.EnsureOpen(...)`, capture `var indexAtIterationStart = index;`. (2) Change the once-per-run gate: compute `var cycleHasWork = oncePerRunEntries.Count > 0 || everyStepEntries.Count > 0 || !handle.PendingOncePerRun.IsEmpty || index != indexAtIterationStart;` and enter the block when `queue.CycleExecution ? cycleHasWork : !schedule.OncePerRunPassDone`. A cycling run's first iteration with no work must NOT enter it (FR-002). The non-cycling first pass still counts its one cycle as today (FR-009). (3) Replace `if (queue.CycleExecution) continue;` with: `if (queue.CycleExecution) { if (index != indexAtIterationStart) continue; handle.Cycles.DiscardOpenIfEmpty(); } else if (!HasPendingRelativeOrLive()) break;`, so an empty cycling iteration falls through to the existing idle-pause / poll wait. Update the surrounding comments to cite feature 093 / #200 (an empty iteration is not a cycle and waits; a cycling run never ends on its own). Leave the non-cycling path and the empty-template `else` branch untouched
- [X] T006 [US1] Run T004 green, then run the whole `QueueExecutionServiceTests` class and confirm that `AtQueueStartOnlyTimerRescheduleFiresInCyclingRun`, `CycleExecutionRepeatsWithoutReloadingTemplate`, `TimerFiresAtMostOncePerCalendarDayAcrossCycles`, `AtQueueStartRunsOncePerRunOnCyclingQueue`, `EveryStepAlsoRunsInCyclicMode`, `CycleWithEmptyTemplateCompletesWithoutBusyLoop` and `OnlyAtQueueStartTemplateCompletesEvenWhenCycling` still pass

**Checkpoint**: no spin; the timer still fires; cycle counting is honest.

---

## Phase 4: User Story 2 - The idle pause applies to a cycling queue that is waiting (Priority: P1)

**Goal**: the wait from T005 enters the existing idle-pause hold when it is enabled and the gap is beyond the threshold, and polls otherwise.

**Independent Test**: a cycling scheduled-only queue with `pauseWhenIdle:true` and a firing 10 min out reports `IsIdlePaused` with the right resume time, then fires after the clock advances.

- [X] T007 [US2] Add tests to tests/unit/Queues/QueueExecutionServiceTests.cs (feature 093 region), modelled on `AtQueueStartOnlyRescheduleWaitUsesIdlePause` and `IdlePauseEnabledBacksGameOutThenForegroundsBeforeDueSequence`. (a) `CyclingScheduledOnlyQueueEntersIdlePauseWhileWaiting`: cycling, `pauseWhenIdle: true`, `idleThresholdSeconds: 30`, template `AtQueueStart("S")` + relative timer `T` +10 min. Wait until `handle.IsIdlePaused`, then assert `handle.IdlePausedUntil == FakeStart + 10 min` and `CyclesCompleted == 0`. Advance the clock 10 min, wait for `T` executed, and assert `handle.IsIdlePaused` is false afterwards. Stop. (b) `CyclingScheduledOnlyQueueWithIdlePauseDisabledNeverPauses`: same template with `pauseWhenIdle: false`. After 500 ms assert `handle.IsIdlePaused` is false and `CyclesCompleted == 0`. Stop. Write the tests before T005 lands where possible; (a) must fail on the pre-fix code (it never pauses). Run them green after T005

**Checkpoint**: idle pause works for cycling queues.

---

## Phase 5: User Story 3 - Cycling rosters that do real work each cycle are unchanged (Priority: P2)

**Goal**: regression guard: a cycling roster with once-per-run or every-step entries still loops with no wait and counts one cycle per pass.

**Independent Test**: a cycling once-per-run template reaches several cycles quickly and never idle-pauses, even with idle pause enabled.

- [X] T008 [US3] Add tests to tests/unit/Queues/QueueExecutionServiceTests.cs (feature 093 region). (a) `CyclingOncePerRunQueueStillCountsACyclePerPassWithoutWaiting`: cycling, `pauseWhenIdle: true`, `idleThresholdSeconds: 1`, template `OncePerRun("A")` + relative timer `T` +10 min, handler with `Task.Delay(10, ct)`. Wait until `A` executed ≥ 5 times within 5 s, assert `CyclesCompleted >= 4` and that `handle.IsIdlePaused` was never observed true (sample it while waiting). Stop. (b) `CyclingEveryStepOnlyQueueStillCyclesEachPass`: cycling template `EveryStep("E")` with a 10 ms handler. Wait until `E` executed ≥ 3 times and assert `CyclesCompleted >= 2`. Stop

**Checkpoint**: working cycling rosters are unaffected.

---

## Phase 6: Polish & Cross-Cutting Concerns

- [X] T009 [P] Update docs/architecture.md: in the "Queue cycle observability (feature 086)" bullet add one sentence saying that a cycling run's iteration that runs no sequence is not a cycle, is not published, and waits for the next due firing (idle pause when enabled, otherwise the 250 ms poll) instead of looping at once (feature 093, #200). Refresh the `_Last reviewed:` line at the top to `2026-09-17 (feature 093 cycling queue with nothing due waits instead of spinning empty cycles)`
- [X] T010 [P] Set `**Status**: Implemented` in specs/093-fix-empty-cycle-spin/spec.md and add the row `| 093 | Cycling Queue With Nothing Due Waits Instead of Spinning | Implemented |` after the 092 row in specs/STATUS.md (edit with the Edit tool to keep UTF-8 intact)
- [X] T011 Full gate: `dotnet build C:\src\GameBot\GameBot.sln -c Debug` with no new warnings, then `dotnet test C:\src\GameBot\tests\unit\GameBot.UnitTests.csproj` all green. Run the integration cycle and failure-policy tests with `dotnet test C:\src\GameBot\tests\integration\GameBot.IntegrationTests.csproj --filter "FullyQualifiedName~QueueCycles|FullyQualifiedName~QueueFailurePolicy"` 
- [X] T012 Write the PR perf note required by constitution IV for hot-path changes, to be included in the PR body: the queue run loop no longer busy-loops for cycling runs with nothing due (was ~450k iterations/s, now ≤ 4 polls/s or an idle-pause hold). Cycling rosters with real work pay one extra integer comparison per iteration. The evidence is T004(a), where `CyclesCompleted` stays 0 over 500 ms against hundreds of thousands before the fix

## Coverage notes

- SC-002 (wake-up rate) is verified indirectly by T004(a) and T007(b): the cycle count stays 0 while waiting, and only the 250 ms poll or the idle-pause hold can pace the loop.
- FR-008's failure-policy pause gate (`WaitIfPausedAsync` at the top of each iteration) and the daily-retry wake-up (`ComputeNextDue` includes armed retries) reuse unchanged code. Their existing tests (feature 087 pause tests, `FailedDailyTimerIsRetriedAfterTheDelayAndStopsOnceItSucceeds`) stay green under T011.

---

## Dependencies & Execution Order

- **Setup (T001)** → **Foundational (T002 → T003)** → **US1 (T004 → T005 → T006)** → **US2 (T007)** and **US3 (T008)** → **Polish (T009, T010 → T011)**
- T005 needs T003 (it calls `DiscardOpenIfEmpty`).
- T007 and T008 edit the same test file as T004, so they run sequentially after T004.
- T011 runs after T009/T010. T012 is prose for the PR and can be written any time after T006.

## Parallel Example

```text
# After T006:
T009 docs/architecture.md   |   T010 spec status + STATUS.md
```

## Implementation Strategy

- **MVP** = Phases 1–3 (US1). They remove the CPU hot loop and make `cyclesCompleted` honest.
- US2 comes with the same code change and only adds verification. US3 is a pure regression guard.
- Commit after T011 is green.
