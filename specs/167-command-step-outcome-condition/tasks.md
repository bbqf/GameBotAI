# Tasks: Sequence condition on a step outcome inside a command

**Input**: Design documents in `specs/167-command-step-outcome-condition/` (plan.md, spec.md, research.md, data-model.md, contracts/, quickstart.md)
**Tests**: The plan requires tests (Constitution Principle II). Test tasks are included. Each test task has its own test file.
**Format**: `- [ ] T### [P?] [US?] Description with file path. Done: condition.` [P] means the task can run in parallel (different file, no open dependency).
**Rules**:
- Two [P] tasks never edit the same file. A task that edits a file that an earlier task also edits has no [P] and runs in order.
- Order check scope: the order check against the command runs ONLY in the save validation and the save dry run (`SequencesEndpoints.cs`, through `ICommandRepository`). The run dry run and the run check only the `stepRef` rules. A missing order at run time gives "no outcome recorded".
- End-run step: the existing step type `SequenceStepType.Break` (`src/GameBot.Domain/Commands/SequenceStep.cs`) with a `BreakCondition`, inside a loop. No new step type.

## Phase 1: Setup

- [x] T001 Read `specs/167-command-step-outcome-condition/contracts/command-step-outcome-condition.md` and `quickstart.md`. Read how `commandOutcome` is wired in `src/GameBot.Domain/Services/SequenceStepConditionEvaluator.cs` and `src/GameBot.Domain/Services/SequenceRunner.cs`. Use `commandOutcome` as the pattern for all next tasks. Done: no file change; the files to copy patterns from are known.

## Phase 2: Foundational (blocks all user stories)

**Goal**: Domain types and the data path from the command run to the runner.

- [x] T002 [P] Create `src/GameBot.Domain/Commands/CommandStepOutcomeStates.cs`: constants `Executed`, `SkippedDetectionFailed`, `CompletedTimeout`, `ImageDetected`; `All`; `AllText` (`executed|skipped_detection_failed|completed_timeout|image_detected`); `IsAllowed(string?)` (not case-sensitive). The reason code `detection_failed_after_3_retries` is not allowed. Add XML comments. Done: the Domain project builds with no new warning.
- [x] T003 [P] Add class `CommandStepOutcomeStepCondition` (fields `StepRef`, `CommandStepOrder`, `ExpectedState`, `Negate`) and the discriminator `commandStepOutcome` in `src/GameBot.Domain/Commands/SequenceStepCondition.cs`. Make the JSON polymorphism list the new type. Done: a JSON string with `"type":"commandStepOutcome"` deserializes to the new class and serializes back equal.
- [x] T004 [P] Add the optional field `StepStates` (`IReadOnlyDictionary<int, string>?`, default null) to `src/GameBot.Domain/Services/CommandDispatchOutcome.cs`. Done: existing callers build without change.
- [x] T005 [P] Add the optional field `Depth` (int, default 0) to the type `PrimitiveTapStepOutcome` in `src/GameBot.Service/Services/ICommandExecutor.cs`. Add an XML comment (FR-009a). Done: existing callers build without change.
- [x] T006 Set `Depth` in `src/GameBot.Service/Services/CommandExecutor.cs`: 0 for a step of the command that the sequence step runs, and depth + 1 in the nested-command recursion. This is the only change in the command run path (FR-009, FR-009a). Depends on T005. Done: existing `CommandExecutor` tests pass with no change.
- [x] T007 [P] Copy the readable step states into `CommandDispatchOutcome.StepStates` in `src/GameBot.Service/Services/SequenceExecution/SequenceExecutionService.cs`. Keep only depth 0 entries with status `executed`, `skipped_detection_failed`, `completed_timeout`, or `image_detected`. Key: step order. Use null when the map is empty. Depends on T004 and T006. Done: the Service project builds.
- [x] T008 [P] Do the same copy in `src/GameBot.Service/Services/SequenceExecution/SequenceExecutionService.StepThrough.cs`. Depends on T004 and T006. Done: the Service project builds.
- [x] T009 In `src/GameBot.Domain/Services/SequenceRunner.cs`, add the run-scoped map `commandStepStates` (sequence step key, not case-sensitive, to order-to-state map). After each command run of a step, set the entry to `StepStates`, or remove it when `StepStates` is null (a new command run replaces the older entry, even with no readable states). Do not change the entry for a step that is skipped, fails, or does not run in a dry run. Do not touch `stepOutcomes`. Pass the map to the evaluator through a new optional parameter. Depends on T004. Done: the Domain project builds; existing runner tests pass.
- [x] T010 Add the new optional map parameter to the evaluator entry point in `src/GameBot.Domain/Services/SequenceStepConditionEvaluator.cs` (signature only, no new case yet). Depends on T009. Done: existing evaluator tests pass with no change.

**Checkpoint**: The state data reaches the evaluator. `commandOutcome` still works.

## Phase 3: User Story 1 - Branch on a step inside a command (P1)

**Goal**: The condition is true only when the command step ended in the expected state. The condition works in each slot where `commandOutcome` works. The API takes and gives the condition.
**Independent test**: A run where step 12 ran gives true. A run where step 12 was skipped gives false. `negate` inverts. A nested command step with the same order does not change the result.

### Tests for User Story 1 (write first; they fail until the implementation tasks are done)

- [x] T011 [P] [US1] Write unit tests in `tests/unit/Sequences/CommandStepOutcomeConditionTests.cs`: state equals expected (true), other state (false), `negate`, case-insensitive match, and the `if` branch choice (acceptance scenarios 1 to 5). Done: tests compile and pass after T017.
- [x] T012 [P] [US1] Write unit tests for the position rules (step condition, all, any, none, if, loop, break) in `tests/unit/Sequences/CommandStepOutcomePositionTests.cs`. Done: tests compile and pass after T022.
- [x] T013 [P] [US1] Write unit tests in `tests/unit/Commands/CommandExecutorStepDepthTests.cs`: a command with a nested command gives `Depth` 0 for its own steps and `Depth` 1 or more for the nested steps. The step order and result do not change. Done: tests pass after T006.
- [x] T014 [P] [US1] Write unit tests for nested-command step order in `tests/unit/Sequences/CommandStepOutcomeNestedCommandTests.cs`: a command with a top-level step of order 2 and a nested command that also has a step of order 2 with another state. `StepStates` holds only the top-level state. The condition on order 2 reads the top-level state, and the nested step does not change the result (FR-009a). Done: tests pass after T007 and T017.
- [x] T015 [P] [US1] Write contract tests in `tests/contract/Sequences/CommandStepOutcomeConditionContractTests.cs`: create, read back, and update a sequence with the condition in a step condition, an all/any/none child, an if, a loop, and a break. The saved JSON equals the data-model example. Done: tests pass after T025.
- [x] T016 [P] [US1] Write OpenAPI tests in `tests/contract/Sequences/CommandStepOutcomeOpenApiTests.cs`: the condition `type` enum lists `commandStepOutcome`; `expectedState` has the four states; `commandStepOrder` has `minimum: 1` (FR-011). Done: tests pass after T026 to T028.

### Implementation for User Story 1

- [x] T017 [US1] Add a leaf case for `CommandStepOutcomeStepCondition` to `EvaluateLeafAsync` in `src/GameBot.Domain/Services/SequenceStepConditionEvaluator.cs`. Look up `commandStepStates[stepRef][order]`. Compare with `expectedState` (not case-sensitive). `negate` applies in the existing path. A missing state gives false and no failure. Depends on T003, T010. Done: T011 passes.
- [x] T018 [P] [US1] Add the new type to the condition union in `src/GameBot.Domain/Commands/ConditionExpression.cs`. Done: the Domain project builds; a leaf of the new type parses.
- [x] T019 [P] [US1] Add the new type to the `if` condition handling in `src/GameBot.Domain/Commands/IfConfig.cs`. Done: an `if` with the new condition round-trips in JSON.
- [x] T020 [P] [US1] Add the new type to the step condition handling in `src/GameBot.Domain/Commands/SequenceStep.cs`. Done: a step condition and a `BreakCondition` of the new type round-trip in JSON.
- [x] T021 [P] [US1] Add the new type to the break positions in `src/GameBot.Domain/Services/BreakStepIndex.cs`. Done: a break step with the new condition is found by the index.
- [x] T022 [US1] Add the new type to the loop and condition walks in `src/GameBot.Domain/Services/SequenceRunner.cs`. Depends on T017. Done: T012 passes.
- [x] T023 [P] [US1] Add the new type to the leaf gate in `Validate` of `src/GameBot.Domain/Services/CompositeConditionValidator.cs`. Done: a valid composite with the new leaf passes validation.
- [x] T024 [P] [US1] Add `CommandStepOutcomeConditionContract` (four fields) in `src/GameBot.Service/Models/SequenceStepContracts.cs`. Done: the Service project builds.
- [x] T025 [US1] Map the contract to and from the domain class in the request and response mapping of `src/GameBot.Service/Endpoints/SequencesEndpoints.cs`. Depends on T024. Done: T015 passes.
- [x] T026 [P] [US1] List the new type and fields in `src/GameBot.Service/Swagger/ConditionalFlowSchemaDocumentFilter.cs`. Set `minimum: 1` on `commandStepOrder` and list the four states for `expectedState`. Done: the OpenAPI document shows both.
- [x] T027 [P] [US1] List the new type in `src/GameBot.Service/Swagger/ConditionReferenceScopeSchemaFilter.cs`. Done: the schema lists the type with `stepRef`.
- [x] T028 [P] [US1] List the new type in `src/GameBot.Service/Swagger/PrimitiveActionSchemaFilter.cs`. Done: T016 passes.

**Checkpoint**: US1 works at the domain level and in the API. `commandOutcome` tests still pass.

## Phase 4: User Story 2 - All four states and the run log (P1)

**Goal**: Each of the four states matches. Each evaluation writes one log line. A missing outcome gives false and no failure.
**Independent test**: For each state, run a command that gives it and check the result and the log line.

### Tests for User Story 2

- [x] T029 [P] [US2] Write unit tests in `tests/unit/Sequences/CommandStepOutcomeLogTests.cs`: each of the four states gives true for the expected state and false for the others (SC-002); the log line shows `stepRef`, order, expected state, and the state read; no outcome gives `actual=none (no outcome recorded)` and false before `negate`; a skipped sequence step, a dry-run step, an unreadable status, and a step order that was not reached give "no outcome recorded"; an unloadable command at run time gives "no outcome recorded" and the run does not fail; a loop reads the latest run; a new command run with no readable states replaces the older entry. Done: tests pass after T032.
- [x] T030 [P] [US2] Write unit tests for the state helper in `tests/unit/Sequences/CommandStepOutcomeStatesTests.cs`: `IsAllowed` accepts the four states in any case, and rejects empty text and `detection_failed_after_3_retries`. Done: tests pass (T002).

### Implementation for User Story 2

- [x] T031 [US2] Add the run-log line for each evaluation in `src/GameBot.Domain/Services/SequenceStepConditionEvaluator.cs` and the matching label text in `src/GameBot.Domain/Services/SequenceRunner.cs`. Format: `actual=<state>` or `actual=none (no outcome recorded)`. Depends on T017, T022. Done: the log assertions of T029 pass.
- [x] T032 [US2] Check that the runner keeps the latest state for a step in a loop and does not clear the entry on a skipped, failed, or dry-run step (data-model state transitions). Fix `src/GameBot.Domain/Services/SequenceRunner.cs` if a test in T029 fails. Depends on T031. Done: T029 passes.

**Checkpoint**: US1 and US2 work together.

## Phase 5: User Story 3 - Bad reference gives an error (P2)

**Goal**: A bad `stepRef` gives an error at save, at save dry run, at run dry run, at run, and at load. A bad order gives an error ONLY at save and at save dry run.
**Independent test**: Save a sequence with each wrong reference and check the error message.

### Tests for User Story 3

- [x] T033 [P] [US3] Write unit tests in `tests/unit/Sequences/CommandStepOutcomeValidationTests.cs`: empty `stepRef`, unknown step, step not before, step that is not a command step, order below 1, bad `expectedState`, the same checks for an all/any/none child, and the load check in the repository (messages from `contracts/command-step-outcome-condition.md`). Done: tests pass after T037.
- [x] T034 [P] [US3] Write contract tests in `tests/contract/Sequences/CommandStepOutcomeValidationContractTests.cs`: HTTP 400 for each case at create, update, and save dry run; an order that the command does not have gives the error with the command id and the order at save and save dry run; when the command cannot be loaded, the save is accepted and the order check is skipped; the run dry run gives the `stepRef` errors only and does NOT check the order (a missing order is accepted at run dry run). Done: tests pass after T038.

### Implementation for User Story 3

- [x] T035 [US3] Add the `stepRef`, order minimum 1, and `expectedState` checks to the per-step check near the `commandOutcome` code, and to the second check for a step condition, in `src/GameBot.Domain/Services/SequenceStepValidationService.cs`. These checks use no command repository. Done: T033 step-level cases pass.
- [x] T036 [US3] Add the `stepRef`, order minimum 1, and `expectedState` checks for `all`, `any`, `none` children in `src/GameBot.Domain/Services/CompositeConditionValidator.cs`. Depends on T023 (same file, so no [P]). Done: T033 composite cases pass.
- [x] T037 [P] [US3] Add the load check in `src/GameBot.Domain/Commands/FileSequenceRepository.cs`. A saved file with the condition loads without change. Done: T033 repository cases pass.
- [x] T038 [US3] Add the order check against the command to `src/GameBot.Service/Endpoints/SequencesEndpoints.cs`, in the create, update, patch, and save dry run paths (the code that runs `ValidatePerStepForPersistenceAsync`, which has the `ICommandRepository`). Load the command of the referenced sequence step with `ICommandRepository`; if the command has no top-level step with that order, return the error with the command id and the order. Skip the check when the command cannot be loaded. Do NOT add this check to the run endpoint or to `ValidateSequenceFlowAsync`. Depends on T025. Done: T034 save cases pass.
- [x] T039 [US3] Make the run dry run and the run in `src/GameBot.Service/Services/SequenceExecution/SequenceExecutionService.cs` apply the `stepRef` rules of T035 through `SequenceStepValidationService`. Add NO order check here. Depends on T007 (same file, so no [P]). Done: the run dry run case of T034 passes.
- [x] T040 [US3] Make the step-through run in `src/GameBot.Service/Services/SequenceExecution/SequenceExecutionService.StepThrough.cs` apply the same `stepRef` rules. Add NO order check here. Depends on T008 (same file, so no [P]). Done: a step-through run with a bad `stepRef` returns the error.

**Checkpoint**: Each wrong reference gives an error before the sequence runs (SC-004).

## Phase 6: User Story 4 - The Feature 023 case (P2)

**Goal**: A sequence ends the run after a recall and fails by name when the recall fails.
**Independent test**: A test sequence with a send step, a condition on the Confirm tap, and an end-run step. Run it with a command that skips the Confirm tap and with a command that runs it.

- [x] T041 [P] [US4] Write integration tests for the end-run flow in `tests/integration/Sequences/CommandStepOutcomeEndRunIntegrationTests.cs`. Use the existing end-run step: a loop that holds the send step and a `SequenceStepType.Break` step (`src/GameBot.Domain/Commands/SequenceStep.cs`) whose `BreakCondition` is a `commandStepOutcome` condition. Cases: (1) the Confirm tap ran, so the condition is true, the Break step ends the run, and no further send runs; (2) the Confirm tap was skipped, so the run goes on to the next send; (3) a shield step fails after a recall, and a condition picks a named failure step, so the run fails with that name (SC-001). Use a fake command executor that gives `executed` and `skipped_detection_failed`. Done: all three tests pass after T042.
- [x] T042 [US4] Fix any gap that T041 shows, in the file that the failing test points to. Run the quickstart steps in `specs/167-command-step-outcome-condition/quickstart.md` against the test host. Depends on T041. Done: T041 passes and the quickstart steps give the expected results.

**Checkpoint**: The real case of Feature 023 works with one condition and no change to the command (FR-013).

## Phase 7: User Story 5 - Edit the condition in the web UI (P3)

**Goal**: The editor creates and edits the condition, with the same errors as the service.
**Independent test**: Add the condition, save, load again. The fields keep their values and a bad reference shows an error.

### Tests for User Story 5

- [x] T043 [P] [US5] Write Vitest tests for validation in `src/web-ui/src/lib/__tests__/commandStepOutcomeValidation.spec.ts`: empty `stepRef`, order below 1 or empty, missing or bad state, and a `stepRef` that is not a prior command step. Done: tests pass after T047.
- [x] T044 [P] [US5] Write Vitest tests for the fields in `src/web-ui/src/components/sequences/__tests__/CommandStepOutcomeFields.test.tsx`: the type picker shows `stepRef`, order, and state fields; a saved condition shows its values; an empty order or no state shows an error and sends no request; the break row shows the condition. Done: tests pass after T050.

### Implementation for User Story 5

- [x] T045 [P] [US5] Add the type and fields in `src/web-ui/src/types/sequenceFlow.ts`. Done: `tsc` passes.
- [x] T046 [P] [US5] Add the type to the step entry model in `src/web-ui/src/types/stepEntry.ts`. Done: `tsc` passes.
- [x] T047 [P] [US5] Add the validation (same messages as the service) in `src/web-ui/src/lib/validation.ts`. Done: T043 passes.
- [x] T048 [P] [US5] Add the type, the `stepRef` picker, the order field (minimum 1), and the four-state picker in `src/web-ui/src/components/sequences/ConditionFields.tsx`. Done: the fields render in the type picker.
- [x] T049 [P] [US5] Add the type to the break condition row in `src/web-ui/src/components/sequences/BreakStepRow.tsx`. Done: the break row shows the condition.
- [x] T050 [US5] Wire the type into the load, edit, and save logic in `src/web-ui/src/pages/SequencesPage.tsx`. Phone support level: reduced, per `docs/mobile-ui-principles.md`. Depends on T045 to T049. Done: T044 passes.

**Checkpoint**: All five stories work.

## Phase 8: Polish and cross-cutting

- [x] T051 Run the existing `commandOutcome` tests and the full `dotnet test` and Vitest suites. Confirm that saved sequences with `commandOutcome` load and run with the same result (SC-005, FR-010). Fix any failure in the file that the test points to. Done: both suites pass.
- [x] T052 [P] Update `docs/architecture.md`: add the `commandStepOutcome` condition type and refresh the "Last reviewed" date and text (Principle V). Done: the file shows the type and the new date.
- [x] T053 [P] Set the `Status` in `specs/167-command-step-outcome-condition/spec.md` and the row in `specs/STATUS.md` (Principle V). Done: both files show the new status.
- [x] T054 Check that all new text (comments, messages, docs) uses STE (Principle VI). Done: no non-STE text is left in the changed files.
- [ ] T055 Before the push, bump the version in `installer/versioning/version.override.json` (constitution, "Version bump"): run `git fetch origin master`, compare with `origin/master`, set `patch`, `updatedBy`, and `updatedAtUtc`. Done: the file has the new values.

## Dependencies and order

- Phase 1, then Phase 2, then the user stories. Phase 2 blocks all stories.
- US1 first (MVP). US2 depends on US1 (T017, T022). US3 depends on US1 (T023, T025) and on T007, T008. US4 depends on US1, US2, and US3. US5 depends only on the API shape of US1 and can run in parallel with US2 to US4.
- Edits to one file run in this order:
  - `SequenceRunner.cs`: T009, T022, T031, T032.
  - `SequenceStepConditionEvaluator.cs`: T010, T017, T031.
  - `CompositeConditionValidator.cs`: T023, then T036.
  - `SequencesEndpoints.cs`: T025, then T038.
  - `SequenceExecutionService.cs`: T007, then T039. `SequenceExecutionService.StepThrough.cs`: T008, then T040.

## Parallel examples

- Phase 2: T002, T003, T004, T005 together. Then T007 and T008 together.
- US1 tests: T011 to T016 together (six files). US1 source: T018, T019, T020, T021, T023, T024, T026, T027, T028 together (nine files).
- US3: T037 can run with T035 (different files).
- US5: T043 to T049 together (seven files).

## Implementation strategy

- MVP: Phase 1, Phase 2, and US1. This gives the condition in the domain and in the API.
- Then US2 (log), US3 (validation), US4 (end-run proof), US5 (web UI), and Polish. Run `dotnet test` after each phase.
