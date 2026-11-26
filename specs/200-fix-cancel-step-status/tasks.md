# Tasks: Give a reschedule-self Cancel step the status success when the cancel runs (B-046)

**Input**: Design documents from `specs/200-fix-cancel-step-status/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/execution-log-step-status.md, quickstart.md

**Tests**: Required. The constitution (Principle II) says that a bug fix MUST include a failing test that shows the defect before the fix. Write each test task first and make sure that it fails before the fix task of the same story.

**Organization**: One phase for each user story of the spec (US1 P1, US2 P2, US3 P3), then the polish phase.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: The task can run in parallel (different files, no dependency on an open task).
- **[Story]**: The user story of the task (US1, US2, US3).

---

## Phase 1: Setup

**Purpose**: Make sure that the base is green before the change.

- [ ] T001 Build the solution with `dotnet build "C:\src\GameBot\GameBot.sln" -c Debug` and run `dotnet test "C:\src\GameBot\tests\unit\GameBot.UnitTests.csproj" --filter "FullyQualifiedName~ExecutionLogServiceMapStepStatusTests|FullyQualifiedName~ExecutionSubtreeProjectionTests"`. Record that the base is green (Constitution, release blocker).

---

## Phase 2: Foundational

No foundational task. The three stories change different code paths. US2 uses the same file as US1 (`ExecutionLogService.cs`), so do US1 before US2.

---

## Phase 3: User Story 1 - A Cancel step that removes its bookings shows success (Priority: P1) MVP

**Goal**: A `reschedule-self` step node with the action outcome `cancelled` has the status `success`. A step node of another type with the outcome `cancelled` keeps `failure` (FR-001, FR-006, FR-009, FR-011).

**Independent Test**: `BuildSubtree` gives `success` to a stored `reschedule-self` step with the outcome `cancelled`, with the same message text, and `failure` to a `waitForImage` step with the outcome `cancelled`.

### Tests for User Story 1 (write first)

T003 fails before T004. T002 is a guard test: it passes before and after the fix.

- [ ] T002 [P] [US1] In `tests/unit/ExecutionLogs/ExecutionLogServiceMapStepStatusTests.cs`, add the test `CancelledOutcomeStaysFailureInTheSharedMapping`: `ExecutionLogService.MapStepStatus("cancelled").Should().Be("failure")`. Add an STE comment: a primitive or WaitForImage step that a stop cut off has the outcome `cancelled`, and only a `reschedule-self` node gets `success` for it (feature 200, issue #414). This test passes before and after the fix; it guards the scope of the fix.
- [ ] T003 [P] [US1] In `tests/unit/ExecutionLogs/ExecutionSubtreeProjectionTests.cs`, add the test `BuildSubtreeRendersARescheduleCancelThatRemovedBookingsAsSuccess`. Build a sequence entry (use the `SequenceEntry` helper of the file) with one `ExecutionStepOutcome(1, "reschedule-self", "cancelled", "cancelled", "Self-reschedule 'cancel-retry' cancelled: removed 1 pending booking(s) of this sequence; applies to the current run only", StepId: "cancel-retry")`. Assert that the node has `Status` `success` and that `Message` is the same text as the `ReasonText`. In the same file, add the test `BuildSubtreeKeepsACancelledWaitStepAsFailure` with one `ExecutionStepOutcome(1, "waitForImage", "cancelled", "cancelled", "cancelled")`, and assert `Status` `failure`.

### Implementation for User Story 1

- [ ] T004 [US1] In `src/GameBot.Service/Services/ExecutionLog/ExecutionLogService.cs`, add the private static method `ResolveStepStatus(ExecutionLogEntry entry, ExecutionStepOutcome step)`. When `step.StepType` is `reschedule-self` (ignore case) and `step.Outcome` is `cancelled` (ignore case), it returns `success`. In all other cases it returns `MapStepStatus(step.Outcome)`. In `BuildStepNode`, replace `MapStepStatus(step.Outcome)` with `ResolveStepStatus(entry, step)` (keep the condition-step rule first). Do not change `MapStepStatus`. Add an STE comment: "Feature 200 (issue #414): a reschedule-self Cancel that removed its bookings did its work. Other steps with the outcome cancelled were cut off by a stop and keep the status failure." Run T002 and T003 and make sure that they pass.

**Checkpoint**: US1 is complete. A stored log with a `cancelled` `reschedule-self` step shows `success`.

---

## Phase 4: User Story 2 - A Cancel step that finds no booking shows success (Priority: P2)

**Goal**: A `reschedule-self` step node with the outcome `noop` and the option `Cancel` has the status `success`. Other `noop` nodes keep `skipped` (FR-002, FR-003, FR-008, FR-009).

**Independent Test**: `BuildSubtree` gives `success` to a `noop` node whose stored step detail has `option` `Cancel`, and `skipped` to a `noop` node with `option` `Timer` or with no detail.

### Tests for User Story 2 (write first, make sure that they fail)

- [ ] T005 [US2] In `tests/unit/ExecutionLogs/ExecutionSubtreeProjectionTests.cs`, add the test `BuildSubtreeRendersARescheduleCancelWithNothingToRemoveAsSuccess`. Use a `[Theory]` with the three `noop` messages of a `Cancel` step: "no pending booking of this sequence; nothing to cancel", "originating queue run no longer active; nothing to cancel", and "no originating queue, no reschedule performed" (the edge cases of the spec). For each case, build a sequence entry with one `ExecutionStepOutcome(1, "reschedule-self", "noop", "noop", "Self-reschedule 'cancel-retry' noop: <case message>", StepId: "cancel-retry")` and one `ExecutionDetailItem("step", <same text>, new Dictionary<string, object?> { ["stepOrder"] = 1, ["stepType"] = "reschedule-self", ["actionOutcome"] = "noop", ["option"] = "Cancel", ["removed"] = false }, "normal")` in `Details`. Assert `Status` `success`.
- [ ] T006 [US2] In the same file, add the test `BuildSubtreeReadsTheCancelOptionFromAStoredJsonDetail`. Make the same entry as T005, but give the attribute values as `JsonElement` values (for example `JsonSerializer.SerializeToElement(1)` and `JsonSerializer.SerializeToElement("Cancel")`), as after a read from disk. Assert `Status` `success`.
- [ ] T007 [US2] In the same file, add the test `BuildSubtreeKeepsANoopRescheduleWithAnotherOptionAsSkipped` with two cases (`[Theory]`): the detail has `option` `Timer`, and the entry has no detail item for the step. Assert `Status` `skipped` in both cases.

### Implementation for User Story 2

- [ ] T008 [US2] In `src/GameBot.Service/Services/ExecutionLog/ExecutionLogService.cs`, add the private static helper `ReadRescheduleOption(ExecutionLogEntry entry, int stepOrder)` that returns `string?`. It loops over `entry.Details`, skips each item that does not have `Kind` `step` (ordinal) or has no `Attributes`, reads `stepOrder` and `option` through `JsonSerializer.SerializeToElement` (the same pattern as `ReadStepParameters`), and returns the `option` string of the first item with the same `stepOrder` and a `stepType` of `reschedule-self` (ignore case). It returns null when it finds no such item. Add an STE `<summary>` comment.
- [ ] T009 [US2] In the same file, extend `ResolveStepStatus` (from T004): it also returns `success` when `step.StepType` is `reschedule-self` (ignore case), `step.Outcome` is `noop` (ignore case), and `ReadRescheduleOption(entry, step.StepOrder)` is `Cancel` (ignore case). Call `ReadRescheduleOption` only in this case. Add an STE comment that names feature 200. Run T005 to T007 and make sure that they pass.
- [ ] T010 [US2] Add the end-to-end test `CancelStepNodesOfAStoredRunHaveTheStatusSuccess` to `tests/integration/Queues/SelfRescheduleCancelDispatchIntegrationTests.cs`. Do these steps:
  1. Use a `[Theory]` with two cases: a Timer booking (`00:30:00`) before the Cancel step, and no booking.
  2. Seed the sequence. Register a run handle. Run the sequence in the queue context.
  3. Get the execution id of the sequence run. If `SequenceExecutionResult` has the execution id, use it. If not, call `IExecutionLogService.QueryAsync` with an `ExecutionLogQuery` that filters on the object id of the sequence, and take the newest entry.
  4. Call `IExecutionLogService.GetSubtreeAsync` with the execution id.
  5. Assert that the node of the `cancel` step has `Status` `success`.
  6. Assert the message. For the first case, it is "Self-reschedule 'cancel' cancelled: removed 1 pending booking(s) of this sequence; applies to the current run only". For the second case, it is "Self-reschedule 'cancel' noop: no pending booking of this sequence; nothing to cancel".
  - Note: this test checks the full path, not one rule. It passes only after T004 and T009. It proves that the stored detail has `stepType` `reschedule-self`, `option` `Cancel`, and the same `stepOrder` (SC-001, SC-002).

**Checkpoint**: US1 and US2 are complete. Each `Cancel` step that ran shows `success`.

---

## Phase 5: User Story 3 - A Cancel step that cannot run shows failure (Priority: P3)

**Goal**: An error in `CancelSelf` gives the outcome `failed`, the message "cancel not performed: <error text>", `removed` `false`, and the status `failure`. The run continues (FR-004, FR-005, FR-010).

**Independent Test**: With a coordinator that throws in `CancelSelf`, a sequence run records the `Cancel` step with `ActionOutcome` `failed` and `Removed` `false`, the run status stays `Succeeded`, and the next step runs.

### Tests for User Story 3 (write first)

T012 fails before T013. T011 is a guard test: it passes before and after the fix.

- [ ] T011 [P] [US3] In `tests/unit/ExecutionLogs/ExecutionLogServiceMapStepStatusTests.cs`, add the test `FailedOutcomeOfARescheduleCancelIsFailure`: `MapStepStatus("failed")` gives `failure`. (This row exists already; the test locks it for feature 200.)
- [ ] T012 [US3] Add the test `CancelThatThrowsRecordsAFailedStepAndTheRunContinues` to `tests/integration/Queues/SelfRescheduleCancelDispatchIntegrationTests.cs`. Do these steps:
  1. Add a private nested test class `ThrowingCancelCoordinator : ISelfRescheduleCoordinator`. Its constructor takes a real `SelfRescheduleCoordinator`.
  2. Make each interface member of the test class call the same member of the real coordinator. Make `CancelSelf` throw `InvalidOperationException("store unavailable")`.
  3. Make the factory as follows: `new WebApplicationFactory<Program>().WithWebHostBuilder(b => b.ConfigureServices(s => { s.RemoveAll<ISelfRescheduleCoordinator>(); s.AddSingleton<SelfRescheduleCoordinator>(); s.AddSingleton<ISelfRescheduleCoordinator>(sp => new ThrowingCancelCoordinator(sp.GetRequiredService<SelfRescheduleCoordinator>())); }))`.
  4. `ISelfRescheduleCoordinator` is `internal`. The service project already has `InternalsVisibleTo("GameBot.IntegrationTests")`, so no change is necessary.
  5. Seed a sequence with step 0 `Cancel` (`cancel`) and step 1 `Timer` `00:30:00` (`book`). Register a run handle. Run the sequence.
  6. Assert that `result.Status` is `Succeeded`.
  7. Assert that the `cancel` step has `ActionOutcome` `failed`, `Removed` `false`, and `Message` `cancel not performed: store unavailable`.
  8. Assert that the `book` step has `ActionOutcome` `scheduled`.
  - Note on FR-010: the test does not assert the `commandOutcome` value. The runner code that sets it does not change (`SequenceRunner.RecordServiceLevelActionAsync`, the `stepOutcomes[stepKey]` line). T018 checks this with `git diff`.

### Implementation for User Story 3

- [ ] T013 [US3] Change the method `DispatchSelfReschedule` in `src/GameBot.Service/Services/SequenceExecution/SequenceExecutionService.cs`. Do these steps:
  1. Put the call `_selfRescheduleCoordinator.CancelSelf(originatingQueueId!, sequenceId)` in `try`/`catch (Exception ex)`.
  2. Search `src` for `EventId = ` and select an EventId that no other `LoggerMessage` uses.
  3. Add a new `[LoggerMessage]` method to the static partial class `SequenceExecutionLog` at the end of the same file. Use the level `Warning` and the EventId of step 2.
  4. Write the log message in STE. Example: "Sequence {SequenceId} could not cancel its pending bookings in queue {QueueId}. The step has the outcome failed and the run continues."
  5. In the `catch`, call `SequenceExecutionLog.<Name>(_logger, ...)` when `_logger` is not null.
  6. In the `catch`, return `new ActionDispatchResult("failed", $"cancel not performed: {ex.Message}", false)`.
  7. Do not change the `switch` on `cancel.Outcome`.
  8. Replace the comment "It never fails the run." with an STE comment. The comment says that an error gives the outcome `failed` and that the run continues.
  9. If the analyzers report CA1031 (catch of a general exception), use the pattern of the other `catch (Exception)` blocks in the same file.
  10. Run T011 and T012. Make sure that they pass.

**Checkpoint**: All three stories are complete.

---

## Phase 6: Polish & Cross-Cutting Concerns

- [ ] T014 [P] Change the "Self-reschedule Cancel" paragraph (feature 123) in `docs/architecture.md`. Add these facts in STE:
  1. The execution log gives a `Cancel` step the status `success` for the outcome `cancelled` and for the outcome `noop`.
  2. This rule is only for `reschedule-self` step nodes. Other steps with the outcome `cancelled` keep the status `failure`.
  3. An error during the cancel gives the outcome `failed`, the status `failure`, and `removed` false. The run continues.
  4. The log calculates the status when it reads the log, so the logs stored before the fix also show it.
  5. Add a feature 200 (issue 414) part at the start of the "_Last reviewed:" line (line 13). Set its date to the date of the change.
- [ ] T015 [P] In `specs/123-reschedule-self-cancel/spec.md`, set the status line to `**Status**: Implemented (iterated by 200)`. In `specs/STATUS.md`, set the row of 123 to `Implemented (iterated by 200)` and add the row `| 200 | Reschedule-Self Cancel step status in the execution log | Implemented |` after the row of 199. In `specs/200-fix-cancel-step-status/spec.md`, set the status line to `Implemented`.
- [ ] T016 [P] In `CHANGELOG.md`, add an STE entry for B-046 (issue #414): a `reschedule-self` `Cancel` step that runs has the status `success` in the execution log, also in the logs stored before, and an error during the cancel gives a failed step and the run continues. Follow the format of the newest entry.
- [ ] T017 Run the full build and the test projects that the change touches: `dotnet build "C:\src\GameBot\GameBot.sln" -c Debug`, `dotnet test "C:\src\GameBot\tests\unit\GameBot.UnitTests.csproj"`, and `dotnet test "C:\src\GameBot\tests\integration\GameBot.IntegrationTests.csproj" --filter "FullyQualifiedName~SelfReschedule|FullyQualifiedName~ExecutionLog"`. Fix each failure, also in old code. Do not continue with a red build.
- [ ] T018 Do the checks of `specs/200-fix-cancel-step-status/quickstart.md` ("Automatic tests"). With `git diff`, make sure that the message text of each `Cancel` case in `SequenceExecutionService.DispatchSelfReschedule` is the same as before the change (FR-006), that `MapStepStatus` did not change (FR-001 scope), and that `src/GameBot.Domain/Services/SequenceRunner.cs` did not change (FR-010).

---

## Dependencies & Execution Order

- Phase 1 (T001) before all other tasks.
- US1 (T002 to T004) before US2 (T005 to T010), because both change `ExecutionLogService.cs` and the subtree test file, and T009 extends the method of T004.
- US3 (T011 to T013) is independent of US1 and US2. It can run after T001. T010 and T012 change the same integration test file, so do not run them in parallel.
- Phase 6 after all stories. T014, T015, and T016 are in different files and can run in parallel. T017 and T018 come last.

## Parallel Example

```text
After T001:
  T002 and T003 (US1 tests, different files)
  T011 and T012 (US3 tests, different files from US1 tests)
After all stories:
  T014, T015, T016 (different files)
```

## Implementation Strategy

1. MVP: T001 to T004 (US1). This removes the red step from each daily run of the reporter, also in stored logs.
2. Then US2 (T005 to T010): the `noop` Cancel nodes change from `skipped` to `success`.
3. Then US3 (T011 to T013): an error during the cancel gives a clear failed step.
4. Then the polish phase. The version bump and the push are steps of phase 2 of the pipeline, not tasks here.
