---

description: "Task list for feature 164: digit keys and raw key codes in key input steps"
---

# Tasks: Digit Keys and Raw Key Codes in Key Input Steps

**Input**: Design documents from `/specs/164-key-input-digits/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/key-input.md, quickstart.md

**Tests**: Tests are required. Constitution Principle II needs tests for all executable logic. Write each test first and see it fail before the code change.

**Organization**: Tasks are grouped by user story. Story 1 is the MVP.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependency on an open task)
- **[Story]**: The user story of the task (US1, US2, US3)
- Method names use CamelCase with no underscore (Constitution quality gate).
- All new or changed text (comments, messages, docs) MUST use STE (Principle VI).

## Phase 1: Setup

**Purpose**: Confirm a green baseline before any change (Constitution: a red build is a hard stop).

- [X] T001 Run the build and the tests for the whole solution with `dotnet test "C:\src\GameBot\GameBot.sln"`. Run `npm test` in `C:\src\GameBot\src\web-ui`. Record each failure that exists before this feature.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: One helper owns the key rules. All stories use it.

**CRITICAL**: No story work starts before this phase is complete.

- [X] T002 Create `C:\src\GameBot\src\GameBot.Emulator\Session\AndroidKeyCodes.cs`: a public static class `AndroidKeyCodes` with the enum `KeyResolveResult` (`Ok`, `Empty`, `UnknownName`, `OutOfRange`), the constant `MaxRawKeyCode = 300`, the key name map moved from `SessionManager` (old names only, same codes, ignore case), and `TryResolve(string? text, out int keyCode)`. For now `TryResolve` handles `Empty`, a listed name after trim, and `UnknownName`. Add XML comments with inputs, outputs, and error modes (Principle I).
- [X] T003 Create `C:\src\GameBot\tests\unit\Emulator\AndroidKeyCodesTests.cs` with regression tests: every old name keeps its key code, a name matches with any case and with spaces at the start and end, empty text gives `Empty`, an unknown name gives `UnknownName`.
- [X] T004 In `C:\src\GameBot\src\GameBot.Emulator\Session\SessionManager.cs`, make `ResolveAndroidKeyCode` call `AndroidKeyCodes.TryResolve`. Keep the same exceptions and the same "Unknown key name" message. Delete the private `KeyNameMap` field.
- [X] T005 Run the unit tests for the Emulator and Commands areas. All must pass (no behavior change yet).

**Checkpoint**: The helper exists. Behavior is unchanged.

---

## Phase 3: User Story 1 - Send a digit key from a command (Priority: P1) MVP

**Goal**: A key input step with "0" to "9" or "NUMPAD_0" to "NUMPAD_9" sends the right key. A key step that the device does not accept does not show `executed` (FR-001, FR-002, FR-006, FR-008, FR-013).

**Independent Test**: Run a command with one key input step with key "6". The device gets key code 13 and the outcome is `executed`.

### Tests for User Story 1 (write first, see them fail)

- [X] T006 [US1] In `C:\src\GameBot\tests\unit\Emulator\AndroidKeyCodesTests.cs`, add tests: "0" to "9" resolve to 7 to 16; "NUMPAD_0" to "NUMPAD_9" resolve to 144 to 153; "numpad_6" and " 6 " resolve.
- [X] T007 [P] [US1] Create `C:\src\GameBot\tests\unit\Emulator\SessionManagerKeyInputTests.cs`. Follow the setup in `SessionManagerInputResultsTests.cs` and use `FakeAdbSessionClient.cs`. Test: a `key` action with key "6" reaches the fake ADB client as key code 13. Test: a `key` action with key "6a" gives `Dispatched` false and the reason `key: unknown key name '6a'`, in stub mode and with the fake ADB client. Test: a `key` action with the numeric `keyCode` argument 13 still reaches the fake ADB client as 13 (FR-008).
- [X] T008 [P] [US1] In `C:\src\GameBot\tests\unit\Commands\CommandExecutorKeyInputTests.cs`, add tests with the recording session manager: key "6" is passed as the `key` argument unchanged and the outcome is `executed` when `SendResult` is 1. With `SendResult` 0 and key "6a", the outcome status is `not_accepted` and the reason is `key_input_unknown_key`. With `SendResult` 0 and key "6", the reason is `key_input_not_accepted`.
- [X] T009 [US1] In `C:\src\GameBot\tests\unit\Emulator\SessionManagerKeyInputTests.cs`, add tests for the old input path `SendInputsAsync`: key "6" reaches the fake ADB client as key code 13, and key "6a" is not accepted. The `key` action of a sequence step calls this path (`SequenceExecutionService.cs` near line 1202, a private method) and sends the key text unchanged, so these tests cover that step.

### Implementation for User Story 1

- [X] T010 [US1] In `C:\src\GameBot\src\GameBot.Emulator\Session\AndroidKeyCodes.cs`, add the names "0" to "9" (codes 7 to 16) and "NUMPAD_0" to "NUMPAD_9" (codes 144 to 153) to the map.
- [X] T011 [US1] In `C:\src\GameBot\src\GameBot.Emulator\Session\SessionManager.cs`, change the `key` branch of `DispatchOneInputAsync`: call `AndroidKeyCodes.TryResolve` before the stub check. Return `(false, "key: unknown key name '<text>'")` for `UnknownName`. For `Empty`, return `(false, "key: key name is empty")`. Keep the `keyCode` argument path as it is.
- [X] T012 [US1] In `C:\src\GameBot\src\GameBot.Service\Services\CommandExecutor.cs`, change the `KeyInput` branch. When `keyAccepted` is more than 0, keep the `executed` outcome. When it is 0, call `AndroidKeyCodes.TryResolve` and return the status `not_accepted` with the reason `key_input_unknown_key` (for `Empty` and `UnknownName`) or `key_input_not_accepted`. Do not add a check before the dispatch (research R-004).
- [X] T013 [US1] Run the tests of T006 to T009. All must pass. Run the `CommandExecutor*` and `SessionManager*` tests to find a test that expects `executed` with accepted 0. Fix such a test only if it checks the failed case.

**Checkpoint**: Story 1 works. The digit 6 reaches the device from a saved command and from a sequence step.

---

## Phase 4: User Story 2 - Send any Android key by its raw key code (Priority: P2)

**Goal**: A whole number from 0 to 300 works as the key text. A number outside the range gives a clear reason (FR-003, FR-004, FR-005).

**Independent Test**: Run a key input step with key "85". The device gets key code 85. With key "301", no key is sent and the outcome gives the reason `key_input_code_out_of_range`.

### Tests for User Story 2 (write first, see them fail)

- [X] T014 [US2] In `C:\src\GameBot\tests\unit\Emulator\AndroidKeyCodesTests.cs`, add tests: "85" gives 85; "300" gives 300; "301" gives `OutOfRange`; "99999999999" gives `OutOfRange`; " 85 " gives 85; "-1", "+5", "6.5", and "6a" give `UnknownName`; "6" gives 13 and not 6 (priority); "0" gives 7 and not 0.
- [X] T015 [P] [US2] In `C:\src\GameBot\tests\unit\Emulator\SessionManagerKeyInputTests.cs`, add tests: key "85" reaches the fake ADB client as key code 85; key "301" gives `Dispatched` false and the reason `key: key code 301 is outside 0 to 300`, and the fake ADB client gets no key.
- [X] T016 [P] [US2] In `C:\src\GameBot\tests\unit\Commands\CommandExecutorKeyInputTests.cs`, add a test: `SendResult` 0 and key "301" give the status `not_accepted` and the reason `key_input_code_out_of_range`.
- [X] T017 [US2] In `C:\src\GameBot\tests\unit\Emulator\SessionManagerKeyInputTests.cs`, add tests for the old input path `SendInputsAsync`: key "85" reaches the fake ADB client as key code 85, and key "301" is not accepted and sends no key. A sequence step with a `key` action gets only the generic reason "input was not accepted" in this case. Record this in the PR.

### Implementation for User Story 2

- [X] T018 [US2] In `C:\src\GameBot\src\GameBot.Emulator\Session\AndroidKeyCodes.cs`, add the raw key code step to `TryResolve` after the name lookup. If the trimmed text has only ASCII digits, read it with `NumberStyles.None` and `CultureInfo.InvariantCulture`. A value from 0 to `MaxRawKeyCode` gives `Ok`. Any other value, also a number too large to read, gives `OutOfRange`. Other text gives `UnknownName`.
- [X] T019 [US2] In `C:\src\GameBot\src\GameBot.Emulator\Session\SessionManager.cs`, map `OutOfRange` in `DispatchOneInputAsync` to the reason `key: key code <n> is outside 0 to 300`. Make `ResolveAndroidKeyCode` throw a `KeyNotFoundException` with the text `Key code '<text>' is outside 0 to 300` for `OutOfRange`.
- [X] T020 [US2] In `C:\src\GameBot\src\GameBot.Service\Services\CommandExecutor.cs`, map `OutOfRange` to the reason `key_input_code_out_of_range` in the `KeyInput` branch.
- [X] T021 [US2] Run the tests of T014 to T017. All must pass.

**Checkpoint**: Stories 1 and 2 both work.

---

## Phase 5: User Story 3 - Find the key rules in the Swagger docs (Priority: P2)

**Goal**: The Swagger docs and the web UI hint text show the key rules with examples (FR-010, FR-011, FR-014).

**Independent Test**: Open the Swagger JSON. The `key` property of `KeyInputConfigDto` names the digits, the number pad names, the raw key code rule, and has both examples.

### Tests for User Story 3 (write first, see them fail)

- [X] T022 [P] [US3] Create `C:\src\GameBot\tests\contract\KeyInputOpenApiTests.cs`. Follow `tests\contract\PrimitiveTapHoldOpenApiTests.cs`. Test: the `key` property of `KeyInputConfigDto` has a description that contains "NUMPAD_0", "0 to 300", the digit example "6", and the raw key code example "85". Test: the `payload` description of the primitive action has the `key` line with the same rule. Test: the help text of the key action definition points to the rule.
- [X] T023 [P] [US3] Create `C:\src\GameBot\src\web-ui\src\components\commands\__tests__\KeyInputPanel.test.tsx`. Follow `ActionTypeSelector.test.tsx`. Test: the placeholder has a digit example and a raw code example; the help line shows the rule; the confirm button sends the trimmed key "6" and "85".

### Implementation for User Story 3

- [X] T024 [US3] Create `C:\src\GameBot\src\GameBot.Service\Swagger\KeyInputSchemaFilter.cs`. It is an `ISchemaFilter` for `KeyInputConfigDto`. It holds the shared description constant (key names, digit names, number pad names, raw key code rule 0 to 300, priority rule, examples "6" and "85") and sets the description and the example of the `key` property. Follow `PrimitiveTapHoldSchemaFilter.cs`. Use STE.
- [X] T025 [US3] Register `options.SchemaFilter<KeyInputSchemaFilter>();` in `C:\src\GameBot\src\GameBot.Service\GameBotServiceSetup.cs` next to the other schema filters.
- [X] T026 [P] [US3] In `C:\src\GameBot\src\GameBot.Service\Swagger\PrimitiveActionSchemaFilter.cs`, change the `ActionTypes.Key` text to use the digit names, the number pad names, and the raw key code rule from the shared constant of T024.
- [X] T027 [P] [US3] In `C:\src\GameBot\src\GameBot.Service\Swagger\SwaggerConfig.cs` (key action definition, near line 1845), change the help text of the `key` attribute to use the shared constant of T024. Keep the `keyCode` attribute as it is.
- [X] T028 [P] [US3] In `C:\src\GameBot\src\web-ui\src\components\commands\KeyInputPanel.tsx`, change the placeholder to `e.g. Enter, Escape, 6, a, 85`. Add a short help line below the field with the digit and raw code rule, in STE. Do not change the layout (phone support level: full).
- [X] T029 [US3] Run the tests of T022 and T023. Run `PrimitiveActionTypesOpenApiTests` and `SequenceActionTypeSyncTests` to check that no Swagger test breaks.

**Checkpoint**: All three stories work.

---

## Phase 6: Polish and Cross-Cutting Concerns

- [X] T030 [P] Update `C:\src\GameBot\docs\architecture.md`: add the key text rule (names, digits, number pad, raw key code 0 to 300, priority) and the `not_accepted` outcome of the key input step. Refresh the "Last reviewed" date (Principle V).
- [X] T031 [P] Add an entry to `C:\src\GameBot\CHANGELOG.md` in STE: digit keys, number pad keys, raw key codes, the changed outcome of a key step that the device does not accept, and the Swagger docs. Mention the version bump.
- [X] T032 [P] Set the `**Status**:` line of `C:\src\GameBot\specs\164-key-input-digits\spec.md` to the correct value, and make `C:\src\GameBot\specs\STATUS.md` consistent (Principle V).
- [X] T033 Run the full build and all tests again: `dotnet test "C:\src\GameBot\GameBot.sln"`, then `npm test` and the lint command in `C:\src\GameBot\src\web-ui`. Fix each warning that the change adds. A red result is a hard stop.
- [X] T034 Do the steps of `C:\src\GameBot\specs\164-key-input-digits\quickstart.md` (the failure cases and the Swagger check). Result: the unit and contract tests cover these steps. No run on a real device was done.
- [X] T035 Before the push for the PR: run `git fetch origin master`. Compare `C:\src\GameBot\installer\versioning\version.override.json` with `origin/master`. If `minor` or `major` changed, set `patch` to "0". If not, set `patch` to the `patch` on `origin/master` plus 1. Set `updatedBy` and `updatedAtUtc`.

---

## Dependencies and Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependency.
- **Foundational (Phase 2)**: Needs Phase 1. It blocks all stories.
- **US1 (Phase 3)**: Needs Phase 2. This is the MVP.
- **US2 (Phase 4)**: Needs Phase 2. It edits the same three source files as US1 (T018 to T020 after T010 to T012), so do it after US1.
- **US3 (Phase 5)**: Needs Phase 2 only. It touches other files, so it can run in parallel with US1 and US2.
- **Polish (Phase 6)**: Needs all stories. T035 runs last, before the push.

### Within Each Story

- Tests first, and see them fail.
- The helper before `SessionManager` and `CommandExecutor`.
- Tasks in the same file are not parallel: `AndroidKeyCodesTests.cs` (T003, T006, T014), `SessionManagerKeyInputTests.cs` (T007, T015), `CommandExecutorKeyInputTests.cs` (T008, T016), `SequenceRunnerPrimitiveInputDispatchTests.cs` (T009, T017).

### Parallel Opportunities

- T007, T008, and T009 (different test files).
- T015, T016, and T017.
- T022 and T023 (C# contract test and web UI test).
- T026, T027, and T028 (different files).
- T030, T031, and T032 (docs).
- US3 as a whole, with US1 and US2.

## Parallel Example: User Story 3

```text
Task: "Contract test in tests/contract/KeyInputOpenApiTests.cs"            (T022)
Task: "Web UI test in src/web-ui/.../__tests__/KeyInputPanel.test.tsx"     (T023)
Task: "Key text in PrimitiveActionSchemaFilter.cs"                         (T026)
Task: "Help text in SwaggerConfig.cs"                                      (T027)
Task: "Placeholder and help line in KeyInputPanel.tsx"                     (T028)
```

## Implementation Strategy

### MVP First (User Story 1)

1. Phase 1 and Phase 2.
2. Phase 3 (US1).
3. Stop. Check that the digit 6 reaches the device. A PR for this alone is valid.

### Incremental Delivery

1. Add US2 (raw key codes). Test.
2. Add US3 (Swagger and web UI text). Test.
3. Do the polish phase and the version bump.

## Notes

- The `keyCode` argument path does not change.
- The behavior change: a key step that the device does not accept shows `not_accepted`, not `executed` (research R-004, FR-013).
- Commit after each task or small group of tasks.
