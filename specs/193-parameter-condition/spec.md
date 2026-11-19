# Feature Specification: Parameter Condition

**Feature Branch**: `193-parameter-condition`  
**Created**: 2026-11-19  
**Status**: Draft  
**Input**: User description: "A sequence condition on the value of a sequence parameter (FR-046). GitHub issue #401: https://github.com/bbqf/GameBotAI/issues/401. Closes #401"

## Background

The PNS authoring repository asked for this feature for its feature 032 (continuous troop training). The tracker row is FR-046 in `docs/api-feature-requests.md` in the PNS repository.

Feature 032 trains troops in four camps. For each account and each troop type, the operator sets a batch size and a tier. A batch size of 0 means: skip this camp. The tier `max` means: keep the tier that the game selects, and tap no tier.

The sequence conditions today are `imageVisible`, `matchedImage`, `ocrText`, `commandOutcome`, `commandStepOutcome`, `lastRun`, `all`, `any`, and `none`. A condition can read a sequence parameter only as an image id in `imageVisible`. No condition reads a number value or a text value. Thus, feature 032 v1 uses a sentinel image for `max`, and it has no workaround for the batch size 0.

## Clarifications

### Session 2026-11-19

- Q: Do `greaterThan` and `lessThan` apply to a text or choice parameter, or does the save refuse them? → A: They apply. The system compares the texts in ordinal order, case-insensitive (FR-005). Rationale: the issue says that text and choice parameters compare as text with the same four operators, and acceptance criterion 3 names all operators.
- Q: For a `number` parameter with a `pattern` (feature 168), does the condition compare the entered number or the text after the pattern? → A: The entered number (FR-012). Rationale: the author writes the condition on the number that the operator sets, for example batch size 0. The pattern is only a format for a device field.
- Q: What happens when the parameter has no run value, or when a `number` parameter has a run value that is not a whole number? → A: The step fails with a parameter failure that names the parameter (FR-010, FR-011). The condition does not give false. Rationale: this is the same rule as for an unresolved placeholder in an `imageVisible` condition, and a broken setup must not skip a step in silence.
- Q: Does a read of the sequence return `value` with the JSON type that the author wrote? → A: Yes. A JSON number stays a number and a JSON string stays a string (FR-001). Rationale: a client that writes `0` must read `0`, not `"0"`, so that a compare of the stored sequence with the source file shows no change.
- Q: Can `name` hold a placeholder such as `{{fighterBatch}}`, or a queue built-in name? → A: No. `name` is the bare name of a declared parameter of the sequence. The save refuses all other names (FR-006). Rationale: the issue asks to refuse an unknown name, and a bare name is the simplest contract for the client.
- Q (analyze): A `choice` parameter can have the type `number`. Does it compare as text or as a number? → A: The declared type selects the compare for each input kind. A `choice` parameter of the type `number` compares as a number (FR-004). Rationale: the declared type is the type of the value, and the issue says that number parameters compare as numbers.
- Q (analyze): Which `dryRun` must refuse an unknown name? → A: The save `dryRun`: the create, update, and patch requests with `"dryRun": true` (FR-006). Rationale: these requests run the save checks. The `dryRun` of `/execute` is a run option, not a save check, and this feature does not change it.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Skip a step when a number parameter has a given value (Priority: P1)

An author adds a `parameter` condition to a step of a sequence: `fighterBatch` `equals` 0. When the run value of `fighterBatch` is 0, the condition is true. With `negate`, or in a `none` composite, the step does not run for the batch size 0.

**Why this priority**: This is the case of feature 032 that has no workaround today. The operator cannot switch off a troop type.

**Independent Test**: Save a sequence with the `number` parameter `fighterBatch` and a step with the condition `{"type":"parameter","name":"fighterBatch","operator":"equals","value":0,"negate":true}`. Run the sequence with `fighterBatch` = 0, and then with `fighterBatch` = 250. Make sure that the step does not run in the first run, and that the step runs in the second run.

**Acceptance Scenarios**:

1. **Given** a sequence with the `number` parameter `fighterBatch` and a step with a `parameter` condition `fighterBatch` `equals` 0, **When** the author saves the sequence, **Then** the save accepts the sequence, and a read of the sequence returns the condition with the same fields.
2. **Given** the same step and the run value 0 for `fighterBatch`, **When** the step condition is evaluated, **Then** the condition is true.
3. **Given** the run value 250, **When** the condition `fighterBatch` `equals` 0 is evaluated, **Then** the condition is false. The conditions `notEquals` 0 and `greaterThan` 0 are true, and `lessThan` 0 is false.
4. **Given** a `number` parameter with the run value 10, **When** the condition `greaterThan` 9 is evaluated, **Then** the condition is true. The compare is a number compare, not a text compare, so 10 is greater than 9.

---

### User Story 2 - Skip a tap when a text or choice parameter has a given value (Priority: P1)

An author adds a `parameter` condition `fighterTier` `equals` `max` to an `If` step. When the run value of `fighterTier` is `max` or `MAX`, the condition is true, and the sequence does not tap a tier.

**Why this priority**: This removes the sentinel image workaround of feature 032 v1.

**Independent Test**: Save a sequence with the `choice` parameter `fighterTier` (options `t1` to `t10` and `max`) and an `If` step with the condition `fighterTier` `equals` `max`. Run the sequence with `MAX`, and then with `t7`. Make sure that the `If` step goes to its `then` branch in the first run and to its `else` branch in the second run.

**Acceptance Scenarios**:

1. **Given** a `text` or `choice` parameter `fighterTier` with the run value `MAX`, **When** the condition `fighterTier` `equals` `max` is evaluated, **Then** the condition is true.
2. **Given** the run value `t7`, **When** the condition `fighterTier` `equals` `max` is evaluated, **Then** the condition is false, and `notEquals` `max` is true.
3. **Given** a text parameter, **When** the operator is `greaterThan` or `lessThan`, **Then** the system compares the two texts in ordinal order, case-insensitive.

---

### User Story 3 - Combine a parameter condition with other conditions (Priority: P1)

An author puts a `parameter` condition as a child of `all`, `any`, or `none`, or as the condition of an `If` step. For example: `all` of `fighterBatch` `greaterThan` 0 and `imageVisible` of the camp button.

**Why this priority**: Feature 032 must combine the batch check with screen checks.

**Independent Test**: Save a sequence with an `If` step whose condition is `all` of a `parameter` condition and an `imageVisible` condition. Run it with values that make each child true or false. Make sure that the result follows the `all` rule.

**Acceptance Scenarios**:

1. **Given** an `all` composite with a `parameter` child that is false, **When** the composite is evaluated, **Then** the composite is false, and the execution log names the `parameter` child as the child that decided the result.
2. **Given** an `any` composite with a `parameter` child that is true, **When** the composite is evaluated, **Then** the composite is true.
3. **Given** a `none` composite with a `parameter` child that is true, **When** the composite is evaluated, **Then** the composite is false.
4. **Given** an `If` step with a `parameter` condition, **When** the step runs, **Then** the step selects its branch from the condition result.

---

### User Story 4 - Find an unknown parameter name before a run (Priority: P2)

The save (create, update) and the `dryRun` validation of a sequence refuse a `parameter` condition whose `name` is not a parameter of the sequence. The error names the parameter and the field path of the condition.

**Why this priority**: An incorrect parameter name must not make a condition false in silence at run time.

**Independent Test**: Send a sequence create request with `"dryRun": true` and a `parameter` condition with the name `fighterBach`, and no parameter of that name. Make sure that the response is a validation error that names `fighterBach` and the condition field path.

**Acceptance Scenarios**:

1. **Given** a `parameter` condition whose `name` is not a declared parameter of the sequence, **When** the author saves the sequence or sends a `dryRun` validation, **Then** the service refuses the request with a validation error that names the parameter and the field path.
2. **Given** a `parameter` condition with no `name`, an unknown `operator`, or no `value`, **When** the author saves the sequence, **Then** the service refuses the request with a validation error that names the field.
3. **Given** a `parameter` condition on a `number` parameter whose `value` is not a whole number (for example `"abc"`), **When** the author saves the sequence, **Then** the service refuses the request with a validation error.

---

### User Story 5 - Read the condition in the OpenAPI document (Priority: P2)

A client author reads the OpenAPI document and finds the `parameter` condition kind with its fields and operators.

**Why this priority**: The PNS authoring repository writes sequences from the OpenAPI document.

**Independent Test**: Get the OpenAPI document. Make sure that it lists the `parameter` condition kind, the fields `name`, `operator`, and `value`, and the four operators.

**Acceptance Scenarios**:

1. **Given** the OpenAPI document, **When** a client reads the sequence condition schema, **Then** the document lists `parameter` as a condition kind, with the fields `name` (string), `operator` (one of `equals`, `notEquals`, `greaterThan`, `lessThan`), and `value` (string or number), and the compare rules.

---

### User Story 6 - Author the condition in the sequence editor (Priority: P3)

An author selects the condition kind `parameter` in the condition fields of the sequence editor. The author selects a parameter of the sequence, an operator, and types a value.

**Why this priority**: Authors can use the API directly. The editor makes the condition available to authors who use the web UI.

**Independent Test**: In the sequence editor, add a `parameter` condition to an `If` step, select `fighterBatch`, `equals`, type 0, save, and open the sequence again. Make sure that the condition shows the same values.

**Acceptance Scenarios**:

1. **Given** the condition fields of the sequence editor, **When** the author opens the condition kind list, **Then** the list shows `parameter`.
2. **Given** a `parameter` condition in the editor, **When** the author saves the sequence, **Then** the saved condition has the selected `name`, `operator`, and `value`.

---

### Edge Cases

- The parameter has no value at run time (no binding and no default): the condition cannot be evaluated. The step fails with the same parameter resolution failure as an unresolved placeholder. The condition is not false in silence.
- A `number` parameter has a run value that is not a whole number (for example a queue binding `abc`): the condition cannot be evaluated, and the step fails with a parameter value failure that names the parameter.
- A `number` parameter has a `pattern` (feature 168): the condition compares the number that the person entered, not the text after the pattern.
- A `choice` parameter: the condition compares the technical value of the option, not the label.
- An `image` parameter: the condition compares the image id as text, case-insensitive.
- A multi-value parameter (feature 179): the save refuses a `parameter` condition on it, because the condition needs one value.
- `value` is a JSON number for a text parameter (for example 0): the system compares the text of the number (`0`).
- `value` is a text for a number parameter (for example `"0"`): the save accepts it when the text is a whole number.
- A text compare trims no space. The text `max ` (with a space) is not equal to `max`.
- `negate` on a `parameter` condition: the system applies it after the compare, as for the other condition kinds.
- A `parameter` condition in a loop condition or a break condition: the same rules apply, because these use the same condition model.
- The name of a parameter is case-sensitive, the same as a `{{name}}` placeholder.
- The current condition kinds and the `imageVisible` parameter behaviour do not change.
- A `choice` parameter of the type `number`: the declared type selects the compare, so the condition compares the option value as a whole number.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The system MUST add a sequence condition kind `parameter` with three fields: `name` (string, required), `operator` (required, one of `equals`, `notEquals`, `greaterThan`, `lessThan`), and `value` (string or number, required). The condition MUST accept `negate`, the same as the other condition kinds. A read of the sequence MUST return `value` with the JSON type that the author wrote (number or string).
- **FR-002**: The system MUST accept a `parameter` condition in every place that takes a sequence condition: a step condition, the condition of an `If` step, a loop condition, a break condition, and a child of `all`, `any`, and `none`.
- **FR-003**: At run time, the system MUST read the run value of the named parameter from the parameter scope of the sequence run (call-site binding, queue value, or default), with the same rules as a `{{name}}` placeholder.
- **FR-004**: The declared type of the parameter (`number` or `text`) selects the compare, for each input kind. For a parameter of the type `number` (also a `choice` parameter of the type `number`), the system MUST compare the run value and `value` as whole numbers. `equals` is true when the two numbers are equal. `notEquals` is true when they are not equal. `greaterThan` is true when the run value is greater than `value`. `lessThan` is true when the run value is less than `value`.
- **FR-005**: For a parameter of the type `text` (the input kinds `text`, `choice`, and `image`), the system MUST compare the run value and the text of `value` as text, case-insensitive, with an ordinal compare. `greaterThan` and `lessThan` use the same ordinal, case-insensitive order.
- **FR-006**: The save (create, update, and patch) and the save `dryRun` validation of a sequence (the same requests with `"dryRun": true`) MUST refuse a `parameter` condition whose `name` is not a declared parameter of the sequence. `name` is the bare name, not a `{{name}}` placeholder. The error MUST name the parameter and the field path of the condition.
- **FR-007**: The save and the `dryRun` validation MUST refuse a `parameter` condition with an empty `name`, an `operator` that is not one of the four operators, no `value`, or a `value` that is not a JSON string or a JSON number (for example `true` or an object). The error MUST name the field.
- **FR-008**: The save and the `dryRun` validation MUST refuse a `parameter` condition on a `number` parameter when `value` is not a whole number (a JSON whole number, or a text that is a whole number).
- **FR-009**: The save and the `dryRun` validation MUST refuse a `parameter` condition on a multi-value parameter.
- **FR-010**: When the parameter has no run value, the system MUST NOT give false. The step MUST fail with the parameter resolution failure that an unresolved placeholder gives, and the failure MUST name the parameter.
- **FR-011**: When a `number` parameter has a run value that is not a whole number, the system MUST NOT give false. The step MUST fail with a parameter value failure that names the parameter.
- **FR-012**: For a `number` parameter with a `pattern`, the system MUST compare the number that the person entered, before the pattern changes it.
- **FR-013**: The execution log MUST describe a `parameter` condition with its name, operator, `value`, and the run value, so that a reader can see why the condition is true or false.
- **FR-014**: The OpenAPI document MUST list the `parameter` condition kind, the fields `name`, `operator`, and `value`, the four operators, and the compare rules.
- **FR-015**: The system MUST NOT change the behaviour of the current condition kinds, or of the parameter behaviour of `imageVisible`.
- **FR-016**: The step-through, the gamepack export and import, and other places that read or copy a sequence condition MUST keep a `parameter` condition with all its fields.
- **FR-017**: The condition fields of the sequence editor in the web UI MUST let an author add, edit, and save a `parameter` condition. The editor MUST show the declared parameters of the sequence for `name`. The sequence editor is in the Authoring area. The phone support level of this feature is `desktop-only`, the level of the Authoring area, and the feature MUST follow docs/mobile-ui-principles.md.
- **FR-018**: `docs/architecture.md` MUST describe the `parameter` condition kind, and the `CHANGELOG.md` MUST have an entry for it.

### Key Entities

- **Parameter condition**: A sequence condition of the kind `parameter`. It has the fields `name`, `operator`, `value`, and `negate`.
- **Operator**: One of `equals`, `notEquals`, `greaterThan`, `lessThan`.
- **Sequence parameter**: A declared parameter of the sequence (feature 078 and feature 168). Its type (`text` or `number`) selects the compare rule.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: An author can switch off a troop type with one `parameter` condition on the batch size, with no sentinel image and no extra parameter.
- **SC-002**: An author can skip the tier tap for `max` with one `parameter` condition, with no fake value in `allowedImages`.
- **SC-003**: For each of the four operators, the condition gives the correct result for number and text parameters in 100% of the test cases.
- **SC-004**: The save and the `dryRun` validation find 100% of the unknown parameter names in `parameter` conditions before a run.
- **SC-005**: All condition tests that are in the repository before this feature pass without a change.

## Assumptions

- Only the declared parameters of the sequence are valid names. Queue built-in names (`queue.*`) and the loop name `iteration` are not valid names for a `parameter` condition.
- The issue names only four operators. Other operators (for example `contains`, `greaterOrEqual`) are out of scope.
- The condition compares a parameter with a fixed value. A compare of two parameters is out of scope.
- Changes in the PNS authoring repository are out of scope.
