---

description: "Task list for feature 188: Before Each Run before every firing"
---

# Tasks: Before Each Run Before Every Firing

**Input**: Design documents from `/specs/188-before-each-entry/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/execution-order.md, quickstart.md

**Tests**: The constitution (Principle II) requires tests. Write the tests of each story first, and make sure that they fail before the engine change.

**Organization**: The tasks are in groups by user story.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: The task can run in parallel (different files, no dependencies).
- **[Story]**: The user story of the task (US1, US2, US3).

## Path Conventions

- Service: `src/GameBot.Service/`, `src/GameBot.Domain/`
- Unit tests: `tests/unit/Queues/`
- All test tasks edit `tests/unit/Queues/QueueExecutionServiceBeforeEachRunTests.cs`. This file is a part of the partial class `QueueExecutionServiceTests` and uses its `Harness`, `AddQueueWithEntries`, `TimerEntry`, `RelativeTimer`, `OncePerRun`, `EveryStep`, `FakeTimeProvider`, and `FakeSequenceExecution` helpers.

---

## Phase 1: Setup

**Purpose**: Make sure that the start point is green.

- [ ] T001 Build the solution with `dotnet build "C:\src\GameBot\GameBot.sln" -c Debug` and run `dotnet test "C:\src\GameBot\tests\unit\GameBot.UnitTests.csproj" --filter "FullyQualifiedName~BeforeEachRun"`. Record that all tests pass before a change.

---

## Phase 2: Foundational

**Purpose**: None. The change has no shared prerequisite. User Story 1 holds the engine change.

---

## Phase 3: User Story 1 - Establish the start screen before each due task (Priority: P1) 🎯 MVP

**Goal**: The Before Each Run pass runs immediately before each triggering firing, also when several firings are due in one wake-up (spec FR-001 to FR-004).

**Independent Test**: A template with `B` and three co-due timers `T1`, `T2`, `T3` gives `B, T1, B, T2, B, T3` (contracts/execution-order.md).

### Tests for User Story 1

- [ ] T002 [US1] In `tests/unit/Queues/QueueExecutionServiceBeforeEachRunTests.cs`, rename the section comment `// ── US2: once per wake-up ──` to `// ── Feature 188 (#393): one pass before each triggering firing ──`. Rename the test `BeforeEachRunRunsOnceForSeveralCoDueFiringsAndTriggersNoEveryStepPass` to `BeforeEachRunRunsBeforeEachCoDueFiringAndTriggersNoEveryStepPass`. Change its expected order to `"B", "T1", "E", "B", "T2", "E"` and its comment to say that B runs before each firing and that no every-step pass runs after B (spec US1-AS3, FR-004).
- [ ] T003 [US1] In the same file, add the test `BeforeEachRunRunsBeforeEachOfThreeCoDueTimers`: template `BeforeEachRun("B")`, `TimerEntry("T1", TimeOnly.MinValue)`, `TimerEntry("T2", TimeOnly.MinValue)`, `TimerEntry("T3", TimeOnly.MinValue)`. Start, wait until stopped, and expect `"B", "T1", "B", "T2", "B", "T3"` (spec US1-AS1, SC-001).
- [ ] T004 [US1] In the same file, add the test `SeveralBeforeEachRunEntriesRunInTemplateOrderBeforeEachCoDueFiring`: template `BeforeEachRun("B1")`, `TimerEntry("T1", TimeOnly.MinValue)`, `BeforeEachRun("B2")`, `TimerEntry("T2", TimeOnly.MinValue)`. Expect `"B1", "B2", "T1", "B1", "B2", "T2"` (spec US1-AS2, FR-003).
- [ ] T005 [US1] In the same file, add the test `BeforeEachRunRunsBeforeEachFiringOfMixedKindsInOneWakeUp`: template `BeforeEachRun("B")`, `TimerEntry("T", TimeOnly.MinValue)`, `RelativeTimer("R", TimeSpan.Zero)`, `OncePerRun("A")`. Expect `"B", "T", "B", "R", "A"`. A template OncePerRun step gets no pass (spec US1-AS5, FR-002).
- [ ] T006 [US1] In the same file, add the test `TwoCoDueRelativeTimersAfterAnIdleGapEachGetAPass`: use a `FakeTimeProvider` at `FakeStart`, template `OncePerRun("A")`, `RelativeTimer("T1", TimeSpan.FromMinutes(10))`, `RelativeTimer("T2", TimeSpan.FromMinutes(10))`, `BeforeEachRun("B")`. Wait until `A` ran, wait 100 ms, advance the clock by 10 minutes, wait until stopped. Expect `"A", "B", "T1", "B", "T2"`. This checks that the next wake-up after an idle gap also runs one pass for each co-due firing.
- [ ] T007 [US1] In the same file, add the test `BeforeEachRunRunsBeforeCoDueRelativeLiveAndSelfRescheduledFirings`: use a `FakeTimeProvider` at `FakeStart`, template `OncePerRun("A")`, `RelativeTimer("T", TimeSpan.FromMinutes(10))`, `BeforeEachRun("B")`. The handler calls `h.Coordinator.ScheduleSelf("q1", "R", SelfRescheduleOption.Timer, null, TimeSpan.FromMinutes(10))` one time, when `A` runs (use the pattern of `BeforeEachRunRunsBeforeSelfRescheduleTimerFiring`). After the start, wait until `A` ran, call `h.Service.ScheduleRelative("q1", "L", TimeSpan.FromMinutes(10))`, wait 100 ms, and advance the clock by 10 minutes. Wait until `T`, `L`, and `R` ran, then stop the queue. Expect that `B` runs exactly 3 times, and that the item immediately before each of `T`, `L`, and `R` is `B` (spec US1-AS5, FR-002). Do not check the order of `T`, `L`, and `R`. Run-next requests, next-cycle-start firings, and daily retries use the same pass method through the same call sites (research R-002), so this test and `BeforeEachRunRunsBeforeDailyRetryOfFailedTimer` cover them.
- [ ] T008 [US1] Run `dotnet test "C:\src\GameBot\tests\unit\GameBot.UnitTests.csproj" --filter "FullyQualifiedName~BeforeEachRun"` and make sure that T002 to T007 fail with the current engine.

### Implementation for User Story 1

- [ ] T009 [US1] In `src/GameBot.Service/Services/QueueExecution/QueueExecutionService.cs`, remove the local `var beforeEachRunRanThisIteration = false;`, change the first line of `RunBeforeEachRunPassAsync` to `if (plan.BeforeEachRun.Count == 0) return;`, remove the line `beforeEachRunRanThisIteration = true;`, and remove the reset `beforeEachRunRanThisIteration = false;` at the start of the loop iteration (research R-001). Do not change any call site of `FireGroupAsync` or `RunBeforeEachRunPassAsync` (research R-002). Do not change the order in `FireGroupAsync` (operator gate, plan check, liveness gate, pass, main firing). Thus an operator pause between two co-due firings stops the loop before the next pass, and the existing operator-gate tests cover this edge case (research R-003). `PlanChangedSinceIterationStart` also runs before the pass, so after a live template change the rest of the wake-up runs no pass, and the existing feature 171 tests cover this edge case.
- [ ] T010 [US1] In the same file, rewrite the comment above `RunBeforeEachRunPassAsync` in STE: the pass runs immediately before each timed, live, or self-rescheduled firing, also when several firings are due in one wake-up (feature 188, #393). Keep the sentences about the accounting and about the loop safety. Rewrite the `BeforeEachRun` lines of the class comment at the top of the file in the same way, and remove "at most once per wake-up".
- [ ] T011 [US1] Run the filter of T008 again and make sure that all `BeforeEachRun` tests pass.

**Checkpoint**: User Story 1 works. Three co-due firings give three passes.

---

## Phase 4: User Story 2 - Keep the failure accounting and the run log as today (Priority: P1)

**Goal**: Each pass execution is counted and logged as before (spec FR-005, FR-006, FR-007).

**Independent Test**: A failed `B` with two co-due firings adds 2 to the failed total, and the run log has two `B` nodes, each immediately before its firing.

### Tests for User Story 2

- [ ] T012 [US2] In `tests/unit/Queues/QueueExecutionServiceBeforeEachRunTests.cs`, add the test `BeforeEachRunFailureIsCountedForEachPassAndBothFiringsRun`: template `BeforeEachRun("B")`, `RelativeTimer("T1", TimeSpan.Zero)`, `RelativeTimer("T2", TimeSpan.Zero)`, `OncePerRun("A")`. The handler fails `B` only. Expect the order `"B", "T1", "B", "T2", "A"`, `h.Log.FinalStatus` `"success"`, `h.Log.Summary` contains `"3 sequence(s) executed"` (T1, T2, A) and `"2 failed"` (spec US2-AS1, US2-AS2).
- [ ] T013 [US2] In the same file, add the test `EachBeforeEachRunPassIsItsOwnLogNodeUnderTheRunRoot`: template `BeforeEachRun("B")`, `TimerEntry("T1", TimeOnly.MinValue)`, `TimerEntry("T2", TimeOnly.MinValue)`. After the run stops, make sure that `h.Sequences.Executed` has `B` at index 0 and index 2, and that `h.Sequences.RootIds` of each `B` is not null and is equal to the root id of `T1` (spec US2-AS3, FR-006). Use the test `BeforeEachRunIsLoggedUnderTheRunRootAndRecordedInTheCycleLedger` as the pattern.
- [ ] T014 [US2] In `tests/unit/Queues/QueueLivenessGateTests.cs`, add the test `HeldCoDueFiringsRunNoBeforeEachRunPass` after `OneGateForEachFiringGroupAndNoGuardSequenceRuns`, with the same pattern: template `Every("E")`, `Before("B")`, `Timer("T1", 11)`, `Timer("T2", 11)`, `rig.Liveness.SetNotLive(DeviceLivenessReasons.TransportNotReady)`, start, `await rig.PassIterationsAsync(6)`. Expect that `rig.Executed()` is empty (the first firing is held, and the second firing is held in hold-only mode), then stop the queue (spec Edge Cases, FR-007). Then run `dotnet test "C:\src\GameBot\tests\unit\GameBot.UnitTests.csproj" --filter "FullyQualifiedName~BeforeEachRun|FullyQualifiedName~QueueLivenessGate"` and make sure that T012, T013, T014, and the existing liveness tests that use `Before("B")` pass.

**Checkpoint**: Counts and log nodes are correct for each pass.

---

## Phase 5: User Story 3 - Existing templates work with no change (Priority: P2)

**Goal**: No new flag or template option. Templates with one triggering firing, or with no Before Each Run entry, behave as before (spec FR-008, FR-009, SC-003, SC-004).

**Independent Test**: The existing single-firing tests and the contract and integration tests for `BeforeEachRun` pass with no change.

- [ ] T015 [US3] Make sure that no file in `src/GameBot.Domain/QueueTemplates/` and no file in `src/GameBot.Service/Contracts/` gets a new property, and that no stored template file changes (`git diff --stat` shows no change outside the files of this task list).
- [ ] T016 [US3] Run `dotnet test "C:\src\GameBot\tests\unit\GameBot.UnitTests.csproj" --filter "FullyQualifiedName~Queues"`, `dotnet test "C:\src\GameBot\tests\contract\GameBot.ContractTests.csproj" --filter "FullyQualifiedName~QueueTemplates"`, and `dotnet test "C:\src\GameBot\tests\integration\GameBot.IntegrationTests.csproj" --filter "FullyQualifiedName~QueueTemplatesScheduleType"`. All tests must pass. The single-firing tests (for example `BeforeEachRunRunsImmediatelyBeforeTimeOfDayTimer`) must pass with no change. The integration test `SaveBeforeEachRunEntryRoundTripsCorrectly` and the unit tests prove that a template saved before this feature loads and runs with no change, because no file format changes (data-model.md, spec US3-AS1). The monitor test `BeforeEachRunIsSurfacedOnceAfterTimedItemsAndBeforeEveryStep` in `tests/unit/Queues/QueueMonitorServiceTests.cs` must pass with no change: it proves FR-007a. Check the test project names with `Get-ChildItem C:\src\GameBot\tests -Recurse -Filter *.csproj` first.

**Checkpoint**: Existing behaviour is the same for single-firing wake-ups.

---

## Phase 6: Polish & Cross-Cutting Concerns

- [ ] T017 [P] In `src/GameBot.Domain/QueueTemplates/ScheduleType.cs`, rewrite the XML comment of `BeforeEachRun` in STE: the entries run in template order immediately before each timed, live-scheduled, or self-rescheduled firing, also when several firings are due in one wake-up. Remove "at most once per scheduler wake-up".
- [ ] T018 [P] In `src/GameBot.Service/Contracts/QueueTemplates/TemplateEntrySaveRequest.cs` and `src/GameBot.Service/Contracts/QueueTemplates/QueueTemplateDetailResponse.cs`, rewrite the `BeforeEachRun` part of the XML comment of `ScheduleType` in STE as in T017 (contracts/execution-order.md, "API descriptions").
- [ ] T019 [P] In `src/GameBot.Service/Services/QueueExecution/QueueMonitorSnapshot.cs`, rewrite the comment of `ScheduleKind.BeforeEachRun` in STE: it runs before each timed, live, or self-rescheduled firing, and the monitor shows it one time as "Before Each Run" (FR-007a).
- [ ] T020 [P] In `tests/unit/Queues/QueueExecutionServiceBeforeEachRunTests.cs`, rewrite the header comment (lines 14 and 15) in STE: the entries run before each triggering firing, also when several firings are due in one wake-up (feature 095, changed by feature 188).
- [ ] T021 [P] In `docs/architecture.md`, rewrite the *Before each run* paragraph (near line 361) in STE: the entries run immediately before each timed, live-scheduled, or self-rescheduled firing, also when several firings are due in one scheduling-loop iteration (feature 188). Remove "the first" and "at most once per iteration". Update the "Last reviewed" line near line 13 with the date and feature 188.
- [ ] T022 Run a search for `once per wake-up`, `once per scheduler wake-up`, `at most once per iteration`, and `first timed` in `src/`, `docs/`, and `tests/`. Rewrite each result that you find (FR-010). The old CHANGELOG entry of feature 095 stays as it is.
- [ ] T023 [P] In `specs/STATUS.md`, add the row `| 188 | Before Each Run Before Every Firing | Implemented |` after the row for 187. Set `**Status**: Implemented` in `specs/188-before-each-entry/spec.md`. In `specs/095-before-each-run-schedule/spec.md`, add one sentence in STE after FR-003: "Feature 188 (#393) replaces FR-003: the pass now runs before each triggering firing."
- [ ] T024 Add a `CHANGELOG.md` entry in STE for feature 188 (#393): the Before Each Run pass now runs before each triggering firing, also when several firings are due in one wake-up. Add a performance note: a wake-up with N triggering firings takes (N - 1) more passes; a wake-up with one firing does not change. Before the push, bump the version in `installer/versioning/version.override.json` as the constitution ("Version bump") and `CLAUDE.md` tell, set `updatedBy` and `updatedAtUtc`, and add a line about the version to this CHANGELOG entry. Put the same performance note in the PR description (plan, Constitution Check IV).
- [ ] T025 Run `dotnet build "C:\src\GameBot\GameBot.sln" -c Debug` with zero new warnings, and run the full unit test project `dotnet test "C:\src\GameBot\tests\unit\GameBot.UnitTests.csproj"`. All tests must pass.
- [ ] T026 Follow `specs/188-before-each-entry/quickstart.md`, "Automatic check".

---

## Dependencies & Execution Order

- Phase 1 (T001) comes first.
- US1 (T002 to T011): write the tests (T002 to T007), see them fail (T008), then change the engine (T009, T010), then see them pass (T011).
- US2 (T012 to T014) depends on T009. Its tests pass after the engine change.
- US3 (T015, T016) depends on T009.
- Polish (T017 to T026) comes after the user stories. T022 comes after T017 to T021. T025 and T026 come last.

### Parallel Opportunities

- T012 and T013 add different tests in the same file. Write them one after the other. They have no dependency on each other. T014 edits a different file and can run in parallel with T012 and T013.
- T017, T018, T019, T021, and T023 edit different files and can run in parallel.

## Parallel Example: Polish

```text
Task: "T017 Rewrite the BeforeEachRun comment in src/GameBot.Domain/QueueTemplates/ScheduleType.cs"
Task: "T018 Rewrite the BeforeEachRun API descriptions in the two contract files"
Task: "T021 Rewrite the Before each run paragraph in docs/architecture.md"
```

## Implementation Strategy

### MVP First (User Story 1 Only)

1. Do T001.
2. Do T002 to T011. Stop and check: three co-due firings give three passes.

### Incremental Delivery

1. US1 gives the full engine change.
2. US2 adds the proof of accounting and log nodes.
3. US3 adds the proof of no change for existing templates.
4. Polish updates the text and the docs.
