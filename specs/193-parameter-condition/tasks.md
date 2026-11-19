---

description: "Task list for feature 193: parameter condition"
---

# Tasks: Parameter Condition

**Input**: Design documents from `/specs/193-parameter-condition/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/parameter-condition.md, quickstart.md

**Tests**: The constitution (Principle II) requires tests for all executable logic. Each story has test tasks. Write each test first and make sure that it fails before the implementation.

**Organization**: Tasks are grouped by user story. Each story is an increment that you can test.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependency on a task that is not complete)
- **[Story]**: The user story of the task (US1 to US6)
- All paths are relative to the repository root `C:\src\GameBot`.

---

## Phase 1: Setup

**Purpose**: Make sure that the baseline is green before the change.

- [X] T001 Build the solution with `dotnet build "C:\src\GameBot\GameBot.sln" -c Debug` and run `npm --prefix "C:\src\GameBot\src\web-ui" run lint`. Record any failure that exists before the change. Do not continue on a red baseline (constitution, release blocker).

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: The domain type, its JSON form, the compare rules, and the API contract. All stories need these.

**CRITICAL**: Complete this phase before a user story phase.

- [X] T002 Create src/GameBot.Domain/Commands/ParameterConditionRules.cs with: the record `ParameterConditionValue(string Text, bool IsNumber)` and its `JsonConverter` (read a JSON string or a JSON number with the raw number text, write the same kind, throw `JsonException` for other kinds); the static class `ParameterConditionRules` with the constants `EqualsOperator` = `equals`, `NotEqualsOperator` = `notEquals`, `GreaterThanOperator` = `greaterThan`, `LessThanOperator` = `lessThan`, the list `Operators`, `OperatorsText` (`equals, notEquals, greaterThan, lessThan`), `ValidateShape(ParameterStepCondition)` that gives the message tails `parameter condition requires name.`, `parameter operator must be one of equals, notEquals, greaterThan, lessThan.`, and `parameter condition requires value.`, `TryParseWhole(string?, out long)` (trim, `NumberStyles.AllowLeadingSign`, `CultureInfo.InvariantCulture`), and `Compare(string runValue, bool compareAsNumber, ParameterConditionValue value, string op)` (research R-001, R-003; data-model.md). Add XML comments.
- [X] T003 Add the sealed class `ParameterStepCondition` (discriminator `parameter`, `[JsonIgnore] Type`, `Name`, `Operator`, `ParameterConditionValue? Value`, and the run-time properties `[JsonIgnore] string? ResolvedValue` and `[JsonIgnore] bool CompareAsNumber`) and the line `[JsonDerivedType(typeof(ParameterStepCondition), typeDiscriminator: "parameter")]` in src/GameBot.Domain/Commands/SequenceStepCondition.cs (data-model.md).
- [X] T004 [P] Write `ParameterConditionRulesTests` in tests/unit/Sequences/ParameterConditionRulesTests.cs: the compare table of data-model.md for each operator (number: 0 vs 0, 250 vs 0, 10 vs 9, -5 vs 0; text: `MAX` vs `max`, `t7` vs `max`, `max ` vs `max`, ordinal order of `a` and `B`; JSON number value 0 for a text run value `0`), each shape message, `TryParseWhole` (` 12 `, `-3`, `2.5`, `abc`, empty), and the JSON converter round trip (`0` stays a number, `"0"` stays a string, `true` and `{}` throw). Also a round trip of a sequence with a `parameter` condition through the `FileSequenceRepository` JSON options.
- [X] T005 Add the record `ParameterConditionContract` (discriminator `parameter`; `string? Name`, `string? Operator`, `JsonElement? Value`, inherited `Negate`) and the line `[JsonDerivedType(typeof(ParameterConditionContract), typeDiscriminator: "parameter")]` in src/GameBot.Service/Models/SequenceStepContracts.cs.
- [X] T006 In src/GameBot.Service/Endpoints/SequencesEndpoints.cs: map `ParameterConditionContract` to `ParameterStepCondition` in `MapPerStepCondition` (a JSON string gives `IsNumber` = false; a JSON number gives `IsNumber` = true with `GetRawText()`; no value or a JSON null gives `Value` = null); map `ParameterStepCondition` back in `MapPerStepConditionToDto` (`type`, `name`, `operator`, `value` as a number or a string from `IsNumber`, `negate`); add to `CollectConditionRegionErrors` the error `{path} parameter value must be a string or a number.` when `Value` is present and its `ValueKind` is not `String` or `Number`.
- [X] T007 Add a `ParameterStepCondition` case to the last guard in src/GameBot.Domain/Commands/FileSequenceRepository.cs that throws `InvalidOperationException($"Step '{stepId}' {firstShapeProblem}")` when `ParameterConditionRules.ValidateShape` gives a problem (the same form as the `ocrText` guard).

**Checkpoint**: A sequence with a `parameter` condition saves, and a read returns it with the same JSON kind of `value`.

---

## Phase 3: User Story 1 - Skip a step when a number parameter has a given value (Priority: P1) 🎯 MVP

**Goal**: A `parameter` condition on a `number` parameter gives the correct result at run time.

**Independent Test**: Save a sequence with the `number` parameter `fighterBatch` and a step with the condition `fighterBatch` `equals` 0 and `negate` true. Run it with 0 and with 250. The step runs only with 250.

### Tests for User Story 1

- [X] T008 [P] [US1] Write `ParameterScopeEnteredValueTests` in tests/unit/Parameters/ParameterScopeEnteredValueTests.cs: `TryResolveEntered` gives the bound value, the default, and no value; for a `number` declaration with `pattern` `x{value}`, `TryResolve` gives `x5` and `TryResolveEntered` gives `5`; a `Mapped` bound value stays as it is.
- [X] T009 [P] [US1] Write the number cases of `ParameterConditionEvaluationTests` in tests/unit/Sequences/ParameterConditionEvaluationTests.cs: resolve with `SequenceStepConditionResolver.TryResolve` and evaluate with `SequenceStepConditionEvaluator.EvaluateAsync` for each operator; `negate`; the field path in the error; no value gives a resolution error with `ParameterResolutionReasons.Unresolved`; a run value `abc` for a `number` declaration gives `ParameterResolutionReasons.NotANumber`; `SequenceRunner.ToFailureKind("not_a_number")` gives `ParameterValueInvalid`; a `ParameterStepCondition` with no `ResolvedValue` at the evaluator gives a `ConditionEvaluationException` with `ParameterUnresolved`; one log line in `CommandStepStateScope` with the format of contracts/parameter-condition.md.
- [X] T010 [P] [US1] Write `ParameterConditionRunTests` in tests/integration/Sequences/ParameterConditionRunTests.cs: a sequence with the `number` parameter `fighterBatch` and a step condition `fighterBatch` `equals` 0 with `negate` true; run with the values 0 and 250 through the ad-hoc route `POST /api/sequences/{id}/execute` with the body field `parameters` (a list of `{ "name", "value" }` bindings); the step is skipped with 0 and runs with 250. T016 and T019 use the same route.

### Implementation for User Story 1

- [X] T011 [US1] Add `public bool TryResolveEntered(string name, out ParameterValue value)` with an XML comment to src/GameBot.Domain/Parameters/ParameterScope.cs. It walks the same layers as `TryResolve` (bound values, then declaration defaults) and does not apply the declaration `pattern`. A `Mapped` bound value stays as it is (research R-002).
- [X] T012 [US1] In src/GameBot.Domain/Parameters/SequenceStepConditionResolver.cs: make `HasPlaceholder` return true for each `ParameterStepCondition`; add the case `ParameterStepCondition parameter` to `TryResolveNode` that calls the new private method `TryResolveParameter`. The method finds the declaration with `scope.TryFindDeclaration` and the value with `scope.TryResolveEntered`. No declaration or no value gives `ParameterResolutionError(name, $"{fieldPath}.name", Unresolved)`. A `number` declaration whose value fails `ParameterConditionRules.TryParseWhole` gives the reason `NotANumber` with the value as `OffendingValue`. Success gives a new `ParameterStepCondition` with the same stored fields, `ResolvedValue`, and `CompareAsNumber`, and adds a `ResolvedParameter` to `used`. Update the class XML comment.
- [X] T013 [US1] In src/GameBot.Domain/Services/SequenceRunner.cs, change `ToFailureKind` so that `ParameterResolutionReasons.NotANumber` also gives `ConditionEvaluationFailureKind.ParameterValueInvalid`. Update its XML comment. Read `ParameterResolutionError.ToMessage` in src/GameBot.Domain/Parameters/ParameterResolutionError.cs. If it has no branch for `not_a_number`, add one with the text `Step '<stepKey>': parameter '<name>' at <fieldPath> has the value '<value>', which is not a whole number.` Add a unit test for this message to tests/unit/Sequences/ParameterConditionEvaluationTests.cs.
- [X] T014 [US1] In src/GameBot.Domain/Services/SequenceStepConditionEvaluator.cs: add the case `ParameterStepCondition parameter` to `EvaluateLeafAsync` that calls the new private method `EvaluateParameter`. The method throws `ConditionEvaluationException(ParameterUnresolved, name, ...)` when `ResolvedValue` is null, calls `ParameterConditionRules.Compare`, and writes the log line `parameter(name={0}, operator={1}, value={2}) actual={3} compare={number|text}` to `CommandStepStateScope.Current?.AddLog`. Add the `Describe` case `{NOT }parameter(name=..., operator=..., value=...)`.

**Checkpoint**: US1 tests pass. The MVP works for number parameters through the API.

---

## Phase 4: User Story 2 - Skip a tap when a text or choice parameter has a given value (Priority: P1)

**Goal**: The text compare works for the kinds `text`, `choice`, and `image`.

**Independent Test**: Run an `If` step with the condition `fighterTier` `equals` `max` and the values `MAX` and `t7`. The step runs its body only with `t7` when the condition is `notEquals`.

### Tests for User Story 2

- [X] T015 [P] [US2] Add the text cases to tests/unit/Sequences/ParameterConditionEvaluationTests.cs: a `text` parameter, a `choice` parameter of the type `text` (the technical option value, not the label), and an `image` parameter; a `choice` parameter of the type `number` compares as a number (`10` `greaterThan` `9` is true); each operator; `MAX` equals `max`; `max ` is not equal to `max`; a JSON number value 0 against the text run value `0`.
- [X] T016 [P] [US2] Add to tests/integration/Sequences/ParameterConditionRunTests.cs: an `If` step with the condition `fighterTier` `notEquals` `max` on a `choice` parameter; run with `MAX` (the body does not run) and with `t7` (the body runs).

### Implementation for User Story 2

- [X] T017 [US2] In src/GameBot.Domain/Parameters/SequenceStepConditionResolver.cs, set `CompareAsNumber` from `declaration.Type == ParameterValueType.Number` only (not from `EffectiveKind`), so that a `choice` parameter follows its declared type. In src/GameBot.Domain/Commands/ParameterConditionRules.cs, make the text branch of `Compare` use `string.Compare(runValue, value.Text, StringComparison.OrdinalIgnoreCase)` with no trim. Done when T015 and T016 pass.

**Checkpoint**: US1 and US2 tests pass.

---

## Phase 5: User Story 3 - Combine a parameter condition with other conditions (Priority: P1)

**Goal**: The condition works as a child of `all`, `any`, `none`, and in every condition slot.

**Independent Test**: An `If` step whose condition is `all` of a `parameter` child and an `imageVisible` child follows the `all` rule.

### Tests for User Story 3

- [X] T018 [P] [US3] Add composite cases to tests/unit/Sequences/ParameterConditionEvaluationTests.cs: `all` with a false `parameter` child (result false, `DecidingPath` `$.children[0]`, `DecidingDescription` from `Describe`, the image evaluator is not called); `any` with a true `parameter` child; `none` with a true `parameter` child; a `parameter` child in a nested composite resolves with the field path `if.condition.children[0].children[1].name` in the error.
- [X] T019 [P] [US3] Add to tests/integration/Sequences/ParameterConditionRunTests.cs: a `parameter` condition in a `while` loop condition and in a `breakCondition` (the loop stops when the condition changes the result), and an `If` step with an `all` composite of a `parameter` child and a `commandOutcome` child.

### Implementation for User Story 3

- [X] T020 [US3] In src/GameBot.Domain/Parameters/SequenceStepConditionResolver.cs, make sure that `AnyChildHasPlaceholder` returns true for a composite with a `parameter` child at any depth, so that the composite branch of `TryResolveNode` makes a new composite with the resolved child. Read the condition slots of src/GameBot.Domain/Services/StepThrough/SequenceStepper.cs (lines with `ResolveCondition` and `EvaluateLoopConditionAsync`) and src/GameBot.Domain/Services/SequenceRunner.cs, and confirm that each slot calls `SequenceRunner.ResolveCondition` before the evaluation. If a slot calls the evaluator directly, change it to call `ResolveCondition` first. Done when T018 and T019 pass.

**Checkpoint**: All P1 stories pass.

---

## Phase 6: User Story 4 - Find an unknown parameter name before a run (Priority: P2)

**Goal**: The save and the `dryRun` refuse a bad `parameter` condition with a clear message.

**Independent Test**: A `dryRun` create with the name `fighterBach` gives a 400 that names `fighterBach` and the field path.

### Tests for User Story 4

- [X] T021 [P] [US4] Write `ParameterConditionValidationTests` in tests/unit/Sequences/ParameterConditionValidationTests.cs: `CompositeConditionValidator.Validate` gives the three shape messages for a root leaf and for a nested child (with the `$`-rooted path); `ParameterValidationService.ValidateSequence` gives `unknown_condition_parameter` for an unknown name, a `{{fighterBatch}}` name, and a `queue.*` name; `invalid_condition_value` for a `number` parameter with the value `"abc"` and `2.5`; no error for a `number` parameter with `"0"` or `0`; `list_in_single_field` for a `multiple: true` parameter; the field paths `condition`, `if.condition.children[1]`, `loop.condition`, and `breakCondition` for steps in nested bodies.
- [X] T022 [P] [US4] Write `ParameterConditionContractTests` in tests/contract/Sequences/ParameterConditionContractTests.cs: create (`POST /api/sequences`), update (`PUT`), and `PATCH` accept a valid `parameter` condition, and a `GET` returns `"value": 0` as a number and `"value": "max"` as a string; `"dryRun": true` with a valid condition gives `{ valid: true, dryRun: true }` and stores nothing; create and `dryRun` give a 400 for an unknown name, an unknown operator, no value, a boolean value, a `number` parameter with `"abc"`, and a multi-value parameter, and each message names the field or the parameter.

### Implementation for User Story 4

- [X] T023 [US4] In src/GameBot.Domain/Services/CompositeConditionValidator.cs: add `and not ParameterStepCondition` to the root gate of `Validate`, and add the case `ParameterStepCondition parameter` to `Walk` that calls the new private method `ValidateParameter`. The method adds each message of `ParameterConditionRules.ValidateShape` with the prefix `Step '{stepLabel}' condition at {path}: `.
- [X] T024 [US4] In src/GameBot.Domain/Services/ParameterValidationService.cs: add the codes `UnknownConditionParameter` = `unknown_condition_parameter` and `InvalidConditionValue` = `invalid_condition_value` to `ParameterValidationCodes` with XML comments; add the private method `AddParameterConditionIssues(CommandSequence, List<ParameterValidationIssue>)` and call it from `ValidateSequence`. The method walks each step (also `Body` and `ElseBody`) and each slot (`condition`, `if.condition`, `loop.condition` of a while or repeat-until loop, `breakCondition`) and each composite child. For each `ParameterStepCondition` with a name that is not empty: an unknown name gives `UnknownConditionParameter` with the message `Step '<label>' condition at <path>: parameter '<name>' is not a parameter of this sequence.`; a `Multiple` declaration gives `ListInSingleField`; a `number` declaration with a value that fails `TryParseWhole` gives `InvalidConditionValue`. Use the messages of contracts/parameter-condition.md.
- [X] T025 [US4] Read the create, update, and `PATCH` handlers of src/GameBot.Service/Endpoints/SequencesEndpoints.cs. Confirm that each handler calls `CompositeConditionValidator.Validate` and `ParameterValidationService.ValidateSequence` before the `IsDryRunRequested` return, and gives a 400 with the errors. If a handler does not, add the call before the `IsDryRunRequested` return. Done when T021 and T022 pass.

**Checkpoint**: US4 tests pass.

---

## Phase 7: User Story 5 - Read the condition in the OpenAPI document (Priority: P2)

**Goal**: The OpenAPI document describes the `parameter` condition.

**Independent Test**: The OpenAPI document has the `ParameterCondition` schema with the fields, the operator enum, and the compare rules.

### Tests for User Story 5

- [X] T026 [P] [US5] Write `ParameterConditionOpenApiTests` in tests/contract/Sequences/ParameterConditionOpenApiTests.cs: the document has the schema `ParameterCondition` in the condition `oneOf` list with the discriminator value `parameter`; the properties `name`, `operator`, `value`, and `negate`; the `enum` of `operator` is the four operators; the `value` schema permits a string and an integer; the description states the compare rules (follow `CommandStepOutcomeOpenApiTests` and `MatchedImageContractTests`).

### Implementation for User Story 5

- [X] T027 [P] [US5] Create src/GameBot.Service/Swagger/ParameterConditionSchemaFilter.cs (the same form as `MatchedImageConditionSchemaFilter`): the schema description (discriminator `parameter`, permitted slots, compare rules, save rules, run failures, an example), the field descriptions, the `enum` on `operator`, and a `oneOf` of `string` and `integer` on `value`.
- [X] T028 [US5] Register the filter with `options.SchemaFilter<ParameterConditionSchemaFilter>()` in src/GameBot.Service/GameBotServiceSetup.cs, and generate and alias `ParameterConditionContract` as `ParameterCondition` in src/GameBot.Service/Swagger/ConditionalFlowSchemaDocumentFilter.cs (the same lines as for `MatchedImageConditionContract`). If an OpenAPI snapshot test in tests/contract/ApiContractSnapshots fails, update the snapshot.

**Checkpoint**: US5 tests pass.

---

## Phase 8: User Story 6 - Author the condition in the sequence editor (Priority: P3)

**Goal**: The sequence editor can add, edit, and save a `parameter` condition.

**Independent Test**: In the editor, add a `parameter` condition, save, and open the sequence again. The condition shows the same values.

### Tests for User Story 6

- [X] T029 [P] [US6] Write src/web-ui/src/components/sequences/__tests__/ParameterConditionFields.test.tsx: the kind list shows `parameter`; a select of the parameter names from the prop `parameterNames`; a select of the four operators; a text input for the value; a change calls `onConditionChange` with the new condition; the value is a number when the selected parameter is a `number` parameter and the text is a whole number, else a string.
- [X] T030 [P] [US6] Write src/web-ui/src/lib/__tests__/parameterConditionValidation.spec.ts: the shape messages and the unknown-name message of `validation.ts` are the same as the service messages.

### Implementation for User Story 6

- [X] T031 [P] [US6] Add the type `ParameterStepCondition` (`type: 'parameter'`, `name: string`, `operator: 'equals' | 'notEquals' | 'greaterThan' | 'lessThan'`, `value: string | number`, `negate?: boolean`), the constant `PARAMETER_CONDITION_OPERATORS`, the union member, and `'parameter'` in `PerStepConditionType` to src/web-ui/src/types/sequenceFlow.ts; add `'parameter'` to `conditionType` in src/web-ui/src/types/stepEntry.ts.
- [X] T032 [US6] In src/web-ui/src/components/sequences/ConditionFields.tsx: add the optional prop `parameters?: { name: string; type: 'text' | 'number' }[]`, the option `parameter`, and the three controls (`${testIdPrefix}-parameterName`, `${testIdPrefix}-operator`, `${testIdPrefix}-value`). Convert the value as T029 states.
- [X] T033 [US6] In src/web-ui/src/pages/SequencesPage.tsx: pass the declared parameters of the sequence to each `ConditionFields`; add `'parameter'` to the condition type select of a linear step, with the form fields `parameterName`, `parameterOperator`, and `parameterValue`; map the condition on load (`withParameterConditionFields`) and on save (`buildParameterConditionPayload`), the same as the code for the `matchedImage` condition (`withMatchedImageFields`, `buildMatchedImagePayload`).
- [X] T034 [US6] In src/web-ui/src/lib/validation.ts: add `validateParameterCondition` with the shape rules and the unknown-name rule (the same messages as the service), and call it for each slot that has a `parameter` condition.

**Checkpoint**: All stories pass.

---

## Phase 9: Polish & Cross-Cutting Concerns

- [X] T035 [P] Write a gamepack round-trip test in tests/unit/Gamepacks/ParameterConditionGamepackTests.cs: export a sequence with a `parameter` condition (at the root of a step and as a child of `all`) with the gamepack export code, import it, and make sure that `name`, `operator`, `value` (with its JSON kind), and `negate` do not change (FR-016).
- [X] T036 [P] Update docs/architecture.md: add the `parameter` condition kind to the list of sequence conditions (fields, operators, compare rules, save rules, run failures, log line, the new validation codes, `ParameterScope.TryResolveEntered`), and refresh the "Last reviewed" date.
- [X] T037 [P] Add an entry to CHANGELOG.md for the `parameter` condition (issue #401, FR-046). State "no performance effect on other conditions" and the new version.
- [X] T038 Set `**Status**: Implemented` in specs/193-parameter-condition/spec.md and add a row for 193 to specs/STATUS.md.
- [X] T039 Bump the version: run `git fetch origin master`, compare installer/versioning/version.override.json with `origin/master`, and set `patch` to the `patch` on `origin/master` plus 1 (or `"0"` when `minor` or `major` changed). Set `updatedBy` and `updatedAtUtc`.
- [X] T040 Run the full checks: `dotnet build "C:\src\GameBot\GameBot.sln" -c Debug` with 0 warnings as errors, `dotnet test` for the unit, contract, and integration projects, and `npm --prefix "C:\src\GameBot\src\web-ui" run lint` and `npm --prefix "C:\src\GameBot\src\web-ui" test`. Run `scripts/ci/measure-shards.ps1` and split a shard if the shard balance rule of docs/ci-time.md requires it. Run the steps of quickstart.md.

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies.
- **Foundational (Phase 2)**: Depends on Setup. Blocks all user stories.
- **US1 (Phase 3)**: Depends on Phase 2. The MVP.
- **US2 (Phase 4)**: Depends on US1 (T012 and T014 hold the resolver and the evaluator).
- **US3 (Phase 5)**: Depends on US1.
- **US4 (Phase 6)**: Depends on Phase 2 only. It can run in parallel with US1 to US3.
- **US5 (Phase 7)**: Depends on T005 only.
- **US6 (Phase 8)**: Depends on Phase 2 (the API shape). The web UI tasks do not touch the C# files.
- **Polish (Phase 9)**: Depends on all stories.

### Within Each User Story

- Write the tests first and make sure that they fail.
- Domain changes before service changes. Service changes before the web UI.

### Parallel Opportunities

- T004 runs in parallel with T005 to T007.
- T008, T009, and T010 run in parallel.
- US4 (T021 to T025) and US5 (T026 to T028) run in parallel with US1 to US3.
- T029, T030, and T031 run in parallel.
- T035, T036, and T037 run in parallel.

## Parallel Example: User Story 4

```text
Task: "T021 Write ParameterConditionValidationTests in tests/unit/Sequences/ParameterConditionValidationTests.cs"
Task: "T022 Write ParameterConditionContractTests in tests/contract/Sequences/ParameterConditionContractTests.cs"
```

## Implementation Strategy

### MVP First (User Story 1 Only)

1. Complete Phase 1 and Phase 2.
2. Complete Phase 3 (US1).
3. Stop and test: a number parameter condition works through the API.

### Incremental Delivery

1. US1, then US2 and US3 (all P1): the runtime of feature 032.
2. US4 and US5 (P2): the save checks and the OpenAPI document.
3. US6 (P3): the web UI.
4. Polish: docs, changelog, status, version, full checks.
