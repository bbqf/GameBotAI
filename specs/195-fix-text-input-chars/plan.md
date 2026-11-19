# Implementation Plan: TextInput step types all characters

**Branch**: `195-fix-text-input-chars` | **Date**: 2026-11-19 | **Spec**: [spec.md](./spec.md)
**Input**: Feature specification from `specs/195-fix-text-input-chars/spec.md` (GitHub issue #404)

## Summary

A `TextInput` step sends a number such as `250` as one `adb shell input text` command. Game input fields drop the later characters of this burst. The fix sends one input command for each character, with a pause between two characters. The pause comes from the new service setting `GAMEBOT_TEXT_INPUT_CHAR_DELAY_MS` (default 100 ms, range 0 to 2000). Each character has its own command, so `typedLength` counts the accepted characters exactly, and the result is `executed` only when the device accepted all of them. The change is in the device path of the input action `text` (`SessionManager.SendTextAsync`), so literal texts and texts from placeholders both get the fix.

## Technical Context

**Language/Version**: C# (`LangVersion` preview) / .NET 9 (service, domain, emulator); no web UI change
**Primary Dependencies**: ASP.NET Core minimal APIs, Swashbuckle (OpenAPI descriptions), ADB through `AdbClient`
**Storage**: N/A (no stored data changes)
**Testing**: xUnit + FluentAssertions (`tests/unit`, `tests/integration`, `tests/contract`)
**Target Platform**: Windows service host with Android emulators (LDPlayer) through ADB
**Project Type**: web service (backend) with a web UI (not changed)
**Performance Goals**: A text of 3 characters takes at most 1 second more than before (SC-004). One character costs one ADB command (50 to 150 ms) plus the pause.
**Constraints**: The unit tests do not wait for real pauses (deterministic, fast). No change to `KeyInput`, OCR, placeholder resolution, or the stored command format.
**Scale/Scope**: Typical texts have 1 to 5 characters. The maximum text has 500 characters.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

*NON-NEGOTIABLE*: If `build` or required `test` runs are failing (local or CI), implementation progression is blocked until failures are fixed or a documented maintainer waiver exists.

*NON-NEGOTIABLE*: All text in this plan and in the artifacts it produces (research, data model, contracts, quickstart, tasks, code comments, user-facing messages) MUST obey Simplified Technical English (Constitution Principle VI).

*Phone check (Constitution Principle III)*: The feature does not change the web UI. Phone support level: not applicable.

| Gate | Status | Note |
|---|---|---|
| I. Code quality | PASS | Small change in `SessionManager`, `AdbTextEncoder`, `AppConfig`, the two config readers, and the OpenAPI text. No new dependency. Doc comments on the new public members. |
| II. Testing | PASS | Bug fix: a test that fails first (the call order `text:2`, `pause`, `text:5`, `pause`, `text:0` fails with the burst code). Unit tests for the order, the pause, the value 0, a space, a failed character, retries, the clamp, and the placeholder path. No real waits in the tests (R-005). |
| III. UX consistency | PASS | The step result fields and reason codes stay. The setting follows the `GAMEBOT_ADB_*` pattern. No web UI change. |
| IV. Performance | PASS | Declared in Technical Context and research R-008. The perf note goes into the PR. |
| V. Living documentation | PASS | Update `docs/architecture.md` (TextInput paragraph and "Last reviewed"), `ENVIRONMENT.md`, `CHANGELOG.md`. Spec 192 `Status` becomes "Implemented (iterated by 195)". `specs/STATUS.md` gets row 195 and the changed row 192. |
| VI. STE | PASS | All artifacts are in STE. Code comments and the OpenAPI text are in STE. |
| Version bump | PASS (phase 2) | Bump `patch` in `installer/versioning/version.override.json` before the push (pipeline step 10). |

Post-design re-check: PASS. The design adds no project, no pattern, and no stored data.

## Project Structure

### Documentation (this feature)

```text
specs/195-fix-text-input-chars/
├── plan.md              # This file
├── research.md          # Phase 0 output
├── data-model.md        # Phase 1 output
├── quickstart.md        # Phase 1 output
├── contracts/
│   └── text-input-typing.md
├── checklists/
│   └── requirements.md
├── pr-notes.md          # Implementation output (T037, T038): perf note and device check result for the PR
└── tasks.md             # Phase 2 output (/speckit-tasks)
```

### Source Code (repository root)

```text
src/
├── GameBot.Domain/Config/AppConfig.cs                 # + TextInputCharDelayMs, + NormalizeTextInputCharDelayMs
├── GameBot.Emulator/Adb/AdbTextEncoder.cs             # Split -> SplitCharacters
├── GameBot.Emulator/Adb/AdbClient.cs                  # doc comment of InputTextAsync
├── GameBot.Emulator/Session/SessionManager.cs         # SendTextAsync: one command per character + pause; CharacterPause seam
├── GameBot.Service/GameBotServiceSetup.cs             # read GAMEBOT_TEXT_INPUT_CHAR_DELAY_MS at start
├── GameBot.Service/Services/IConfigApplier.cs         # apply the setting at run time
├── GameBot.Service/Services/ConfigSnapshotService.cs  # default 100 in the default list
└── GameBot.Service/Swagger/TextInputSchemaFilter.cs   # text and typedLength descriptions

tests/
├── unit/Emulator/AdbTextEncoderTests.cs               # SplitCharacters tests
├── unit/Emulator/SessionManagerTextInputTests.cs      # order, pause, 0, space, failure, retries
├── unit/Emulator/FakeAdbSessionClient.cs              # SeamSessionManager: textInputCharDelayMs + pause record
├── unit/Config/ConfigApplierTextInputDelayTests.cs    # new: clamp and run-time apply
├── unit/Config/AppConfigTextInputDelayTests.cs        # new: NormalizeTextInputCharDelayMs
└── unit/Commands/CommandExecutorTextInputDeviceTests.cs  # new: executor + seam SessionManager, literal and placeholder text

docs/architecture.md, ENVIRONMENT.md, CHANGELOG.md, specs/STATUS.md, specs/192-text-input-step/spec.md
```

**Structure Decision**: The current backend layout. The change stays in the device path of the input action `text` (research R-003). No new project.

## Design Details

### D-1: `AdbTextEncoder.SplitCharacters`

`public static IReadOnlyList<string> SplitCharacters(string text)`: throws `ArgumentNullException` for null; returns one string of length 1 for each character, in order. `IsSpace`, `SpaceKeyCode`, and `QuoteForDeviceShell` stay. Remove `Split` (research R-004).

### D-2: `SessionManager.SendTextAsync`

```text
parts = AdbTextEncoder.SplitCharacters(text)
pause = _appConfig.TextInputCharDelayMs
typed = 0
for i in 0..parts.Count-1:
  if i > 0 and pause > 0: await CharacterPause(pause, ct)
  ok = IsSpace(part) ? SendRecordedInputAsync("key", KeyEvent 62) : SendRecordedInputAsync("text", InputText(part))
  if !ok: write typedLength = typed; return false
  typed += 1
write typedLength = typed; return true
```

- `CharacterPause` is `internal Func<int, CancellationToken, Task> CharacterPause { get; set; } = static (ms, ct) => Task.Delay(ms, ct);`.
- The pause value comes from `_appConfig` at each send, so a run-time change applies to the next step.
- The pause value in `_appConfig` is already in the range 0 to 2000 (readers clamp it). `SendTextAsync` also applies `Math.Clamp(value, 0, 2000)`, because tests and other code can set `AppConfig` directly.
- Cancellation: `OperationCanceledException` from the pause or a send goes up (research R-007). The current `try` in `SendInputsAsync` does not catch it.
- Logs: the new code writes no text and no character into logs (FR-011). A log can hold the count only.
- Update the doc comment of `SendTextAsync` (STE).

### D-3: Setting

- `AppConfig.TextInputCharDelayMs { get; set; } = 100;` with a doc comment that names the key, the default, and the range.
- `public static int NormalizeTextInputCharDelayMs(int? value)`: null or negative gives 100; larger than 2000 gives 2000; other values stay. Constants `DefaultTextInputCharDelayMs = 100`, `MaxTextInputCharDelayMs = 2000`.
- `GameBotServiceSetup`: parse the environment variable (`int.TryParse`), then call `NormalizeTextInputCharDelayMs`.
- `ConfigApplier.Apply`: read the key with the new private helper `GetNullableInt` (null when the key is absent or is not an integer), then call `NormalizeTextInputCharDelayMs`.
- `ConfigSnapshotService`: add `["GAMEBOT_TEXT_INPUT_CHAR_DELAY_MS"] = 100` after `GAMEBOT_ADB_RETRY_DELAY_MS`.

### D-4: Executor

No logic change in `CommandExecutor.ExecuteTextInputAsync`. It already reads `typedLength` from the args and reports `executed` only when `accepted > 0`. With one text action, `accepted` is 1 only when `SendTextAsync` returns true (all characters accepted). Update its doc comment: the device path sends one character at a time.

### D-5: Documentation

- `TextInputSchemaFilter.TextDescription` and `TypedLengthDescription`: see contracts/text-input-typing.md, section 1.
- `docs/architecture.md`: change the TextInput paragraph (one ADB command for each character, the pause setting, the definition of `typedLength`) and the "Last reviewed" date.
- `ENVIRONMENT.md`: a new entry `GAMEBOT_TEXT_INPUT_CHAR_DELAY_MS` after `GAMEBOT_ADB_RETRY_DELAY_MS`.
- `CHANGELOG.md`: an entry under `[Unreleased]` (Fixed) with the issue #404.
- `specs/192-text-input-step/spec.md`: `**Status**: Implemented (iterated by 195)`. `specs/STATUS.md`: row 192 changes the same way, and a new row 195.

## Test Plan

| Test | Location | Covers |
|---|---|---|
| A number goes as one text command for each character, with the pause between them and no pause after the last | `SessionManagerTextInputTests` | FR-001, FR-002, US1-1 |
| A space goes as key event 62 between character commands, with the pause | `SessionManagerTextInputTests` | FR-001, US1-2 |
| The pause 0 sends with no pause call | `SessionManagerTextInputTests` | FR-003, US1-3 |
| A failed character stops the send; `typedLength` counts the accepted characters (fail at `0` gives 2, at `5` gives 1, at `2` gives 0) | `SessionManagerTextInputTests` | FR-005, FR-007, US3 |
| Each character gets the ADB retries; no pause between retries | `SessionManagerTextInputTests` | FR-004 |
| The liveness tracker records one input for each character | `SessionManagerTextInputTests` | FR-004 |
| A cancelled pause throws `OperationCanceledException` and sends no more characters | `SessionManagerTextInputTests` | FR-009 |
| A text with `%s` and `'` goes as one command for each character | `SessionManagerTextInputTests` | Edge cases |
| `SplitCharacters` gives one part for each character; null throws | `AdbTextEncoderTests` | D-1 |
| `NormalizeTextInputCharDelayMs`: null, -5 -> 100; 0 -> 0; 150 -> 150; 5000 -> 2000 | `AppConfigTextInputDelayTests` | FR-003 |
| `ConfigApplier.Apply` sets `TextInputCharDelayMs` from the snapshot (absent, "abc", "-1", "0", "250", "9999") | `ConfigApplierTextInputDelayTests` | FR-003 |
| Executor + seam `SessionManager`: literal `250` gives `executed`, `typedLength` 3, calls `text:2`, `pause`, `text:5`, `pause`, `text:0` | `CommandExecutorTextInputDeviceTests` | US1, FR-006 |
| Executor + seam `SessionManager`: `fieldTemplates` `textInput.text` = `{{fighterBatch}}` (parameter default `250`) and inline `{{fighterBatch}}` give the same calls | `CommandExecutorTextInputDeviceTests` | US2, FR-008 |
| Executor + seam `SessionManager`: a failed character gives `not_accepted`, `text_input_not_accepted`, and the accepted count | `CommandExecutorTextInputDeviceTests` | US3, FR-006 |
| OpenAPI descriptions name the pause setting | `tests/contract/TextInputOpenApiTests.cs` (extend) | FR-012 |
| Stub mode sends no input and calls no pause | `SessionManagerTextInputTests` | Edge cases (stub mode) |
| Device check of issue #404 (10 tries of `250`) | Manual, quickstart.md section 2 | SC-001, SC-002 |

The current tests that expect one chunk (`ANumberGoesToTheDeviceAsOneTextCommand`, `ASpaceGoesToTheDeviceAsKeyEvent62`, `AFailedChunkStopsTheSendAndKeepsTheTypedLength`, `AChunkGetsTheConfiguredAdbRetries`, `TheLivenessTrackerRecordsOneInputForEachAdbCommand`) change to the new per-character expectations.

## Performance Note (for the PR)

The PR description MUST contain this note (Constitution IV). A text of n characters takes n ADB commands and (n - 1) pauses. For `250` with the default pause: 3 x (50 to 150 ms) + 2 x 100 ms = 350 to 650 ms. Before the fix: 1 command (50 to 150 ms). The increase is less than 1 second (SC-004). The change has no effect on other step types.

## Complexity Tracking

No constitution violations. No entries.
