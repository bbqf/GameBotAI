# Tasks: Any-of image condition and WaitForImage for a list parameter

**Input**: Design documents in `/specs/180-any-of-image-list/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/image-list-contract.md

Format: `- [ ] T### [P?] [US?] Description with file path`. Tests are required (spec FR-013).

## Phase 1: Setup

- [X] T001 Read `src/GameBot.Domain/Commands/SequenceStepCondition.cs`, `DetectionTarget.cs`, `WaitForImageConfigMapper.cs`, `src/GameBot.Domain/Parameters/SequenceStepConditionResolver.cs`, `CommandStepResolver.cs`, `ParameterReferenceScanner.cs`, and `src/GameBot.Domain/Services/ParameterValidationService.cs` to confirm the extension points in plan.md. Write the facts that change a later task in a short note in the commit message.

## Phase 2: Foundational (blocks all stories)

- [X] T002 Create `src/GameBot.Domain/Commands/ImageListRules.cs` with: `MatchAny` constant `any`, `MaxItems` constant 50, `Normalize(IEnumerable<string>)` (unique, first occurrence, drop empty), `TryValidateWritten(imageId, imageIds, match, fieldPrefix, out errors)` (exclusive `imageId` and `imageIds`, 1 to 50 items, no empty item, `match` only `any`). Add XML docs in STE.
- [X] T003 [P] Add unit tests for `ImageListRules` in `tests/unit/Commands/ImageListRulesTests.cs` (duplicates, order, empty item, more than 50 items, both fields set, `match` values).
- [X] T004 Add `ImageIds` (`IReadOnlyList<string>?`) and `Match` (`string?`) to `ImageVisibleStepCondition` in `src/GameBot.Domain/Commands/SequenceStepCondition.cs`, omitted from JSON when null. Add optional `ReferenceImageIds` to `src/GameBot.Domain/Commands/DetectionTarget.cs` (constructor parameter at the end, omitted from JSON when null). Update `src/GameBot.Domain/Commands/WaitForImageConfigMapper.cs` to map it both ways.
- [X] T005 [P] Add `imageIds` and `match` to the sequence condition DTO and `referenceImageIds` to `DetectionTargetDto`, and `matchedImageId` to the WaitForImage result DTOs, in `src/GameBot.Service/Models/Commands.cs` and `src/GameBot.Service/Models/SequenceStepContracts.cs`. Omit null members from JSON. Add XML docs in STE.
- [X] T006 Map the new members both ways: `ImageVisibleConditionContract` to `ImageVisibleStepCondition` (mapping near line 1546 of `src/GameBot.Service/Endpoints/SequencesEndpoints.cs`, and the reverse mapping in the same file), and `DetectionTargetDto` to `DetectionTarget` (in `src/GameBot.Service/Endpoints/CommandsEndpoints.cs` and `StepsEndpoints.cs`). Normalize lists with `ImageListRules.Normalize` on every write.
- [X] T007 [P] Add `bool AllowsList = false` to the `ParameterReference` record in `src/GameBot.Domain/Parameters/ParameterReferenceScanner.cs`. In the same file set it true for `{fieldPath}.imageId` of `imageVisible`, `waitForImage.detectionTarget.referenceImageId`, and the same overlay key in `FieldTemplates`. Scan each placeholder item of `ImageIds` and `ReferenceImageIds` as a reference with `AllowsList = false` and `DefeatsStaticCheck = true`.

**Checkpoint**: the new members round-trip through the domain and the DTOs; old files load unchanged.

## Phase 3: User Story 1 - Image-visible condition for a list (P1)

**Goal**: An `imageVisible` condition on a list, written or from a list placeholder, is true when any image is visible.  
**Independent test**: Run a condition with `{{quests}}` = `[A, B]` with B visible (true) and with none visible (false).

- [X] T008 [P] [US1] Add evaluator tests in `tests/unit/Services/SequenceStepConditionEvaluatorImageListTests.cs`: any item visible gives true; none gives false; empty list gives false; `negate` flips; first-match stops further calls; one-image condition unchanged; a list of one item behaves as one image; `Describe` text for a list.
- [X] T009 [P] [US1] Add resolver tests in `tests/unit/Parameters/SequenceStepConditionResolverImageListTests.cs`: whole placeholder of a multi-value parameter expands to `ImageIds` and the stored condition does not change; a list of one item; a single-value placeholder keeps the old result; a text such as `x_{{quests}}` keeps the feature 179 text rule; `used` holds the parameter; the resolved `Match` is `any`, and a condition with no `match` value is read as `any`.
- [X] T010 [US1] Implement the any-of branch in `src/GameBot.Domain/Services/SequenceStepConditionEvaluator.cs`: call the image evaluator for each item in order, return true at the first match, treat a missing image as no match, and make `Describe` show `imageIds=[...]`. Keep the one-image path unchanged.
- [X] T011 [US1] Implement the expansion in `src/GameBot.Domain/Parameters/SequenceStepConditionResolver.cs` (`TryResolveImage`): when `ImageId` is a whole placeholder that resolves to a multi-value parameter, set `ImageIds` from the split scope text and `Match` to `any`. Resolve placeholder items of a written `ImageIds` as single values. Fast path stays when no leaf has a placeholder.
- [X] T012 [US1] Add the written-list rules for `imageVisible` to `src/GameBot.Domain/Services/SequenceStepValidationService.cs` and `src/GameBot.Domain/Services/CompositeConditionValidator.cs` with `ImageListRules.TryValidateWritten`. In `src/GameBot.Service/Endpoints/SequencesEndpoints.cs` (`ValidatePerStepImageReferencesAsync` and `CollectImageConditions`) check the existence of each non-placeholder item of `ImageIds` like one `ImageId`. Update `src/GameBot.Service/Services/ImageReferenceExtractor.cs` to include the items.
- [X] T013 [P] [US1] Add a contract test in `tests/contract/Sequences/ImageListConditionContractTests.cs`: save and read back a condition with `imageIds` and `match`; `imageId` with `imageIds` is refused; `match` other than `any` is refused; a missing image in a written list is refused with the existing message; an old condition keeps its stored JSON form (no `imageIds` and no `match` in the output).

## Phase 4: User Story 2 - WaitForImage for a list (P1)

**Goal**: `WaitForImage` ends when any image of the list is visible and names the matched image.  
**Independent test**: Run a wait with `{{quests}}` = `[A, B]` with A shown late: the step ends with `image_detected` and `matchedImageId` = `A`.

- [X] T014 [P] [US2] Add resolver tests in `tests/unit/Parameters/CommandStepResolverImageListTests.cs`: the inline field and the overlay key expand a whole list placeholder to `ReferenceImageIds`, with `ReferenceImageId` set to the first item; a single-value placeholder is unchanged; `primitiveTap` and `ensureGameRunning` still give `list_in_single_field` for a list of two or more items.
- [X] T015 [US2] Implement the expansion in `src/GameBot.Domain/Parameters/CommandStepResolver.cs` (`TryDetection`): add an `allowList` argument that is true only for the `waitForImage` detection target. When true and the whole placeholder resolves to a multi-value parameter, build the `DetectionTarget` with the list. Keep `singleValueField: true` for the other callers.
- [X] T016 [US2] Add the written-list rules for `waitForImage.detectionTarget` (`referenceImageId` with `referenceImageIds`, item limits) in the validation in `src/GameBot.Service/Endpoints/CommandsEndpoints.cs` and `src/GameBot.Service/Endpoints/StepsEndpoints.cs` with `ImageListRules.TryValidateWritten`. Refuse `referenceImageIds` on `primitiveTap` and `ensureGameRunning`.
- [X] T017 [US2] Update `ExecuteWaitForImageStepAsync` and `PollForImageAsync` in `src/GameBot.Service/Services/CommandExecutor.cs`: load one template set for each item and skip items that do not load; if none loads, return the existing `image_unavailable` outcome with load status `missing`; in each pass detect the items in list order and stop at the first found; set `MatchedImageId` on the outcome; set `ReferenceImageId` to the first item. The one-image path keeps its results.
- [X] T018 [US2] Add `MatchedImageId` to the record `PrimitiveTapStepOutcome` in `src/GameBot.Service/Services/ICommandExecutor.cs` and copy it to the execution log attributes in `src/GameBot.Service/Services/ExecutionLog/ExecutionLogService.cs` and to the step result in `src/GameBot.Service/Services/SequenceExecution/SequenceExecutionService.cs`. Null is omitted.
- [X] T019 [P] [US2] Add an executor test in `tests/unit/Services/CommandExecutorWaitForImageListTests.cs` (use the existing WaitForImage executor tests as the pattern): the second item shows, so the step is `executed` with `matchedImageId`; none shows, so the result is the timeout outcome; a missing first item does not stop a match on the second; all items missing gives `image_unavailable`.
- [X] T020 [P] [US2] Add a contract test in `tests/contract/Commands/WaitForImageListContractTests.cs`: save and read back `referenceImageIds`; both fields set are refused; `primitiveTap` with `referenceImageIds` is refused; an old step is unchanged.

## Phase 5: User Story 3 - Placeholder only where a list is allowed (P2)

**Goal**: A multi-value parameter in a one-value position is refused at save with a clear error.  
**Independent test**: Save a `primitiveTap` step with `{{quests}}` as the image: 400 with the parameter name and the field path.

- [X] T021 [P] [US3] Add tests in `tests/unit/Services/ParameterValidationServiceImageListTests.cs`: multi-value parameter as a whole placeholder in `primitiveTap` image, `ensureGameRunning` image, a number field, and `targetId` gives the error; the same parameter in `imageVisible.imageId` and `waitForImage` image gives no error; a single-value parameter gives no error; a placeholder that is only part of a text is not refused by this rule; a placeholder item of `imageIds` that names a multi-value parameter gives the error.
- [X] T022 [US3] Implement the rule in `src/GameBot.Domain/Services/ParameterValidationService.cs`: for each reference with `AllowsList = false` whose text is a whole placeholder and whose name is a declared multi-value parameter, add a blocking error with the code `list_in_single_field` that names the parameter and the field path. Use the declarations of the scope of the entity (sequence, command, or queue entry).
- [X] T023 [US3] Make the sequence, command, and queue-template save endpoints show the new error in the 400 response, like the other parameter errors. Check `src/GameBot.Service/Endpoints/SequencesEndpoints.cs`, `CommandsEndpoints.cs`, and `QueueTemplatesEndpoints.cs`.
- [X] T024 [P] [US3] Add a contract test in `tests/contract/Sequences/ImageListPlaceholderPositionContractTests.cs` for the 400 response and message text (names the parameter and the field path) for a sequence and for a command.

## Phase 6: User Story 4 - Static check stays skipped (P2)

**Goal**: A list placeholder in an allowed field gives `static_check_skipped` and no image-exists error.  
**Independent test**: Save `imageVisible.imageId = {{quests}}`: 200 with the `static_check_skipped` warning.

- [X] T025 [P] [US4] Add a contract test in `tests/contract/Sequences/ImageListStaticCheckContractTests.cs`: a list placeholder in `imageVisible.imageId` and in the WaitForImage image key gives the `static_check_skipped` warning and no error; a written list with a placeholder item gives the warning for that item.
- [X] T026 [US4] Fix any place where the list-allowed fields do not give the warning or give an image-exists error. Check `src/GameBot.Domain/Services/ParameterValidationService.cs` and `src/GameBot.Service/Endpoints/SequencesEndpoints.cs`. If T025 passes with no change, mark this task done with no change.

## Phase 7: Documentation, schema, and UI

- [X] T027 Update the published schema in `src/GameBot.Service/Swagger/SwaggerConfig.cs`, `ParametrizedReferenceImageSchemaFilter.cs`, and `PrimitiveActionSchemaFilter.cs`: describe `imageIds`, `match`, `referenceImageIds`, `matchedImageId`, and the list placeholder rule, with an example. Add a Swagger test in `tests/contract/Swagger/` (use the existing Swagger contract test file) that asserts the new members exist.
- [X] T028 Check the web UI: search `src/web-ui/src/components/commands/WaitForImagePanel.tsx`, `src/web-ui/src/components/parameters/fieldTemplatePaths.ts`, and the sequence condition editor in `src/web-ui/src/pages/SequencesPage.tsx` for a filter that blocks a multi-value image parameter as the image key. If there is a filter, allow a multi-value image parameter in these two fields only, add a Vitest test in the matching `__tests__` folder, and keep phone support level full (docs/mobile-ui-principles.md). If there is no filter, make no UI change.
- [X] T029 [P] Update `docs/architecture.md` (API surface and domain model, refresh "Last reviewed") and add the new fields and rules to the parameter docs (find them with a search for `list_in_single_field` under `docs`).
- [X] T030 [P] Add a row for feature 180 to `specs/STATUS.md` and set the spec `Status` to Implemented when all tasks are done.
- [X] T031 [P] Add a `CHANGELOG.md` entry for feature 180 (issue #379). Bump the version as the CLAUDE.md rule says, before the PR.
- [X] T032 Run the full build and the Domain, Service, and web UI tests. Fix failures. Run the quickstart steps in `specs/180-any-of-image-list/quickstart.md` that the test projects can run.

## Dependencies

- T001 first. Phase 2 (T002 to T007) blocks all stories.
- US1 (T008 to T013) and US2 (T014 to T020) are independent after Phase 2.
- US3 (T021 to T024) needs T007. US4 (T025, T026) needs US1 and US2.
- Phase 7 comes last. T032 is the final task.

## Parallel examples

- After Phase 2: T008, T009, T013, T014, T019, T020 can run together (different files).
- T021, T025, T027, T029, T030, T031 can run together.

## Implementation strategy

MVP is US1 plus US2 (the consumer needs both the condition and the wait). Then US3 and US4. Commit after each phase.
