# Feature Specification: TextInput step types all characters

**Feature Branch**: `195-fix-text-input-chars`
**Created**: 2026-11-19
**Status**: Implemented
**Input**: GitHub issue #404, "B-042: TextInput reports executed but the game field receives only 1-2 of 3 characters". Closes #404.

## Problem *(context)*

A `TextInput` command step (feature 192) sends a text to the input field that has the focus on the device. For a text without spaces, the step sends all characters in one burst. Many game input fields accept only the first character of a burst and drop some of the other characters.

Measurement from the issue (2026-11-19, emulator-5558, a game number input field, text `250`, 8 tries): only 1 try put `250` into the field. The other tries put `20`, `25`, or `2` into the field. Each try reported `executed` with `typedLength` 3. The first character always arrived. The loss of the later characters had no fixed pattern.

The selected fix: the step sends **one character at a time**, with a **pause between two characters**. An operator can change the pause through a service setting. The step result counts only the characters that the device accepted.

## Clarifications

### Session 2026-11-19

Autonomous mode: the pipeline selected each answer. Each answer has a one-line rationale.

- Q: Where does the operator set the pause: a per-step field, a service setting, or both? → A: Only a service setting (`GAMEBOT_TEXT_INPUT_CHAR_DELAY_MS`). Rationale: the issue has one game field type; a service setting fixes all steps with no change to stored commands, the API contract, or the web UI.
- Q: Does the pause setting have an upper limit? → A: Yes, 2000 ms. A larger value gives 2000. Rationale: an incorrect value (for example 100000) must not block a queue run for hours on a long text.
- Q: What does the step do when the run is cancelled before the step sent all characters? → A: The cancellation goes up as for the other step types (the step gives no result). The characters that the device accepted before the cancellation stay in the field. Rationale: this is the cancellation behavior of all steps; a partial result for a cancelled run has no consumer.
- Q: Does the change apply to other users of the input action `text` (for example the session input route)? → A: The input action `text` of the session manager is the only path, and the `TextInput` step is its only caller. The change applies to this path. Rationale: the code has no other text input path.
- Q: What is the default pause? → A: 100 ms. Rationale: one ADB command takes approximately 50 to 150 ms, so the gap between two characters is 150 to 250 ms, which is slower than a human types; a 3-character number takes less than 1 second more.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Type a number into a game field that drops burst input (Priority: P1)

An operator has a command with a `TextInput` step, for example the text `250` for the batch amount of a troop training task. The operator runs the command on a device. The game input field must get the full text `250`.

**Why this priority**: This is the defect of issue #404. With the defect, the game trains a wrong amount (for example 20 instead of 250) in most runs.

**Independent Test**: Run a command with one `TextInput` step `{text: "250"}` against a test device double. Make sure that the double gets three separate character inputs `2`, `5`, `0` in this order, with the configured pause between two inputs, and that the step result is `executed` with `typedLength` 3.

**Acceptance Scenarios**:

1. **Given** a `TextInput` step with the text `250` and the default pause, **When** the step runs on a device, **Then** the device gets the inputs `2`, `5`, `0` as three separate input commands in this order, with a pause of the default length between two commands, and no pause after the last command.
2. **Given** a `TextInput` step with the text `25 0`, **When** the step runs, **Then** the device gets `2`, `5`, the space key event (key code 62), and `0` as four separate input commands in this order, with a pause between two commands.
3. **Given** the pause setting is 0, **When** a `TextInput` step with the text `250` runs, **Then** the device gets the three separate character inputs with no pause between them.
4. **Given** a game input field that drops characters of a burst, **When** an operator runs a `TextInput` step with the text `250` 10 times (the field is clear before each try), **Then** the field shows `250` after each try.

---

### User Story 2 - Text from a parameter placeholder (Priority: P1)

A queue run resolves the text of a `TextInput` step from a parameter, for example `{{fighterBatch}}` through the `fieldTemplates` key `textInput.text`. The device must get each character of the resolved text in the same way as a literal text.

**Why this priority**: The troop training task of the issue uses this path. The fix is not complete if it applies only to literal texts.

**Independent Test**: Run a command whose `TextInput` step has the `fieldTemplates` key `textInput.text` = `{{fighterBatch}}` and the parameter value `250`. Make sure that the device double gets `2`, `5`, `0` as separate inputs with the pause between them.

**Acceptance Scenarios**:

1. **Given** a `TextInput` step with the `fieldTemplates` key `textInput.text` = `{{fighterBatch}}` and the value `250`, **When** the step runs, **Then** the device gets `2`, `5`, `0` as three separate input commands with the pause between them, and the result is `executed` with `typedLength` 3.
2. **Given** a `TextInput` step with the inline text `{{fighterBatch}}` and the value `250`, **When** the step runs, **Then** the device gets the same three separate inputs.

---

### User Story 3 - Correct result when the device does not accept a character (Priority: P2)

When the device does not accept the input command of a character (after the configured ADB retries), the step stops. The step result must not report `executed`, and `typedLength` must count only the characters that the device accepted before the failure.

**Why this priority**: The issue asks for a result that does not report success for characters that the step did not send. With the selected fix, each character has its own input command, so the result can count the accepted characters exactly.

**Independent Test**: Make the device double fail the input command of the character `0` for the text `250`. Make sure that the device gets no input after the failed character, and that the result is `not_accepted` with the reason code `text_input_not_accepted` and `typedLength` 2.

**Acceptance Scenarios**:

1. **Given** the text `250` and a device that does not accept the input command for `0`, **When** the step runs, **Then** the step result is `not_accepted`, the reason code is `text_input_not_accepted`, and `typedLength` is 2.
2. **Given** the text `250` and a device that does not accept the input command for `5`, **When** the step runs, **Then** the device gets no input command for `0`, and `typedLength` is 1.
3. **Given** the text `250` and a device that does not accept the first character, **When** the step runs, **Then** `typedLength` is 0 and the result is `not_accepted`.

---

### Edge Cases

- **Text with one character**: the step sends one input command and does not pause.
- **Text with only spaces**: each space is one key event 62, with the pause between two key events.
- **Text with `%s`**: each character is a separate input command, so no command holds the sequence `%s`. The `%` and the `s` arrive as two separate characters.
- **Text with a single quote `'` or other shell characters**: each character goes to the device with the shell quoting of feature 192.
- **Long text (500 characters, the maximum)**: the step sends 500 input commands. With the default pause, the pauses alone take approximately 50 seconds. This is accepted: the typical texts are short numbers.
- **Cancellation during the step** (for example, a queue stop): the step stops at the next input or pause. The step does not send more characters. The cancellation goes up as for the other step types. The characters that the device accepted stay in the field.
- **Pause setting larger than 2000**: the service uses 2000.
- **Retry of one character**: the ADB retries (`GAMEBOT_ADB_RETRIES`) apply to the input command of each character. A retry sends the same character again. The pause does not apply between two retries of one character (the retry delay applies there).
- **Stub mode (no device)**: no input goes to a device. The result stays as in feature 192 (`executed`, `typedLength` = the text length). Stub mode does not pause.
- **Logs**: the new code writes no text and no character into logs. Logs can hold counts only (FR-011).
- **Dry run and preview**: no change. They do not send input.
- **A device that accepts the command but the game field drops the character**: the step cannot see the field. The result counts the accepted commands. The non-goals exclude an OCR check in the step.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The `TextInput` step MUST send the text to the device one character at a time: one input command for each character. A space MUST stay one key event with key code 62 (as in feature 192). Each other character MUST be one text input command that holds only that character.
- **FR-002**: The step MUST pause between two character inputs. The pause MUST NOT occur before the first character or after the last character.
- **FR-003**: The length of the pause MUST come from a new service setting `GAMEBOT_TEXT_INPUT_CHAR_DELAY_MS` (milliseconds). The default MUST be 100. The value 0 MUST send the characters with no pause. A negative value or a value that is not a number MUST give the default. A value larger than 2000 MUST give 2000. The service MUST apply a change of the setting at run time, as with the other `GAMEBOT_ADB_*` settings.
- **FR-004**: The ADB retries and the liveness records of feature 192 MUST apply to the input command of each character.
- **FR-005**: The step MUST stop at the first character whose input command the device does not accept (after the retries). The step MUST NOT send the characters after it.
- **FR-006**: The step result MUST be `executed` only when the device accepted the input command of every character. Otherwise the result MUST be `not_accepted` with the reason code `text_input_not_accepted`.
- **FR-007**: `typedLength` in the step result MUST be the number of characters whose input command the device accepted. A character MUST NOT count before the device accepted its input command.
- **FR-008**: FR-001 to FR-007 MUST apply to a literal text and to a text that the service resolves from a placeholder (inline placeholder or the `fieldTemplates` key `textInput.text`).
- **FR-009**: The pause MUST stop when the run is cancelled. After a cancellation, the step MUST NOT send more characters.
- **FR-010**: The step MUST NOT change the `KeyInput` step, the OCR endpoint `POST /api/ocr/read`, the placeholder resolution, or the `fieldTemplates` resolution.
- **FR-011**: The step MUST NOT write the text into logs or the execution log (as in feature 192). Logs can hold the typed length.
- **FR-012**: The API description of `textInput.text` and `typedLength` MUST tell that the service types one character at a time with the pause from `GAMEBOT_TEXT_INPUT_CHAR_DELAY_MS`, and that `typedLength` counts the characters that the device accepted.
- **FR-013**: `ENVIRONMENT.md` MUST describe the new setting `GAMEBOT_TEXT_INPUT_CHAR_DELAY_MS` (purpose, where it is used, default).
- **FR-014**: Automated tests MUST cover: the order of the character inputs, the pause between two inputs (and no pause after the last), the pause value 0, a space in the text, a failed character and the `typedLength` after it, the ADB retries for one character, and a text from a placeholder.

### Key Entities

- **Character input**: one input command to the device for one character of the text. It is a text input with one character, or the space key event 62.
- **Character pause setting** (`GAMEBOT_TEXT_INPUT_CHAR_DELAY_MS`): the pause in milliseconds between two character inputs of a `TextInput` step. Default 100. 0 = no pause. Range 0 to 2000.
- **Step result** (unchanged fields): `status` (`executed` or `not_accepted`), `reasonCode`, `typedLength`.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: On the game input field of the issue, a `TextInput` step with the text `250` puts `250` into the field in 10 of 10 tries (before the fix: 1 of 8).
- **SC-002**: A queue run that types a batch amount from a parameter puts the full amount into the field in 10 of 10 runs.
- **SC-003**: In 100% of the step results, `typedLength` is equal to the number of characters that the device accepted, and `executed` occurs only when this number is equal to the text length.
- **SC-004**: A text of 3 characters takes at most 1 second more than before the fix with the default setting.
- **SC-005**: The automated tests of FR-014 pass in CI.

## Assumptions

- The device drops characters because they arrive in one burst. Separate input commands with a pause fix this for the game fields of the issue. The default pause of 100 ms is sufficient: a separate ADB command alone takes approximately 50 to 150 ms, so the gap between two characters is approximately 150 to 250 ms.
- A service setting is sufficient. A per-step pause field is not necessary for the known game fields. An operator can change the service setting if a game needs a longer pause.
- The step cannot know which characters the game field shows. It knows only which input commands the device accepted. An OCR check of the field is a non-goal.
- The step result keeps its fields. No new field is necessary.
- The web UI does not change: the step editor has no new field. The setting shows in the configuration page through the configuration snapshot like the other `GAMEBOT_*` settings.

## Non-goals

- No change to `KeyInput` steps or to the OCR endpoint `POST /api/ocr/read`.
- No OCR check of the field in the `TextInput` step.
- No change to how placeholders or `fieldTemplates` resolve text.
- No per-step pause field (`charDelayMs`) in the step configuration.
