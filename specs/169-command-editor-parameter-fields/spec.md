# Feature Specification: Command Editor Parameter Fields

**Feature Branch**: `169-command-editor-parameter-fields`
**Created**: 2026-10-30
**Status**: Draft
**Input**: User description: "Use a parameter in a numeric field or an image field of a command step in the command editor. Source: GitHub issue #358. Closes #358."

## Overview

A command can declare parameters (features 078 and 168). The service lets a step field take its value from a parameter. The step stores the placeholder, for example `{{delay}}`, in `CommandStep.FieldTemplates` under a field path from `CommandStepFieldPaths`. Examples: `swipe.durationMs` = `{{delay}}` and `primitiveTap.detectionTarget.referenceImageId` = `{{buttonToTap}}`.

The command editor has no control that writes `FieldTemplates`. A numeric field accepts digits only. The image field of a tap step is a picker of stored images. A person can use a parameter in a step field only through the API. Because of this, a person cannot use image, choice, or number parameters in a command through the UI alone. The error `text_parameter_in_numeric_field` cannot be reached from the UI. The manual checks of feature 168 need an API script (`specs/168-friendly-sequence-parameters/demo-data.ps1`).

This feature adds a "Use a parameter" control to each parametrizable step field of the command editor. The control writes and removes the placeholder in `FieldTemplates`. It does not change the service rules for `FieldTemplates`.

## Clarifications

### Session 2026-10-30

- Q: Which step fields get the control? → A: Each field whose path is in `CommandStepFieldPaths`. The numeric fields (for example `swipe.durationMs`) and the image field of a tap step (`primitiveTap.detectionTarget.referenceImageId`) are the required cases. *Rationale: the issue names both kinds and asks for "each parametrizable step field".*
- Q: Which service rules stay as they are? → A: All rules for `FieldTemplates`, including the check that gives `text_parameter_in_numeric_field`. *Rationale: non-goal of the issue.*
- Q: Does the editor block a save when a parameter type does not fit the field? → A: No. The editor shows a hint only. The service refuses the save and the editor shows the service error. *Rationale: the service owns the rule (non-goal), and one source of truth avoids two rules that can differ.*
- Q: Which parameters fit which field? → A: A number parameter fits a numeric field. An image-picker parameter fits an image field. A text parameter fits an image field only if it is the image picker kind. A text parameter does not fit a numeric field. All other pairs show the type hint. *Rationale: this follows the feature 168 input kinds and the existing numeric rule.*
### Session 2026-10-30 (analysis loop 1)

- Q: (C1) Do the paths in `CommandStepFieldPaths` that have no field in the editor get a control? → A: No. Only a path that has a field in the editor gets a control. Today the four `ensureGameRunning` numeric paths (`readinessTimeoutMs`, `readinessImage.confidence`, `readinessImage.offsetX`, `readinessImage.offsetY`) have no editor field. Their templates stay unchanged on save (FR-008). *Rationale: the issue says "no scope beyond the issue", and a new panel is a new feature.*
- Q: (I1) Does the control list queue built-in values? → A: Yes. The control shows them in a separate group below the parameters of the command, as the sequence step form of feature 168 does. A queue built-in has no declared type, so it shows no type hint. *Rationale: the placeholder syntax and the service accept them.*
- Q: (I2) Which type does a choice parameter have for the fit rule? → A: The type of its option values. A choice with number options fits a numeric field. A choice with text options does not fit a numeric field and does not fit an image field.
- Q: (U1) What does the editor show for a parameter that no longer exists? → A: The chip shows the placeholder name and the text "Parameter not found". The person can remove it. The editor does not block the save.
- Q: (U4) What plain value does the editor save with an active template? → A: The plain value that the person had before (kept in memory). If a required whole-number field was empty, the value is 0. The service ignores the plain value when a template exists. *Rationale: the saved command stays valid for the service.*
- Q: (U2) What if the service refuses an empty inline image with an image template? → A: The control asks the person to select a stored image first, then select the parameter. The first task of the image story checks this before the UI work.

### Session 2026-10-30 (analysis loop 2)

- Q: (F1) What happens to templates when the step type changes? → A: One rule. On a type change, the editor drops every template key that does not belong to the new step type, and keeps the other keys. A step whose type stays the same keeps all its keys, including keys with no control. *Rationale: a key of another type has no meaning on the new step.*
- Q: (U1) How does the editor show `text_parameter_in_numeric_field` when a command has two steps of the same type? → A: The editor shows the error on the field of the step that the service message names. If the editor cannot find the step, it shows the error in the form summary only. The message always names the field and the parameter. *Rationale: the message must never point to the wrong step.*
- Q: (C1) Does this feature change `specs/168-friendly-sequence-parameters/demo-data.ps1` or the quickstart of feature 168? → A: No. The feature makes the UI path possible. The manual check of this feature shows that the Check 1 and Check 5a Part B flows of feature 168 run in the UI alone. A change to the 168 files is out of scope.
- Q: (I2) Does the "Command parameter" entity include the input kind? → A: Yes. A parameter has a name, a type (text or number), and an optional input kind (image picker, choice list with the type of its option values, or number with pattern).

- Q: What is the phone support level? → A: Desktop-only. The command editor is an authoring screen, and feature 168 already makes authoring desktop-only.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Use a parameter in a numeric field (Priority: P1)

A person who edits a command with a Swipe step wants the swipe duration to come from a parameter of the command, for example `delay`. The person opens the "Use a parameter" control next to the duration field and selects the parameter. The field then shows the name of the parameter in place of the number. The person saves the command. The saved command has `fieldTemplates` with the placeholder `{{delay}}` for `swipe.durationMs`.

**Why this priority**: This is the main gap in the issue. Without it, the person cannot use a number parameter in a step through the UI.

**Independent Test**: Open a command that has a number parameter and a Swipe step. Select the parameter for the duration field. Save. Read the command from the service. The `fieldTemplates` member has `swipe.durationMs` = `{{delay}}`.

**Acceptance Scenarios**:

1. **Given** a command with a number parameter `delay` and a Swipe step, **When** the person selects `delay` in the control of the duration field and saves, **Then** the saved command has `fieldTemplates` with `swipe.durationMs` = `{{delay}}`.
2. **Given** a field that uses a parameter, **When** the person looks at the field, **Then** the field shows the name of the parameter in place of the number and the plain number input is not editable.
3. **Given** a field that uses a parameter, **When** the person removes the parameter, **Then** the field returns to a plain number input and the saved command has no template for that path.

---

### User Story 2 - Use an image parameter in the image field of a tap step (Priority: P1)

A person who edits a command with a tap step wants the image to come from an image parameter, for example `buttonToTap`. The person selects the parameter in the control next to the image picker. The saved command has `fieldTemplates` with `primitiveTap.detectionTarget.referenceImageId` = `{{buttonToTap}}`.

**Why this priority**: Without it, the person cannot use the image picker parameters of feature 168 in a command through the UI alone.

**Independent Test**: Open a command that has an image parameter and a tap step with an image target. Select the parameter in the image field. Save. Read the command from the service. The `fieldTemplates` member has the placeholder for the image path.

**Acceptance Scenarios**:

1. **Given** a command with an image parameter `buttonToTap` and a tap step, **When** the person selects `buttonToTap` in the control of the image field and saves, **Then** the saved command has `fieldTemplates` with `primitiveTap.detectionTarget.referenceImageId` = `{{buttonToTap}}`.
2. **Given** an image field that uses a parameter, **When** the person looks at the field, **Then** the field shows the name of the parameter in place of the stored image.
3. **Given** an image field that uses a parameter, **When** the person removes the parameter, **Then** the field returns to the image picker.
4. **Given** the service refuses an empty inline image together with an image template, **When** the person selects an image parameter in the image field, **Then** the control asks the person to select a stored image first, and then to select the parameter.

---

### User Story 3 - See which parameters fit a field (Priority: P2)

The control lists all parameters of the command. A parameter whose type does not fit the field shows a hint. For example, a text parameter in a numeric field shows a hint that the save will fail. When the person saves a text parameter in a numeric field anyway, the service refuses the save with the error `text_parameter_in_numeric_field`. The message names the field and the parameter.

**Why this priority**: The hint prevents errors. The service already enforces the rule, so the hint is a help and not a gate.

**Independent Test**: Open a command with a text parameter and a Swipe step. Open the control of the duration field. The text parameter shows a hint. Select it and save. The UI shows the error with the field and the parameter name.

**Acceptance Scenarios**:

1. **Given** a command with a text parameter, **When** the person opens the control of a numeric field, **Then** the control lists the text parameter with a hint that the type does not fit.
2. **Given** a text parameter selected in a numeric field, **When** the person saves, **Then** the save is refused with the error `text_parameter_in_numeric_field` and the message names the field and the parameter.
3. **Given** a command with no parameters, **When** the person opens the control of a field, **Then** the control shows that the command has no parameters.
4. **Given** a step field with a placeholder for a parameter that does not exist, **When** the person looks at the field, **Then** the chip shows the placeholder name and the text "Parameter not found", the person can remove it, and the editor does not block the save.
5. **Given** a choice parameter with number options, **When** the person opens the control of a numeric field, **Then** the control lists the parameter as a fit, with no hint.
6. **Given** a choice parameter with text options, **When** the person opens the control of a numeric field or an image field, **Then** the control lists the parameter with the type hint.

---

### Edge Cases

- A command has no parameters: the control shows an empty state. It does not write a template.
- A person renames or deletes a parameter that a step field uses: the field keeps showing the placeholder name and the chip shows the text "Parameter not found". The person can remove the placeholder. The editor does not block the save. The existing service rules decide whether the save is refused.
- A command from the API already has `fieldTemplates` for a path: the editor shows the parameter name for that field when it loads the command, and saves the template again without change.
- A field has a plain value and the person selects a parameter: the editor keeps the plain value in memory until the save, so removing the parameter in the same session restores it. The saved plain value is the kept value, or 0 for an empty required whole-number field. The service ignores the plain value when a template exists. The saved command uses the template.
- A step has a template for a path that has no control in the editor: the editor keeps the template unchanged on save.
- Step type changes: the editor drops each template key that does not belong to the new step type and keeps all other keys. A step whose type stays the same keeps all its keys, including keys with no control.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The command editor MUST show a "Use a parameter" control next to each step field that has a path in `CommandStepFieldPaths`. This includes the numeric fields and the image field of a tap step. A path with no field in the editor (the four `ensureGameRunning` numeric paths) gets no control.
- **FR-002**: The control MUST list the parameters of the command that is being edited. It MUST show queue built-in values in a separate group, with no type hint.
- **FR-003**: When the person selects a parameter, the editor MUST write the placeholder `{{name}}` to `FieldTemplates` for the path of the field.
- **FR-004**: A field that uses a parameter MUST show the name of the parameter in place of the value, and the plain input MUST NOT be editable. The editor keeps the plain value that the person had before, and saves it with the command. The service ignores the kept value when a template exists.
- **FR-005**: The control MUST let the person remove the parameter. The field MUST return to a plain value input, and the editor MUST remove the path from `FieldTemplates`.
- **FR-006**: The control MUST show a hint for a parameter whose type does not fit the field, for example a text parameter in a numeric field. The control MUST still let the person select it.
- **FR-007**: The editor MUST load existing `FieldTemplates` of a command and show the parameter name for each field that has a template.
- **FR-008**: The editor MUST keep templates for paths that it does not show a control for. When the step type changes, the editor MUST drop each template key that does not belong to the new step type, and MUST keep all other keys. When the step type stays the same, the editor MUST keep all keys, including keys that have no control.
- **FR-009**: A save of a text parameter in a numeric field MUST show the error `text_parameter_in_numeric_field` in the UI. The message MUST name the field and the parameter. The editor MUST show the error at the field of the step that the service message names. If the editor cannot find that step, it MUST show the error in the form summary only.
- **FR-010**: The feature MUST NOT change the service rules for `FieldTemplates`, the saved format of `FieldTemplates`, or any API.
- **FR-011**: Every new control MUST work with the keyboard only, and MUST have a text name that a screen reader reads.
- **FR-012** *(web UI change)*: The feature MUST state its phone support level and follow docs/mobile-ui-principles.md. The phone support level is **desktop-only**: the command editor is an authoring screen, and on a phone it shows "Use a larger screen" as it does today.

### Key Entities

- **Command step field template**: A map from a field path to a placeholder such as `{{delay}}`. It is stored in `CommandStep.FieldTemplates`.
- **Parametrizable field**: A step field whose path is in `CommandStepFieldPaths`. It is numeric, or it is an image reference.
- **Command parameter**: A declared parameter of the command. It has a name, a type (text or number), and an optional input kind (since feature 168): image picker, choice list (with the type of its option values), or number with pattern.

## Assumptions

- The list of parametrizable paths comes from `CommandStepFieldPaths`. The editor does not define its own list.
- Fit rule: a number parameter fits a numeric field. A parameter of the image picker kind fits an image field. A choice parameter fits by the type of its option values: a choice with number options fits a numeric field, and a choice with text options fits neither a numeric field nor an image field. A text parameter does not fit a numeric field. A text parameter fits an image field only if it is the image picker kind. All other pairs show the type hint.
- `CommandStepFieldPaths` is in the domain project (`src/GameBot.Domain/Commands/CommandStep.cs`).
- SC-001 is checked by hand, with a timer, in the manual check.
- The existing save errors of the service are shown in the editor in the same way as other save errors.
- Feature 168 files are not changed (SC-003 is shown in the manual check).
- FR-011 evidence: component tests for keyboard keys and accessible names; the manual check covers keyboard use.
- Performance target: the control opens in under 100 ms with up to 50 parameters (a test or a reasoned target).
- The first task of the image story records the service result for an empty inline image with an image template. It records the result in a code comment and in the PR description. The test asserts the real behavior of the service.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A person can set a Swipe duration to a parameter of the command, and save, in the UI alone in under 1 minute, and the saved command has the placeholder in `fieldTemplates`.
- **SC-002**: A person can set the image of a tap step to an image parameter in the UI alone, with no API call.
- **SC-003**: The manual checks of feature 168 that use a parameter in a step field can run with no API script.
- **SC-004**: A save of a text parameter in a numeric field shows an error that names the field and the parameter in 100% of cases.
- **SC-005**: A command that has `fieldTemplates` from the API loads and saves in the editor with no change to the templates in 100% of cases.
