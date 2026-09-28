# Tasks: Let a parameter choose the reference image

**Input**: Design documents from `specs/114-parametrized-reference-image/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/api.md, quickstart.md

**Tests**: The constitution (Principle II) and the plan test table require tests. Test tasks come before the implementation tasks in each story. Each new test must fail before the change.

**Organization**: Tasks are in groups by user story. Each story can be tested alone.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependency on an incomplete task)
- **[Story]**: The user story of the task (US1, US2, US3)

---

## Phase 1: Setup

**Purpose**: Get a known baseline.

- [ ] T001 Build the solution with `dotnet build GameBot.sln -warnaserror` from the repo root, and run `tests/unit`, `tests/contract` and `tests/integration` on the base commit (with `DOTNET_ROLL_FORWARD=Major` on a host without the .NET 9 runtime). Record the tests that fail before any change, so that later runs can separate environment failures from real failures.

---

## Phase 2: Foundational (blocking prerequisites)

**Purpose**: Shared types and codes that all stories use. No behavior changes.

- [ ] T002 In `src/GameBot.Domain/Commands/CommandStep.cs`, add `CommandStepFieldPaths.SupportedImagePaths` (`IReadOnlySet<string>` with `primitiveTap.detectionTarget.referenceImageId` and `waitForImage.detectionTarget.referenceImageId`) and `IsImagePath(string path)`. Change `IsSupported(path)` to return true for a numeric key or an image key. Change the doc comments of `CommandStep.FieldTemplates` and `CommandStepFieldPaths` so that they do not say "numeric only" (research R-001, data-model.md "CommandStepFieldPaths"). Use STE in all comments.
- [ ] T003 [P] In `src/GameBot.Domain/Services/ParameterValidationService.cs`, add the constants `ParameterValidationCodes.InvalidFieldTemplateValue = "invalid_field_template_value"` and `ParameterValidationCodes.UnknownImageReference = "unknown_image_reference"` with STE doc comments.
- [ ] T004 [P] In `src/GameBot.Domain/Parameters/ParameterReferenceScanner.cs`, add the optional member `string? SourceText = null` to the record `ParameterReference` (the full text of the field that holds the reference). Set it in each place where the scanner makes a reference today (research R-005).

**Checkpoint**: The solution builds with `-warnaserror`. All baseline tests keep their result.

---

## Phase 3: User Story 1 - A tap finds the image that a parameter selects (Priority: P1) MVP

**Goal**: A command step accepts the `fieldTemplates` keys `primitiveTap.detectionTarget.referenceImageId` and `waitForImage.detectionTarget.referenceImageId`. At dispatch the overlay value replaces the inline image id. The inline placeholder keeps its behavior.

**Independent Test**: Save the command of contracts/api.md section 1. The save gives 201 with `static_check_skipped` warnings. Run it with `novaOption = option-b` and then `option-c`. The execution log shows the two resolved image ids and the `parameters` item.

### Tests for User Story 1

- [ ] T005 [P] [US1] Create `tests/unit/Parameters/ParameterValidationImageTests.cs` with the key-set and value-rule tests: `CommandStepFieldPaths.IsSupported` and `IsImagePath` for the two image keys; the numeric keys stay supported and are not image keys; `ValidateFieldTemplateKeys` accepts `{{novaOption}}` on an image key; it gives `invalid_field_template_value` for `nova-{{option}}`, `option-a` and `{{a}}{{b}}`, and the message names the step order and the key; an unknown key (`ensureGameRunning.readinessImage.referenceImageId`) gives `unknown_field_template_path` with the text "is not a parametrizable field." (no "numeric"); a numeric key with the value `12` still passes the save (FR-013).
- [ ] T006 [P] [US1] Extend `tests/unit/Parameters/CommandStepResolverTests.cs`: (a) a `PrimitiveTap` step with inline `option-a` and the overlay `{{novaOption}}` resolves to `option-b` when the scope has `novaOption = option-b`, and `used` holds `novaOption`; (b) the same for `WaitForImage`; (c) with the overlay key present, an inline placeholder with no value in scope does not fail the step; (d) an overlay with a name that has no value gives a `ParameterResolutionError` with field path `primitiveTap.detectionTarget.referenceImageId`; (e) a malformed stored overlay value (`nova-{{x}}`) gives the reason `unresolved`, not `not_a_number`; (f) an overlay that resolves to an empty text fails as the current empty-id check does; (g) an inline placeholder with no overlay key behaves as before (FR-004).
- [ ] T007 [P] [US1] Extend `tests/unit/Parameters/ParameterReferenceScannerTests.cs`: a `fieldTemplates` image key gives a reference with `DefeatsStaticCheck: true`, the key as field path, and `SourceText` equal to the value; a numeric `fieldTemplates` key keeps `DefeatsStaticCheck: false`.
- [ ] T008 [P] [US1] Create `tests/contract/Sequences/ParametrizedReferenceImageContractTests.cs` with the command part of contracts/api.md section 1: `POST /api/commands` with both image keys gives 201, `fieldTemplates` comes back as sent, and `warnings` has one `static_check_skipped` item for each image key; section 1a gives 400 `invalid_field_template_value`; section 1b gives 400 `unknown_field_template_path` with the new text; section 1c gives 400 `unresolvable_parameter_reference`; `PATCH /api/commands/{id}` applies the same rules; a numeric key request gives the same result as before.
- [ ] T009 [P] [US1] Create `tests/integration/Commands/ParametrizedReferenceImageIntegrationTests.cs`: save the command of contracts/api.md section 1 and run it two times with `novaOption = option-b` and `option-c` (use the pattern of `PrimitiveTapExecutionIntegrationTests.cs`). On a host with no device, the step gives `image_unavailable`; assert that the step detail names the resolved id each time and that the execution log step detail has the `parameters` item with `novaOption` and its scope layer (FR-011). Add one case with the stored inline form `"referenceImageId": "{{novaOption}}"` and no overlay, which resolves as before.

### Implementation for User Story 1

- [ ] T010 [US1] In `ParameterValidationService.ValidateFieldTemplateKeys` in `src/GameBot.Domain/Services/ParameterValidationService.cs`, change the `unknown_field_template_path` message to "Step {order}: '{path}' is not a parametrizable field." (FR-012). For an image key, require that the value is one whole placeholder (use `TemplateSubstitutor`); otherwise add an error with `invalid_field_template_value`, the message "Step {order}: the value of '{path}' must be one whole placeholder, for example {{name}}.", and the key as field path (FR-002, research R-002). Do not add a value check for numeric keys (FR-013). Depends on T002, T003.
- [ ] T011 [US1] In `src/GameBot.Domain/Parameters/ParameterReferenceScanner.cs`, set `defeatsStaticCheck: CommandStepFieldPaths.IsImagePath(path)` and `SourceText` in the `fieldTemplates` loop of `ScanCommandStep`, so that `AddReferenceIssues` gives `static_check_skipped` for the image keys (FR-006, research R-005). Depends on T002, T004.
- [ ] T012 [US1] In `src/GameBot.Domain/Parameters/CommandStepResolver.cs`, change `TryDetection`: when `FieldTemplates` has the key `{prefix}.referenceImageId`, resolve that value with `TryOverlayValue` and do not resolve the inline value; otherwise resolve the inline value with `TryText` as today. Add a text-field flag to `TryOverlayValue`, so that a malformed stored value gives the reason `unresolved` and not `not_a_number`. Keep the empty-id check after both paths. Add the used value to `used` (FR-003, FR-004, FR-008, FR-011, research R-003). Depends on T002.
- [ ] T013 [P] [US1] Change the comment on `CommandStepDto.FieldTemplates` in `src/GameBot.Service/Models/Commands.cs` and on `fieldTemplates` in `src/web-ui/src/services/commands.ts`, so that they list the two image keys and do not say "numeric fields only". Use STE.

**Checkpoint**: T005 to T009 pass. All baseline tests keep their result (SC-005).

---

## Phase 4: User Story 2 - A guard checks the image that a parameter selects (Priority: P2)

**Goal**: An `imageVisible.imageId` with an inline placeholder saves in each condition position, and at run time it resolves against the scope of the step.

**Independent Test**: Save the sequence of contracts/api.md section 2. The save gives 201 with a `warnings` member. Run it with an image id that is visible and with one that is not. The step runs in the first case and is skipped in the second case. The unit tests of `SequenceRunnerConditionScopeTests` show the resolved id in each position.

### Tests for User Story 2

- [ ] T014 [P] [US2] Extend `tests/unit/Parameters/ParameterReferenceScannerTests.cs` with condition scans: an `imageVisible` leaf with `{{novaOption}}` in `condition`, `if.condition`, a while `loop.condition`, a repeat-until `loop.condition` and `breakCondition` gives a reference with the field paths of data-model.md and `DefeatsStaticCheck: true`; a leaf in a composite gives a path such as `condition.children[1].imageId`; `InsideLoop` is true for a loop condition and a break condition and false for a step condition and an `If` condition; a literal `imageId` gives no reference.
- [ ] T015 [P] [US2] Create `tests/unit/Parameters/SequenceStepConditionResolverTests.cs`: a tree with no placeholder comes back as the same instance with empty `used`; a leaf `{{novaOption}}` resolves to `option-b` and keeps `MinSimilarity` and `Negate`; a leaf `nova-{{option}}` resolves with the surrounding text; a composite (`all`, `any`, `none`) resolves each child and keeps `Negate`; other leaf types come back as the same instance; an unknown name gives `false` and an error with reason `unresolved` and the composite field path; an empty result gives `false` with reason `unresolved`.
- [ ] T016 [P] [US2] Create `tests/unit/Sequences/SequenceRunnerConditionScopeTests.cs` (use the fakes of `PerStepConditionRunnerTests.cs` and `CompositeConditionRunnerTests.cs`): the action-step guard, the loop-step guard, the `If` condition, the while condition, the repeat-until condition and the break condition each send the resolved image id to the image evaluator (the loop and break conditions use the scope of each iteration); an unresolved name fails the step with the message `Step '<stepKey>': parameter 'novaOption' used by field 'condition.imageId' could not be resolved from any scope.` and sends no device input; a break condition with an unresolved name gives "No break" with the error detail in the log (feature 066 rule); the log description of the condition shows the resolved id, for example `imageVisible(imageId=option-b, minSimilarity=default)`.
- [ ] T017 [P] [US2] Add the sequence part of contracts/api.md section 2 to `tests/contract/Sequences/ParametrizedReferenceImageContractTests.cs`: `POST /api/sequences` with a step condition `imageId` `{{novaOption}}` and the declaration gives 201 with a `warnings` member that has `static_check_skipped` and `fieldPath` `condition.imageId`; `PUT` and `PATCH` give 200 with the same `warnings`; `if.condition`, `loop.condition`, `breakCondition` and a composite child save with the matching field paths; section 2a (no declaration) gives 400 `unresolvable_parameter_reference`; section 2b (a literal id with no image) keeps the current existence error; a sequence response with no warnings has no `warnings` member. Depends on T008 (same file).

### Implementation for User Story 2

- [ ] T018 [US2] In `ScanSequenceSteps` in `src/GameBot.Domain/Parameters/ParameterReferenceScanner.cs`, scan each `imageVisible` leaf, also inside composites, in `step.Condition`, `step.If.Condition`, `WhileLoopConfig.Condition`, `RepeatUntilLoopConfig.Condition` and `step.BreakCondition`, with the field paths and `insideLoop` rules of research R-005, `DefeatsStaticCheck: true` and `SourceText` (FR-005, FR-006). Depends on T004, T011.
- [ ] T019 [US2] In `src/GameBot.Service/Endpoints/SequencesEndpoints.cs`, change `ValidatePerStepImageReferencesAsync` to skip an `imageId` when `TemplateSubstitutor.ContainsPlaceholder(imageId)` is true (research R-006). Add an optional `warnings` argument to `ToSequenceResponseAsync` and pass `parameterCheck.Warnings` from the create, update and patch routes; map them with `ParameterDtoMapper.ToResponseWarnings`, and leave out the member when the list is empty (research R-009).
- [ ] T020 [P] [US2] Create `src/GameBot.Domain/Parameters/SequenceStepConditionResolver.cs`, a static class with `TryResolve(SequenceStepCondition condition, ParameterScope scope, out SequenceStepCondition resolved, out ParameterResolutionError? error, out IReadOnlyList<ResolvedParameter> used)` as in data-model.md: fast path for a tree with no placeholder, substitution of each `imageVisible.ImageId` with `TemplateSubstitutor.TrySubstitute`, new composites with resolved children, reason `unresolved` for an unknown name or an empty result (FR-007, research R-007). Add STE doc comments.
- [ ] T021 [P] [US2] In `src/GameBot.Domain/Services/SequenceStepConditionEvaluator.cs`, add `ConditionEvaluationFailureKind.ParameterUnresolved` with an STE doc comment (`Detail` holds the parameter name).
- [ ] T022 [US2] In `src/GameBot.Domain/Services/SequenceRunner.cs`, add a `ParameterScope scope` argument to `EvaluateStepGuardAsync` and `EvaluateLoopConditionAsync`, and call `SequenceStepConditionResolver.TryResolve` before the evaluation; on failure throw `ConditionEvaluationException` with `ParameterUnresolved` and the message `ParameterResolutionError.ToMessage(stepKey)`. Pass `scope` or `iterScope` at the six call sites (action-step guard, loop-step guard, while, repeat-until, `If`, break). Map the new kind in `FailGuard` to the resolution message. Compute the log description of the `If` and break conditions from the resolved tree (FR-005, FR-007, FR-008, FR-009, research R-007). Depends on T020, T021.

**Checkpoint**: T014 to T017 pass. US1 tests still pass.

---

## Phase 5: User Story 3 - An incorrect image id fails early and clearly (Priority: P2)

**Goal**: A queue template save rejects a known value that goes to an image field and names no image (`unknown_image_reference`). At run time, an unknown resolved id gives `image_unavailable`, and an unresolved name gives the current resolution error with no device input.

**Independent Test**: Save the template of contracts/api.md section 4 with `novaOption = no-such-image`. The save gives 400 `unknown_image_reference` and stores nothing. With `option-b` (an image that exists) the save succeeds.

### Tests for User Story 3

- [ ] T023 [P] [US3] Add `FindImageValueCandidates` tests to `tests/unit/Parameters/ParameterValidationImageTests.cs`: an entry value that goes to `condition.imageId` gives one candidate `(EntryIndex, ParameterName, ImageId, FieldPath)`; with no entry value, the sequence default and then the first command default in reach order are used; a queue built-in or a name with no known value gives no candidate; a value that goes to an image field and to a text field gives a candidate; the same image id from two fields gives no duplicate; `fieldTemplates` image keys, inline command image fields and `detection.referenceImageId` of a reachable command give candidates; numeric fields give no candidate. Depends on T005 (same file).
- [ ] T024 [P] [US3] Create `tests/contract/QueueTemplates/TemplateImageReferenceContractTests.cs` for contracts/api.md section 4: an entry value with no image gives 400 `unknown_image_reference` with the message "Entry 0: parameter 'novaOption' gives the image id 'no-such-image' to field 'condition.imageId', but no image has that id." and `GET` shows that the service stored nothing; a default with no image gives the same error; a disabled entry is checked; an image that exists gives success; a value that goes through a reachable command `fieldTemplates` image key is checked; a queue built-in is not checked; an entry whose sequence does not exist is not checked.
- [ ] T025 [P] [US3] Add the run-time failure cases to `tests/integration/Commands/ParametrizedReferenceImageIntegrationTests.cs`: a run where `novaOption` resolves to an id with no image gives `image_unavailable` and the log records the resolved value (FR-009); a run with no value for `novaOption` gives `skipped_parameter_unresolved` with the message of contracts/api.md section 3 and no device input (FR-008, SC-004). Depends on T009 (same file).

### Implementation for User Story 3

- [ ] T026 [US3] In `src/GameBot.Domain/Services/ParameterValidationService.cs`, add the record `ImageValueCandidate(int EntryIndex, string ParameterName, string ImageId, string FieldPath)` and the method `FindImageValueCandidates(entry, entryIndex, sequence, reachableCommands)` as in research R-008 and data-model.md: scan the sequence and each reachable command, keep references with `DefeatsStaticCheck: true`, substitute known values (entry value, then first declaration default) in `SourceText`, and return distinct candidates. Depends on T004, T011, T018.
- [ ] T027 [US3] In `src/GameBot.Service/Endpoints/QueuesEndpoints.cs`, add the internal method `CollectReachableCommandsAsync(sequence, commands)` that returns the reachable `Command` objects, and change `CollectReachableDeclarationsAsync` to use it. The result of `CollectReachableDeclarationsAsync` must not change.
- [ ] T028 [US3] In `src/GameBot.Service/Endpoints/QueueTemplatesEndpoints.cs`, add `ICommandRepository` and `IImageRepository` to the save route. After the value name check and before the save, for each entry (also a disabled entry) with an existing sequence, call `CollectReachableCommandsAsync` and `FindImageValueCandidates`, check each distinct image id one time with `IImageRepository.ExistsAsync`, and for each missing id add an issue with `unknown_image_reference` and the message of contracts/api.md section 4. Return 400 with `ParameterDtoMapper.ToErrorBody` and do not save (FR-010). Depends on T026, T027.

**Checkpoint**: T023 to T025 pass. US1 and US2 tests still pass.

---

## Phase 6: Polish & cross-cutting concerns

**Purpose**: API documentation, living docs, and the final checks (FR-014, Principle V).

- [ ] T029 [P] Create `tests/contract/Sequences/ParametrizedReferenceImageOpenApiTests.cs` (pattern of `tests/contract/Sequences/LastRunConditionOpenApiTests.cs`): `/swagger/v1/swagger.json` describes `CommandStepDto.fieldTemplates` with the two image keys, the whole-placeholder rule and the codes `unknown_field_template_path` and `invalid_field_template_value`; `ImageVisibleConditionContract.imageId` tells the placeholder and `static_check_skipped`; the template entry `parameterValues` tells `unknown_image_reference`.
- [ ] T030 Create `src/GameBot.Service/Swagger/ParametrizedReferenceImageSchemaFilter.cs` with the descriptions of T029 in STE (research R-010), and register it in `src/GameBot.Service/GameBotServiceSetup.cs` next to `PrimitiveTapHoldSchemaFilter`. Depends on T029.
- [ ] T031 [P] Update `specs/openapi.json`: add the `fieldTemplates` member to `CommandStepDto`, and add the descriptions of `fieldTemplates`, `ImageVisibleCondition.imageId`, `ImageVisibleConditionContract.imageId`, the template save `parameterValues`, and the optional `warnings` member of the sequence write responses (contracts/api.md section 5).
- [ ] T032 [P] Update `docs/architecture.md`: in the parameters section and the feature 078 API list, tell the two new `fieldTemplates` image keys, the placeholder in `imageVisible.imageId` in each condition position, the codes `invalid_field_template_value` and `unknown_image_reference`, and the sequence `warnings` member. Change the "Last reviewed" date. Use STE.
- [ ] T033 [P] Add an entry for feature 114 (closes #243) to `CHANGELOG.md`, add row `| 114 | Let a parameter choose the reference image | Implemented |` to `specs/STATUS.md`, and set the `Status` line of `specs/114-parametrized-reference-image/spec.md` to `Implemented`. Use STE.
- [ ] T034 Build with `dotnet build GameBot.sln -warnaserror` and run `tests/unit`, `tests/contract` and `tests/integration`. Compare with the baseline of T001. Fix each new failure. Confirm that the current numeric `fieldTemplates` and inline placeholder tests pass with no change (SC-005).
- [ ] T035 Follow `specs/114-parametrized-reference-image/quickstart.md` against a local service and confirm each response and each error code that it shows.

---

## Dependencies & execution order

### Phase dependencies

- **Setup (Phase 1)**: no dependencies.
- **Foundational (Phase 2)**: after Setup. It blocks all user stories.
- **US1 (Phase 3)**: after Phase 2. No dependency on other stories.
- **US2 (Phase 4)**: after Phase 2. T018 extends the scanner change of T011 (US1), so do US1 T011 first. The contract test T017 shares a file with T008.
- **US3 (Phase 5)**: after Phase 2. T026 uses the image references of T011 (US1) and T018 (US2), so it needs both scanner changes. T023 and T025 share files with T005 and T009.
- **Polish (Phase 6)**: after the stories that you deliver.

### User story order

- US1 (P1) → US2 (P2) → US3 (P2). US3 checks the image fields that US1 and US2 make parametrizable.

### Within each story

- Write the tests first. Make sure that they fail.
- Domain types and scanner before resolvers, resolvers before the runner, domain before endpoints.

### Parallel opportunities

- Phase 2: T003 and T004 in parallel (T002 is in a different file and can also run in parallel).
- US1: T005, T006, T007, T008, T009 in parallel; T013 in parallel with T010 to T012.
- US2: T014, T015, T016, T017 in parallel; T020 and T021 in parallel, then T022.
- US3: T023, T024, T025 in parallel; T026 and T027 in parallel, then T028.
- Polish: T029, T031, T032, T033 in parallel.

---

## Parallel example: User Story 1

```text
Task: "Create tests/unit/Parameters/ParameterValidationImageTests.cs (key set, value rule)"
Task: "Extend tests/unit/Parameters/CommandStepResolverTests.cs (overlay image value)"
Task: "Extend tests/unit/Parameters/ParameterReferenceScannerTests.cs (image keys)"
Task: "Create tests/contract/Sequences/ParametrizedReferenceImageContractTests.cs (command part)"
Task: "Create tests/integration/Commands/ParametrizedReferenceImageIntegrationTests.cs"
```

## Parallel example: User Story 2

```text
Task: "Create src/GameBot.Domain/Parameters/SequenceStepConditionResolver.cs"
Task: "Add ConditionEvaluationFailureKind.ParameterUnresolved in src/GameBot.Domain/Services/SequenceStepConditionEvaluator.cs"
```

## Parallel example: User Story 3

```text
Task: "Add ImageValueCandidate and FindImageValueCandidates in src/GameBot.Domain/Services/ParameterValidationService.cs"
Task: "Add CollectReachableCommandsAsync in src/GameBot.Service/Endpoints/QueuesEndpoints.cs"
```

---

## Implementation strategy

### MVP first (User Story 1 only)

1. Do Phase 1 and Phase 2.
2. Do Phase 3 (US1).
3. Stop and validate: the probes of the issue with the new keys get a success response (SC-002), and a run uses the resolved image id.

### Incremental delivery

1. Setup + Foundational: the foundation is ready.
2. US1: parametrized tap and wait (MVP).
3. US2: parametrized guards.
4. US3: save check of template values and clear run-time failures.
5. Polish: API docs, living docs, full test run.

---

## Notes

- [P] tasks change different files and have no dependency on an incomplete task.
- The stored JSON does not change. No migration task is necessary.
- Do not change the behavior of the numeric `fieldTemplates` keys (FR-013).
- All new text, messages and comments must obey STE (Principle VI).
