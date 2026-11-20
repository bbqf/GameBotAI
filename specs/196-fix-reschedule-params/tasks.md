---

description: "Task list for the fix of the booked run parameter values (issue #409)"
---

# Tasks: A booked run uses the current parameter values of its template entry

**Input**: Design documents from `specs/196-fix-reschedule-params/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/booked-run-scope.md, quickstart.md

**Tests**: This is a bug fix. Constitution II requires a test that fails before the fix. Write each test first and make sure that it fails before the fix where the task says so.

**Organization**: The tasks are grouped by user story. Each story can be tested alone after Phase 2.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependency on an incomplete task)
- **[Story]**: The user story of the task (US1, US2, US3, US4)
- All paths are relative to the repository root `C:\src\GameBot`. Use absolute paths in PowerShell commands.

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Make sure that the baseline is green before the change.

- [ ] T001 Build the solution `GameBot.sln` and run `tests/unit/GameBot.UnitTests.csproj` with the filter `FullyQualifiedName~QueueParameterScope|FullyQualifiedName~ParameterScope|FullyQualifiedName~QueueRunSchedule|FullyQualifiedName~QueueRunPlan|FullyQualifiedName~SelfReschedule`. Record the result. A red baseline is a hard stop (constitution "Release blocker").

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: The entry key on the scope, the key lookup in the plan, and the test helper for a plan swap. All stories need them.

- [ ] T002 [P] Write tests (they must fail first, because the members do not exist) in the new file `tests/unit/Parameters/ParameterScopeEntryKeyTests.cs`: `ChildForEntry("e1", values)` gives `LayerName` = `entry`, `EntryKey` = `e1`, and resolves each value with the origin layer `entry`; a binding with a null value is skipped; a null or empty key throws `ArgumentException`; `EntryKey` of `Child(...)`, `TryBindChild(...)`, and `WithIteration(...)` under a keyed layer gives the key of the parent; `ParameterScope.FromQueue(queue).EntryKey` and `ParameterScope.Empty.EntryKey` are null; an `entry` layer with no values adds no name to `Describe()` and `ToSubstitutionContext()` (research R-006).
- [ ] T003 Add to `src/GameBot.Domain/Parameters/ParameterScope.cs`: a private field `_entryKey` (`string?`, set by a new constructor parameter; the existing calls pass null), `public ParameterScope ChildForEntry(string entryKey, IEnumerable<ParameterBinding>? values)`, and `public string? EntryKey` (walk from this node outward, first key that is not null). Write STE doc comments (feature 196) (plan D-1). Make T002 pass.
- [ ] T004 [P] Write tests (they must fail first) for `QueueRunPlan.KeyFor` in `tests/unit/Queues/QueueRunPlanTests.cs`: an enabled entry object gives its key, a disabled entry object gives its key, an entry with no ID gives `idx:<position>`, an object that is not in the plan (a clone) gives null.
- [ ] T005 Add `public string? KeyFor(QueueTemplateEntry entry)` to `src/GameBot.Service/Services/QueueExecution/QueueRunPlan.cs` (search `AllEntries` by reference, return `AllKeys[index]` or null; STE doc comment) (plan D-2). Make T004 pass.
- [ ] T006 [P] Write tests (they must fail first) for `QueueRunSchedule.TryGetEntry` in `tests/unit/Queues/QueueRunScheduleSwapTests.cs`: a key of the current plan gives the entry and `currentKey` = the key; a key of a disabled entry gives the entry; after a `SwapPlan` with new values, the key gives the new entry object with the new values; a key that a `SwapPlan` removed gives false; after `SwapPlan(..., renameMap: {"idx:0": "e-1"})` the key `idx:0` gives the entry with `currentKey` = `e-1`; two renames in a row (`idx:0` -> `e-1`, then `e-1` -> `e-2`) give the entry with `currentKey` = `e-2`.
- [ ] T007 Add to `src/GameBot.Service/Services/QueueExecution/QueueRunSchedule.cs`: the field `_renamedKeys` (ordinal), the record of each rename pair in `SwapPlan` under the lock, and `public bool TryGetEntry(string key, out QueueTemplateEntry entry, out string currentKey)` with the bounded walk of plan D-3. Write STE doc comments. Make T006 pass.
- [ ] T008 Add test helpers to `tests/unit/Queues/QueueParameterScopeTests.cs` (feature 196 section): `ApplyWhilePausedAsync(Harness h, string queueId, Func<QueueRunPlan, QueueRunPlan> change)` that calls `handle.RequestOperatorPause(now)`, waits until `handle.OperatorPause == OperatorPauseState.Paused`, calls `schedule.SwapPlan(change(schedule.Plan))` (the new plan has `Version` + 1, built with `QueueRunPlan.FromAll`), and calls `handle.ReleaseOperatorPause()`, as `QueueLiveApplyService.ApplyAsync` does; and `WithSlotValue(QueueRunPlan plan, string key, string? value)` that returns a plan where the entry with `key` is a clone with `slot` = `value` (null removes all values). Set `EntryId` on each new test entry (for example `var a = OncePerRun("A"); a.EntryId = "e-A";`), so the keys are stable. Only the rename test of T024 uses an entry with no ID.

**Checkpoint**: The scope can carry an entry key, and the schedule can find an entry by key. No behavior of the run changes yet.

---

## Phase 3: User Story 1 - A pending booking uses the new entry value (Priority: P1) MVP

**Goal**: When a booking fires, the booked run resolves the values that its entry has in the run plan at that time.

**Independent Test**: A Timer booking of entry `e-A` made with `slot` = `one`; apply `slot` = `two` while paused; the booked run resolves `two` from the `entry` layer.

### Tests for User Story 1

- [ ] T009 [US1] Write the test `TimerBookingUsesTheNewEntryValueAfterAnApply` in `tests/unit/Queues/QueueParameterScopeTests.cs` (fake clock, entry `e-A` with `slot` = `one`, the first run books `A` with Timer +15 min and `keep: earliest` through `h.Coordinator.ScheduleSelf(..., CurrentScope(h), SelfRescheduleKeep.Earliest)`; then `ApplyWhilePausedAsync` with `slot` = `two`; advance the clock 15 min; the booked run resolves `slot` = `two` with the origin layer `entry`). Run it and make sure that it FAILS before T013 (it resolves `one`).
- [ ] T010 [US1] Write the theory `BookingOfEachRegisterUsesTheNewEntryValueAfterAnApply` in `tests/unit/Queues/QueueParameterScopeTests.cs` for the variants `OncePerRun`, `EveryStep`, `AtQueueStartCycling`, `TimerOffset`. Do these steps:
  1. The first run of `A` (entry `e-A`, `slot` = `one`) books `R` with the variant and `CurrentScope(h)`.
  2. In the same handler call, after the booking, call `handle.RequestOperatorPause(now)` and start a background task. The task waits until `handle.OperatorPause == OperatorPauseState.Paused`, calls `schedule.SwapPlan(WithSlotValue(schedule.Plan, "e-A", "two"))` (version + 1), and calls `handle.ReleaseOperatorPause()`. For `TimerOffset`, use `ApplyWhilePausedAsync` after the booking and then advance the clock.
  3. For `OncePerRun`, `AtQueueStartCycling`, and `TimerOffset`: the booking fires only after a gate, so assert that each run of `R` resolves `two`. If the swap happens while the booking waits at its gate, the booking is held and fires in the next iteration (replan rule, research R-003).
  4. For `EveryStep`: the every-step pass has no gate and can run `R` once before the pause takes effect. Wait for the background task to end, then for one more run of `R`, and assert that the last run of `R` resolves `two`.
- [ ] T011 [US1] Write the test `KeepEarliestBookingKeepsItsFireTimeAfterAnApply` in `tests/unit/Queues/QueueParameterScopeTests.cs`: after `ApplyWhilePausedAsync`, `handle.SnapshotPendingTimerFirings()` has the same count, the same `FireAt`, and the same `Id` as before (FR-005, US1-3).

### Implementation for User Story 1

- [ ] T012 [US1] In `src/GameBot.Service/Services/QueueExecution/QueueExecutionService.cs` (`RunAsync`), change the local function `EntryScope(entry)`: `key = plan.KeyFor(entry)`; when `key` is null, keep the code of today; else return `queueScope.ChildForEntry(key, entry.ParameterValues)` (plan D-4).
- [ ] T013 [US1] In the same file, add the local function `BookingScope(SelfRescheduleEntry firing)` (plan D-4: `firing.Scope?.EntryKey`, `handle.Schedule.TryGetEntry`, `queueScope.ChildForEntry(currentKey, current.ParameterValues)`, else `firing.Scope ?? queueScope`). Use it in place of `x.Scope ?? queueScope` at the four sites: the `EveryStep` injection loop (`injection`), the next-cycle-start drain (`nextCycleEntry`), the Timer drain (`timerFiring`), and the once-per-run drain (`oprFiring`). Add an STE comment that names feature 196 and issue #409. Make T009, T010, and T011 pass.
- [ ] T014 [US1] Write the test `BookingOfARunNextRunUsesTheCurrentEntryValue` in `tests/unit/Queues/QueueExecutionServiceRunNextTests.cs` (FR-011): a run-next request for entry `e-A` (`slot` = `one`); the run-next run books `R` (OncePerRun or Timer) with its scope; apply `slot` = `two` with the pause, swap, and release steps of T010; `R` resolves `two`. Also assert that the run-next run itself resolved `one` (feature 184, FR-005 does not change). Run it and make sure that it FAILS before T015 (`R` resolves `one`, because the run-next scope has no key).
- [ ] T015 [US1] In the same file as T012, change `RunNext`: build the scope with `queueScope.ChildForEntry(plan.Keys[index], entry.ParameterValues)` (plan D-5). Keep the comment that the values are fixed at request time (feature 184, FR-005), and add that the key lets the bookings of the run-next run use the current values (FR-011). Make T014 pass.
- [ ] T016 [US1] Run all tests of `tests/unit/Queues/QueueParameterScopeTests.cs` and of `tests/unit/Queues` with `RunNext` in the name. The feature 116 tests (`BookedRunResolvesTheEntryValueOfTheRunThatBookedIt`, `TemplateEditAfterTheBookingDoesNotChangeTheBookedRun`, `BookingWithNoScopeStillGetsTheQueueScope`, and the Timer chain tests) MUST stay green (US1-2, clarification 1).

**Checkpoint**: US1 works. A pending booking uses the new value.

---

## Phase 4: User Story 2 - The bookings that a booked run makes also use the current value (Priority: P1)

**Goal**: Each booking of a chain (also the step-0 retry) resolves the current value.

**Independent Test**: R1 books R2; apply `slot` = `two` between; R2 resolves `two`.

- [ ] T017 [US2] Write the test `ChainBookingUsesTheNewEntryValueAfterAnApply` in `tests/unit/Queues/QueueParameterScopeTests.cs`: fake clock; each run of `A` (entry `e-A`, `slot` = `one`) books `A` with Timer +90 s; let the first booked run (R1) fire and book R2; apply `slot` = `two`; advance the clock; R2 resolves `two`. Also assert that the scope of R1 (before the apply) resolves `one`.
- [ ] T018 [US2] Write the test `StepZeroRetryOfAFailedBookedRunUsesTheNewEntryValue` in `tests/unit/Queues/QueueParameterScopeTests.cs`: each run of `A` books `A` with Timer +15 min at its start and then returns a failure (`FakeSequenceExecution` failure result) while `slot` resolves to `one`; apply `slot` = `two`; advance 15 min; the retry resolves `two` and the handler returns success.
- [ ] T019 [US2] Run the US2 tests. No production code change is expected (research R-001: the booked run gets a scope with the same key). If a test fails, fix the cause in `src/GameBot.Service/Services/QueueExecution/QueueExecutionService.cs`.

**Checkpoint**: US1 and US2 work. The chain of the issue recovers after an apply.

---

## Phase 5: User Story 3 - A type change of a parameter does not make pending bookings fail (Priority: P1)

**Goal**: A booking made with an old value of a different type resolves the new value.

**Independent Test**: The kept value is `pns-troop-tier-max`; the applied value is `0`; the booked run resolves `0`.

- [ ] T020 [US3] Write the test `BookingAfterAParameterTypeChangeResolvesTheNewNumberValue` in `tests/unit/Queues/QueueParameterScopeTests.cs`: entry `e-A` with `fighterTier` = `pns-troop-tier-max`; the run books `A` (Timer); apply `fighterTier` = `0`; the booked run resolves `fighterTier` = `0` from the `entry` layer, and the resolved text parses as a whole number (`int.TryParse` is true).
- [ ] T021 [US3] Add an integration test `TimerBookedRunUsesTheValueThatTheApplyChanged` to `tests/integration/Queues/SelfRescheduleRunIntegrationTests.cs`. Use the real dispatch path and the real HTTP apply path: the slot command and sequence of feature 116 with `timerRelativeOffset` `00:00:05`, and a template entry with an `EntryId` (so that the apply does not need an upgrade). Do these steps with the test `HttpClient`:
  1. Start the queue and wait for the first `parameters` log item (`slot` = `pns-nova-option-affinity`).
  2. Pause the queue with `POST /api/queues/{id}/pause`, and wait until the run handle has the state `OperatorPauseState.Paused`.
  3. Change the stored template entry to `slot` = `pns-other-option` through the template store (or `PUT /api/queue-templates/{id}`).
  4. Call `GET /api/queues/{id}/template-diff`, then `POST /api/queues/{id}/apply-template` with the base token and the key of the parameter difference. Expect `Applied`.
  5. Resume the queue with `POST /api/queues/{id}/resume`, and wait for a `parameters` log item with `slot` = `pns-other-option` and the origin layer `entry`. Use a long wait (60 s) for slow CI runners.

**Checkpoint**: The failure of the measurement of issue #409 does not occur.

---

## Phase 6: User Story 4 - A change of one entry does not touch the bookings of other entries (Priority: P2)

**Goal**: A change of one entry does not change the bookings or the values of other entries.

**Independent Test**: Bookings of `e-A` and `e-B`; apply a change of `e-A` only; both bookings stay with the same fire times; `e-B` resolves its own value.

- [ ] T022 [US4] Write the test `ApplyOfOneEntryDoesNotChangeTheBookingsOfOtherEntries` in `tests/unit/Queues/QueueParameterScopeTests.cs`: entries `e-A` (`slot` = `a1`) and `e-B` (`slot` = `b1`) on different sequences; each books itself with Timer (+10 min and +20 min); record the snapshot of the pending Timer firings; apply `e-A` `slot` = `a2`; the snapshot has the same count, `Id`, and `FireAt` values; advance the clock; `A` resolves `a2` and `B` resolves `b1`.
- [ ] T023 [US4] Write the test `TwoEntriesOfOneSequenceEachUseTheirOwnCurrentValue` in `tests/unit/Queues/QueueParameterScopeTests.cs`: entries `e-A1` and `e-A2` both run sequence `A` with `slot` = `x1` and `y1`; each run books sequence `R` (OncePerRun or Timer) with its own scope; apply `e-A1` `slot` = `x2`; the booking from `e-A1` resolves `x2` and the booking from `e-A2` resolves `y1` (clarification 5).

**Checkpoint**: All user stories work.

---

## Phase 7: Edge cases, Polish, and Cross-Cutting Concerns

- [ ] T024 Write the edge case tests in `tests/unit/Queues/QueueParameterScopeTests.cs`: `BookingOfARemovedEntryUsesItsKeptValues` (apply removes `e-A`; the booking of `R` from `e-A` still fires and resolves `one`, FR-004); `BookingOfADisabledEntryUsesItsCurrentValues` (apply sets `Enabled = false` and `slot` = `two` on `e-A`; the booking resolves `two`); `BookingOfAnEntryWithNoValuesGetsTheValuesThatTheApplyAdded` (`e-A` has no values at the booking, apply adds `slot` = `two`); `BookingAfterTheApplyRemovedAllValuesGetsNoEntryValue` (apply removes all values; `slot` does not resolve from the `entry` layer); `BookingAfterAKeyRenameUsesTheCurrentValues` (entry with no ID, key `idx:0`; `SwapPlan(plan.WithRenamedKeys(map), renameMap: map)` then an apply of `slot` = `two` under the new key; the booking resolves `two`).
- [ ] T025 [P] Write the test `HeldAtQueueStartEntryUsesTheCurrentValuesWhenItFires` in `tests/unit/Queues/QueueLivenessGateTests.cs` (FR-009). This file is a part of the partial class `QueueExecutionServiceTests`, so it can call the helpers of T008 (`ApplyWhilePausedAsync`, `WithSlotValue`). Use the liveness hold harness of feature 106 in that file (see `AnAtQueueStartOnlyTemplateDoesNotEndWhileHeldAndRunsInOrderAfterARecovery`). Do these steps:
  1. Make a template with an at-queue-start entry `e-S` (`slot` = `one`) and a once-per-run entry, and make the liveness check hold.
  2. Start the queue. The hold moves `e-S` to the next-cycle register before the schedule of the run exists.
  3. Wait until `handle.Schedule` is not null and the loop holds an iteration (the held sequence of `e-S` did not run).
  4. Call `ApplyWhilePausedAsync` with `slot` = `two` for `e-S`. The loop reaches the operator gate at the top of the next iteration.
  5. Let the device recover. Assert that the first run of the sequence of `e-S` resolves `slot` = `two`.
- [ ] T026 [P] Update the STE doc comments of `SelfRescheduleEntry.Scope` in `src/GameBot.Service/Services/QueueExecution/QueueRunHandle.cs`, the class summary of `src/GameBot.Service/Services/QueueExecution/SelfRescheduleCoordinator.cs`, and the `scope` parameter of `ScheduleSelf` in `src/GameBot.Service/Services/QueueExecution/ISelfRescheduleCoordinator.cs` (plan D-6): the kept scope is the fallback; the booked run uses the current values of the entry when the entry is in the run plan (feature 196).
- [ ] T027 [P] Update `docs/architecture.md`: change the sentences "A booked run keeps the parameter scope of the run that booked it ..." in the self-reschedule paragraph to the rule of contracts/booked-run-scope.md (the booked run uses the current values of its template entry in the run plan; the kept scope is the fallback for a removed entry or a run with no entry; feature 196, #409). Refresh the "Last reviewed" date.
- [ ] T028 [P] Add an entry to `CHANGELOG.md` under `[Unreleased]` (Fixed): a booked run of `reschedule-self` uses the current parameter values of its template entry after `apply-template`; no stop and start is necessary (#409).
- [ ] T029 [P] Set `**Status**: Implemented (iterated by 196)` in `specs/116-reschedule-keeps-params/spec.md`. In `specs/STATUS.md`, change row 116 the same way and add row 196 (`Booked run uses the current entry parameter values`, `Implemented`). Set `**Status**: Implemented` in `specs/196-fix-reschedule-params/spec.md`.
- [ ] T030 Build the solution with zero new warnings (the analyzers run in CI; see the memory note about CA1860/CA2027) and run the full `tests/unit/GameBot.UnitTests.csproj` and the integration tests `FullyQualifiedName~SelfReschedule|FullyQualifiedName~QueueApplyTemplate|FullyQualifiedName~RunNext`. All tests MUST pass.
- [ ] T031 Check that all new and changed text (code comments, docs, changelog, spec status) obeys STE (Constitution VI). Write the performance note of plan.md "Performance Note" into the PR description in phase 2 step 12.

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: no dependency. Start here.
- **Foundational (Phase 2)**: needs Phase 1. Blocks all user stories. T002 -> T003, T004 -> T005, T006 -> T007; T008 needs T005 and T007 (it uses `QueueRunPlan.FromAll` and the entry IDs).
- **US1 (Phase 3)**: needs Phase 2. T009-T011 (tests first) -> T012 -> T013; T014 (test first) -> T015; then T016.
- **US2 (Phase 4)**: needs T013 (the fix). Only tests.
- **US3 (Phase 5)**: needs T013.
- **US4 (Phase 6)**: needs T013.
- **Phase 7**: T024 and T025 need T013. T026-T029 can run at any time after T013. T030 and T031 come last.

### Parallel Opportunities

- T002, T004, T006 are in different files and can run in parallel. T003, T005, T007 can run in parallel after their tests.
- T009, T010, and T011 are in one file, so write them one after the other. T014 is in a different file and can be written in parallel with them.
- After T013: the tasks in different files can run in parallel: T021 (integration file), T025 (`QueueLivenessGateTests.cs`), and T026-T029. The tests in `QueueParameterScopeTests.cs` (T017, T018, T020, T022, T023, T024) are written one after the other.

### Parallel Example: Phase 2

```text
Task: "T002 Write ParameterScopeEntryKeyTests in tests/unit/Parameters/ParameterScopeEntryKeyTests.cs"
Task: "T004 Write KeyFor tests in tests/unit/Queues/QueueRunPlanTests.cs"
Task: "T006 Write TryGetEntry tests in tests/unit/Queues/QueueRunScheduleSwapTests.cs"
```

### Parallel Example: Phase 7

```text
Task: "T027 Update docs/architecture.md"
Task: "T028 Add the CHANGELOG.md entry"
Task: "T029 Update the Status lines of specs 116 and 196 and specs/STATUS.md"
```

## Implementation Strategy

### MVP First (User Story 1 Only)

1. Do Phase 1 and Phase 2.
2. Do Phase 3 (US1). Make sure that T009 fails before T013 and passes after T013.
3. Stop and check: the booking of the issue scenario resolves the new value.

### Incremental Delivery

1. US1 (the fix itself) -> US2 (the chain, tests only) -> US3 (the type change, unit and integration) -> US4 (no side effects).
2. Phase 7: edge cases, documentation, full test run.

## Notes

- [P] tasks = different files, no dependencies.
- Commit after each phase or logical group (phase 2 of the pipeline commits once at the end).
- The version bump of `installer/versioning/version.override.json` is a step of the pipeline (phase 2, step 10), not a task here.
