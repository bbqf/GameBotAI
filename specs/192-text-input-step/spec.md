# Feature Specification: TextInput Command Step

**Feature Branch**: `192-text-input-step`  
**Created**: 2026-11-18  
**Status**: Implemented (iterated by 195)  
**Input**: User description: "Add a TextInput command step that types a string into the device, and accept a parameter placeholder in the text. GitHub issue #400 (FR-045): https://github.com/bbqf/GameBotAI/issues/400. Closes #400"

## Background

The PNS authoring repository asked for this feature for its feature 032 (continuous troop training). The tracker row is FR-045 in `docs/api-feature-requests.md` in the PNS repository.

The `KeyInput` step presses one key. The key text `250` is the raw Android key code 250, not the three digits. No other step type types text. Thus, feature 032 v1 uses three fixed `KeyInput` steps `2`, `5`, and `0`, and every troop type in every account trains 250. A templated `KeyInput` step for each digit needs up to four parameters for each troop type (16 for each account). The operator rejected that workaround.

The game takes the amount in a number input box. A tap on the amount field opens a keyboard box with `OK`. The sequence must type the amount from a sequence parameter, for example 250 Fighters and 100 Riders.

## Clarifications

### Session 2026-11-18

- Q: Must a `fieldTemplates` value for `textInput.text` be one whole placeholder, or can it hold text around placeholders? → A: One whole placeholder. The save checks this, the same as for the image keys (feature 114). Rationale: one rule for the text keys. An inline placeholder in `textInput.text` already permits text around a placeholder.
- Q: Where does the service check the permitted characters, the empty text, and the length? → A: At save for a literal text (validation error), and at run time for the resolved text (`not_accepted` with a reason code). Rationale: the author sees a literal error early, and a parameter value is known only at run time.
- Q: Does the step result or the step log show the typed text? → A: The step result shows the typed length only. The execution log records the parameter names and values that the step used, as for other templated fields. Rationale: the issue asks for the length, and the parameter log already shows the value.
- Q: Does the text input use the same ADB retries and the same input time limit as `KeyInput`? → A: It uses the same ADB retries and the same liveness records as a `KeyInput` step. A `KeyInput` step of a command run has no input time limit (only the session input route has one), so the `TextInput` step gets no new time limit. Rationale: the same device input rules give the same reliability.
- Q: Does a step-through or a run of one step (the steps endpoint) run a `TextInput` step? → A: Yes. A step-through runs the step (no preview), and the steps endpoint accepts and runs it, the same as `KeyInput`. The public ad-hoc session input route does not get a text action. Rationale: the step has no outside effect, and the session input route is not in the request.
- Q (plan): Which `dryRun` reports an unknown placeholder in `textInput.text`? → A: The command save checks it today, but the command requests have no `dryRun`. The command create and update requests get `"dryRun": true`, the same as the sequence requests. Rationale: a `TextInput` step is in a command, and the sequence `dryRun` does not check the steps of a command.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Type a fixed text into the device (Priority: P1)

An author adds a `TextInput` step to a command and writes a text in `textInput.text`, for example `250`. When the command runs, the service types the full text into the input field that has the focus on the device. The step result shows how many characters the service typed.

**Why this priority**: This is the base capability. Without it, no sequence can type a number or a word in one step.

**Independent Test**: Save a command with one `TextInput` step with the text `250`. Run the command on a session. Make sure that the device gets the text `250` in one text input, and that the step result shows the typed length 3.

**Acceptance Scenarios**:

1. **Given** a command with a `TextInput` step with the text `250`, **When** the command runs on a session with a device, **Then** the device gets the text `250` as one text input command (not the key code 250), and the step result has the status `executed` and the typed length 3.
2. **Given** a create or update request for a command with a `TextInput` step that has no `textInput` config or an empty `textInput.text`, **When** the service validates the request, **Then** the service rejects the request with a validation error that names the field `textInput.text`.
3. **Given** the OpenAPI document, **When** a client reads it, **Then** the document shows the `TextInput` step type, the `textInput` config, the `text` field (required), the permitted characters, and the reason codes of the step.

---

### User Story 2 - Type a text from a sequence parameter (Priority: P1)

An author sets `fieldTemplates` on the `TextInput` step with the key `textInput.text` and the value `{{fighterBatch}}`. The sequence declares `fighterBatch` as a `text` or `number` parameter. When the sequence runs, the service types the parameter value (for example `250`) as the full string.

**Why this priority**: This is the request of feature 032. The operator sets a batch size for each troop type and each account. One step must type the value of one parameter.

**Independent Test**: Make a command with a `TextInput` step with `fieldTemplates` `{"textInput.text": "{{fighterBatch}}"}`. Make a sequence that calls the command with the `number` parameter `fighterBatch` = 250. Run the sequence. Make sure that the device gets the text `250` and that the step result shows the typed length 3.

**Acceptance Scenarios**:

1. **Given** a `TextInput` step with `fieldTemplates` `{"textInput.text": "{{fighterBatch}}"}` and a `number` parameter `fighterBatch` with the value 250, **When** the step runs, **Then** the device gets the text `250`, and the execution log records the parameter name and the value used.
2. **Given** the same step and a `text` parameter `fighterBatch` with the value `100`, **When** the step runs, **Then** the device gets the text `100`.
3. **Given** a `TextInput` step with an inline placeholder in `textInput.text` (for example `{{fighterBatch}}`), **When** the step runs, **Then** the service replaces the placeholder with the parameter value, the same as for `keyInput.key`.
4. **Given** a `TextInput` step with both an inline `textInput.text` and a `textInput.text` key in `fieldTemplates`, **When** the step runs, **Then** the value of the `fieldTemplates` key replaces the inline text.

---

### User Story 3 - Get a clear reason when the service cannot type the text (Priority: P2)

When the resolved text is empty, or holds a character that the device cannot type, the step does not report a false success. The step result has the status `not_accepted` and a reason code, the same as `KeyInput` does.

**Why this priority**: A false success hides a wrong troop amount. The operator must see the cause in the step log.

**Independent Test**: Run a `TextInput` step whose parameter value is empty, and a step whose value holds a character that is not permitted. Make sure that each step result has the status `not_accepted` and the correct reason code, and that the device gets no input.

**Acceptance Scenarios**:

1. **Given** a `TextInput` step whose resolved text is empty, **When** the step runs, **Then** the step result has the status `not_accepted` and the reason code `text_input_empty_text`, and the service sends no input to the device.
2. **Given** a `TextInput` step whose resolved text holds a character that is not permitted (for example `é` or a line break), **When** the step runs, **Then** the step result has the status `not_accepted` and the reason code `text_input_unsupported_character`, and the service sends no input to the device.
3. **Given** a `TextInput` step whose resolved text is longer than the maximum length, **When** the step runs, **Then** the step result has the status `not_accepted` and the reason code `text_input_too_long`.
4. **Given** a valid text and a device that does not accept the input, **When** the step runs, **Then** the step result has the status `not_accepted` and the reason code `text_input_not_accepted`.

---

### User Story 4 - Find an unknown placeholder before a run (Priority: P2)

An author sends a command create or update request with `"dryRun": true`. The validation reports a placeholder in `textInput.text` (inline or in `fieldTemplates`) that the command does not declare. The sequence `dryRun` does not check the steps of a command, so it does not change.

**Why this priority**: The author finds a typing error in a parameter name before the sequence runs on a device.

**Independent Test**: Send a command create request with `"dryRun": true` and `fieldTemplates` `{"textInput.text": "{{fighterBach}}"}` and no parameter of that name. Make sure that the validation reports the unknown name and the field `textInput.text`.

**Acceptance Scenarios**:

1. **Given** a `TextInput` step with `{{fighterBach}}` in `textInput.text` (inline or in `fieldTemplates`) and no parameter of that name, **When** the author sends a `dryRun` validation, **Then** the validation fails and reports the name `fighterBach` and the field `textInput.text`, the same as for other templated fields.
2. **Given** a `TextInput` step with a placeholder of a declared parameter, **When** the author sends a `dryRun` validation, **Then** the validation does not report the placeholder.

---

### User Story 5 - Author the step in the command editor (Priority: P3)

An author selects the step type "Text Input" in the command editor, writes the text, and can set a parameter placeholder for `textInput.text`.

**Why this priority**: Authors can use the API directly. The editor makes the step type available to authors who use the web UI.

**Independent Test**: In the command editor, add a "Text Input" step, type `250`, save, and open the command again. Make sure that the step shows the text `250`.

**Acceptance Scenarios**:

1. **Given** the command editor, **When** the author adds a step, **Then** the step type list shows "Text Input".
2. **Given** a "Text Input" step in the editor, **When** the author sets a parameter placeholder for `textInput.text`, **Then** the saved command has the `textInput.text` key in `fieldTemplates`.

---

### Edge Cases

- A text with a space (for example `250 Fighters`): the device gets the space as a space, not as a separator.
- A text with a character that the device shell gives a special meaning (for example `&`, `;`, `'`, `"`, `%`, `\`, `(`): the device gets the character as a literal character.
- A `number` parameter value with a sign or a decimal point (for example `-5` or `2.5`): the service types the text of the value as it is. The service does not change the format of the value.
- A multi-value parameter with two or more items in `textInput.text`: the step fails with the existing `list_in_single_field` resolution error.
- A `fieldTemplates` value for `textInput.text` that is not one whole placeholder (for example `{{a}}0`): the save rejects the step with `invalid_field_template_value`, the same as for an image key.
- An inline text with a placeholder and a character that is not permitted (for example `é{{a}}`): the save does not check the text, because the text has a placeholder. The run-time check gives `text_input_unsupported_character`.
- A `TextInput` step in a session without a device (stub mode): the step succeeds and reports the typed length, the same as `KeyInput` in stub mode.
- An existing `KeyInput` step: the behaviour does not change.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The system MUST add a command step type `TextInput` with a config `textInput` that has one required field `text` (string).
- **FR-002**: When a `TextInput` step runs, the system MUST type the full resolved text into the input field that has the focus on the device, as text. The system MUST NOT interpret the text as a key name or a key code. A text with no space and no `%s` sequence MUST go to the device as one text input command. The system MAY send a space as a space key event, and MAY split the text at `%s`, so that the device types the literal text (FR-015).
- **FR-003**: The system MUST accept the key `textInput.text` in `fieldTemplates`. The value MUST be one whole placeholder `{{name}}`. At run time, the value of the named parameter replaces `textInput.text`.
- **FR-004**: The system MUST resolve an inline placeholder in `textInput.text`, the same as for `keyInput.key`. A `fieldTemplates` value for `textInput.text` MUST replace the inline text, and the system MUST NOT resolve the inline text in that case.
- **FR-005**: A `text` or `number` parameter value MUST give the text without a change. For example, the value 250 gives the text `250`.
- **FR-006**: The step result of a `TextInput` step MUST include the typed length: the number of characters that the system typed. For the status `executed`, the typed length MUST be the length of the text. For the status `not_accepted`, the typed length MUST be the number of characters that the device accepted before the failure (0 when the check of the text fails). For `skipped_invalid_config` and for a parameter resolution failure, the typed length MUST be absent.
- **FR-007**: The system MUST permit only the printable ASCII characters from U+0020 (space) to U+007E (`~`) in the resolved text. The maximum length MUST be 500 characters.
- **FR-008**: When the resolved text is empty, the step result MUST have the status `not_accepted` and the reason code `text_input_empty_text`.
- **FR-009**: When the resolved text holds a character that FR-007 does not permit, the step result MUST have the status `not_accepted` and the reason code `text_input_unsupported_character`.
- **FR-010**: When the resolved text is longer than 500 characters, the step result MUST have the status `not_accepted` and the reason code `text_input_too_long`.
- **FR-011**: When the text is valid and the device does not accept the input, the step result MUST have the status `not_accepted` and the reason code `text_input_not_accepted`.
- **FR-012**: When a `TextInput` step has no `textInput` config at run time, the step result MUST have the status `skipped_invalid_config` and the reason code `text_input_missing_config`, the same as `KeyInput`.
- **FR-013**: The save (create, update, and `dryRun`) of a command MUST reject a `TextInput` step without `textInput`, and a step whose literal `textInput.text` (with no placeholder and no `fieldTemplates` key) is empty, has a character that FR-007 does not permit, or is too long.
- **FR-014**: A `dryRun` validation MUST report a placeholder in `textInput.text` (inline or in `fieldTemplates`) that no declared parameter supplies. The report MUST name the parameter and the field `textInput.text`, the same as for other templated fields. The command create request (`POST /api/commands`) and the command update request (`PATCH /api/commands/{id}`) MUST accept `"dryRun": true` in the body. With it, the service runs all save checks, stores nothing, and gives `{ "valid": true, "dryRun": true, "errors": [] }` when all checks pass, the same as the sequence requests. A failed check gives the same 400 response as a real save. Without `dryRun`, the save keeps its current behaviour.
- **FR-015**: The system MUST send each special character of the text to the device as a literal character. A space MUST arrive as a space. A shell character MUST NOT start a shell function on the device.
- **FR-016**: The OpenAPI document MUST describe the `TextInput` step type, the `textInput` config, the `text` field, the permitted characters, the maximum length, the `textInput.text` key of `fieldTemplates`, the typed length in the step result, and the reason codes.
- **FR-017**: The system MUST NOT change the behaviour of the `KeyInput` step or of other templated fields.
- **FR-018**: The parameter reference scan, gamepack export and import, and other places that list the step types and their config MUST include the `TextInput` step, so that a `TextInput` step keeps its config and its placeholders in each of these places.
- **FR-019**: The command editor in the web UI MUST let an author add, edit, and save a `TextInput` step, and set a parameter placeholder for `textInput.text`. The command editor is in the Authoring area. The phone support level of this feature is `desktop-only`, the level of the Authoring area, and the feature MUST follow docs/mobile-ui-principles.md.
- **FR-020**: `docs/architecture.md` MUST describe the `TextInput` step type, and the `CHANGELOG.md` MUST have an entry for it.
- **FR-021**: The system MUST send each text input command and each space key event with the same ADB retries as a `KeyInput` step, and MUST record the input start and outcome of each in the liveness records in the same way. The system MUST NOT add an input time limit that a `KeyInput` step of a command run does not have.
- **FR-022**: A step-through MUST run a `TextInput` step (no preview), and the run of one step (the steps endpoint) MUST accept and run a `TextInput` step, the same as a `KeyInput` step. The public ad-hoc session input route does not change.
- **FR-023**: The step result and the execution log detail item of the step MUST NOT include the typed text. The execution log MUST record the parameter names and values that the step used, as for other templated fields. The ADB debug trace keeps its current behaviour for the text input command, the same as for the other ADB commands.

### Key Entities

- **TextInput step**: A command step of the type `TextInput`. It has the config `textInput` with the field `text`. It can have a `textInput.text` key in `fieldTemplates`.
- **TextInput config**: The config of the step. The field `text` holds the text to type, or a text with inline placeholders.
- **Step result of a TextInput step**: The status (`executed`, `not_accepted`, or `skipped_invalid_config`), the reason code, and the typed length.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: An author can make a sequence step type a parameter value of 1 to 6 digits with one step and one parameter, not one step and one parameter for each digit.
- **SC-002**: For the value 250, the device gets the three characters `2`, `5`, and `0` in this order in 100% of the test runs, and never the key code 250.
- **SC-003**: 100% of the step results of `TextInput` steps show the typed length or a reason code.
- **SC-004**: Each of the four failure causes (empty text, character not permitted, text too long, device did not accept) gives its own reason code in 100% of the test runs.
- **SC-005**: A `dryRun` validation finds 100% of the unknown placeholders in `textInput.text` before a run.
- **SC-006**: All existing `KeyInput` tests pass without a change.

## Assumptions

- The device is an Android device or emulator that the service controls through ADB. The input field on the device has the focus before the step runs. An earlier tap step opens the input box.
- The printable ASCII set is sufficient for the request (digits and simple words). Characters outside this set are out of scope.
- The maximum length of 500 characters is sufficient for a number or a short word.
- A `TextInput` step does not tap the input field and does not press `OK`. Other steps (`PrimitiveTap`, `KeyInput`) do these actions.
- The `fieldTemplates` rule of the image keys (one whole placeholder, checked at save) applies to `textInput.text` also.
- Sequence-level inline action steps (`SequenceStep.Action`) are out of scope. A sequence uses the `TextInput` step through a command, as for `KeyInput` command steps.
