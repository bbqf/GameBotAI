---

description: "Task list for feature 192: TextInput command step"
---

# Tasks: TextInput Command Step

**Input**: Design documents from `/specs/192-text-input-step/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/text-input-step.md, quickstart.md

**Tests**: The constitution (Principle II) requires tests for all executable logic. Each story has test tasks. Write each test first and make sure that it fails before the implementation.

**Organization**: Tasks are grouped by user story. Each story is an increment that you can test.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependency on a task that is not complete)
- **[Story]**: The user story of the task (US1 to US5)
- All paths are relative to the repository root `C:\src\GameBot`.

---

## Phase 1: Setup

**Purpose**: Make sure that the baseline is green before the change.

- [ ] T001 Build the solution with `dotnet build "C:\src\GameBot\GameBot.sln" -c Debug` and run `npm --prefix "C:\src\GameBot\src\web-ui" run lint`. Record any failure that exists before the change. Do not continue on a red baseline (constitution, release blocker).

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: The step type, its config, and its mapping. All stories need these.

**⚠️ CRITICAL**: Complete this phase before a user story phase.

- [ ] T002 Add `TextInput` at the end of `CommandStepType`, add the class `TextInputConfig` (`required string Text`, with an XML comment), and add `TextInputConfig? TextInput` to `CommandStep` with `[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]` in src/GameBot.Domain/Commands/CommandStep.cs (data-model.md, research R-010).
- [ ] T003 [P] Create `TextInputRules` (`MaxLength` = 500, `MinCharacter` = U+0020, `MaxCharacter` = U+007E, `Check(string?)` that returns the enum `TextInputCheck` { `Ok`, `Empty`, `TooLong`, `UnsupportedCharacter` } in the order empty, length, characters, and `ReasonCode(TextInputCheck)` that returns `text_input_empty_text`, `text_input_too_long`, `text_input_unsupported_character`, or null) in src/GameBot.Domain/Commands/TextInputRules.cs (research R-004).
- [ ] T004 [P] Write `TextInputRulesTests` (empty and null text, 1 character, 500 and 501 characters, space only, each boundary character U+0020 and U+007E, U+001F, a tab, a line break, `é`, the check order when a text is too long and has a character that is not permitted, each reason code) in tests/unit/Commands/TextInputRulesTests.cs.
- [ ] T005 Add `TextInput` at the end of `CommandStepTypeDto`, add `TextInputConfigDto` (`required string Text`), and add `TextInputConfigDto? TextInput` to `CommandStepDto` in src/GameBot.Service/Models/Commands.cs.
- [ ] T006 Map `TextInput` in both directions in `ToDomainType`/`ToDtoType`, `ToDomainStep`, and `ToResponseStep` (`TextInput = s.Type == ...TextInput ? ... : null`) in src/GameBot.Service/Endpoints/CommandsEndpoints.cs.
- [ ] T007 Map `TextInput` in the type switch and the step mapping (`TextInput = type == CommandStepType.TextInput && s.TextInput is not null ? new TextInputConfig { Text = s.TextInput.Text } : null`) in src/GameBot.Service/Endpoints/StepsEndpoints.cs.
- [ ] T008 [P] Copy `TextInput = step.TextInput` in the step copy of src/GameBot.Domain/Gamepacks/GamepackReferences.cs. Add a test that `GamepackReferences` keeps the `TextInput` config and the `textInput.text` key of `fieldTemplates` when it maps the ids of a command (FR-018), in tests/unit/Gamepacks/GamepackReferencesTextInputTests.cs (follow tests/unit/Gamepacks/GamepackReferencesRewriteTests.cs).
- [ ] T009 [P] Add `[CommandStepType.TextInput] = false` to `PreviewEffectRules.ByType` in src/GameBot.Service/Services/StepThrough/PreviewEffectRules.cs (research R-011). Add an assertion to the current `PreviewEffectRules` unit test that `TryDescribe` gives false for a `TextInput` step (FR-022). Run the test: it must pass.
- [ ] T010 [P] Add the round-trip test: a command with a `TextInput` step keeps its `textInput.text` after save and load, and a stored command without a `TextInput` step has no `textInput` property in its JSON, in tests/unit/Commands/TextInputStepStorageTests.cs (use the current `FileCommandRepository` test setup).

**Checkpoint**: The solution builds. A `TextInput` step saves and loads. The executor does not run it yet.

---

## Phase 3: User Story 1 - Type a fixed text into the device (Priority: P1) 🎯 MVP

**Goal**: A `TextInput` step with a literal text types the full text on the device and reports `typedLength`.

**Independent Test**: Save a command with a `TextInput` step with the text `250`. Run it. The device gets one `input text '250'`. The outcome has `executed`, `text-input`, and `typedLength` 3.

### Tests for User Story 1

- [ ] T011 [P] [US1] Write `AdbTextEncoderTests` (`250` gives one chunk; `250 Fighters` gives `250`, a space, `Fighters`; two spaces give two space parts; `50%s` gives `50%` and `s`; `100%` gives one chunk; the parts in order give the original text; `QuoteForDeviceShell` gives `'abc'`, `'it'\''s'`, and keeps `&;|<>()$"\*?` as they are inside the quotes) in tests/unit/Emulator/AdbTextEncoderTests.cs.
- [ ] T012 [P] [US1] Write `SessionManagerTextInputTests` with `FakeAdbSessionClient`: action `text` with `250` calls `InputTextAsync("250")` once and sets `Args["typedLength"]` to 3; `25 0` calls `InputTextAsync("25")`, `KeyEventAsync(62)`, and `InputTextAsync("0")`; a chunk that fails after the ADB retries stops the send, the action does not count, and `typedLength` holds the characters before the failure; the retry count of a chunk is the configured `AdbRetries`; the liveness tracker records one input for each ADB command; stub mode (no device) counts the action and does not set `typedLength`; a `key` action with the key `250` still calls `KeyEventAsync(250)` (FR-017), in tests/unit/Emulator/SessionManagerTextInputTests.cs.
- [ ] T013 [P] [US1] Write `CommandExecutorTextInputTests` for the success path: a `TextInput` step with `250` sends one input action of type `text` with the argument `text` = `250`, and the outcome has `Status` `executed`, `StepType` `text-input`, `Reason` null, and `TypedLength` 3; in stub mode the outcome has `TypedLength` = text length, in tests/unit/Commands/CommandExecutorTextInputTests.cs (follow tests/unit/Commands/CommandExecutorKeyInputTests.cs).
- [ ] T014 [P] [US1] Write the integration tests: `POST /api/commands` with a `TextInput` step `250` gives 201 and `GET` gives the same step; a step without `textInput`, with an empty literal text, with 501 characters, or with `é` gives 400 with the messages of contracts/text-input-step.md section 1; `POST /api/steps/execute` with a `TextInput` step gives an outcome with `stepType` `text-input` and `typedLength` 3, in tests/integration/Commands/TextInputStepEndpointTests.cs.
- [ ] T015 [P] [US1] Write `TextInputOpenApiTests`: the OpenAPI document lists `TextInput` in `CommandStepTypeDto`, `TextInputConfigDto.text` is required and has a description that names U+0020 to U+007E, 500, `fieldTemplates`, and each reason code, and `StepExecutionOutcomeDto.typedLength` has a description, in tests/contract/TextInputOpenApiTests.cs (follow tests/contract/KeyInputOpenApiTests.cs).

### Implementation for User Story 1

- [ ] T016 [P] [US1] Create `AdbTextEncoder` with `Split(string)` (parts: a space part or a chunk with no space and no `%s`) and `QuoteForDeviceShell(string)` (single quotes, `'` becomes `'\''`), with XML comments, in src/GameBot.Emulator/Adb/AdbTextEncoder.cs (research R-002, R-003).
- [ ] T017 [US1] Add `Task<(int ExitCode, string StdOut, string StdErr)> InputTextAsync(string chunk, CancellationToken ct = default)` to src/GameBot.Emulator/Adb/IAdbSessionClient.cs, and implement it in tests/unit/Emulator/FakeAdbSessionClient.cs (record the calls, return a configurable exit code).
- [ ] T018 [US1] Implement `AdbClient.InputTextAsync`: move the process code of `ExecAsync` into a private method that takes a `ProcessStartInfo`, keep `ExecAsync(string)` as it is, and send the arguments `-s <serial>` (when set), `shell`, and `input text <QuoteForDeviceShell(chunk)>` through `ProcessStartInfo.ArgumentList`. Add a log call for the text input that logs the chunk length, not the text, in src/GameBot.Emulator/Adb/AdbClient.cs.
- [ ] T019 [US1] Handle the input action type `text` in `SessionManager.SendInputsAsync`: read `Args["text"]` (string or `JsonElement`), send each part from `AdbTextEncoder.Split` through `SendRecordedInputAsync` (operation `text` with `InputTextAsync`, or operation `key` with `KeyEventAsync(62)` for a space), stop at the first failure, write `Args["typedLength"]`, and count the action only when all parts succeed. Keep the stub path as it is. Put the loop in a private method to keep the method short, in src/GameBot.Emulator/Session/SessionManager.cs (research R-005).
- [ ] T020 [US1] Add `int? TypedLength = null` at the end of `PrimitiveTapStepOutcome` in src/GameBot.Service/Services/ICommandExecutor.cs, and add `int? TypedLength` with `[JsonIgnore(Condition = WhenWritingNull)]` and an XML comment to `StepExecutionOutcomeDto` in src/GameBot.Service/Models/Commands.cs.
- [ ] T021 [US1] Add the private method `ExecuteTextInputAsync` to `CommandExecutor` and call it from the step switch after the `KeyInput` branch: send one `InputAction("text", { ["text"] = text })`, and on success give `executed` with `StepType: "text-input"` and `TypedLength` from `Args["typedLength"]`, or the text length when the argument is absent, in src/GameBot.Service/Services/CommandExecutor.cs.
- [ ] T022 [US1] Write `TypedLength` into the outcome DTO in the command run mapping of src/GameBot.Service/Endpoints/CommandsEndpoints.cs and in the outcome mapping of src/GameBot.Service/Endpoints/StepsEndpoints.cs.
- [ ] T023 [US1] Add the shared save check `CommandsEndpoints.ValidateTextInput(TextInputConfigDto?, bool hasTextTemplate)`: no config gives `textInput.text is required for TextInput steps`; a text with no placeholder and no template key must pass `TextInputRules.Check` (empty: the same "is required" message; too long: `textInput.text must have 500 characters or fewer`; character: `textInput.text can have only the printable ASCII characters from space to ~`). Call it from `ValidateStep` in src/GameBot.Service/Endpoints/CommandsEndpoints.cs and from the step check of src/GameBot.Service/Endpoints/StepsEndpoints.cs.
- [ ] T024 [US1] Add a detail item for an outcome with `StepType` `text-input` and `Status` `executed`: kind `text-input`, text `Text input typed N characters.`, attribute `typedLength`, in src/GameBot.Service/Services/ExecutionLog/ExecutionLogService.cs. Add the test `ExecutionLogTextInputDetailTests` (an executed `TextInput` outcome gives one detail item with kind `text-input`, the text `Text input typed 3 characters.`, the attribute `typedLength` 3, and no attribute or text that holds the typed text) in tests/unit/ExecutionLogs/ExecutionLogTextInputDetailTests.cs.
- [ ] T025 [P] [US1] Create `TextInputSchemaFilter` for `TextInputConfigDto.text` (description: the text to type, the permitted characters U+0020 to U+007E, the maximum length 500, inline placeholders, the `fieldTemplates` key `textInput.text`, the reason codes; example `"250"`) in src/GameBot.Service/Swagger/TextInputSchemaFilter.cs, register it next to `KeyInputSchemaFilter` in src/GameBot.Service/GameBotServiceSetup.cs, and add the `typedLength` description of `StepExecutionOutcomeDto` in the same filter or the current outcome schema filter.
- [ ] T026 [US1] Run T011 to T015 and make them pass. Run the current `KeyInput` tests (`CommandExecutorKeyInputTests`, `SessionManagerKeyInputTests`, `KeyInputOpenApiTests`) with no change: they must pass (SC-006).

**Checkpoint**: User Story 1 works on its own (MVP).

---

## Phase 4: User Story 2 - Type a text from a sequence parameter (Priority: P1)

**Goal**: `fieldTemplates` `textInput.text` and an inline placeholder give the text from a `text` or `number` parameter.

**Independent Test**: A command with `fieldTemplates` `{"textInput.text": "{{fighterBatch}}"}` and a `number` parameter `fighterBatch` = 250, called from a sequence, types `250`.

### Tests for User Story 2

- [ ] T027 [P] [US2] Add resolver tests: overlay `{{fighterBatch}}` with a `number` value 250 gives `TextInput.Text` = `250` and records the parameter; a `text` value `100` gives `100`; an inline `{{fighterBatch}}` resolves; inline text around a placeholder (`x{{a}}`) resolves; the overlay replaces the inline text and an unknown inline placeholder is then not resolved; an unknown overlay name gives `Unresolved` with field path `textInput.text`; a multi-value parameter with two items gives `ListInSingleField`, in tests/unit/Parameters/CommandStepResolverTextInputTests.cs.
- [ ] T028 [P] [US2] Add scanner tests: an inline placeholder in `textInput.text` gives a reference with field path `textInput.text`; an overlay key gives a reference with field path `textInput.text` and `DefeatsStaticCheck` false, in tests/unit/Parameters/ParameterReferenceScannerTests.cs.
- [ ] T029 [P] [US2] Add validation tests: the key `textInput.text` is accepted; the value `{{a}}0` gives `invalid_field_template_value`; a `text` parameter in `textInput.text` gives no `TextParameterInNumericField` error, in tests/unit/Parameters/ParameterValidationTextInputTests.cs.
- [ ] T030 [P] [US2] Add an integration test: a command with a `number` parameter `fighterBatch` (default 250) and a `TextInput` step with the overlay, called by a sequence that binds `fighterBatch` = 100. Replace `ISessionManager` in the test host with a double that records the input actions of `SendInputsAsync` (follow `SerialSessionManager` in tests/integration/DeviceRecoveryTestSupport.cs). The run sends one input action `text` with the argument `text` = `100` and gives `typedLength` 3, and the execution log entry of the step records the resolved parameter `fighterBatch` with the value `100` (US2 scenario 1, FR-023); with no binding it sends `250`, in tests/integration/Commands/TextInputParameterTests.cs.

### Implementation for User Story 2

- [ ] T031 [US2] Add `SupportedTextPaths` = { `textInput.text` }, include it in `IsSupported`, add `IsTextPath`, and update the XML comments of `CommandStepFieldPaths` and `CommandStep.FieldTemplates` in src/GameBot.Domain/Commands/CommandStep.cs.
- [ ] T032 [US2] Resolve `textInput.text` in `CommandStepResolver.TryResolve` (overlay with `TryOverlayValue(isTextField: true)`, else `TryText(..., singleValueField: true)`), add `TextInput = textInput` to the result step, and add `step.TextInput?.Text` to `HasInlinePlaceholder` in src/GameBot.Domain/Parameters/CommandStepResolver.cs (research R-008).
- [ ] T033 [US2] Add `AddFrom(found, step.TextInput?.Text, "textInput.text", label)` to `ScanCommandStep` in src/GameBot.Domain/Parameters/ParameterReferenceScanner.cs.
- [ ] T034 [US2] Require one whole placeholder for a text path in `ValidateFieldTemplateKeys` (same message as for an image key) in src/GameBot.Domain/Services/ParameterValidationService.cs.
- [ ] T035 [US2] Add `{ path: 'textInput.text', kind: 'text' }` and the kind `'text'` to src/web-ui/src/components/parameters/fieldTemplatePaths.ts, and update `FieldTemplatePathMirrorTests` (regex kind `integer|decimal|image|text`, service entries include `SupportedTextPaths` as `text`) in tests/unit/Parameters/FieldTemplatePathMirrorTests.cs.
- [ ] T036 [US2] Add the key `textInput.text` to the `fieldTemplates` description (`FieldTemplatesDescription`) in src/GameBot.Service/Swagger/ParametrizedReferenceImageSchemaFilter.cs and to the XML comment of `CommandStepDto.FieldTemplates` in src/GameBot.Service/Models/Commands.cs.
- [ ] T037 [US2] Run T027 to T030 and the current resolver, scanner, and validation tests. All must pass.

**Checkpoint**: User Stories 1 and 2 work. Feature 032 of the PNS repository can type a batch size from a parameter.

---

## Phase 5: User Story 3 - Get a clear reason when the service cannot type the text (Priority: P2)

**Goal**: Each failure gives `not_accepted` (or `skipped_invalid_config`) with its reason code, and no false success.

**Independent Test**: Run a `TextInput` step with a resolved empty text, `é`, 501 characters, and a device that refuses. Each outcome has its reason code.

### Tests for User Story 3

- [ ] T038 [P] [US3] Add executor tests: resolved empty text gives `not_accepted`, `text_input_empty_text`, `TypedLength` 0, and no input action; `é` gives `text_input_unsupported_character`; 501 characters give `text_input_too_long`; a device that accepts no input gives `text_input_not_accepted` with `TypedLength` from `Args["typedLength"]`; a step with no config gives `skipped_invalid_config` and `text_input_missing_config`, in tests/unit/Commands/CommandExecutorTextInputTests.cs.

### Implementation for User Story 3

- [ ] T039 [US3] In `ExecuteTextInputAsync`, give `skipped_invalid_config` / `text_input_missing_config` when `step.TextInput` is null; call `TextInputRules.Check` on the resolved text before the send and give `not_accepted` with `TextInputRules.ReasonCode` and `TypedLength` 0 when the check fails; give `not_accepted` / `text_input_not_accepted` with the typed length when the session accepts no action, in src/GameBot.Service/Services/CommandExecutor.cs.
- [ ] T040 [US3] Make sure that the `TextInputSchemaFilter` description (T025) names all five reason codes (`text_input_empty_text`, `text_input_too_long`, `text_input_unsupported_character`, `text_input_not_accepted`, `text_input_missing_config`) in src/GameBot.Service/Swagger/TextInputSchemaFilter.cs, and run T038. All must pass.

**Checkpoint**: Each failure is visible in the step result.

---

## Phase 6: User Story 4 - Find an unknown placeholder before a run (Priority: P2)

**Goal**: `"dryRun": true` on the command create and update requests runs all checks and stores nothing. An unknown placeholder in `textInput.text` gives 400.

**Independent Test**: `POST /api/commands` with `dryRun` and `{{fighterBach}}` in `textInput.text` gives 400 `unresolvable_parameter_reference` with field path `textInput.text`. With a declared name it gives 200 `{ valid: true, dryRun: true, errors: [] }` and no command is stored.

### Tests for User Story 4

- [ ] T041 [P] [US4] Write integration tests: `POST /api/commands` with `dryRun: true` and a valid body gives 200 with `valid` true, `dryRun` true, empty `errors`, and the command list does not change; with `{{fighterBach}}` (inline and in `fieldTemplates`) and no such declaration it gives 400 with code `unresolvable_parameter_reference` and field path `textInput.text`; `PATCH /api/commands/{id}` with `dryRun: true` gives 200 and the stored command does not change; `dryRun: false` stores as before; a step error (empty literal text) with `dryRun: true` gives the same 400, in tests/integration/Commands/CommandDryRunTests.cs.

### Implementation for User Story 4

- [ ] T042 [US4] Add `bool? DryRun` with an XML comment to `CreateCommandRequest` and `UpdateCommandRequest` in src/GameBot.Service/Models/Commands.cs.
- [ ] T043 [US4] In the create and patch handlers, after all checks and before `repo.AddAsync` / `repo.UpdateAsync`, return `Results.Ok(new { valid = true, dryRun = true, errors = Array.Empty<string>() })` when `DryRun == true`. In the patch handler, the changes to `existing` are not stored: `FileCommandRepository.GetAsync` deserializes a new instance from the file for each call, so the dry run changes only a private copy. Add a comment that says this, in src/GameBot.Service/Endpoints/CommandsEndpoints.cs.
- [ ] T044 [US4] Add the `dryRun` text to the OpenAPI description of `CreateCommand` and `UpdateCommand` (route description or schema filter, in the same way as the sequence routes) in src/GameBot.Service/Endpoints/CommandsEndpoints.cs or src/GameBot.Service/Swagger/SwaggerConfig.cs, and add a check of this text to tests/contract/TextInputOpenApiTests.cs.
- [ ] T045 [US4] Run T041 and the current command endpoint tests. All must pass.

**Checkpoint**: An author finds an unknown name before a run.

---

## Phase 7: User Story 5 - Author the step in the command editor (Priority: P3)

**Goal**: The command editor adds, edits, and saves a `TextInput` step, with a parameter for `textInput.text`.

**Independent Test**: Add a "Text Input" step with `250`, save, open again: the step shows `Text: 250`.

### Tests for User Story 5

- [ ] T046 [P] [US5] Write `TextInputPanel.test.tsx` (an empty text shows the error "Text is required." on Add; a text with `é` shows the error about printable ASCII; `250` calls `onConfirm` with `{ text: '250' }`; a parameter selection gives `fieldTemplates` `{ 'textInput.text': '{{name}}' }` and permits an empty text; the label `Save` for an edit) in src/web-ui/src/components/commands/__tests__/TextInputPanel.test.tsx.
- [ ] T047 [P] [US5] Update src/web-ui/src/components/commands/__tests__/ActionTypeSelector.test.tsx (the option `TextInput` with the text `Text Input`) and src/web-ui/src/components/parameters/__tests__/fieldTemplatePaths.test.ts (`textInput.text` is parametrizable; `parameterFit` gives fits for `text` and `number` without a pattern and with a pattern, and not for `image`; `dropForeignTemplateKeys` drops `textInput.text` for another step type and keeps it for `TextInput`).

### Implementation for User Story 5

- [ ] T048 [P] [US5] Add `'TextInput'` to the step type unions, `TextInputConfigDto = { text: string }`, and `textInput?: TextInputConfigDto` in src/web-ui/src/services/commands.ts.
- [ ] T049 [US5] Update `fieldTemplatePaths.ts`: `parameterFit` for a `text` path gives fits for `text`, `number`, and single `choice` parameters and not for `image`; add `TextInput: 'textInput.'` to `STEP_KEY_PREFIX`; update the `EDITOR_PATHS` comment (13 paths), in src/web-ui/src/components/parameters/fieldTemplatePaths.ts.
- [ ] T050 [US5] Create `TextInputPanel` (one text field "Text *", help text with the permitted characters and the 500 limit, a `ParameterSourceControl` for `textInput.text` with `useFieldTemplates`, as in `SwipePanel`) in src/web-ui/src/components/commands/TextInputPanel.tsx.
- [ ] T051 [US5] Add the option `<option value="TextInput">Text Input</option>` and the type in src/web-ui/src/components/commands/ActionTypeSelector.tsx.
- [ ] T052 [US5] In src/web-ui/src/components/commands/CommandForm.tsx: add `'TextInput'` to the step type union, `textInput?: { text: string }`, the label `Text: <text>` (or the parameter name), `TextInput` in `EDITABLE_TYPES`, and the `TextInputPanel` branch with `fieldTemplates`.
- [ ] T053 [US5] Map `textInput` in both directions (load and save, `{ type: 'TextInput', order, textInput: { text }, fieldTemplates }`) in src/web-ui/src/pages/CommandsPage.tsx.
- [ ] T054 [US5] Run `npm --prefix "C:\src\GameBot\src\web-ui" test`, `npm --prefix "C:\src\GameBot\src\web-ui" run lint`, and `npm --prefix "C:\src\GameBot\src\web-ui" run build`. All must pass.

**Checkpoint**: All user stories work.

---

## Phase 8: Polish & Cross-Cutting Concerns

- [ ] T055 [P] Update docs/architecture.md: the `TextInput` command step (config, characters, limit, chunks, key event 62 for a space, reason codes, `typedLength`, step type `text-input`), the `fieldTemplates` text key, the command `dryRun`, and a new "Last reviewed" entry for feature 192.
- [ ] T056 [P] Add a CHANGELOG.md entry: the new `TextInput` step, the `textInput.text` key, `typedLength`, the command `dryRun`, "no performance effect on other steps", and the version line (issue #400).
- [ ] T057 [P] Set `**Status**: Implemented` in specs/192-text-input-step/spec.md and add the 192 row to specs/STATUS.md.
- [ ] T058 Run the full suites: `dotnet test "C:\src\GameBot\tests\unit\GameBot.UnitTests.csproj"`, `dotnet test "C:\src\GameBot\tests\integration\GameBot.IntegrationTests.csproj"`, `dotnet test "C:\src\GameBot\tests\contract\GameBot.ContractTests.csproj"`. All must pass. Run `C:\src\GameBot\scripts\ci\measure-shards.ps1` for the projects with new test classes, and split a shard that is more than 20% above the average (docs/ci-time.md).
- [ ] T059 Check that all new and changed text (comments, messages, OpenAPI text, docs, UI text) obeys STE (Principle VI).
- [ ] T060 Run the steps of specs/192-text-input-step/quickstart.md sections 1 and 2 (dry run and save) against a local service. Record the result in the PR description.

---

## Dependencies & Execution Order

### Phase Dependencies

- Setup (Phase 1) → Foundational (Phase 2) → user stories.
- US1 (Phase 3) needs Phase 2. It is the MVP.
- US2 (Phase 4) needs Phase 2. Its device test (T030) needs US1 (T019, T021).
- US3 (Phase 5) needs US1 (`ExecuteTextInputAsync`, T021).
- US4 (Phase 6) needs Phase 2. Its placeholder test needs US2 (T033).
- US5 (Phase 7) needs Phase 2. The parameter control needs T035 (US2).
- Polish (Phase 8) needs all stories.

### Within Each Story

- Write the tests first and make sure that they fail.
- Domain, then emulator, then service, then endpoints, then OpenAPI.

### Parallel Opportunities

- T003, T004, T008, T009, T010 in Phase 2.
- T011 to T016 and T025 in US1 (different files).
- T027 to T030 in US2.
- US4 (T041 to T044) and US5 (T046 to T053) can run in parallel after US2.
- T055 to T057 in Polish.

## Parallel Example: User Story 1

```text
T011 AdbTextEncoderTests          (tests/unit/Emulator/AdbTextEncoderTests.cs)
T012 SessionManagerTextInputTests (tests/unit/Emulator/SessionManagerTextInputTests.cs)
T013 CommandExecutorTextInputTests(tests/unit/Commands/CommandExecutorTextInputTests.cs)
T014 TextInputStepEndpointTests   (tests/integration/Commands/TextInputStepEndpointTests.cs)
T015 TextInputOpenApiTests        (tests/contract/TextInputOpenApiTests.cs)
T016 AdbTextEncoder               (src/GameBot.Emulator/Adb/AdbTextEncoder.cs)
```

## Implementation Strategy

### MVP First

1. Phase 1 and Phase 2.
2. Phase 3 (US1). Stop and check: a literal text types on the device.

### Incremental Delivery

1. US1: literal text (MVP).
2. US2: text from a parameter. This is the request of PNS feature 032.
3. US3: reason codes for failures.
4. US4: command `dryRun`.
5. US5: the command editor.
6. Polish: docs, CHANGELOG, status, full test runs, shard balance.

## Notes

- Do not change the `KeyInput` behaviour (FR-017).
- The step result and the execution log detail item do not hold the typed text (FR-023). They hold the length. The resolved parameter values stay in the execution log, and the ADB debug trace stays as for the other ADB commands. The new `AdbClient` log call for the text input logs the chunk length only.
- The version bump is done in the implementation commit step of the pipeline, before the push.
