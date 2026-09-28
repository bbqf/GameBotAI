# Feature Specification: Let a parameter choose the reference image

**Feature Branch**: `ccr-843497ee-pjzcth` (spec number 114)  
**Created**: 2026-09-28  
**Status**: Draft  
**Input**: GitHub issue #243 (FR-011): "let a parameter choose the reference image of a detection target (fieldTemplates on referenceImageId)". Closes #243. Full description: see the issue and the feature description that started this spec.

## Background

The PNS authoring repository automates a daily interaction on one game screen. The screen shows three options. Each account uses a different option. One sequence serves all accounts. The option of each account comes from `parameterValues` on its queue template entries.

The issue owner made these probes on 2026-09-28:

- `fieldTemplates` key `primitiveTap.detectionTarget.referenceImageId` gets 400 `unknown_field_template_path`: "is not a parametrizable numeric field".
- `fieldTemplates` key `waitForImage.detectionTarget.offsetX` gets the same 400.
- `fieldTemplates` keys `primitiveTap.detectionTarget.offsetX` and `offsetY` are accepted.

Thus the workaround taps one fixed image and moves the tap by an offset for each account. The tap does not check that the image of the selected option is below it.

A code review found these facts:

- A command step can already hold an inline placeholder in `primitiveTap.detectionTarget.referenceImageId` and in `waitForImage.detectionTarget.referenceImageId` (for example `"referenceImageId": "{{novaOption}}"`). The service substitutes it before dispatch (feature 078). But this is not in the documentation, and `fieldTemplates` rejects the same field. Thus the author did not find it.
- The `imageVisible` condition of a sequence cannot take a parameter. The save check looks for an image with the literal id `{{novaOption}}` and rejects the sequence. The runtime does not substitute the condition image id.
- No save check looks at a value that a queue template entry supplies for an image field. An incorrect value shows only at run time.

## Clarifications

### Session 2026-09-28

The pipeline ran without a human reviewer. Thus the clarify step selected each answer. Each answer has a rationale.

- Q: Which form does the author use to parametrize the image of a detection target: a `fieldTemplates` key, the inline placeholder, or both? → A: Both. The service accepts the `fieldTemplates` keys `primitiveTap.detectionTarget.referenceImageId` and `waitForImage.detectionTarget.referenceImageId`, and it continues to accept the inline placeholder. When the two forms are on the same step, the `fieldTemplates` value wins. Rationale: the issue asks for the `fieldTemplates` shape. The inline form works today, and stored commands use it, so it must not break. A `fieldTemplates` value replaces the field in the same way for numeric fields.
- Q: Which form does the `imageVisible` condition use? → A: An inline placeholder in `imageId`, for example `"imageId": "{{novaOption}}"`. Rationale: a condition has no `fieldTemplates` overlay. `imageId` is a string, so an inline placeholder is the direct form, and it matches the inline form of the detection target.
- Q: Which conditions get the parametrized `imageId`? → A: Each `imageVisible` leaf in each position where a sequence holds a condition: the step condition, the condition of an `If` step, the condition of a `Loop` step, and a break condition, also inside a composite condition. Rationale: the issue asks that a guard can check the selected option. A guard can be in each of these positions, and one rule is easier to understand than a list of exceptions.
- Q: Is an unknown image id in a template entry value an error or a warning at save? → A: An error. The template entry save fails with 400 and the new code `unknown_image_reference`, when an entry value goes to an image field and no image has that id. Rationale: the issue asks for a check "on save against existing image ids when the value is known". An image id that does not exist can never work, so a warning is not sufficient.
- Q: What does a template value for the image field look like? → A: One whole placeholder, for example `{{novaOption}}`, the same rule as for a numeric field. Text around the placeholder (for example `nova-{{option}}`) is not accepted in a `fieldTemplates` value. The inline form keeps its current rule and accepts text around the placeholder. Rationale: one rule for all `fieldTemplates` keys is easy to explain. The inline form already exists, so it must not change.
- Q: Does a queue start also refuse to start when a template entry value names an image that does not exist? → A: No. The check is at template entry save (FR-010) and at run time (FR-009). Rationale: the save check catches the known values before any run. An image that is deleted after the save gives the step the current missing-image result of its step type, the same as for a literal image id today (FR-009). A new queue-start check adds scope that the issue does not ask for.
- Q: Which save routes run the check of FR-010? → A: The save route `POST /api/queue-templates` (it creates or overwrites a template by name). No validation route exists. Rationale: this route is the only route that saves a queue template, so the new check goes into this route.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - A tap finds the image that a parameter selects (Priority: P1)

A sequence author declares a parameter `novaOption` and sets `fieldTemplates` `primitiveTap.detectionTarget.referenceImageId` to `{{novaOption}}` on a tap step. Each queue template entry supplies an image id in `parameterValues`. At run time, the tap looks for the image of that entry and taps it.

**Why this priority**: This is the request of the issue. It removes the offset workaround and lets a tap check that the selected option is on the screen.

**Independent Test**: Save a command with the `fieldTemplates` key. The save succeeds. Run the command two times with two different values. The execution log records the value of `novaOption` each time. For a `waitForImage` step, the step detail also shows the resolved image id, which is different in the two runs.

**Acceptance Scenarios**:

1. **Given** a command whose tap step has `fieldTemplates` `{"primitiveTap.detectionTarget.referenceImageId": "{{novaOption}}"}` and declares `novaOption`, **When** the author saves it, **Then** the save succeeds and the response has the warning `static_check_skipped` for that field.
2. **Given** that command, **When** a run supplies `novaOption = option-b`, **Then** the tap looks for image `option-b` and the execution log records `novaOption = option-b`.
3. **Given** a `waitForImage` step with `fieldTemplates` `{"waitForImage.detectionTarget.referenceImageId": "{{novaOption}}"}`, **When** a run supplies `novaOption = option-c`, **Then** the step waits for image `option-c`.
4. **Given** a step with an inline `referenceImageId` `option-a` and `fieldTemplates` for the same field, **When** a run supplies `novaOption = option-b`, **Then** the step uses `option-b`.
5. **Given** a stored command with the inline form `"referenceImageId": "{{novaOption}}"`, **When** a run supplies `novaOption`, **Then** the step behaves as before this feature.

---

### User Story 2 - A guard checks the image that a parameter selects (Priority: P2)

A sequence author uses `"imageId": "{{novaOption}}"` in an `imageVisible` condition. The service saves the sequence. At run time, the condition looks for the image that the parameter selects.

**Why this priority**: The issue asks that a guard can check the selected option. It depends on the same parameter as story 1, but a sequence can use it without a parametrized tap.

**Independent Test**: Save a sequence that declares `novaOption` and has a step condition `imageVisible` with `imageId` `{{novaOption}}`. The save succeeds. Run it with a value of an image that is visible, and then with a value of an image that is not visible. The step runs in the first case and is skipped in the second case.

**Acceptance Scenarios**:

1. **Given** a sequence that declares `novaOption`, **When** the author saves a step condition `imageVisible` with `imageId` `{{novaOption}}`, **Then** the save succeeds and the response has the warning `static_check_skipped`.
2. **Given** a sequence that does not declare `novaOption`, **When** the author saves the same condition, **Then** the save fails with `unresolvable_parameter_reference`.
3. **Given** the saved sequence, **When** a run supplies `novaOption = option-b` and image `option-b` is on the screen, **Then** the condition is true and the step runs.
4. **Given** an `If` step, a `Loop` step, or a break condition with a composite condition that holds the parametrized `imageVisible` leaf, **When** a run supplies the value, **Then** the leaf looks for the image of that value.

---

### User Story 3 - An incorrect image id fails early and clearly (Priority: P2)

An operator supplies an image id that does not exist. When the value is known at save (a template entry value), the save fails. When the value is not known until run time, the step fails with a clear reason. The service never crashes and never taps at random.

**Why this priority**: A parametrized image removes the static check. This story gives back a check at the earliest point where the value is known.

**Independent Test**: Save a template entry with `novaOption = no-such-image` for the sequence of story 1. The save fails with 400 and `unknown_image_reference`. Run the command with the same value from a source that has no save check. The step gets the current missing-image result of its step type (a `waitForImage` step fails with `image_unavailable`; a tap step gets the current `skipped_invalid_config` result). No device input occurs, and the log names the value.

**Acceptance Scenarios**:

1. **Given** a sequence that uses `novaOption` in an image field, **When** the operator saves a template entry with `novaOption = no-such-image`, **Then** the save fails with 400, code `unknown_image_reference`, and the message names the entry, the parameter, and the image id.
2. **Given** the same sequence, **When** the operator saves an entry with `novaOption = option-b` and image `option-b` exists, **Then** the save succeeds.
3. **Given** a run where `novaOption` resolves to an id with no image, **When** the tap or wait step runs, **Then** the step gets the current missing-image result of its step type, the same as for a literal id (`waitForImage`: `image_unavailable`; `primitiveTap`: `skipped_invalid_config` with `template_not_found` on Windows, or `primitive_tap_detection_windows_only` on other hosts), no device input occurs, and the execution log records the resolved value.
4. **Given** a run where no scope supplies `novaOption`, **When** the step runs, **Then** the step fails with the current parameter resolution error, and no device input occurs.

### Edge Cases

- The `fieldTemplates` value for an image key is not one whole placeholder (for example `nova-{{option}}` or `option-a`): the save fails with `invalid_field_template_value`, and the message names the field.
- The parameter resolves to an empty text: the step fails with the current resolution error for an empty `referenceImageId`.
- A template entry value goes to an image field and also to a text field: the check applies because one of the uses is an image field.
- A template entry value comes from a parameter default and not from the entry: the save check applies to the default in the same way. The default follows the run-time scope order: for a reference inside a command, the default of that command comes first, and then the outer (sequence) defaults.
- A sequence step binds a command parameter to a literal image id with `parameterBindings`: the sequence save does not check the id. The run-time check of FR-009 applies.
- A template entry supplies `novaOption`, and a sequence step binds the `novaOption` of its command to a literal with `parameterBindings`: the template save does not check the entry value against the image fields of that command and of the commands that it reaches, because the binding outranks the entry at run time. A binding to `{{otherName}}` is not followed; the run-time check of FR-009 applies.
- The image is deleted after the save: the step gets the current missing-image result of its step type, the same as for a literal image id (FR-009).
- A break condition has an image parameter that is not resolved, or its image is not available: the break condition gives "No break" with the error detail in the log, and the run does not fail (feature 066 FR-002a and FR-010). The loop limit stops the loop.
- A sequence save with `"dryRun": true` and a parametrized `imageId`: the dry-run response does not change. It is `{ valid, dryRun, errors }` and has no `warnings` member. Only a save that stores the sequence returns `warnings`.
- `ensureGameRunning.readinessImage.referenceImageId` in `fieldTemplates`: not in scope; the save fails with `unknown_field_template_path` as before.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The service MUST accept the `fieldTemplates` keys `primitiveTap.detectionTarget.referenceImageId` and `waitForImage.detectionTarget.referenceImageId` on a command step.
- **FR-002**: The value of an image key in `fieldTemplates` MUST be one whole placeholder `{{name}}`. The service MUST reject other values at save with 400 and the new code `invalid_field_template_value`, and the message MUST name the step and the field.
- **FR-003**: At dispatch, the service MUST replace the image id of the step with the resolved text value of the parameter. When the step also has an inline value, the `fieldTemplates` value MUST win.
- **FR-004**: The service MUST keep the inline placeholder in `referenceImageId` as it works today.
- **FR-005**: The `imageId` of an `imageVisible` condition MUST accept an inline placeholder in each condition position of a sequence: step condition, `If` condition, `Loop` condition, and break condition, also inside composites.
- **FR-006**: At save, a parametrized image field or `imageId` MUST skip the image existence check and MUST give the warning `static_check_skipped`. The referenced name MUST be declared by the entity, be a queue built-in, or be `iteration` inside a loop; otherwise the save MUST fail with `unresolvable_parameter_reference`.
- **FR-007**: At run time, the service MUST resolve the `imageId` of an `imageVisible` condition against the parameter scope in effect for that step before it evaluates the condition.
- **FR-008**: When a parameter in an image field cannot be resolved at run time, the step MUST fail with the current parameter resolution error, and no device input MUST occur. Exception: a break condition keeps the rule of feature 066 FR-002a and FR-010. An unresolved image parameter in a break condition gives "No break" with the error detail in the log, and the run does not fail.
- **FR-009**: When the resolved image id names no image, the step or condition MUST get the current missing-image result of its type, the same as for a literal id that names no image: a `WaitForImage` step and an `imageVisible` condition fail with `image_unavailable`; a `PrimitiveTap` step gets the current `skipped_invalid_config` result (`template_not_found` on Windows, `primitive_tap_detection_windows_only` on other hosts). In each case, no device input MUST occur. Exception: a break condition keeps the rule of feature 066 FR-002a and FR-010. An unavailable image in a break condition gives "No break" with the error detail in the log, and the run does not fail.
- **FR-010**: When a queue template entry is saved, the service MUST check each known value that goes to an image field against the stored images. A known value is the entry value, or else a declaration default in the run-time scope order (for a field inside a command: the default of that command first, and then the sequence default). An image field of a command (or of a command that it reaches) is not checked against the entry value or a default when the sequence step that calls the command has a non-null `parameterBindings` entry for that parameter, because the binding outranks the entry at run time. The check does not follow a binding to `{{otherName}}`; the run-time check of FR-009 applies to it. When no image has the id that a checked value gives, the save MUST fail with 400 and the code `unknown_image_reference`. The message MUST name the entry index, the parameter, and the image id.
- **FR-011**: The execution log MUST record the resolved value of each parameter that an image field used, as it does for other parameters (feature 078, FR-024).
- **FR-012**: The error message for an unsupported `fieldTemplates` key MUST no longer say "numeric field". It MUST say that the field is not a parametrizable field.
- **FR-013**: The behavior of the numeric `fieldTemplates` keys MUST not change.
- **FR-014**: The OpenAPI document and the parameter documentation MUST list the two new keys, the placeholder in `imageVisible.imageId`, and the codes `invalid_field_template_value` and `unknown_image_reference`.

### Key Entities

- **Field template overlay** (`fieldTemplates`): a map from a dotted field path of a command step to one whole placeholder. This feature adds two string paths to the numeric paths.
- **imageVisible condition**: a condition leaf with `imageId`, `minSimilarity` and `negate`. `imageId` can now hold a placeholder.
- **Queue template entry**: supplies `parameterValues`. Its save now checks the values that go to image fields.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: One sequence with one parametrized tap serves four accounts that select three different options. The author does not use an offset and does not copy the sequence.
- **SC-002**: 100% of the probes of the issue that use the new keys get a success response on save.
- **SC-003**: An image id that does not exist, supplied by a template entry, is rejected at save in 100% of cases, before any run, for each image field whose parameter name the entry or a default supplies directly and that no step binding overrides.
- **SC-004**: No run with an unresolved or unknown parametrized image sends a tap to the device.
- **SC-005**: All current tests for numeric `fieldTemplates` and for inline placeholders pass without change.

## Assumptions

- A parameter that selects an image is a text parameter. The service does not add a new parameter type for images.
- "An equivalent that selects one image out of a declared list" is not built. The `fieldTemplates` form is sufficient for the issue.
- The check of FR-010 follows the reach of the current template entry validation: the sequence and each command that the sequence can reach.
- The check of FR-010 matches a value to an image field by the parameter name. It has one binding rule: when the sequence step that calls a command has a non-null `parameterBindings` entry for a parameter, the check does not use the entry value or a default for that parameter in the image fields of that command and of the commands that it reaches (the binding outranks the entry at run time). The check does not follow a binding to `{{otherName}}`. This is a known limit; the run-time check of FR-009 applies.
- The authoring UI does not get new controls in this feature. The UI shows the new warning and error codes with its current display of validation feedback.

## Non-Goals

- No change to the numeric `fieldTemplates` keys.
- No parametrization of other string fields, for example `ensureGameRunning.readinessImage.referenceImageId`.
- No `fieldTemplates` key for `waitForImage.detectionTarget.offsetX` or `offsetY`.
- No new UI authoring controls beyond what the API contract needs.
