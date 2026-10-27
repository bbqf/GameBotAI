# Feature Specification: Sequence parameters in ocrText.pattern and ocrText.expected

**Feature Branch**: `165-ocr-pattern-params`  
**Created**: 2026-10-27  
**Status**: Implemented  
**Input**: User description: "Resolve a sequence parameter in ocrText.pattern and ocrText.expected (GitHub issue #347). A `{{name}}` placeholder in `ocrText.pattern` or `ocrText.expected` of a sequence step condition must resolve to the sequence parameter value, as it resolves now in `imageVisible.imageId`."

Issue: #347 - https://github.com/bbqf/GameBotAI/issues/347

## Background

Now, a sequence parameter fills only `imageVisible.imageId` in a step condition. A `{{name}}` placeholder in `ocrText.pattern` or `ocrText.expected` stays as literal text. Thus the OCR check compares the screen text with the placeholder text, not with the parameter value.

The consumer is feature 023 of the PNS repository (`PNS.GatherResources`). A task must test "N or more resource marches are out". N is a value for each account. The test is one OCR read of the `Troop Info` list with the pattern `(?s)(Gathering.*){N}`. Now the pattern holds a fixed N = 3 for all accounts. The other workaround is a gate image for each pair of a level and a value. This workaround needs 25 image files. It also needs five branches for each check, in each of 10 level loops.

## Clarifications

### Session 2026-10-27

- Q: Does a dry run check that the `pattern` is a valid regular expression after substitution? → A: Yes, when each placeholder in the `pattern` has a known value at dry run (a sequence default or a supplied value). The dry run substitutes the values and reports an invalid result as an error. A later answer below gives which dry run uses which values. When a value is known only at run time, the check occurs at run time. Rationale: an invalid pattern must fail early when possible.
- Q: Does the step-through (debug step) mode resolve the placeholders? → A: Yes, in the same way as a normal run. Rationale: the debug mode must show the same result as a run.
- Q: What does the run log show for a resolved `ocrText` condition? → A: Only the resolved value, in the existing description of the condition. Rationale: the log of `imageVisible` shows the resolved `imageId` in the same way.
- Q: Is an empty value for a placeholder inside a longer field an error? → A: No. Only a full field value that is empty or white space after substitution is an error. Rationale: `Level {{suffix}}` with an empty `suffix` is a valid text.
- Q: Does a gamepack install or a gamepack dry run reject an unknown name in `ocrText.pattern` or `ocrText.expected`? → A: No. The gamepack check does not do the parameter reference check now, also not for `imageVisible.imageId`. This feature keeps that scope. The sequence save, the save dry run, and the run reject the unknown name. Rationale: the issue asks for the same behavior as `imageVisible.imageId`, and a change to the gamepack check is new scope.
- Q: Which dry run uses which values for the regular expression check, and which error does each give? → A: Two kinds. (1) The save validation and the save dry run (create, update, patch) use only the sequence defaults. When each name in the `pattern` has a default and the result is not a valid regular expression, the error code is `invalid_resolved_pattern`. (2) A dry-run execution and a run use the supplied values through the run-time resolution. A bad result fails the step with the run-time invalid-pattern message (FR-008). Rationale: the save does not know the caller values, so only the defaults can apply there.
- Q: What occurs when a placeholder in a break condition does not resolve, or its pattern is not valid? → A: The existing break-condition rule applies (feature 066 FR-002a). The error gives "No break", the run log records the error, and the loop continues. All other condition positions fail the step. Rationale: this is the current behavior for `imageVisible.imageId` in a break condition.
- Q: Which form does the field path in an error use? → A: The condition-relative form, for example `condition.pattern` or `if.condition.children[0].expected`. The error message gives the step label as a separate value. Rationale: `imageVisible.imageId` errors use this form now.
- Q: In User Story 4, which dry run rejects an unknown name? → A: The save validation and the save dry run (create, update or patch with `dryRun`) reject it with `unresolvable_parameter_reference`. In a dry-run execution, an unknown name fails the step at run time (FR-007). Rationale: only the save path does the static reference check.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Parameter in an ocrText pattern (Priority: P1)

A sequence author writes an `ocrText` condition with a pattern that contains a `{{name}}` placeholder. The placeholder is the full field (`"{{countPattern}}"`) or a part of a longer pattern (`"(?s)(Gathering.*){{{marchTarget}}}"`). Each account supplies its own value. When the sequence runs, the OCR check uses the pattern with the value in it.

**Why this priority**: This is the request of the issue. It removes the fixed N = 3 and the 25-image workaround.

**Independent Test**: Run a sequence that has an `ocrText` condition with `pattern` `"(?s)(Gathering.*){{{marchTarget}}}"` and the parameter `marchTarget` = `2`. Give an OCR text with two "Gathering" lines. The condition is true. With `marchTarget` = `3`, the same text gives false.

**Acceptance Scenarios**:

1. **Given** a step condition `ocrText` with `pattern` `"{{countPattern}}"` and the parameter `countPattern` = `"(?s)(Gathering.*){2}"`, **When** the step runs, **Then** the OCR check uses the pattern `(?s)(Gathering.*){2}`.
2. **Given** a step condition `ocrText` with `pattern` `"(?s)(Gathering.*){{{marchTarget}}}"` and `marchTarget` = `4`, **When** the step runs, **Then** the OCR check uses the pattern `(?s)(Gathering.*){4}`.
3. **Given** a pattern `"(Gathering.*){3}"` with no placeholder, **When** the step runs, **Then** the OCR check uses the pattern without a change.
4. **Given** an `ocrText` leaf inside an `all`, `any` or `none` condition, **When** the step runs, **Then** the placeholder in the leaf resolves in the same way.
5. **Given** an `ocrText` condition with `pattern` `"(?s)(Gathering.*){{{marchTarget}}}"`, **When** the system makes the resolved condition, **Then** the input condition still holds the text `{{{marchTarget}}}` (FR-006).
6. **Given** a stored sequence with a placeholder in `pattern` and in `expected`, **When** a run of the sequence completes, **Then** the stored sequence still holds the same `{{...}}` text (FR-006).
7. **Given** `pattern` `"(Gathering.*){{3}}"` and no parameter with the name `3` at run time, **When** the step runs, **Then** the step fails with an unresolved-parameter error that names `3`. The OCR check does not get the text `{{3}}`.

---

### User Story 2 - Parameter in an ocrText expected value (Priority: P2)

A sequence author writes an `ocrText` condition with `expected` that contains a `{{name}}` placeholder, for example `"{{accountName}}"` or `"Level {{level}}"`. When the sequence runs, the OCR check compares the screen text with the value after substitution.

**Why this priority**: The issue asks for it, but the PNS consumer uses `pattern` first.

**Independent Test**: Run a sequence with `expected` `"Level {{level}}"` and `level` = `5`. Give the OCR text `Level 5`. The condition is true.

**Acceptance Scenarios**:

1. **Given** `expected` `"{{accountName}}"` and `accountName` = `Alpha`, **When** the OCR text is `Alpha`, **Then** the condition is true.
2. **Given** `expected` `"Level {{level}}"` and `level` = `5`, **When** the OCR text is `Level 6`, **Then** the condition is false.

---

### User Story 3 - The run log shows the resolved value (Priority: P2)

An operator reads the run log of a step with an `ocrText` condition. The log shows the pattern or the expected value after substitution, so the operator can see which value the check used.

**Why this priority**: Without this, the operator cannot tell a wrong value from a wrong OCR read.

**Independent Test**: Run the sequence of User Story 1 and read the log line of the condition. It shows `(?s)(Gathering.*){4}`, not `{{marchTarget}}`.

**Acceptance Scenarios**:

1. **Given** a resolved `ocrText` condition, **When** the run log records the condition, **Then** the log shows the resolved `pattern` or `expected` value.

---

### User Story 4 - A save dry run rejects an unknown parameter name (Priority: P2)

An author saves a sequence or does a save dry run of it (create, update or patch with `dryRun`). A gamepack install is not in this scope. A placeholder in `ocrText.pattern` or `ocrText.expected` names a parameter that the sequence does not define. The save rejects the sequence, as it does now for `imageVisible.imageId`. In a dry-run execution, an unknown name fails the step at run time (FR-007).

**Why this priority**: An unknown name must fail early, not at run time on a device.

**Independent Test**: Do a save dry run of a sequence with `pattern` `"{{unknownName}}"` and no parameter `unknownName`. The result is an error that names `unknownName` and the field path.

**Acceptance Scenarios**:

1. **Given** `pattern` `"(Gathering.*){{{missing}}}"` and no parameter `missing`, **When** the author does a save dry run, **Then** the result is an error that names `missing` and the field path `condition.pattern`. The message gives the step label.
2. **Given** the same error in `expected`, **When** the author does a save dry run, **Then** the error names the field path that ends in `.expected`.
3. **Given** an `ocrText` leaf at `condition.children[1]`, **When** the save dry run finds an unknown name in it, **Then** the field path is `condition.children[1].pattern` (or `.expected`).
4. **Given** `pattern` `"(Gathering.*){{3}}"` and no parameter with the name `3`, **When** the author does a save dry run, **Then** the result is an error that names the unknown name `3` and the field path `condition.pattern`.

### Edge Cases

- **Triple braces**: `{{{name}}}` parses as a literal `{`, then the placeholder `{{name}}`, then a literal `}`. Thus `(Gathering.*){{{n}}}` with `n` = `3` gives `(Gathering.*){3}`.
- **Regex quantifiers**: A quantifier that is not a placeholder, for example `{3}`, `{2,5}` or `{2,}`, does not change.
- **A brace pair around digits only**: `{{3}}` has the placeholder form (a name can be only digits). The system treats it as a placeholder with the name `3`. A save dry run rejects it when no parameter `3` is known. At run time, an unknown name `3` gives an unresolved-parameter error. Authors must not write `{{3}}` as regex text. This is the same placeholder rule that all other fields use now.
- **Unknown name at run time**: When a name does not resolve at run time, the step fails with an unresolved-parameter error. A literal `{{placeholder}}` never goes to the OCR check. This is the same as for `imageVisible.imageId`.
- **Empty result**: When the full field value after substitution is empty or only white space, the step fails with an unresolved-parameter error. An empty pattern matches all text, so an empty result is almost always a mistake. An empty value for a placeholder inside a longer field is not an error when the full result is not empty.
- **Invalid regex after substitution**: The resolved `pattern` can be a regular expression that is not valid. Then the step fails with an error. The error names the field path and gives the resolved pattern. The check does not silently give false. A dry-run execution gives the same run-time error. A save dry run gives the error code `invalid_resolved_pattern` when each name of the pattern has a sequence default (FR-012).
- **Break condition**: In a break condition, an unresolved name, an empty result or an invalid resolved pattern gives "No break". The run log records the error and the loop continues (feature 066 FR-002a). This is the same as for `imageVisible.imageId`.
- **Special characters in the value**: The value goes into the pattern as it is, with no regex escape. Thus a value can be a full pattern or a part of a pattern.
- **Save-time check of a pattern with a placeholder**: Save-time validation does not compile a `pattern` that contains a placeholder as a regular expression. The reason is that the final pattern is not known before the value is known. The regular expression check occurs after substitution: in the save dry run when each name has a sequence default (FR-012), and in the dry-run execution and the run (FR-008).
- **Loop iteration**: In a loop condition and a break condition, `{{iteration}}` is legal in `pattern` and `expected`, the same as in `imageVisible.imageId`.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The system MUST replace each `{{name}}` placeholder in `pattern` with the value of the sequence parameter `name`. The value comes from the scope of the step. The replacement MUST occur before the OCR check of the `ocrText` condition runs.
- **FR-002**: The system MUST replace each `{{name}}` placeholder in `expected` in the same way as FR-001.
- **FR-003**: A placeholder MUST resolve when it is the full field value and when it is a part of a longer value. A field can hold more than one placeholder.
- **FR-004**: The placeholder rule MUST be the same rule that `imageVisible.imageId` uses now. `{{{name}}}` MUST give a literal `{`, the value, and a literal `}`. A regex quantifier that is not a placeholder (`{3}`, `{2,5}`, `{2,}`) MUST NOT change.
- **FR-005**: Resolution MUST occur in each condition position where `imageVisible.imageId` resolves now. These positions are:
  - The step condition.
  - The If condition.
  - The loop condition.
  - The break condition.
  - Each leaf inside an `all`, `any` or `none` condition, at any depth.
- **FR-006**: The stored sequence MUST NOT change. Resolution makes a new condition for the evaluation only. The input condition of the resolution MUST keep its `{{...}}` text.
- **FR-007**: The step MUST fail with an unresolved-parameter error in these two conditions:
  - A name does not resolve at run time. This includes a name of digits only, for example `{{3}}`.
  - The full resolved value is empty or white space.

  The error MUST name the parameter and the field path. The field path is the condition prefix (for example `condition` or `if.condition`), then `.children[i]` for each composite level, then `.pattern` or `.expected`. The message gives the step label as a separate value.
- **FR-008**: When a resolved `pattern` is not a valid regular expression, the step MUST fail with an error that names the field path and gives the resolved pattern. This error MUST have its own failure kind, different from the unresolved-parameter failure kind of FR-007. The errors of FR-007 MUST keep their current failure kind.

  Exception: in a break condition, the errors of FR-007 and FR-008 MUST give "No break", as for `imageVisible.imageId` now (feature 066 FR-002a). The run log MUST record the error.
- **FR-009**: The run log MUST show the `pattern` or `expected` value after substitution, in the existing description of the `ocrText` condition. The log does not also show the placeholder text.
- **FR-010**: The step-through (debug step) mode MUST resolve the placeholders in the same way as a normal run.
- **FR-011**: The save validation and the save dry run (create, update or patch with `dryRun`) of a sequence MUST reject an unknown parameter name in `ocrText.pattern` or `ocrText.expected`. The rules and the error form MUST be the same as for `imageVisible.imageId`. A name of digits only, for example `{{3}}`, has the same rule. The gamepack check does not change.
- **FR-012**: This requirement applies to the save validation and the save dry run (create, update, patch). When each name in a `pattern` has a sequence default, they MUST substitute the defaults into the `pattern`. When the result is not a valid regular expression, they MUST report the error code `invalid_resolved_pattern`. A dry-run execution MUST use the supplied values through the run-time resolution. A bad result MUST fail the step as in FR-008.
- **FR-013**: The parameter reference scan MUST report each placeholder in `ocrText.pattern` and `ocrText.expected`, so that the "unused value" report counts these fields as users of a parameter.
- **FR-014**: Save-time validation MUST NOT compile a `pattern` that contains a placeholder as a regular expression. A `pattern` with no placeholder MUST have the same validation as now.
- **FR-015**: The value MUST go into the field as it is, with no regex escape.
- **FR-016**: The system MUST NOT resolve placeholders in other `ocrText` fields (`region`, `parser`) or in other condition types. The behavior of `imageVisible.imageId` MUST NOT change.
- **FR-017**: When the work is complete, the living docs MUST show the change:
  - The `**Status**:` line of this spec MUST be `Implemented`.
  - `specs/STATUS.md` MUST show feature 165 with the status `Implemented`. Add this row in one step at the end, in the same task that sets the spec status. Do not add the row before with a different status.
  - `docs/architecture.md` MUST name `ocrText.pattern` and `ocrText.expected` as fields that resolve placeholders.
  - `CHANGELOG.md` MUST have an entry for feature 165 (issue #347).

### Key Entities

- **ocrText condition**: A step condition that reads text from a screen region and compares it with `expected` (exact text) or `pattern` (regular expression). After this feature, both fields can hold `{{name}}` placeholders.
- **Sequence parameter scope**: The set of parameter values that apply at a step (sequence defaults, caller values, loop `iteration`). It supplies the values for the placeholders.
- **Field path**: The location of a field in an error, relative to the step, for example `if.condition.children[0].pattern`. The error gives the step label as a separate value.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A single `ocrText` condition with a parameter in `pattern` replaces the 25 gate images and five branches for each check of the workaround. One sequence serves all accounts, each with its own N.
- **SC-002**: In tests, 100% of the placeholder forms in this spec resolve to the expected text. These forms are the full field, inline, triple brace, more than one placeholder, and a nested composite leaf.
- **SC-003**: In tests, 100% of the regex quantifiers `{3}`, `{2,5}` and `{2,}` with no placeholder stay unchanged.
- **SC-004**: The save validation and the save dry run report 100% of the unknown names in `ocrText.pattern` and `ocrText.expected`, with the correct field path.
- **SC-005**: Each run log line of an `ocrText` condition with a placeholder shows the resolved value, not the placeholder text.
- **SC-006**: Performance (Constitution Principle IV). A condition with no placeholder keeps the current fast path. The resolver gives the same condition instance and makes no new allocation. A unit test asserts the same instance. A condition with a placeholder adds less than 1 ms for each evaluation (one substitution and one regex parse). The PR description gives this performance statement.

## Assumptions

- The placeholder syntax and the name rule (`\w+` with optional dotted parts) are the same as in all other fields now. This feature adds no new syntax.
- The scope of a condition is the same scope that `imageVisible.imageId` uses in the same position now.
- The web UI needs no change. The author types the placeholder in the existing text fields. The feature does not change the web UI, so no phone support level applies.
- The PNS gamepack change is not in this work.
