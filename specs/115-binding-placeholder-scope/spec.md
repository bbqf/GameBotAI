# Feature Specification: Resolve a placeholder in a step parameterBindings value against the sequence scope

**Feature Branch**: `115-binding-placeholder-scope`  
**Created**: 2026-09-29  
**Status**: Implemented  
**Input**: GitHub issue #246 (tracker row B-029, P2, bug): "A step `parameterBindings` entry whose value is a placeholder (`{{name}}`) is accepted on save and kept on read-back, but at run time the command receives the placeholder as literal text." Closes #246. Full description: see the issue and the feature description that started this spec.

## Background

A sequence Action step that runs a command can send values to the command through `parameterBindings` (a list of name and value pairs). A nested command step in a command has the same `parameterBindings` list.

The issue owner made this probe on service 1.4.0.214:

1. Command `ZZZ.NovaParamTap` declares the text parameter `novaOptionImage` (required). Its one `PrimitiveTap` step has `fieldTemplates` `{"primitiveTap.detectionTarget.referenceImageId": "{{novaOptionImage}}"}`.
2. A sequence declares `novaOptionImage` (required, no default). An Action step runs the command with `parameterBindings: [{"name": "novaOptionImage", "value": "{{novaOptionImage}}"}]`.
3. A queue template entry supplies `novaOptionImage = pns-alliance-nav-button` (`originLayer` `entry`).
4. The queue runs the sequence.

The execution log shows "Step 0 resolved 1 parameter(s): novaOptionImage={{novaOptionImage}}" with `originLayer` `command`, and "Step 0 was not executed: template_not_found". The binding value `${novaOptionImage}` gives the same failure.

The two controls work: a step with no `parameterBindings` gives the command the same-named sequence value (`originLayer` `entry`), and a literal binding value (`pns-todo-radar`) reaches the command as that value.

A code review found these facts:

- The service puts each binding value into the parameter scope of the command as stored text. It does not replace a placeholder in the value. Thus the command reads the text `{{novaOptionImage}}` as the value of its parameter, and the log gives it the layer `command`.
- The sequence save already checks that a placeholder in a binding value of a sequence step names a parameter that the sequence declares, a queue built-in, or `iteration` inside a loop. Otherwise the save fails with `unresolvable_parameter_reference`.
- The command save does not do this check for a binding value of a nested command step. `ParameterReferenceScanner.ScanCommandStep` does not scan `parameterBindings`. Thus a command save accepts `{{anyUndeclaredName}}` in a nested binding value.
- An `imageVisible` condition with `imageId` `{{name}}` resolves against the scope in effect for its step (feature 114). This is the rule that the issue asks for.
- The placeholder syntax of the service is `{{name}}` only. The text `${name}` is not a placeholder anywhere in the service.

## Clarifications

### Session 2026-09-29

The pipeline ran without a human reviewer. Thus the clarify step selected each answer. Each answer has a rationale.

- Q: What does the service do with the binding value `${novaOptionImage}`? → A: The service treats it as literal text, the same as each other placeholder site of the service (an inline field, an `imageVisible` `imageId`, an action parameter). The service does not add a `${name}` syntax. The documentation of `parameterBindings` states that only `{{name}}` is a placeholder. Rationale: acceptance criterion 5 of the issue permits "the same way that other placeholder sites treat it" and forbids a new syntax. A save rejection of `${` would be a new rule for one site only.
- Q: What does the service do with text around a placeholder in a binding value (for example `nova-{{option}}`)? → A: The service replaces each placeholder in the text, the same as for an inline text field of a command step. The command receives the result with the origin layer `command`, because the value is made at the call site. The log also records each placeholder name that the value used, with the layer that supplied it. Rationale: inline text fields already accept text around a placeholder (feature 078). One rule for text values is easier to understand.
- Q: Does the fix also apply to `parameterBindings` on a nested command step inside a command? → A: Yes. A placeholder in a binding value of a nested command step resolves against the scope of the command that calls it, with the same rules. Rationale: the two sites use the same binding model and the same defect. A fix at one site only leaves the same silent literal value at the other site.
- Q: What is the result when a binding placeholder cannot be resolved? → A: The step gets the current parameter resolution result of a command step: a `ParameterResolutionError` with the parameter name, the field path `parameterBindings.<bindingName>`, and the reason `unresolved`. The command does not run, and no device input occurs. A sequence step fails. A nested command step gets the outcome `skipped_parameter_unresolved`, and the calling command continues (FR-004a). Rationale: this is the current error path for an unresolved placeholder in a command field, so the operator sees one known shape of error. (Analysis follow-up: the message of a binding error also gets a short remediation hint, per Constitution Principle III. See FR-004.)

## User Scenarios & Testing *(mandatory)*

### User Story 1 - A binding placeholder gets the value of the sequence scope (Priority: P1)

A sequence author binds a command parameter to a placeholder of a sequence parameter, for example `{{novaOptionImage}}`. A queue template entry supplies the value. At run time, the command receives the value of the entry, and the tap finds the image of that value.

**Why this priority**: This is the defect of the issue. Without the fix, a parametrized command that a sequence calls through a binding receives text that is not a value, with no warning, and the tap does not occur.

**Independent Test**: Run the reproduction of the issue with a test double for the device. The command receives `novaOptionImage = pns-alliance-nav-button`, the execution log records that value with `originLayer` `entry`, and the tap looks for image `pns-alliance-nav-button`.

**Acceptance Scenarios**:

1. **Given** the reproduction of the issue, **When** the queue runs the sequence, **Then** the command receives `novaOptionImage = pns-alliance-nav-button`.
2. **Given** the same run, **When** the operator reads the execution log of the command, **Then** `details[kind=parameters]` shows `novaOptionImage=pns-alliance-nav-button` with `originLayer` `entry`, not `command`.
3. **Given** the same run and image `pns-alliance-nav-button` on the screen, **When** the tap step runs, **Then** the tap looks for image `pns-alliance-nav-button` and does not report `template_not_found`.
4. **Given** a sequence step `imageVisible {{novaOptionImage}}` guard and a binding `{{novaOptionImage}}` on the same step, **When** the step runs, **Then** the guard and the command use the same value.

---

### User Story 2 - The current binding forms do not change (Priority: P1)

A sequence author uses a step with no `parameterBindings`, or with a literal binding value. These steps work as before.

**Why this priority**: The issue names them as controls that must continue to work. Stored sequences use them.

**Independent Test**: Run the two controls of the issue. The results are the same as before this feature.

**Acceptance Scenarios**:

1. **Given** a step with no `parameterBindings`, **When** the queue runs it with an entry value, **Then** the command receives the same-named value of the entry with `originLayer` `entry`.
2. **Given** a step with the literal binding value `pns-todo-radar`, **When** the queue runs it, **Then** the command receives `pns-todo-radar` with `originLayer` `command`, and the tap looks for image `pns-todo-radar`.
3. **Given** a binding entry with no value (`null`, "inherit"), **When** the step runs, **Then** the command gets the value from the outer scope, as before.

---

### User Story 3 - An unresolved binding placeholder fails clearly (Priority: P2)

The placeholder in a binding value names a parameter that the run scope does not supply. For example, the sequence declares the parameter as not required (`required: false`) with no default, and no queue entry or run request supplies a value. (A required sequence parameter with no value cannot get to the step: the service refuses the run at start with `missing_required_parameters`. An undeclared name cannot get to the step: the save fails with `unresolvable_parameter_reference`.) The step fails with a clear reason and a remediation hint. The command never receives the placeholder text.

**Why this priority**: The issue asks that the behavior is clear and not silent. A silent literal value is the defect.

**Independent Test**: Run a sequence that declares `novaOptionImage` as not required with no default. Its binding value is `{{novaOptionImage}}`, and no entry or run request supplies `novaOptionImage`. The step fails with the current parameter resolution error that names `novaOptionImage`, plus the remediation hint of FR-004. The command does not run, and no device input occurs.

**Acceptance Scenarios**:

1. **Given** a sequence that declares `novaOptionImage` as `required: false` with no default, a binding value `{{novaOptionImage}}`, and no layer that supplies `novaOptionImage`, **When** the step runs, **Then** the step fails with the current parameter resolution error plus the remediation hint, the message names `novaOptionImage` and the binding, and the command does not run.
2. **Given** the same failure, **When** the operator reads the execution log, **Then** the log records the failure reason, and no log entry shows the text `{{novaOptionImage}}` as a resolved value.

### Edge Cases

- The binding value is `${novaOptionImage}`: this is not a placeholder in the service. The command receives the literal text, the same as at each other placeholder site. The documentation states that only `{{name}}` is a placeholder.
- The binding value has text around the placeholder (for example `nova-{{option}}`): the service replaces each placeholder, and the command receives the result with the origin layer `command`.
- The binding name and the placeholder name are the same (`novaOptionImage` = `{{novaOptionImage}}`): the placeholder resolves against the scope outside the binding. It never resolves against the binding itself.
- The binding value names a different parameter (`novaOptionImage` = `{{otherName}}`): the command receives the value of `otherName`.
- The placeholder resolves to a declared default, not to a bound value: the log records the origin layer `default`.
- A loop body step binds `{{iteration}}`: the command receives the number of the current iteration.
- A nested command step in a command has a binding placeholder: the same rule applies against the scope of the command that calls it. When the placeholder cannot be resolved, the nested step gets `skipped_parameter_unresolved` and the calling command continues (FR-004a).
- A nested command step has a binding value with a placeholder that the calling command does not declare (for example `{{anyUndeclaredName}}`): the command save accepts it, because the save does not check a nested binding value today. This feature does not add that check. At run time, the placeholder is unresolved, and the result of FR-004a applies.
- The binding placeholder is unresolved, but the called command declares a default for the bound name (for example, the called command declares `novaOptionImage` with the default `pns-todo-radar`, and the binding value is `{{novaOptionImage}}`): the placeholder resolves only against the scope outside the binding, and the declarations of the called command are not in that scope. Thus the step fails (FR-004), or the nested step gets `skipped_parameter_unresolved` (FR-004a). The service does not use the default of the called command. To use that default, remove the binding, or set its value to `null` (inherit).
- A dry run: the resolution is the same as in a real run.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: At run time, the service MUST replace each placeholder `{{name}}` in a `parameterBindings` value of a sequence Action step before the command receives the value. The resolution MUST use the scope in effect for that step, the same scope that an `imageVisible {{name}}` condition of the step uses.
- **FR-002**: The resolution MUST use the scope outside the binding layer. A binding value MUST NOT resolve against the bindings of the same step.
- **FR-003**: When the binding value is one whole placeholder, the command MUST receive the resolved value with the origin layer of the layer that supplied it (for example `entry`, `sequence`, `queue`, `loop`, or `default`).
- **FR-003a**: When the binding value has text around one or more placeholders, the service MUST replace each placeholder, and the command MUST receive the result with the origin layer `command`.
- **FR-004**: When a placeholder in a binding value of a sequence step cannot be resolved, the step MUST fail with the current parameter resolution error (the parameter name, the field path `parameterBindings.<bindingName>`, and the reason `unresolved`). The command MUST NOT run, and no device input MUST occur. The message MUST end with a remediation hint (Constitution Principle III): "Do one of these to supply a value for '<name>'. Supply the value in the queue template entry or in the run request. Give '<name>' a default value in the sequence or in the calling command. Bind a literal value, or bind a value in the calling command." The default must be on a declaration of '<name>' in the sequence (for a sequence step) or in the calling command (for a nested command step). A default of the called command is not in the scope of the resolution, so it does not help (see Edge Cases). The last part is for a nested command step, where the usual fix is a binding or a declaration in the calling command. The hint is the same at the two call sites. Only an unresolved error with a `parameterBindings.` field path gets this hint. The messages of all other parameter resolution errors MUST NOT change.
- **FR-004a**: FR-001 to FR-003a MUST also apply to `parameterBindings` on a nested command step inside a command. The resolution uses the scope of the command that calls it. When a placeholder in a binding value of a nested command step cannot be resolved, the nested command step MUST get the outcome `skipped_parameter_unresolved` with the message of FR-004 (with the hint). The nested command MUST NOT run, and no device input from it MUST occur. The calling command MUST continue with its next step. This is the current result of an unresolved placeholder in a command step field (clarification 4).
- **FR-005**: A literal binding value (no placeholder) MUST reach the command as it does now, with the origin layer `command`.
- **FR-006**: A step with no `parameterBindings`, and a binding entry with a `null` value, MUST behave as they do now.
- **FR-007**: The service MUST NOT add a new placeholder syntax. The placeholder rules of the service apply to a binding value. A `${name}` value MUST stay literal text, and the documentation of `parameterBindings` MUST state that only `{{name}}` is a placeholder.
- **FR-008**: The execution log MUST record the resolved binding value and its origin layer in `details[kind=parameters]`, as it does for other parameters (feature 078, FR-024).
- **FR-009**: The save rules for a binding value MUST not change: the save check `unresolvable_parameter_reference` for a binding placeholder of a sequence step stays as it is. A binding value of a nested command step keeps its current save behavior (no check).
- **FR-010**: The service MUST NOT change how a `not_executed` tap outcome affects the final sequence status, and MUST NOT change the layer precedence rules.
- **FR-011**: Automated tests MUST cover the resolution of FR-001 to FR-004a and the controls of FR-005 and FR-006.

### Key Entities

- **Parameter binding** (`parameterBindings` entry): a name and a value on a step that runs a command. The value can be a literal, a text with a placeholder, or `null` (inherit).
- **Parameter scope**: the chain of layers (queue, entry, sequence, command, loop) that supplies a value for each name, with the origin layer of each value.
- **Execution log parameter detail**: the entry of kind `parameters` that records each resolved value and its origin layer.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: In the reproduction of the issue, the tap looks for the image that the entry supplies in 100% of runs, and the log shows that value with `originLayer` `entry`.
- **SC-002**: 0 runs send the text of a `{{name}}` placeholder to a command as a parameter value.
- **SC-003**: The two controls of the issue give the same result as before this feature in 100% of runs.
- **SC-004**: All current tests for parameter scopes, bindings, and field templates pass without change.

## Assumptions

- The placeholder syntax of the service is `{{name}}` with a dotted identifier. No other syntax is added.
- The authoring UI does not change.
- The service does not add a save check that is new. For a sequence step, the current save check of binding placeholders is sufficient.
- A binding value of a nested command step has no save check today: the command save accepts a placeholder with an undeclared name. This feature does not add that check (it is out of scope). The run-time result of FR-004a applies to such a value. A save check for a nested binding value is a follow-up item.

## Non-Goals

- No change to a step with no `parameterBindings`, or to a literal binding value.
- No new placeholder syntax.
- No change to how a `not_executed` tap outcome affects the final sequence status.
- No change to the layer precedence rules, except that a resolved binding carries the correct origin layer.
