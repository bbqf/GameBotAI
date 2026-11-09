# Tasks: Report which image of a list matched

**Input**: Design documents in `specs/181-report-matched-image/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/matched-image-contract.md

Format: `- [X] T### [P?] [US?] Description with file path`. [P] means the task can run in parallel (other file, no open dependency).

## Phase 1: Setup

No setup task. The solution and the test projects exist.

## Phase 2: Foundational (blocks all stories)

- [X] T002 Create `src/GameBot.Domain/Services/StepImageMatchScope.cs`: ambient (AsyncLocal) per-run store with `Push`, `Current`, `Set(stepKey, imageId, confidence)`, `Clear(stepKey)`, `TryGet(stepKey, out match)`. Key is not case-sensitive. Same shape as `CommandStepStateScope`. Add XML docs.
- [X] T003 [P] Create unit tests in `tests/unit/StepImageMatchScopeTests.cs` for set, replace, clear, key case, and no scope (Current is null).
- [X] T004 In `src/GameBot.Domain/Services/SequenceRunner.cs`, push a `StepImageMatchScope` for each run, next to the existing `CommandStepStateScope.Push`.

## Phase 3: User Story 1 - Matched image id in the step result (P1)

**Goal**: A `WaitForImage` step result holds `matchedImageId` and `confidence` on a match.
**Independent test**: Run a `WaitForImage` step with a list of two images where the second matches. The result holds the second id.

- [X] T005 [P] [US1] Add tests in `tests/unit/SequenceRunnerWaitForImageMatchedImageTests.cs`: list of 2 (second matches), list of 5 (last matches), list of 1, single image, timeout (no id), `until: absent` (no id), old result without the field.
- [X] T006 [US1] Add `MatchedImageId` (nullable, omitted when null) to `WaitForImageStepResultDetails` in `src/GameBot.Domain/Services/SequenceRunner.cs`.
- [X] T007 [US1] In `WaitForImageAsync` of `src/GameBot.Domain/Services/SequenceRunner.cs`, make the list evaluator closure keep the id of the first item that returns true. On `image_detected`, set `detail.MatchedImageId` (list or single image), `detail.Confidence`, and call `StepImageMatchScope.Current?.Set(stepKey, ...)`. On any other exit, call `Clear(stepKey)`. Do not change the item order or the matching.
- [X] T008 [US1] In `src/GameBot.Service/Services/SequenceExecution/SequenceExecutionService.cs` and `src/GameBot.Service/Models/SequenceStepContracts.cs`, copy `matchedImageId` into the step result DTO. Update Swagger text in `src/GameBot.Service/Swagger/ParametrizedReferenceImageSchemaFilter.cs` if the schema lists result members.
- [X] T009 [US1] In `SequenceStepConditionEvaluator` (`src/GameBot.Domain/Services/SequenceStepConditionEvaluator.cs`) and the gate in `SequenceRunner.cs`: when the condition of a step is a top-level, non-negated `imageVisible` and it is true, call `StepImageMatchScope.Current?.Set` under the step key with the item that matched. Nested or negated conditions record nothing; a false or absent match calls `Clear`.
- [X] T010 [P] [US1] Add tests for T009 in `tests/unit/StepConditionMatchedImageRecordTests.cs` (top-level list, negated, composite, false).

## Phase 4: User Story 2 - matchedImage condition (P1)

**Goal**: `{ type: matchedImage, stepRef, imageId }` tests the latest match of an earlier step.
**Independent test**: Two runs with different matched images give true and false.

- [X] T011 [P] [US2] Add tests in `tests/unit/MatchedImageConditionTests.cs`: equal id true, other id false, no entry false, `negate`, JSON round trip with the `type` discriminator, latest run wins.
- [X] T012 [US2] Add `MatchedImageStepCondition` (type `matchedImage`, `StepRef`, `ImageId`) and its `JsonDerivedType` line in `src/GameBot.Domain/Commands/SequenceStepCondition.cs`.
- [X] T013 [US2] Evaluate it in `src/GameBot.Domain/Services/SequenceStepConditionEvaluator.cs` against `StepImageMatchScope.Current` (ordinal compare). No entry gives false before `negate`. Add a reason text for the run log (FR-005).
- [X] T014 [US2] Add save rules in `src/GameBot.Domain/Services/SequenceStepValidationService.cs` and `src/GameBot.Domain/Services/CompositeConditionValidator.cs`: `stepRef` not empty and names a prior step (reuse the reference scan of `CommandStepOutcomeRules` in `src/GameBot.Domain/Services/CommandStepOutcomeRules.cs`), `imageId` not empty. Messages say what is wrong and what to do.
- [X] T015 [US2] Handle the new type in the other places that switch on condition type: `src/GameBot.Domain/Parameters/SequenceStepConditionResolver.cs` (resolve a single-value placeholder in `imageId`), `src/GameBot.Domain/Parameters/ParameterReferenceScanner.cs`, `src/GameBot.Domain/Commands/FileSequenceRepository.cs`, `src/GameBot.Domain/Services/StepThrough/SequenceStepper.cs`, `src/GameBot.Service/Models/SequenceStepContracts.cs`, `src/GameBot.Service/Endpoints/SequencesEndpoints.cs`.
- [X] T016 [P] [US2] Add Swagger schema text and an example for the new condition in `src/GameBot.Service/Swagger/ConditionReferenceScopeSchemaFilter.cs` and `src/GameBot.Service/Swagger/ConditionalFlowSchemaDocumentFilter.cs`.
- [X] T017 [P] [US2] Add validation tests in `tests/unit/MatchedImageConditionValidationTests.cs` (empty stepRef, unknown, not prior, empty imageId, valid).
- [X] T018 [P] [US2] Add integration tests in `tests/integration/MatchedImageSequenceTests.cs`: save a sequence with the condition, run it twice, check the branch and the Swagger schema.
- [X] T019 [P] [US2] Web UI: add the type in `src/web-ui/src/types/sequenceFlow.ts`, the prior-step rule in `src/web-ui/src/lib/validation.ts`, and the condition editor type with two fields (step, image id) in the existing condition editor component. Add a Vitest test in `src/web-ui/src/lib/__tests__/matchedImageValidation.spec.ts`. Phone support level: reduced.

## Phase 5: User Story 3 - Placeholder in a later step (P2)

**Goal**: `{{step.<stepRef>.matchedImageId}}` becomes the matched image id.
**Independent test**: A later step text holds the id after a match; with no match the run reports the unresolved-placeholder error.

- [X] T020 [P] [US3] Add tests in `tests/unit/StepResultPlaceholderTests.cs`: resolve, no entry error, unknown stepRef at save, name clash check with `queue.` names, not a parameter declaration.
- [X] T021 [US3] Create `src/GameBot.Domain/Parameters/StepResultPlaceholderRules.cs`: parse `step.<stepRef>.matchedImageId` (prefix, stepRef, member), `IsStepResultName`, and the reserved-namespace rule. Update `ParameterNameRules` so a user parameter cannot use the `step.` namespace.
- [X] T022 [US3] In `src/GameBot.Domain/Parameters/ParameterScope.cs`, make `TryResolve` read `StepImageMatchScope.Current` for `step.*` names and return the id. No entry gives the standard unresolved result.
- [X] T023 [US3] In `src/GameBot.Domain/Parameters/ParameterReferenceScanner.cs` and `src/GameBot.Domain/Services/ParameterValidationService.cs`, accept `step.*` references without an undeclared-parameter error, and check at save that `stepRef` names a prior step.
- [X] T024 [P] [US3] Add an integration test in `tests/integration/MatchedImagePlaceholderTests.cs` (full run: wait, then a step with the placeholder).

## Phase 6: User Story 4 - Run log (P2)

**Goal**: The per-step run log shows the matched image id.
**Independent test**: Read the log entry of a matched image check.

- [X] T025 [US4] In `src/GameBot.Service/Services/ExecutionLog/ExecutionLogService.cs`, add `matchedImageId` and `confidence` to the details of a sequence `waitForImage` step and of a step gated by a matched `imageVisible` list. Omit them with no match.
- [X] T026 [P] [US4] Add tests in `tests/integration/MatchedImageRunLogTests.cs` (match, no match). Show the field in the run log view of the web UI only if the view lists the details by name; otherwise no UI change.

## Phase 7: Polish

- [X] T027 [P] Update `docs/architecture.md` (API surface, domain model, "Last reviewed"), add a row to `specs/STATUS.md`, set the `Status` of this spec, set the `Status` of `specs/180-any-of-image-list/spec.md` to "Implemented (iterated by 181)" (and its `specs/STATUS.md` row), and add a `CHANGELOG.md` entry. All text in STE.
- [X] T028 Run `dotnet build`, `dotnet test`, and `npm test` in `src/web-ui`. Fix failures. Run `scripts/ci/measure-shards.ps1` and split a shard that is more than 20% above the average, as `docs/ci-time.md` says. Run the quickstart steps by test.
- [X] T030 Update the contract snapshots in `tests/contract/ApiContractSnapshots` after the new condition type and the new result field exist (after T016). Run the contract tests and accept only the expected diff. Do this before T028.
- [X] T029 Bump the version in `installer/versioning/version.override.json` per the constitution, before the implementation commit.

## Dependencies

- T002 before T004, T007, T009, T013, T022.
- US1 (T005-T010) before US2 and US3 can see a match at run time. US2 and US3 can be coded in parallel after T002.
- US4 needs T006/T007 for the sequence step; T009 for the gate.
- T001 is not used (the number is kept so that task IDs stay stable). T030 (snapshot update) runs after T016.
- Polish last.

## Parallel examples

- T003, T005, T011, T017, T020 (tests, different files) in parallel after T002.
- T016, T018, T019 in parallel after T012-T015.

## Implementation strategy

MVP is US1 plus US2: the result holds the id and a later condition can branch. Then add US3 (placeholder) and US4 (log), then polish.
