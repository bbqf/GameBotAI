---

description: "Task list for the fix of the TextInput step (issue #404)"
---

# Tasks: TextInput step types all characters

**Input**: Design documents from `specs/195-fix-text-input-chars/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/text-input-typing.md, quickstart.md

**Tests**: The spec requires tests (FR-014, acceptance criterion 4). This is a bug fix, so write each test first and make sure that it fails before the fix (Constitution II).

**Organization**: The tasks are grouped by user story. Each story can be tested alone.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependency on an incomplete task)
- **[Story]**: The user story of the task (US1, US2, US3)
- All paths are relative to the repository root `C:\src\GameBot`. Use absolute paths in PowerShell commands.

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Make sure that the baseline is green before the change.

- [ ] T001 Build the solution `GameBot.sln` and run `tests/unit/GameBot.UnitTests.csproj` with the filter `FullyQualifiedName~TextInput|FullyQualifiedName~AdbTextEncoder`. Record the result. A red baseline is a hard stop (constitution "Release blocker").

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: The pause setting and the test seam. All stories need them.

- [ ] T002 [P] Write tests (they must fail first) for `AppConfig.NormalizeTextInputCharDelayMs` in the new file `tests/unit/Config/AppConfigTextInputDelayTests.cs`: null -> 100, -5 -> 100, 0 -> 0, 150 -> 150, 2000 -> 2000, 5000 -> 2000. Also test that a new `AppConfig` has `TextInputCharDelayMs` = 100.
- [ ] T003 Add to `src/GameBot.Domain/Config/AppConfig.cs`: the constants `DefaultTextInputCharDelayMs = 100` and `MaxTextInputCharDelayMs = 2000`, the property `TextInputCharDelayMs { get; set; } = 100` (doc comment: key `GAMEBOT_TEXT_INPUT_CHAR_DELAY_MS`, default, range 0 to 2000, 0 = no pause), and `public static int NormalizeTextInputCharDelayMs(int? value)` (plan D-3). Make T002 pass.
- [ ] T004 [P] Write tests (they must fail first) for the run-time apply in the new file `tests/unit/Config/ConfigApplierTextInputDelayTests.cs` (use the pattern of `tests/unit/Config/ConfigApplierJitterTests.cs`): key absent -> 100, "abc" -> 100, "-1" -> 100, "0" -> 0, "250" -> 250, "9999" -> 2000.
- [ ] T005 In `src/GameBot.Service/Services/IConfigApplier.cs` (`ConfigApplier.Apply`), read `GAMEBOT_TEXT_INPUT_CHAR_DELAY_MS` as a nullable int (add a private helper `GetNullableInt`) and set `_appConfig.TextInputCharDelayMs = AppConfig.NormalizeTextInputCharDelayMs(value)`. Make T004 pass.
- [ ] T006 [P] In `src/GameBot.Service/GameBotServiceSetup.cs` (the method that builds `AppConfig` from the environment), read `GAMEBOT_TEXT_INPUT_CHAR_DELAY_MS` with `int.TryParse`, pass the parsed value (or null) to `AppConfig.NormalizeTextInputCharDelayMs`, and set `TextInputCharDelayMs` in the `AppConfig` initializer.
- [ ] T007 [P] In `src/GameBot.Service/Services/ConfigSnapshotService.cs`, add `["GAMEBOT_TEXT_INPUT_CHAR_DELAY_MS"] = 100` to the default list, after `GAMEBOT_ADB_RETRY_DELAY_MS`.
- [ ] T008 In `src/GameBot.Emulator/Session/SessionManager.cs`, add the seam `internal Func<int, CancellationToken, Task> CharacterPause { get; set; } = static (ms, ct) => Task.Delay(ms, ct);` with a doc comment (STE: the unit tests replace it to record the pause).
- [ ] T009 In `tests/unit/Emulator/FakeAdbSessionClient.cs`: add the parameter `int textInputCharDelayMs = 0` to `SeamSessionManager.Create` and set it on the `AppConfig`. Add `SeamSessionManager.RecordPauses(SessionManager manager, FakeAdbSessionClient device)`: it sets `manager.CharacterPause` to a function that adds `pause:<ms>` to `device.Calls` (under the same lock as the other calls) and returns `Task.CompletedTask`, or throws `OperationCanceledException` when the token is cancelled. Use the current `FailTextChunk` for the failure tests; do not add a new failure option.

**Checkpoint**: The setting exists, the readers clamp it, and the tests can record the pause. The current tests stay green (the seam default pause is 0).

---

## Phase 3: User Story 1 - Type a number into a game field that drops burst input (Priority: P1) 🎯 MVP

**Goal**: The device gets one input command for each character, with the pause between two commands.

**Independent Test**: `SessionManagerTextInputTests` with the fake device: the text `250` and the pause 100 give the calls `text:2`, `pause:100`, `text:5`, `pause:100`, `text:0`, and the result `typedLength` 3.

### Tests for User Story 1 (write first, make sure that they fail)

- [ ] T010 [P] [US1] In `tests/unit/Emulator/AdbTextEncoderTests.cs`, replace the `Split` tests with `SplitCharacters` tests: `"250"` -> `"2"`, `"5"`, `"0"`; `"25 0"` -> `"2"`, `"5"`, `" "`, `"0"`; `"%s'"` -> `"%"`, `"s"`, `"'"`; `""` -> empty; null -> `ArgumentNullException`. Keep the `IsSpace` and `QuoteForDeviceShell` tests.
- [ ] T011 [US1] In `tests/unit/Emulator/SessionManagerTextInputTests.cs`, change `Build` to take `textInputCharDelayMs` and to call `SeamSessionManager.RecordPauses`. Replace `ANumberGoesToTheDeviceAsOneTextCommand` with `ANumberGoesToTheDeviceOneCharacterAtATimeWithThePause`: text `250`, pause 100 -> `device.Calls` equal `text:2`, `pause:100`, `text:5`, `pause:100`, `text:0`; accepted 1; `typedLength` 3.
- [ ] T012 [US1] In the same file, change `ASpaceGoesToTheDeviceAsKeyEvent62`: text `25 0`, pause 100 -> calls `text:2`, `pause:100`, `text:5`, `pause:100`, `key:62`, `pause:100`, `text:0`; `typedLength` 4.
- [ ] T013 [US1] In the same file, add `APauseOfZeroSendsTheCharactersWithNoPause`: text `250`, pause 0 -> calls `text:2`, `text:5`, `text:0`.
- [ ] T014 [US1] In the same file, add `AOneCharacterTextHasNoPause` (text `7`, pause 100 -> calls `text:7`) and `ATextWithPercentSAndAQuoteGoesOneCharacterAtATime` (text `%s'`, pause 0 -> calls `text:%`, `text:s`, `text:'`). Also extend `StubModeCountsTheActionAndDoesNotSetTheTypedLength`: create the manager with `textInputCharDelayMs: 100` and call `RecordPauses`; expect no `pause:` call in `device.Calls` (spec Edge Cases, stub mode).
- [ ] T015 [US1] In the same file, change `TheLivenessTrackerRecordsOneInputForEachAdbCommand`: text `2 5` gives 3 starts and 3 completions (no change in the count); text `250` gives 3 starts and 3 completions (new case).
- [ ] T016 [US1] In the same file, add `ACancelledPauseStopsTheSend` (FR-009). Do not use `RecordPauses` in this test. Set `manager.CharacterPause` to a test function: it adds `pause:<ms>` to `device.Calls`, calls `cts.Cancel()`, and throws `OperationCanceledException(cts.Token)`. Send the text `250` with the pause 100 and `cts.Token`. Expect `OperationCanceledException`, and the calls `text:2`, `pause:100` only.
### Implementation for User Story 1

- [ ] T017 [US1] In `src/GameBot.Emulator/Adb/AdbTextEncoder.cs`, replace `Split` with `public static IReadOnlyList<string> SplitCharacters(string text)` (plan D-1). Update the class doc comment (STE): the service sends one character in each command. Make T010 pass.
- [ ] T018 [US1] In `src/GameBot.Emulator/Adb/AdbClient.cs`, change the doc comment of `InputTextAsync`: the parameter is one character from `AdbTextEncoder.SplitCharacters` that is not a space.
- [ ] T019 [US1] In `src/GameBot.Emulator/Session/SessionManager.cs`, change `SendTextAsync` as in plan D-2: `SplitCharacters`, a pause of `Math.Clamp(_appConfig.TextInputCharDelayMs, 0, AppConfig.MaxTextInputCharDelayMs)` through `CharacterPause` before each character after the first (only when the pause > 0), `typed += 1` after each accepted character, stop at the first failed character. Do not write the text or a character into a log (FR-011); a log can hold the count only. Update the doc comment (STE). Make T011 to T016 pass.
- [ ] T020 [US1] In `src/GameBot.Service/Services/CommandExecutor.cs`, update only the doc comment of `ExecuteTextInputAsync` (STE): the device path types one character at a time with the pause from `GAMEBOT_TEXT_INPUT_CHAR_DELAY_MS`. No logic change (plan D-4).
- [ ] T021 [US1] Create `tests/unit/Commands/CommandExecutorTextInputDeviceTests.cs`: a `CommandExecutor` (constructed as in `tests/unit/Commands/CommandExecutorTextInputTests.cs`, with the fake command repository, trigger repository, and session context cache copied or shared) on a seam `SessionManager` (`SeamSessionManager.Create` with `textInputCharDelayMs: 100`, `RecordPauses`, and `CreateDeviceSession`). Test `ALiteralTextGoesOneCharacterAtATimeAndIsExecuted`: text `250` -> calls `text:2`, `pause:100`, `text:5`, `pause:100`, `text:0`; outcome `executed`, `typedLength` 3. `CreateSession` sets the status `Running`, so `ForceExecuteDetailedAsync` accepts the session with no more setup.

**Checkpoint**: US1 works. The device gets each character separately with the pause.

---

## Phase 4: User Story 2 - Text from a parameter placeholder (Priority: P1)

**Goal**: A text from a placeholder goes to the device one character at a time, as a literal text does.

**Independent Test**: The executor with a seam `SessionManager` and a command parameter `fighterBatch` (type number, default `250`) gives the calls `text:2`, `pause:100`, `text:5`, `pause:100`, `text:0`.

- [ ] T022 [US2] In `tests/unit/Commands/CommandExecutorTextInputDeviceTests.cs`, add `ATextFromTheFieldTemplatesKeyGoesOneCharacterAtATime`: the step has `TextInput = { Text = "" }` and `FieldTemplates["textInput.text"] = "{{fighterBatch}}"`; the command has the parameter `fighterBatch` (`ParameterValueType.Number`, default `"250"`), as in `tests/integration/Commands/TextInputParameterTests.cs`. Expect the calls of T021 and `executed` with `typedLength` 3.
- [ ] T023 [US2] In the same file, add `AnInlinePlaceholderTextGoesOneCharacterAtATime`: `TextInput = { Text = "{{fighterBatch}}" }`, the same parameter. Expect the same calls and outcome.
- [ ] T024 [US2] Run `tests/integration/Commands/TextInputParameterTests.cs`. It must stay green with no change (the resolved text in the input action stays `250` or `100`; FR-010).

**Checkpoint**: US1 and US2 work.

---

## Phase 5: User Story 3 - Correct result when the device does not accept a character (Priority: P2)

**Goal**: The result counts only the accepted characters and is not `executed` after a failure.

**Independent Test**: The fake device fails the character `0` of `250` -> `not_accepted`, `text_input_not_accepted`, `typedLength` 2.

- [ ] T025 [US3] In `tests/unit/Emulator/SessionManagerTextInputTests.cs`, replace `AFailedChunkStopsTheSendAndKeepsTheTypedLength` with a theory `AFailedCharacterStopsTheSendAndCountsTheAcceptedCharacters`: text `250`, pause 0, `FailTextChunk` = `"2"` -> calls `text:2`, `typedLength` 0; `"5"` -> calls `text:2`, `text:5`, `typedLength` 1; `"0"` -> calls `text:2`, `text:5`, `text:0`, `typedLength` 2. Accepted is 0 in each case.
- [ ] T026 [US3] In the same file, change `AChunkGetsTheConfiguredAdbRetries` to `EachCharacterGetsTheAdbRetriesWithNoPauseBetweenRetries`: adbRetries 2, pause 100, `FailTextChunk` = `"5"`, text `250` -> calls `text:2`, `pause:100`, `text:5`, `text:5`, `text:5`; accepted 0; `typedLength` 1.
- [ ] T027 [US3] In `tests/unit/Commands/CommandExecutorTextInputDeviceTests.cs`, add `AFailedCharacterGivesNotAcceptedWithTheAcceptedCount`: `FailTextChunk` = `"0"`, text `250` -> outcome `not_accepted`, reason `text_input_not_accepted`, `typedLength` 2, `Accepted` 0.
- [ ] T028 [US3] Make T025 to T027 pass. The logic of T019 must give these results with no more change; fix `SendTextAsync` if a test fails.

**Checkpoint**: All user stories work.

---

## Phase 6: Polish & Cross-Cutting Concerns

- [ ] T029 [P] In `src/GameBot.Service/Swagger/TextInputSchemaFilter.cs`, change `TextDescription` and `TypedLengthDescription` as in contracts/text-input-typing.md, section 1 (STE). Extend `tests/contract/TextInputOpenApiTests.cs`: the `text` description contains `GAMEBOT_TEXT_INPUT_CHAR_DELAY_MS`, and the `typedLength` description says that it counts the characters that the device accepted.
- [ ] T030 [P] In `ENVIRONMENT.md`, add the entry `GAMEBOT_TEXT_INPUT_CHAR_DELAY_MS` after `GAMEBOT_ADB_RETRY_DELAY_MS`: purpose (pause in ms between two characters of a TextInput step; 0 = no pause; range 0 to 2000), used in `SessionManager`, default `100`.
- [ ] T031 [P] In `docs/architecture.md`, change the TextInput paragraph (near the text "`AdbTextEncoder` splits the text into chunks"): one ADB command for each character (`input text '<c>'`, space as key event 62), the pause `GAMEBOT_TEXT_INPUT_CHAR_DELAY_MS` (default 100, 0 to 2000) between two characters, `typedLength` = the characters that the device accepted. Remove the sentence "A number is one chunk and one ADB command." Refresh the "Last reviewed" date to 2026-11-19. Write in STE.
- [ ] T032 [P] In `specs/192-text-input-step/spec.md`, set `**Status**: Implemented (iterated by 195)`. In `specs/STATUS.md`, change row 192 to `Implemented (iterated by 195)` and add the row `| 195 | Fix TextInput Characters | Implemented |` after row 194. Set `**Status**: Implemented` in `specs/195-fix-text-input-chars/spec.md`.
- [ ] T033 [P] In `CHANGELOG.md`, under `## [Unreleased]`, add a "Fixed" entry in STE: the TextInput step sends one character at a time with a pause (`GAMEBOT_TEXT_INPUT_CHAR_DELAY_MS`, default 100 ms), so game fields get all characters; `typedLength` counts the accepted characters (#404).
- [ ] T034 Search `src/` and `tests/` for other uses of `AdbTextEncoder.Split` and for test expectations of one chunk (for example `TextChunks.Should().Equal("250")`). Change each one to the per-character behavior.
- [ ] T035 Build `GameBot.sln` with zero warnings that are errors (analyzers on), and run all test projects (`tests/unit`, `tests/integration`, `tests/contract`). All tests must pass. Check the run time of the changed unit tests (no real waits).
- [ ] T036 Check all new and changed text (code comments, OpenAPI text, docs, changelog) against STE (Constitution VI).
- [ ] T037 Write the performance note of plan.md ("Performance Note (for the PR)") into the file `specs/195-fix-text-input-chars/pr-notes.md`, for the PR description (Constitution IV, SC-004).
- [ ] T038 Device check (SC-001, SC-002): if a device with the game is available, do quickstart.md section 2 (10 tries of `250`) and record the result in `specs/195-fix-text-input-chars/pr-notes.md`. If no device is available, record in the same file that the device check is open and that the reporter of issue #404 must do it.

Note: the version bump in `installer/versioning/version.override.json` and its CHANGELOG line are not tasks here. The pipeline step 10 does them before the commit (constitution "Version bump").

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: no dependency.
- **Foundational (Phase 2)**: after Setup. Blocks all stories.
- **US1 (Phase 3)**: after Phase 2. T017 before T019. T019 after T011 to T016 (the tests fail first).
- **US2 (Phase 4)**: after T019 and T021 (it uses the file and the fixture of T021).
- **US3 (Phase 5)**: after T019. T027 after T021.
- **Polish (Phase 6)**: after all stories. T035 is the last code check. T036 to T038 come after T035.

### Within Each Phase

- T002 before T003. T004 before T005. T008 before T009.
- T011 to T016 change the same file: run them in order.

### Parallel Opportunities

- T002, T004, T006, T007 (different files).
- T010 with T011 (different files).
- T029 to T033 (different files).

## Parallel Example: Phase 2

```text
Task: "T002 Write tests for NormalizeTextInputCharDelayMs in tests/unit/Config/AppConfigTextInputDelayTests.cs"
Task: "T004 Write tests for the run-time apply in tests/unit/Config/ConfigApplierTextInputDelayTests.cs"
Task: "T006 Read GAMEBOT_TEXT_INPUT_CHAR_DELAY_MS in src/GameBot.Service/GameBotServiceSetup.cs"
Task: "T007 Add the default to src/GameBot.Service/Services/ConfigSnapshotService.cs"
```

## Implementation Strategy

### MVP First (User Story 1)

1. Do Phase 1 and Phase 2.
2. Do Phase 3 (US1). Stop and run the tests of US1.
3. US1 alone fixes the measurement of issue #404.

### Incremental Delivery

1. US2 proves the placeholder path (the troop training task).
2. US3 proves the result for a failure.
3. Polish: docs, OpenAPI text, status lines, changelog, full test run.
