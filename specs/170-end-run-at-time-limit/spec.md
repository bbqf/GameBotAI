# Feature Specification: End a run at its time limit without a failure status

**Feature Branch**: `170-end-run-at-time-limit`  
**Created**: 2026-10-30  
**Status**: Draft  
**Input**: User description: "GitHub issue #354: FR-038: End a run at its time limit without a failure status, or read the elapsed time. Closes #354."

## Background

A sequence has a time limit (field `watchdogTimeoutMs`; the issue names 900000 ms, the platform default in code is 240000 ms and does not change). When a run reaches the time limit, the platform cancels the run. Today the run gets the status `failure` and the cancellation reason `sequence_time_limit` (feature 094).

Some tasks use the limit as a planned stop. Example: feature 023 task `PNS.GatherResources` searches the world map and sends marches. Its first step books the next run 30 minutes later. When the time limit stops the run, nothing is wrong. But the log shows a failed run. A reader cannot tell this stop from a real fault (a named failure of the task) without the message text. Example: execution `ada0b1f995744452adf98668f201446e` on 2026-10-28.

This feature adds **option 1** from the issue: a sequence option that makes the time limit a safe stop. This feature does **not** add option 2 (a condition on the elapsed time of the run). See Assumptions.

## Clarifications

### Session 2026-10-30

- Q: Where does the option apply? → A: Everywhere the time limit applies today (queue firings and any other run that the limit cancels today). Rationale: one rule, no new paths.
- Q: Does a stop by choice need a new log status? → A: No. The status is `success`; the reason `sequence_time_limit` is the marker. Rationale: the non-goals forbid a new status that is not needed.
- Q: What does a parent sequence see when a child run ends at its limit with `onTimeLimit` = `end`? → A: No change to parent handling. The parent logic stays as it is today. Rationale: scope.
- Q: How does the queue count a firing that ends at the limit with `onTimeLimit` = `end`? → A: As a successful firing: no failure notification and no daily retry. Rationale: the stop is planned; this is the purpose of the option.
- Q: Which log field tells the reader that the stop was by choice? → A: The structured fields only: status `success` plus reason `sequence_time_limit`. The summary text may also say it, but no reader needs it.
- Q: Is the summary text note for a stop by choice a requirement? → A: No. The summary text SHOULD say it, but the contract is the structured fields (status and reason). A test on the summary text is a soft check.
- Q: What must a test show for a time limit that is reached while a step is in progress? → A: The in-flight step is cancelled as today, and only the final status changes (`success` for `end`).
- Q: What is the observable for "parent handling unchanged"? → A: The parent run status and the queue record for the same case with `fail` stay as they are today; only the child run status differs for `end`.
- Q: What is the phone support level for the editor change? → A: Full.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - A planned time-limit stop is not a failure (Priority: P1)

An author sets, on a sequence, the option `onTimeLimit` to `end`. When a run of this sequence reaches its time limit, the run ends and the log shows the status `success`. The cancellation reason `sequence_time_limit` stays in the log entry.

**Why this priority**: This is the whole gap. A planned stop must not show as a failure.

**Independent Test**: Run a sequence with `onTimeLimit` set to `end` and a short time limit, and with steps that take longer than the time limit. Read the execution log. The status is `success` and the reason is `sequence_time_limit`.

**Acceptance Scenarios**:

1. **Given** a sequence with `onTimeLimit` = `end` and a time limit of N ms, **When** a run is still working at N ms, **Then** the run ends, the log status is `success`, and the log entry holds the reason `sequence_time_limit` and the time limit value N.
2. **Given** the same sequence, **When** a run finishes before the time limit, **Then** the status is `success` and no cancellation reason is set.
3. **Given** the same sequence, **When** a step of the run fails before the time limit, **Then** the status is `failure` as before.
4. **Given** a sequence with `onTimeLimit` = `end` and no `watchdogTimeoutMs`, **When** a run is still working at the default time limit, **Then** the run ends, the log status is `success`, and the log entry holds the reason `sequence_time_limit` and the default limit value.
5. **Given** a sequence with `onTimeLimit` = `end`, **When** a step fault is recorded at the same time as the time limit, **Then** the log status stays `failure`.
6. **Given** a parent sequence that starts a child run, **When** the parent runs, **Then** the handling of the parent is the same as before this feature.

---

### User Story 2 - A reader can tell a time-limit stop from a real fault (Priority: P1)

A person who reads the execution log must see from structured fields (not from message text) that the run ended at its time limit. A run that ended at the time limit by choice has the status `success` and the reason `sequence_time_limit`. A run that failed for another reason has the status `failure` and no such reason.

**Why this priority**: This is the second part of the gap: the reader cannot tell the two cases apart.

**Independent Test**: Run one sequence that ends at its time limit with `onTimeLimit` = `end` and one that fails on a step. Check that the log entries differ in the structured fields only (status and reason), with no need to read the message.

**Acceptance Scenarios**:

1. **Given** two log entries, one from a time-limit stop with `onTimeLimit` = `end` and one from a real fault, **When** a reader filters by status and reason, **Then** the reader can separate them.
2. **Given** a run that ended at its time limit with `onTimeLimit` = `end`, **When** the reader opens the log entry, **Then** the structured fields (status and reason) show that the run ended at its time limit by the author's choice. The summary text SHOULD also say it, but it is not a contract.

---

### User Story 3 - Existing sequences keep the current behaviour (Priority: P1)

A sequence without the option, or with the option set to its default value, keeps today's behaviour: at the limit the run gets the status `failure` and the reason `sequence_time_limit`.

**Why this priority**: The change must not alter any existing run.

**Independent Test**: Run an existing sequence (no option) past its time limit. The result is the same as before this feature.

**Acceptance Scenarios**:

1. **Given** a sequence saved before this feature, **When** a run reaches its time limit, **Then** the status is `failure` and the reason is `sequence_time_limit`.
2. **Given** a sequence with `onTimeLimit` set to the default value, **When** a run reaches its time limit, **Then** the result is the same as in scenario 1.

---

### User Story 4 - An author sets the option and gets clear errors (Priority: P2)

An author can set and read the option when the author creates or edits a sequence. A value that the system does not know is rejected with a clear error.

**Why this priority**: The option is useless if the author cannot set it. It is lower than the run behaviour.

**Independent Test**: Save a sequence with `onTimeLimit` = `end`, read it back, and see the same value. Save one with an unknown value and see a clear error.

**Acceptance Scenarios**:

1. **Given** a valid sequence body with `onTimeLimit` = `end`, **When** the author saves it, **Then** the sequence is saved and returns the value.
2. **Given** a sequence body with an unknown `onTimeLimit` value, **When** the author saves it, **Then** the system rejects it with an error that names the field and the allowed values.
3. **Given** a sequence body without `onTimeLimit`, **When** the author saves it, **Then** the sequence is saved with the default behaviour.

---

### Edge Cases

- A run reaches the time limit while a step is in progress: the run ends as it ends today (cancel in-flight work). Only the final status changes.
- A run reaches the time limit and a real fault happens at the same time: a fault that the run records before the cancel keeps the status `failure`.
- The time limit is the platform default (no `watchdogTimeoutMs` on the sequence) and `onTimeLimit` = `end`: the option applies to the default time limit too.
- The sequence runs as a step of a parent sequence or a queue firing: the option of the sequence that reaches its own time limit decides the status of that run. How a parent treats the result is not changed.
- Today only the queue firing path applies the time limit. Parent and child handling is therefore not changed and needs no extra behaviour.
- Export and import of a sequence keep the option.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: A sequence MUST accept an optional setting `onTimeLimit` with the allowed values `fail` (default) and `end`.
- **FR-002**: When a run of a sequence with `onTimeLimit` = `end` reaches its time limit, the system MUST end the run with the log status `success`.
- **FR-003**: In the case of FR-002, the system MUST keep the cancellation reason `sequence_time_limit` and the time limit value in the log entry.
- **FR-004**: A reader MUST be able to tell a time-limit stop from a real fault with structured log fields only (status and cancellation reason), without the message text.
- **FR-004a**: The queue MUST count a firing that ended at the time limit with `onTimeLimit` = `end` as a successful firing (no failure notification, no daily retry).
- **FR-005**: When `onTimeLimit` is absent or `fail`, the system MUST keep the current behaviour: the status `failure` with the reason `sequence_time_limit`.
- **FR-006**: The system MUST reject an unknown `onTimeLimit` value on save with an error that names the field and lists the allowed values. The save check in the API and the save check in the file repository MUST use one shared error message.
- **FR-007**: The system MUST NOT change the value or the default of `watchdogTimeoutMs`.
- **FR-008**: The system MUST NOT add new log statuses or new condition types.
- **FR-009**: The system MUST NOT change the booking logic or other content of the task `PNS.GatherResources`.
- **FR-010**: Sequences saved before this feature MUST load and run with no change in behaviour.
- **FR-011**: The public API description and the authoring documentation MUST describe the option and the log result.
- **FR-012** *(web UI)*: If the web UI shows the sequence editor, the UI MUST let the author set the option. Phone support level: full. The feature MUST follow docs/mobile-ui-principles.md. The select MUST follow the tap-target rule in that file: a hit area of at least 44 x 44 CSS px. The select MUST also use an input font of at least 16 px.

### Key Entities

- **Sequence**: gets the optional setting `onTimeLimit` (`fail` or `end`).
- **Execution log entry**: keeps the existing status, cancellation reason, and time limit value. For an `end` stop it holds status `success` with the reason `sequence_time_limit`.

## Assumptions

- The feature chooses **option 1 only**. Option 2 (a `runElapsed` condition) is out of scope; the issue asks for one or both, and one is enough to meet all criteria for the gap seen in production.
- The status for a stop by choice is the existing `success`, not a new status. This follows the non-goal "no new status beyond what is needed". The reason `sequence_time_limit` is the structured marker that separates the stop from a normal finish.
- The name of the option is `onTimeLimit`, as the issue proposes.
- Existing log readers that show the cancellation reason will show it for `success` entries with no change.
- Task content (for example `PNS.GatherResources`) is changed by the author later, not by this feature.
- When the work ends, the Status line of this spec is set to "Implemented", and the status index (`specs/STATUS.md`) is kept in step with it.
- Tests for the new behaviour go in new test files, separate from the existing time-limit regression file.
- The user term is "time limit". The text `watchdogTimeoutMs` is only the name of the field.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: 100% of runs that reach the time limit on a sequence with `onTimeLimit` = `end` have the status `success` and the reason `sequence_time_limit` in the log.
- **SC-002**: 100% of runs that reach the time limit on a sequence without the option keep the status `failure` and the reason `sequence_time_limit`.
- **SC-003**: A reader separates time-limit stops from real faults with a filter on status and reason, with 0 message reads needed.
- **SC-004**: All sequences saved before this feature load and run with no change in result.
- **SC-005**: An unknown option value is rejected on save in 100% of attempts, with an error that names the allowed values.
