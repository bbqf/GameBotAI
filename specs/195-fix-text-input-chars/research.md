# Research: TextInput step types all characters

Feature: `195-fix-text-input-chars` | Spec: [spec.md](./spec.md) | Plan: [plan.md](./plan.md)

## R-001: Why the game field drops characters

- **Result**: For a text with no spaces, feature 192 sends one `adb shell input text '<chunk>'` command. The Android `input text` tool sends the key events of all characters of the chunk in one fast burst. Many game engines (for example Unity input fields) read the input one time for each frame. When more than one key event arrives in one frame, the field keeps only some of them. The first character always arrives, because the field is idle before it.
- **Evidence**: The measurement of issue #404: `20`, `25`, `2`, and one `250` in 8 tries, always with the first character `2`.

## R-002: Selected fix: one ADB command for each character, with a pause

- **Decision**: Send each character as its own input command. A non-space character goes as `adb shell input text '<c>'`. A space goes as key event 62 (no change from feature 192). Between two character commands, wait for the pause from `AppConfig.TextInputCharDelayMs` (setting `GAMEBOT_TEXT_INPUT_CHAR_DELAY_MS`, default 100 ms, range 0 to 2000).
- **Rationale**: One ADB command takes approximately 50 to 150 ms (process start, ADB transport, and the `input` tool start on the device). With the pause, two characters arrive 150 to 250 ms apart, which is many frames apart. Each character has its own command, so the result can count the accepted characters exactly (FR-007).
- **Alternatives considered**:
  - *Key events (`input keyevent KEYCODE_2`) for digits*: this works only for digits and some symbols, not for all printable ASCII characters. Rejected: two paths for one step.
  - *One `input keyevent` command with many key codes*: this is also a burst. Rejected.
  - *Pause only, with chunks*: no pause is possible inside one `input text` command. Rejected.
  - *A per-step `charDelayMs` field*: this changes the stored command format, the API contract, the web UI editor, and the gamepack format. The known game fields work with one value. Rejected for this fix (spec, Clarifications).
  - *OCR check of the field after the send*: a non-goal of the issue.

## R-003: Where to put the pause

- **Decision**: Put the character split and the pause in `SessionManager.SendTextAsync` (the device path of the input action `text`). Do not change `CommandExecutor.ExecuteTextInputAsync`.
- **Rationale**: `SendTextAsync` already sends each part with the ADB retries and the liveness records, and it writes `typedLength`. The placeholder path (inline placeholder and `fieldTemplates` key `textInput.text`) resolves the text before the executor makes the input action. Thus the fix applies to both paths with no change in the resolver (FR-008, FR-010).
- **Alternatives considered**: a split in the executor (one input action for each character). Rejected: `SendInputsAsync` gives a count of accepted actions only, so the executor cannot count characters, and the session manager would apply its per-action logs and delay logic to each character.

## R-004: How `AdbTextEncoder` changes

- **Decision**: Replace `AdbTextEncoder.Split(string text)` with `AdbTextEncoder.SplitCharacters(string text)`: it returns one part for each character, in order. A space part is `" "` (so `IsSpace` stays true for it). `SendTextAsync` is the only caller of `Split`, so remove `Split`. Change the `Split` tests in `tests/unit/Emulator/AdbTextEncoderTests.cs` to `SplitCharacters` tests. Change the doc comment of `AdbClient.InputTextAsync` (it names `Split`).
- **Rationale**: One character for each part makes the `%s` rule automatic: no part holds two characters. The device-shell quoting (`QuoteForDeviceShell`) of feature 192 stays for each one-character part (for example `'` becomes `''\'''`).

## R-005: How the tests see the pause

- **Decision**: Add an internal seam to `SessionManager`: `internal Func<int, CancellationToken, Task> CharacterPause { get; set; }`, default `Task.Delay`. The unit tests set it to a function that writes `pause:<ms>` into the call list of `FakeAdbSessionClient` and returns at once. `SeamSessionManager.Create` gets a new parameter `textInputCharDelayMs` (default 0, so the current tests do not wait).
- **Rationale**: The tests stay deterministic and fast (Constitution II). The tests can make sure of the order `text:2`, `pause:100`, `text:5`, `pause:100`, `text:0`, and of no pause after the last character.
- **Alternatives considered**: a `TimeProvider` with `FakeTimeProvider.Advance`. Rejected: the code then must use `Task.Delay(TimeSpan, TimeProvider, CancellationToken)`, and the test must step the clock while the send runs, which is more complex.

## R-006: Setting registration

- **Decision**: Add `TextInputCharDelayMs` (default 100) to `AppConfig`. Read `GAMEBOT_TEXT_INPUT_CHAR_DELAY_MS` in `GameBotServiceSetup` (start) and in `ConfigApplier.Apply` (run-time change). Add the key with the default 100 to the defaults of `ConfigSnapshotService`. Clamp: a value that is not a number or is negative gives 100; a value larger than 2000 gives 2000. Put the clamp rule in one place: `AppConfig.NormalizeTextInputCharDelayMs(int?)` (static), which both readers call.
- **Rationale**: The same pattern as `GAMEBOT_ADB_RETRY_DELAY_MS`. The configuration page shows the key through the snapshot with no web UI change.

## R-007: Cancellation

- **Decision**: The pause uses the token of the caller (`ct`). A cancellation throws `OperationCanceledException` from the pause or from the ADB command. `SendTextAsync` does not catch it. The exception goes up through `SendInputsAsync` as for the other inputs (no change).
- **Rationale**: The spec Clarifications: the cancellation goes up as for the other step types.

## R-008: Performance

- **Result**: A text of n characters takes n ADB commands plus (n - 1) pauses. For `250` with the default: 3 x (50 to 150 ms) + 2 x 100 ms = 350 to 650 ms. Before the fix it took 1 command (50 to 150 ms). The increase is less than 1 second (SC-004). A 500-character text takes approximately 50 to 125 seconds. This is accepted: the typical texts are short numbers.
