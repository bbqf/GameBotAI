---

description: "Task list for feature 165: sequence parameters in ocrText.pattern and ocrText.expected"
---

# Tasks: Sequence parameters in ocrText.pattern and ocrText.expected

**Input**: Design documents from `/specs/165-ocr-pattern-params/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/ocr-text-parameters.md, quickstart.md

**Tests**: Included. The plan (Constitution Principle II) requires unit, integration, and contract tests. Write each test task before its implementation task. Make sure that the test fails first.

**Organization**: The tasks are in groups for each user story. You can do and test each story independently after Phase 2.

**Language**: All new text (code comments, error messages, docs) MUST obey ASD-STE100 Simplified Technical English (Constitution Principle VI).

**Scope**: The gamepack check does not change (FR-011, plan "Out of scope: gamepacks"). Do not change `GamepackValidator`.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel. The task is in a different file and has no dependency on an incomplete task.
- **[Story]**: The user story of the task (US1, US2, US3, US4).
- All paths are relative to the repository root `C:\src\GameBot`.

---

## Phase 1: Setup (shared start state)

**Purpose**: Make sure that the start state is good.

- [ ] T001 Do a check of the start state of `GameBot.sln`:
  - Run `dotnet build "C:\src\GameBot\GameBot.sln" -c Debug`.
  - Run `dotnet test "C:\src\GameBot\GameBot.sln" -c Debug`.
  - Record the result.
  - If a build or a test fails, stop. Fix it first (Constitution, NON-NEGOTIABLE build and test gate).

---

## Phase 2: Prerequisites that block other work

**Purpose**: Shared types and helpers that all user stories use.

**CRITICAL**: Do not start a user story before this phase is complete.

- [ ] T002 [P] Add the new reason in `src/GameBot.Domain/Parameters/ParameterResolutionError.cs` (plan D-002):
  - Add the constant `InvalidPattern = "invalid_pattern"` to `ParameterResolutionReasons`.
  - Extend `ToMessage` for the new reason.
  - Use this text: `Step '<label>': field '<path>' resolved to the pattern '<pattern>', which is not a valid regular expression. Change the value of '<name>' or the pattern.`
  - `OffendingValue` holds the resolved pattern.
- [ ] T003 [P] Add the member `ParameterValueInvalid` to `ConditionEvaluationFailureKind` in `src/GameBot.Domain/Services/SequenceStepConditionEvaluator.cs` (data-model, research R-004):
  - Add an STE XML comment.
  - The comment says: a placeholder resolved, but the result is not usable. `Detail` holds the parameter name.
- [ ] T004 [P] Add unit tests to `tests/unit/Sequences/OcrTextConditionRulesTests.cs` (FR-014):
  - `TryValidatePattern` gives true for `(Gathering.*){3}`.
  - `TryValidatePattern` gives false and a problem text for `(abc`.
  - `Validate` accepts the pattern `[{{chars}}]` (placeholder, no compile).
  - `Validate` accepts the pattern `(?s)(Gathering.*){{{n}}}`.
  - `Validate` rejects the pattern `(abc` with no placeholder.
  - The message of that error is the existing message `ocrText pattern is not a valid regular expression: <reason>`.
  - The rule "exactly one of expected and pattern" does not change.
- [ ] T005 Add the pattern check to `src/GameBot.Domain/Commands/OcrTextConditionRules.cs` (plan D-005, research R-007). (depends on T004)
  - Add `public static bool TryValidatePattern(string pattern, out string? problem)`.
  - Compile with `RegexOptions.CultureInvariant` and `PatternTimeout`. These are the same options as `Matches`.
  - Change `Validate`. It calls `TryValidatePattern` only when `TemplateSubstitutor.ContainsPlaceholder(Pattern)` is false.
  - Make T004 pass.
- [ ] T006 Add the shared private method `TryResolveText(string text, string fieldPath, ...)` to `src/GameBot.Domain/Parameters/SequenceStepConditionResolver.cs` (plan D-001):
  - The method calls `TrySubstitute`.
  - The method does the empty or white-space check of the full result.
  - The method adds each key to `used`.
  - `TryResolveImage` calls `TryResolveText` for these steps. The `imageVisible.imageId` results stay the same (FR-016).
  - Run the existing tests in `tests/unit/Parameters/SequenceStepConditionResolverTests.cs`. Make sure that they pass.
- [ ] T007 Change the failure kind map in `src/GameBot.Domain/Services/SequenceRunner.cs` (plan D-002, FR-008). (depends on T002, T003)
  - First, create `tests/unit/Sequences/SequenceRunnerConditionFailureKindTests.cs`. Make sure that the tests fail.
  - Test: `ParameterResolutionReasons.InvalidPattern` maps to `ConditionEvaluationFailureKind.ParameterValueInvalid`.
  - Test: `ParameterResolutionReasons.Unresolved` maps to `ParameterUnresolved`, as now.
  - Test: `SequenceRunner.ResolveCondition` with an unknown name in `imageVisible.imageId` throws with the kind `ParameterUnresolved`.
  - Put the map in a new `internal static` method, for example `ToFailureKind(string reason)`. `ResolveCondition` calls it.
  - Map `InvalidPattern` to `ParameterValueInvalid`. Map all other reasons to `ParameterUnresolved`, as now.
  - Update the XML comment of `ResolveCondition`. It names both kinds and the `ocrText` fields.
  - In `EvaluateStepGuardAsync`, change the kind switch. It shows `ex.Message` for the two kinds.
  - Make the tests pass.

**Checkpoint**: The build passes. All existing tests pass. User story work can start.

---

## Phase 3: User Story 1 - Parameter in an ocrText pattern (Priority: P1), first usable increment

**Goal**: A `{{name}}` placeholder in `ocrText.pattern` resolves to the parameter value before the OCR check. This occurs in each condition position.

**Independent Test**: Run a sequence with `pattern` `(?s)(Gathering.*){{{marchTarget}}}`. Give an OCR text with two "Gathering" lines. With `marchTarget` = `2`, the condition is true. With `marchTarget` = `3`, it is false.

### Tests for User Story 1

- [ ] T008 [US1] Add `ocrText` pattern unit tests to `tests/unit/Parameters/SequenceStepConditionResolverTests.cs`:
  - The full field `{{countPattern}}` resolves (scenario 1).
  - The inline pattern `(?s)(Gathering.*){{{marchTarget}}}` with `4` gives `(?s)(Gathering.*){4}` (scenario 2).
  - Two placeholders in one pattern resolve.
  - The quantifiers `{3}`, `{2,5}`, and `{2,}` stay unchanged (FR-004, SC-003).
  - A pattern with no placeholder gives the same instance (scenario 3, SC-006 fast path). Assert reference equality. This also applies to a composite with no placeholder in its leaves.
  - A leaf at `condition.children[1]` inside `all`, `any`, and `none` resolves (scenario 4).
  - A leaf at depth 2 resolves.
  - The value goes in with no regex escape (FR-015).
  - `Region`, `Parser`, and `Negate` are copied. They are not resolved (FR-016).
  - After the resolve, the input `ocrText` condition still holds its `{{...}}` text (scenario 5, FR-006).
  - After the resolve, the input composite condition and its leaves still hold their `{{...}}` text (FR-006).
  - An unknown name fails with the reason `unresolved` and the field path `condition.pattern`.
  - The pattern `(Gathering.*){{3}}` with no parameter `3` fails with the reason `unresolved` and the name `3` (scenario 7, FR-007).
  - The value `" "` for the full field fails with the reason `unresolved`.
  - An empty value inside a longer pattern does not fail.
  - The value `(` for `{{countPattern}}` fails with the reason `invalid_pattern`.
  - For that failure, the field path is `condition.pattern` and `OffendingValue` is `(`.
  - The `imageVisible.imageId` cases do not change.
- [ ] T009 [P] [US1] Create `tests/integration/Sequences/OcrTextConditionParameterRunTests.cs`. Use `tests/integration/Sequences/OcrTextRunKit.cs`. Add these run tests:
  - A step condition with `marchTarget` = `2` and an OCR text with two "Gathering" lines gives true.
  - The same step with `marchTarget` = `3` gives false.
  - The same placeholder resolves in an `If` condition (FR-005).
  - The same placeholder resolves in a loop condition and in a break condition (FR-005).
  - `{{iteration}}` in a loop condition pattern resolves.
  - The value `(` fails the step with the `invalid_pattern` message.
  - The value `" "` for `{{countPattern}}` fails the step with the unresolved-parameter message.
  - An unknown name fails the step with the unresolved-parameter message.
  - `(Gathering.*){{3}}` with no parameter `3` fails the step. The message names `3`. The OCR check does not get the text `{{3}}` (scenario 7).
  - A break condition with an unknown name gives "No break". The step does not fail. The run log records the error, and the loop continues (FR-008 exception).
  - A break condition with the value `(` for its pattern gives "No break" in the same way.
  - A stored sequence has a placeholder in `pattern` and in `expected`. After a run, the stored sequence still holds the same `{{...}}` text (scenario 6, FR-006).
  - A step-through with the same values resolves in the same way (FR-010).
  - A step-through with a bad value fails with the same message as a run (FR-010).

### Implementation for User Story 1

- [ ] T010 [US1] Resolve `ocrText.pattern` in `src/GameBot.Domain/Parameters/SequenceStepConditionResolver.cs` (plan D-001). (depends on T005, T006, T008)
  - Add an `OcrTextStepCondition` case to `HasPlaceholder`. It is true when `Pattern` or `Expected` holds a placeholder.
  - Add an `OcrTextStepCondition` case to `TryResolveNode`. It calls the new method `TryResolveOcrText`.
  - In `TryResolveOcrText`, resolve `Pattern` with `TryResolveText` and the field path `{prefix}.pattern`.
  - Then call `OcrTextConditionRules.TryValidatePattern` on the resolved pattern.
  - On failure, give `ParameterResolutionError(firstKey, "{prefix}.pattern", ParameterResolutionReasons.InvalidPattern, resolvedPattern)`.
  - Copy a field with no placeholder as it is.
  - Return a new `OcrTextStepCondition` with the same `Region`, `Parser`, and `Negate`.
  - Do not set a property of the input condition (FR-006).
  - Keep each method under 50 lines.
  - Make the T008 pattern cases pass.
- [ ] T011 [US1] Run the T009 tests. Fix a failure only if a test shows a gap (research R-001, R-005). (depends on T007, T009, T010)
  - Research R-001 and R-005 say that no other call site needs a change.
  - Make fixes only in `src/GameBot.Domain/Services/SequenceRunner.cs` or `src/GameBot.Domain/Parameters/SequenceStepConditionResolver.cs`.

**Checkpoint**: User Story 1 works and its tests pass. This is the first usable increment.

---

## Phase 4: User Story 2 - Parameter in an ocrText expected value (Priority: P2)

**Goal**: A `{{name}}` placeholder in `ocrText.expected` resolves before the compare.

**Independent Test**: Run a sequence with `expected` `Level {{level}}` and `level` = `5`. The OCR text `Level 5` gives true. `Level 6` gives false.

### Tests for User Story 2

- [ ] T012 [US2] Add `ocrText` expected unit tests to `tests/unit/Parameters/SequenceStepConditionResolverTests.cs`. (depends on T008, same file)
  - The full field `{{accountName}}` with `Alpha` gives `Alpha`.
  - The inline field `Level {{level}}` with `5` gives `Level 5`.
  - `Level {{suffix}}` with an empty `suffix` does not fail.
  - A full field that resolves to `" "` fails with the reason `unresolved` and the field path `condition.expected`.
  - An unknown name inside a composite fails with the field path `condition.children[0].expected`.
  - The resolver does not compile `expected` as a regular expression. The value `(` is valid text.
  - After the resolve, the input condition still holds its `{{...}}` text in `expected` (FR-006).
- [ ] T013 [US2] Add run tests to `tests/integration/Sequences/OcrTextConditionParameterRunTests.cs`. (depends on T009, same file)
  - `expected` `{{accountName}}` with `Alpha` and the OCR text `Alpha` gives true.
  - `expected` `Level {{level}}` with `5` and the OCR text `Level 6` gives false.

### Implementation for User Story 2

- [ ] T014 [US2] Resolve `Expected` in `TryResolveOcrText` in `src/GameBot.Domain/Parameters/SequenceStepConditionResolver.cs`. (depends on T010, T012)
  - Use `TryResolveText` and the field path `{prefix}.expected`.
  - Do not compile `Expected`.
  - Make T012 and T013 pass.

**Checkpoint**: User Stories 1 and 2 work independently.

---

## Phase 5: User Story 3 - The run log shows the resolved value (Priority: P2)

**Goal**: The run log line and the condition description of an `ocrText` condition show the resolved `pattern` or `expected` value. They do not show the placeholder text.

**Independent Test**: Run the sequence of User Story 1 with `marchTarget` = `4`. The log line of the condition shows `(?s)(Gathering.*){4}`. It does not show `{{marchTarget}}`.

### Tests for User Story 3

- [ ] T015 [US3] Add run log tests to `tests/integration/Sequences/OcrTextConditionParameterRunTests.cs` (FR-009, FR-010, SC-005). (depends on T013, same file)
  - Use the run log asserts of `tests/integration/Sequences/OcrTextConditionRunLogTests.cs` as a model.
  - For a step condition, the condition description shows the resolved `pattern`.
  - For the same step, the `ocrText:` line (`OcrTextEvaluationRecord`) shows the resolved `pattern`.
  - Do the same two checks for an `If` condition and for a break condition.
  - A step with `expected` `Level {{level}}` and `level` = `5` shows `Level 5`.
  - No log text holds `{{`.
  - A step-through shows the same resolved description.

### Implementation for User Story 3

- [ ] T016 [US3] Run T015. Plan D-003 expects no new code. (depends on T014, T015)
  - The callers give the resolved condition to `Describe` and to the evaluator.
  - If a test fails, find the call site that gives the stored condition in place of the resolved condition.
  - Fix it in `src/GameBot.Domain/Services/SequenceRunner.cs` or `src/GameBot.Domain/Services/SequenceStepConditionEvaluator.cs`.

**Checkpoint**: User Stories 1, 2, and 3 work.

---

## Phase 6: User Story 4 - A save dry run rejects an unknown parameter name (Priority: P2)

**Goal**: The save validation and the save dry run reject an unknown name in `ocrText.pattern` and `ocrText.expected` with `unresolvable_parameter_reference`. When each placeholder of a `pattern` has a sequence default, a bad resolved pattern gives `invalid_resolved_pattern`. A dry-run execution uses the supplied values and fails the step with the run-time message (plan D-006).

**Independent Test**: Dry-run a save of a sequence with `pattern` `{{unknownName}}` and no parameter `unknownName`. The result is `400` with `unresolvable_parameter_reference`, `parameterName` `unknownName`, and `fieldPath` `condition.pattern`.

### Tests for User Story 4

- [ ] T017 [P] [US4] Add `ocrText` cases to `tests/unit/Parameters/ParameterReferenceScannerTests.cs` (FR-013, FR-016):
  - Each key in `pattern` gives a reference with the field path `condition.pattern` and `IsRegexPattern` true.
  - Each key in `expected` gives a reference with the field path `condition.expected` and `IsRegexPattern` false.
  - `DefeatsStaticCheck` is false for the two fields.
  - The prefixes `if.condition`, `loop.condition`, and `breakCondition` are correct.
  - A leaf at `condition.children[1]` gives the field path `condition.children[1].pattern`.
  - `InsideLoop` is true for the loop condition and the break condition.
  - `SourceText` holds the field text.
  - `{{3}}` in `pattern` gives a reference with the name `3`.
  - `region` and `parser` give no reference.
- [ ] T018 [P] [US4] Create `tests/unit/Parameters/ParameterValidationOcrPatternTests.cs` for `ParameterValidationService.ValidateSequence` (FR-011, FR-012, FR-013). Add these cases:
  - An unknown name in `pattern` gives `unresolvable_parameter_reference` with the correct field path.
  - An unknown name in `expected` gives the same error. The field path ends in `.expected`.
  - The message is `Step '<label>': '<name>' is not declared here and is not a queue built-in.`
  - The pattern `(Gathering.*){{3}}` with no parameter `3` gives `unresolvable_parameter_reference` (scenario US4-4).
  - For that error, the name is `3` and the field path is `condition.pattern`.
  - A declared name gives no error.
  - `{{iteration}}` is legal in a loop condition and in a break condition.
  - `{{iteration}}` is legal in a step condition inside a loop body.
  - `{{iteration}}` is not legal in a top-level step condition (existing rule).
  - The parameter `group` with the default `(` and the `pattern` `{{group}}abc` gives `invalid_resolved_pattern`.
  - For that error, the field path is `condition.pattern` and `parameterName` is `group`. The message is the contract message.
  - A name with no default stops the `invalid_resolved_pattern` check.
  - A queue built-in and `{{iteration}}` also stop the check.
  - `[{{chars}}]` with no default for a declared `chars` gives no error.
  - A valid pattern with defaults gives no error.
  - A parameter that only `pattern` or `expected` uses is not reported as unused.
- [ ] T019 [P] [US4] Add contract tests to `tests/contract/Sequences/OcrTextConditionContractTests.cs` (contract sections 1 and 2):
  - Use `POST /api/sequences`, `PUT /api/sequences/{id}`, and `PATCH /api/sequences/{id}`.
  - Test each one with and without `"dryRun": true`.
  - The contract 1.1 body gives `400` with `unresolvable_parameter_reference`.
  - For that error, the `fieldPath` is `condition.pattern` and the `parameterName` is `missing`.
  - The same error occurs for `expected` (`condition.expected`).
  - The same error occurs for `condition.children[1].pattern`.
  - The pattern `(Gathering.*){{3}}` with no parameter `3` gives the same error with the `parameterName` `3`.
  - The contract 1.2 body gives `400` with `invalid_resolved_pattern`.
  - The contract 1.3 body (`[{{chars}}]` with no default) saves.
  - The quickstart body with the `marchTarget` default `3` saves. The result is `201`, or `200` for a save dry run.
  - A pattern `(abc` with no placeholder gives the existing error.
  - Add a `POST /api/sequences/{id}/execute` test with `"dryRun": true` and the value `countPattern` = `(`.
  - In that test, the step fails with the `invalid_pattern` message. The result does not hold `invalid_resolved_pattern`.

### Implementation for User Story 4

- [ ] T020 [US4] Scan the two fields in `src/GameBot.Domain/Parameters/ParameterReferenceScanner.cs` (plan D-004). (depends on T017)
  - Add the optional member `IsRegexPattern` (default `false`) to the record `ParameterReference`.
  - Add an `OcrTextStepCondition` case to `ScanCondition`.
  - Add a reference for each key in `Pattern`. Use the field path `{prefix}.pattern` and `IsRegexPattern` true.
  - Add a reference for each key in `Expected`. Use the field path `{prefix}.expected`.
  - Set `DefeatsStaticCheck` to false.
  - Set `SourceText` to the field text.
  - Make T017 pass.
- [ ] T021 [US4] Add the save check with defaults to `src/GameBot.Domain/Services/ParameterValidationService.cs` (plan D-005). (depends on T005, T018, T020)
  - Add the code `invalid_resolved_pattern` to `ParameterValidationCodes`.
  - Add the private method `AddResolvedPatternIssues`. Call it from `ValidateSequence`.
  - In the method, examine each distinct `(StepLabel, FieldPath, SourceText)` with `IsRegexPattern`.
  - For each key, find a sequence declaration with a non-null `Default`.
  - Stop the check of the field for a built-in name, for `iteration`, or for a name with no default.
  - Else, substitute with `TemplateSubstitutor`. Then call `OcrTextConditionRules.TryValidatePattern`.
  - On failure, add the error. Use the first key as `parameterName`.
  - Use this message: `Step '<label>': '<fieldPath>' with the default values gives the pattern '<pattern>', which is not a valid regular expression: <reason>. Change the default value or the pattern.`
  - Keep the method under 50 lines.
  - Make T018 pass.
- [ ] T022 [US4] Run T019. (depends on T010, T019, T021)
  - Make sure that `SequencesEndpoints.ValidateSequenceParametersAsync` gives the new errors for create, update, patch, and the save dry run (research R-006).
  - Do not change the endpoint, unless a test shows a gap.
  - Make sure that the dry-run execution test passes with the T010 resolver.

**Checkpoint**: All user stories work independently.

---

## Phase 7: Polish and shared work

**Purpose**: OpenAPI text, living docs, and the last checks.

- [ ] T023 [P] Update the OpenAPI text in `src/GameBot.Service/Swagger/OcrTextConditionSchemaFilter.cs` (plan D-007):
  - Change the `pattern` and `expected` text in `FieldDescriptions`.
  - Use the sentences of contract section 4.
  - Do not change the schema shape.
- [ ] T024 Update `tests/contract/Sequences/OcrTextConditionOpenApiTests.cs`. Assert the new `pattern` and `expected` description text. (depends on T023)
- [ ] T025 [P] Update `docs/architecture.md` (FR-017, plan D-008):
  - In the sequence parameter section, add `ocrText.pattern` and `ocrText.expected` to the fields that resolve placeholders.
  - In the same section, give the triple-brace rule and the no-escape rule.
  - In the same section, give the reason `invalid_pattern` and the code `invalid_resolved_pattern`.
  - In the `ocrText` section, state that save time does not compile a pattern with a placeholder.
  - State that the gamepack check does not change.
  - Set the "Last reviewed" date to the date of the change.
- [ ] T026 [P] Write the performance statement for the PR description (SC-006, Constitution Principle IV). The PR description must give these two items:
  - A condition with no placeholder keeps the current fast path. The resolver gives the same instance and makes no new allocation (T008 test).
  - A condition with a placeholder adds one substitution and one regex parse, less than 1 ms for each evaluation.
- [ ] T027 [P] Add an STE entry for feature 165 (issue #347) to `CHANGELOG.md` (FR-017).
- [ ] T028 Examine the files of T002 to T027:
  - Make sure that all new code comments, error messages, and docs text obey STE (Principle VI).
  - Make sure that each new method is under 50 lines (Principle I).
  - Make sure that each new method has a CamelCase name with no underscore (Principle I).
  - Make sure that `GamepackValidator` has no change (FR-011).
- [ ] T029 Do the last checks:
  - Run `dotnet build "C:\src\GameBot\GameBot.sln" -c Debug`.
  - Run `dotnet test "C:\src\GameBot\GameBot.sln" -c Debug`. All tests must pass.
  - Compare the steps of `specs/165-ocr-pattern-params/quickstart.md` sections 1 to 6 with the tests. Record the result.
  - Record the T026 performance statement with the result, for the PR description.
- [ ] T030 Before the PR push, bump `installer/versioning/version.override.json`:
  - Run `git fetch origin master`. Compare the file with `origin/master`.
  - If `minor` or `major` changed, set `patch` to `"0"`.
  - Else, set `patch` to the `origin/master` value plus 1.
  - If the file already has this value, do nothing.
  - Set `updatedBy` and `updatedAtUtc`.
- [ ] T031 Set the status of the feature to Implemented (FR-017, Constitution Principle V). Do this task when all other tasks are complete. (depends on T029)
  - Set the `**Status**:` line of `specs/165-ocr-pattern-params/spec.md` to `Implemented`.
  - Add the row for feature 165 to `specs/STATUS.md` with the status `Implemented`. Use the row form of the other features. No earlier task adds this row.
  - Make sure that the items of quickstart section 7 are true.

---

## Dependencies and execution order

### Phase dependencies

- **Setup (Phase 1)**: No dependency.
- **Prerequisites (Phase 2)**: Depends on Phase 1. Blocks all user stories.
- **User Story 1 (Phase 3)**: Depends on Phase 2.
- **User Story 2 (Phase 4)**: Depends on Phase 2. It extends `TryResolveOcrText` from T010 and uses the same test files. Do it after Phase 3.
- **User Story 3 (Phase 5)**: Depends on Phase 3 and Phase 4. It logs the resolved `pattern` and `expected`.
- **User Story 4 (Phase 6)**: T017 to T021 depend only on Phase 2. T022 also needs T010 for the dry-run execution test. You can do Phase 6 in parallel with Phases 3 to 5.
- **Polish (Phase 7)**: Depends on all user stories. T023 to T027 can start at any time after Phase 2. T031 is the last task.

### User story dependencies

- **US1 (P1)**: No dependency on other stories.
- **US2 (P2)**: Shares `TryResolveOcrText` and the test files with US1. Do it after US1.
- **US3 (P2)**: Needs the US1 and US2 code. No new production code is expected.
- **US4 (P2)**: Independent of US1 to US3 (different files). The dry-run execution test in T019 and T022 is the only exception.

### Order in each user story

- Write the tests first. Make sure that they fail.
- Do the resolver and scanner changes before the service and endpoint checks.
- Complete a story before you go to the next priority.

### Parallel opportunities

- Phase 2: T002, T003, and T004 are in different files.
- Phase 3: T009 (integration file) can run in parallel with T008 (unit file).
- Phase 6: T017, T018, and T019 are in different files. All of Phase 6 can run in parallel with Phases 3 to 5.
- Phase 7: T023, T025, and T027 are in different files. T026 changes no file.

---

## Parallel example: User Story 1

```text
Task: "T008 Add ocrText pattern unit tests to tests/unit/Parameters/SequenceStepConditionResolverTests.cs"
Task: "T009 Create tests/integration/Sequences/OcrTextConditionParameterRunTests.cs"
```

## Parallel example: User Story 4

```text
Task: "T017 Add ocrText cases to tests/unit/Parameters/ParameterReferenceScannerTests.cs"
Task: "T018 Create tests/unit/Parameters/ParameterValidationOcrPatternTests.cs"
Task: "T019 Add contract tests to tests/contract/Sequences/OcrTextConditionContractTests.cs"
```

---

## Implementation strategy

### First usable increment (User Story 1 only)

1. Do Phase 1 and Phase 2.
2. Do Phase 3 (User Story 1).
3. Stop and validate. The PNS consumer can use a `pattern` with `{{marchTarget}}`.

### Delivery in steps

1. Setup and prerequisites: the base is ready.
2. US1: the pattern resolves (first usable increment).
3. US2: the expected value resolves.
4. US3: the run log shows the resolved value (tests prove it).
5. US4: the save and the save dry run reject an unknown name and a bad default pattern.
6. Polish: OpenAPI text, docs, changelog, performance statement, version bump, and, in the last task, the spec status and the `specs/STATUS.md` row with `Implemented`.

---

## Notes

- A [P] task is in a different file and has no dependency on an incomplete task.
- The stored sequence does not change (FR-006). Resolution makes a new condition only for the evaluation.
- Do not change the behavior of `imageVisible.imageId` (FR-016). Run its existing tests after each resolver change.
- No web UI change. No phone support level applies.
- Do not change `GamepackValidator` (FR-011, research R-008).
