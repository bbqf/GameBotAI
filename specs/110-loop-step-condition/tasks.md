# Tasks: A condition on a Loop step is a guard that the runtime obeys

**Input**: Design documents from `specs/110-loop-step-condition/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/loop-step-guard.md, quickstart.md

**Tests**: Required. The fix is for a defect, so each story has a regression test. Write the tests before the fix, and make sure that they fail before the fix.

**Format**: `[ID] [P?] [Story] Description`. `[P]` means that the task can run in parallel with other `[P]` tasks (different files, no dependencies).

## Phase 1: Setup

- [ ] T001 Read `MapToLinearSteps` in `src/GameBot.Service/Endpoints/SequencesEndpoints.cs`, `ExecuteSingleStepAsync` and `AddLoopStep` in `src/GameBot.Domain/Services/SequenceRunner.cs`, `ValidateLoopStep` in `src/GameBot.Domain/Services/SequenceStepValidationService.cs` and the `Loop` entry in `src/GameBot.Service/Services/SequenceExecution/SequenceExecutionService.cs`. Make sure that the root cause in research R-001 is correct.

## Phase 2: Foundational (blocks all stories)

- [ ] T002 In `src/GameBot.Domain/Services/SequenceRunner.cs`, add the optional parameters `string? conditionType = null` and `string? conditionResult = null` to `SequenceExecutionResult.AddLoopStep`, and set them on the new `StepResult`. Add the method `SetConditionForLatestLoopStep(string stepKey, string? conditionType, string? conditionResult)`: it finds the last entry with `LoopIterations` not null and `CommandId` equal to `stepKey`, and sets the two fields (research R-004). Write the XML comments in STE.

**Checkpoint**: The solution builds. No behavior change. All existing tests pass.

## Phase 3: User Story 1 - A Loop step with a false guard does not run (Priority: P1)

**Goal**: The runner evaluates the guard of a `Loop` step one time before the loop starts, and skips the loop when the guard is false.

**Independent Test**: Run a sequence with a `Loop` step whose `condition` is false. The body steps do not run. The next top-level step runs.

### Tests for User Story 1

- [ ] T003 [US1] Create `tests/unit/Sequences/SequenceRunnerLoopGuardTests.cs` (namespace `GameBot.UnitTests.Sequences`, same stub repository style as `SequenceRunnerLoopTests.cs`). Add:
  - `FalseGuardSkipsLoopBodyAndRunsNextStep`: a `count` loop (count 3) with a `commandOutcome` guard on a prior top-level step `first` and `expectedState` `failed`; `first` succeeds. The body command does not run, the step `after` runs, and the sequence succeeds (spec US1 scenario 1, SC-001).
  - `FalseCompositeGuardSkipsLoopBody`: the guard is `all(commandOutcome(first, success), none(commandOutcome(first, success)))`; the body does not run (US1 scenario 2).
  - `TrueGuardRunsAllIterations`: guard `commandOutcome(first, success)`; the body runs 3 times (US1 scenario 3, SC-003).
  - `LoopWithoutGuardRunsAsBefore`: no guard; the body runs 3 times (US1 scenario 4).
  - `GuardEvaluationErrorFailsSequence`: guard `imageVisible` with a condition evaluator that throws; the sequence fails, the body does not run, and the `Loop` entry has `ConditionResult` `error` (FR-005).
  - `FalseGuardOnZeroCountLoopIsSkipped`: a `count` loop with count 0 and a false guard gives a `Skipped` entry (spec edge case).
  - `LaterCommandOutcomeReadsSkippedForSkippedLoop`: a step after the skipped loop has the guard `commandOutcome(loop, skipped)` and runs (FR-007).
  - `FalseGuardSkipsWhileLoopBody`: a `while` loop with a true loop condition and a false step guard; the body does not run (the step guard is separate from the loop configuration).
- [ ] T004 [US1] Create `tests/contract/Sequences/SequenceLoopGuardContractTests.cs`. Add `LoopGuardSurvivesSaveAndRead`: `POST /api/sequences` with a `Loop` step with the guard `none(lastRun ...)`; `GET /api/sequences/{id}` returns the `Loop` step with `condition.type` `none` and one child of type `lastRun` (US3 scenario 1, SC-005, FR-001).
- [ ] T005 [US1] Build and run the tests of T003 and T004. Make sure that `FalseGuardSkipsLoopBodyAndRunsNextStep`, `FalseCompositeGuardSkipsLoopBody`, `GuardEvaluationErrorFailsSequence`, `LaterCommandOutcomeReadsSkippedForSkippedLoop`, `FalseGuardSkipsWhileLoopBody` and `LoopGuardSurvivesSaveAndRead` fail before the fix.

### Implementation for User Story 1

- [ ] T006 [US1] In `src/GameBot.Domain/Services/SequenceRunner.cs`, move the guard block of `ExecuteSingleStepAsync` (`if (step.Condition is not null) { ... }`) into a private method `EvaluateStepGuardAsync` (research R-003). It evaluates `step.Condition` with `SequenceStepConditionEvaluator.EvaluateAsync`, and returns `Run`, `Skip` or `Fail`. It records the `Skipped` or `Failed` result through a record delegate, and sets `stepOutcomes[stepKey]`. The `Action` path must record the same fields and messages as now (FR-009).
- [ ] T007 [US1] In `ExecuteSingleStepAsync`, before the `Loop` dispatch, call `EvaluateStepGuardAsync` when the `Loop` step has a `Condition`. For `Skip`, record `AddLoopStep(stepKey, "Skipped", [], message, conditionType, "false")` with the message of data-model.md and return `false`. For `Fail`, record `AddLoopStep(stepKey, "Failed", [], message, conditionType, "error")`, call `result.Fail`, and return `true`. For `Run`, call `ExecuteLoopStepAsync`, then `SetConditionForLatestLoopStep(stepKey, conditionType, "true")`. Use the same `stepKey` as `ExecuteLoopStepAsync` (`loop@<order>` when `StepId` is empty). Write the comment in STE and name issue #232.
- [ ] T008 [US1] In `src/GameBot.Service/Endpoints/SequencesEndpoints.cs`, add `Condition = MapPerStepCondition(step.Condition)` to the `Loop` branch of `MapToLinearSteps` (research R-007). Do not change the `If` branch.
- [ ] T009 [US1] Run the tests of T003 and T004 and all tests in `tests/unit/Sequences`. Make sure that they pass.

**Checkpoint**: US1 is complete. A false guard skips the loop.

## Phase 4: User Story 2 - The execution log shows the condition result of a Loop step (Priority: P1)

**Goal**: The `Loop` entry of the run result and of the execution log has `conditionType` and `conditionResult`.

**Independent Test**: Run a sequence with a guarded `Loop` step, with a false guard and with a true guard. Read the `Loop` entry of the execution log.

### Tests for User Story 2

- [ ] T010 [US2] In `tests/unit/Sequences/SequenceRunnerLoopGuardTests.cs`, add assertions to the T003 tests: a false guard gives a `Loop` entry with `Status` `Skipped`, an empty `LoopIterations`, `ConditionType` `commandOutcome`, `ConditionResult` `false`, and a `Message` that contains `condition is false`; a true guard gives `ConditionResult` `true` and 3 iterations; no guard gives `ConditionType` and `ConditionResult` `null`; a composite false guard gives a message that contains `settled the guard` (spec US2 scenarios 1 to 3, SC-002, SC-003).
- [ ] T011 [US2] Create `tests/contract/ExecutionLogs/ExecutionLogsLoopGuardContractTests.cs` in the style of `ExecutionLogsLoopExitReasonContractTests.cs`. Add:
  - `SkippedLoopEntryCarriesFalseConditionResult`: save a sequence with a `count` loop (count 2) guarded by `{ "type": "lastRun", "sequence": "self", "status": "success", "within": "01:00:00" }` (false in an ad-hoc run), execute it with `dryRun: true`, and read the log. The `loop` detail item has `status` `Skipped`, `iterations` 0, `conditionType` `lastRun` and `conditionResult` `false` (SC-004).
  - `GuardedLoopEntryCarriesTrueConditionResult`: the same with the guard `none(lastRun ...)`. The `loop` detail item has `iterations` 2, `conditionType` `none` and `conditionResult` `true`.
- [ ] T012 [US2] Build and run the tests of T010 and T011. Make sure that the new assertions fail before T013.

### Implementation for User Story 2

- [ ] T013 [US2] In `src/GameBot.Service/Services/SequenceExecution/SequenceExecutionService.cs`, add `["conditionType"] = step.ConditionType` and `["conditionResult"] = step.ConditionResult` to the attributes of the `Loop` detail item (research R-005). Add a comment in STE that names issue #232.
- [ ] T014 [US2] Run the tests of T010 and T011. Make sure that they pass.

**Checkpoint**: US1 and US2 are complete.

## Phase 5: User Story 3 - The condition of a Loop step is stored and checked at save time (Priority: P2)

**Goal**: The validator checks the guard at save time. (The save mapping that keeps the guard is T008 in US1, and its round-trip test is T004.)

**Independent Test**: Save a guarded `Loop` step and read it back. Save an incorrect guard and get a 400.

### Tests for User Story 3

- [ ] T015 [US3] In `tests/unit/Sequences/LoopValidationTests.cs`, add:
  - `LoopGuardWithUnknownCommandOutcomeReferenceIsRejected`: the error names the `Loop` step and the unknown reference.
  - `LoopGuardReferencingOwnBodyStepIsRejected`: the error says that the reference must be a prior step.
  - `LoopGuardImageVisibleWithoutImageIdIsRejected`.
  - `LoopGuardReferencingPriorStepIsAccepted`: no error.
- [ ] T016 [US3] In `tests/contract/Sequences/SequenceLoopGuardContractTests.cs`, add:
  - `DryRunRejectsLoopGuardWithUnknownReference`: `POST /api/sequences` with `dryRun: true` and a `Loop` guard `commandOutcome` with `stepRef` `nope` returns 400, and the body contains the step ID and `nope` (US3 scenario 2, SC-006).
  - `CreateRejectsLoopGuardImageVisibleWithoutImageId`: `POST /api/sequences` returns 400 (US3 scenario 3).
- [ ] T017 [US3] Build and run the tests of T015 and T016. Make sure that they fail before T018 and T019.

### Implementation for User Story 3

- [ ] T018 [US3] In `src/GameBot.Service/Endpoints/SequencesEndpoints.cs`, in `ValidatePerStepForPersistenceAsync`, do the `imageVisible` `minSimilarity` check before the `step.Action is null` test, so that it applies to a `Loop` guard too (research R-006).
- [ ] T019 [US3] In `src/GameBot.Domain/Services/SequenceStepValidationService.cs`, in `ValidateLoopStep`, call `ValidateStepCondition(step, stepLabel, ownPosition, positionByStepId, errors, insideLoop)` for the `Loop` step itself, after the loop configuration checks (research R-006). Add a comment in STE that names issue #232.
- [ ] T020 [US3] Run the tests of T015 and T016. Make sure that they pass.

**Checkpoint**: All stories are complete.

## Phase 6: Polish and cross-cutting

- [ ] T021 [P] In `src/GameBot.Service/Swagger/SequenceNestingRulesSchemaFilter.cs`, add a constant `ConditionDescription` with the text of contracts/loop-step-guard.md ("OpenAPI"), and set it on the `condition` property of the `SequenceStepContract` schema (FR-010). In `tests/contract/Sequences/SequenceLoopGuardContractTests.cs`, add `OpenApiStepConditionDescribesLoopGuard`: the `condition` property of the step schema in `/swagger/v1/swagger.json` has a description that contains `Loop`.
- [ ] T022 [P] In `docs/architecture.md`, add one sentence to the **Sequence** item: a `condition` on a top-level `Loop` step is a guard, evaluated one time before the first iteration; when it is false, the loop does not run and its entry is `Skipped` (feature 110, #232). Change the "Last reviewed" line to 2026-09-28, feature 110.
- [ ] T023 [P] In `CHANGELOG.md`, add an item at the top of `### Fixed` under `[Unreleased]` for 110-loop-step-condition, #232 (FR-010).
- [ ] T024 [P] In `specs/STATUS.md`, add the row `| 110 | A condition on a Loop step is a guard that the runtime obeys | Implemented |`. Set **Status** in `spec.md` to `Implemented`.
- [ ] T025 Build `GameBot.sln` in Release with `-warnaserror`. Run the unit and contract test projects. Make sure that there are no new failures (SC-007).
- [ ] T026 Mark all tasks as done in this file.

## Dependencies and Execution Order

- Phase 1 → Phase 2 → Phase 3 → Phase 4 → Phase 5 → Phase 6.
- Tests before the fix in each story: T003–T005 before T006–T008; T010–T012 before T013; T015–T017 before T018–T019.
- The contract tests of T011 need T008 (the save mapping keeps the guard). T008 is in Phase 3, so the phase order satisfies this.
- T021, T022, T023 and T024 are in different files and can run in parallel.

## Implementation Strategy

MVP: Phases 1 to 3. This removes the defect for sequences saved through the API. Phase 4 adds the log fields. Phase 5 adds the save-time checks. Phase 6 updates the documents.
