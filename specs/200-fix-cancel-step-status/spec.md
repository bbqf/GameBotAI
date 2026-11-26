# Feature Specification: Give a reschedule-self Cancel step the status success when the cancel runs (B-046)

**Feature Branch**: `200-fix-cancel-step-status`  
**Created**: 2026-11-26  
**Status**: Draft  
**Input**: GitHub issue #414, "B-046: reschedule-self Cancel removes the booking but the execution log gives the step the status failure". Closes #414. Labels: bug, P2.

## Background

A `reschedule-self` step with `option: "Cancel"` removes the pending bookings of its own sequence (feature 123, issue #264). The cancel does its work, but the execution log gives the step node the status `failure`.

Example from the issue. Execution log of `PNS.EnhanceGear`, queue `PNS.Production (Farm-1)`, 2026-11-26 02:04 UTC, last step (`stepId` `cancel-retry`):

```json
{
  "nodeKind": "step",
  "label": "PD-3: the work is done, so the retry of step 0 is removed. No condition.",
  "status": "failure",
  "message": "Self-reschedule 'PD-3: the work is done, so the retry of step 0 is removed. No condition.' cancelled: removed 1 pending booking(s) of this sequence; applies to the current run only"
}
```

The message says that the cancel did its work. The sequence status is `success`, and no retry run follows. The production queue logs of 2026-11-23 to 2026-11-26 hold 165 `Cancel` steps on 4 queues and 13 sequences. All 165 have the status `failure`.

Analysis of the code (read only):

- The sequence runner records each `reschedule-self` step as "Succeeded", with the action outcome of the step. A `Cancel` step that removes bookings has the action outcome `cancelled`. A `Cancel` step that removes no booking has the action outcome `noop`.
- The execution log calculates the status of a step node from the action outcome when it reads the log. This calculation knows `scheduled` (status `success`) and `noop` (status `skipped`). It does not know `cancelled`, so `cancelled` gets the default status `failure`.
- Thus a `Cancel` step that removes bookings shows `failure`, and a `Cancel` step that finds no booking shows `skipped`. Neither shows `success`.
- The status is calculated when the log is read. The stored log keeps the action outcome and the option of the step. Thus a fix of the calculation also gives the correct status to the logs that the service stored before the fix.

## Clarifications

### Session 2026-11-26

The pipeline runs with no manual review. Each answer is the most reasonable option, chosen from the issue, the spec, and the code.

- Q: Which status does a `Cancel` step get when it has nothing to remove because the run has no originating queue, or because the queue run is no longer active? → A: `success`, the same as a `Cancel` step that finds no booking. Rationale: the cancel ran, and the result that the step must make (no pending booking of this sequence) is true; the issue keeps `failure` only for a cancel that could not run.
- Q: When an error occurs during the removal of bookings, does the step stop the sequence (as now) or record a failed step and continue? → A: The step records the action outcome `failed`, the message "cancel not performed: <error text>", and the `removed` flag `false`; the sequence continues. Rationale: acceptance criterion 3 needs a step node with the status `failure`, and feature 123 says that a `Cancel` step never fails the run.
- Q: Which outcome does a `commandOutcome` condition read for a `Cancel` step that had an error? → A: No change: the runner records `success` for each `reschedule-self` step, as now. Rationale: the issue is about the execution log status only; a change of the condition outcome changes the run flow, which is a non-goal.
- Q: Does the fix change the stored action outcome (`cancelled`, `noop`) or the `actionOutcome` and `reasonCode` values of the log node? → A: No. Only the calculated `status` of the node changes. Rationale: the message text starts with the action outcome ("... cancelled: ..."), and the message must not change; also the logs stored before the fix then get the correct status.
- Q (analyze C1): Does the rule `cancelled` → `success` apply to all step types? → A: No, only to `reschedule-self` step nodes. Rationale: a primitive or WaitForImage step that a stop cuts off also has the outcome `cancelled` (`CommandExecutor`), and it must keep the status `failure`.
- Q (analyze C2): Does the sequence status change when a `Cancel` step has an error? → A: Yes, for this case only. Today the error ends the run with `failure`; after the fix the run continues and the other steps set the status. Rationale: feature 123 says that a `Cancel` step never fails the run, and acceptance criterion 3 needs a step node.
- Q: How does the log know that a `noop` step is a `Cancel` step? → A: From the `option` value that the log node of a `reschedule-self` step keeps (feature 065). A node with no `option` value keeps the status `skipped`. Rationale: the option is in the stored logs already, so the old logs also get the correct status with no change to the files.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - A Cancel step that removes its bookings shows success (Priority: P1)

An operator opens the execution log of a sequence run that ends with a `reschedule-self` `Cancel` step. The cancel removed one or more pending bookings of the sequence. The step node of the `Cancel` step has the status `success`, and its message stays as it is now.

**Why this priority**: This is the defect in the issue. Each daily run of the reporter shows a red failed step, so the operator cannot see a real failure between them.

**Independent Test**: Read the execution log of a run whose `Cancel` step removed one booking. The step node has `"status": "success"` and the message "Self-reschedule '<label>' cancelled: removed 1 pending booking(s) of this sequence; applies to the current run only".

**Acceptance Scenarios**:

1. **Given** a queue run with one or more pending bookings of a sequence, **When** the sequence runs a `reschedule-self` step with `option: "Cancel"`, **Then** the bookings are removed and the execution log node of that step has `"status": "success"`.
2. **Given** an execution log that the service stored before the fix, with a `Cancel` step whose action outcome is `cancelled`, **When** the operator reads the log, **Then** the step node has `"status": "success"`.
3. **Given** the same run, **When** the operator reads the log, **Then** the message of the step node is the same text as before the fix.

---

### User Story 2 - A Cancel step that finds no booking shows success (Priority: P2)

An operator opens the execution log of a run whose `Cancel` step found no pending booking to remove. The step node has the status `success`, because the result that the step must make (no pending booking of the sequence) is true.

**Why this priority**: The issue asks for this, but the current status (`skipped`) is not red, so the effect on the operator is smaller.

**Independent Test**: Read the execution log of a run whose `Cancel` step found no pending booking. The step node has `"status": "success"` and the message "Self-reschedule '<label>' noop: no pending booking of this sequence; nothing to cancel".

**Acceptance Scenarios**:

1. **Given** a queue run with no pending booking of the sequence, **When** the sequence runs a `reschedule-self` step with `option: "Cancel"`, **Then** the execution log node of that step has `"status": "success"`.
2. **Given** a `reschedule-self` step with an option that is not `Cancel` (for example `Timer`), **When** the step has the action outcome `noop`, **Then** the step node keeps the status `skipped`.

---

### User Story 3 - A Cancel step that cannot run shows failure (Priority: P3)

An operator opens the execution log of a run whose `Cancel` step could not run because an error occurred during the cancel. The step node has the status `failure`, and the message tells the cause.

**Why this priority**: Today an error during the cancel stops the sequence run, and the step gets no clear node. The issue asks that the status `failure` is only for this case.

**Independent Test**: Make the removal of bookings throw an error in a test. The step node has `"status": "failure"`, the message tells that the cancel was not done, and the sequence status is the same as for a `Cancel` step that runs.

**Acceptance Scenarios**:

1. **Given** a `Cancel` step whose removal of bookings causes an error, **When** the sequence runs the step, **Then** the execution log node of that step has `"status": "failure"`, the action outcome `failed`, the `removed` flag `false`, and a message "Self-reschedule '<label>' failed: cancel not performed: <error text>".
2. **Given** the same step, **When** the sequence continues, **Then** the sequence does not stop because of the step, and the next step runs (a `Cancel` step never fails the run, feature 123). Today the error ends the run with the status `failure`, so this is a change for the error case only.

---

### Edge Cases

- A primitive or WaitForImage step that a stop cut off has the outcome `cancelled`. It keeps the status `failure` (FR-001).
- The run has no originating queue (an ad-hoc run). The `Cancel` step has nothing to remove: status `success`, message unchanged ("no originating queue, no reschedule performed").
- The run of the originating queue is no longer active. The `Cancel` step has nothing to remove: status `success`, message unchanged ("originating queue run no longer active; nothing to cancel").
- The step payload cannot be read. The service does not know that the step is a `Cancel` step. The step keeps its current status (`skipped`) and message.
- A stored log has no `option` value on a `reschedule-self` step node with outcome `noop`. The step keeps the status `skipped`.
- A `commandOutcome` condition that reads a `Cancel` step continues to see the outcome `success` (no change).

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The execution log MUST give the status `success` to a `reschedule-self` step node with the action outcome `cancelled`. A step node of another step type with the outcome `cancelled` (for example a WaitForImage step that a stop cut off) MUST keep its current status `failure`.
- **FR-002**: The execution log MUST give the status `success` to a `reschedule-self` step node with the option `Cancel` and the action outcome `noop`.
- **FR-003**: A `reschedule-self` step node with an option that is not `Cancel`, or with no known option, and the action outcome `noop` MUST keep the status `skipped`.
- **FR-004**: When an error occurs during the removal of bookings of a `Cancel` step, the step MUST record the action outcome `failed` and a message "cancel not performed: <error text>". The execution log MUST give this step node the status `failure`.
- **FR-005**: A `Cancel` step MUST NOT stop the sequence, also when an error occurs (feature 123 rule). The other steps of the run set the sequence status. For a `Cancel` step that runs, the sequence status does not change (acceptance criterion 4). For a `Cancel` step that has an error, this is a change: today the error ends the run with the status `failure`; after the fix the run continues.
- **FR-006**: The step message of a `Cancel` step that runs MUST stay the same text as before the fix.
- **FR-007**: The selection and the removal of bookings by `Cancel` MUST NOT change.
- **FR-008**: The status of the other `reschedule-self` options (`Timer` and the other options) MUST NOT change.
- **FR-009**: The fix MUST apply also to the execution logs that the service stored before the fix, because the status is calculated when the log is read.
- **FR-010**: The outcome that a `commandOutcome` condition reads for a `Cancel` step MUST stay `success`, also for a `Cancel` step that had an error.
- **FR-011**: The stored action outcome (`cancelled`, `noop`) and the `actionOutcome` and `reasonCode` values of the log node MUST NOT change. Only the calculated `status` changes. The new action outcome `failed` is only for a `Cancel` step that had an error.

### Key Entities

- **Step node of the execution log**: One step of a sequence run in the log tree. It has `nodeKind`, `label`, `status` (`success`, `failure`, `skipped`, and others), and `message`. The status is calculated from the action outcome of the step.
- **Action outcome of a `reschedule-self` step**: `scheduled`, `cancelled`, `noop`, or (new for an error during a cancel) `failed`. The log node also keeps the `option` of the step and, for `Cancel`, the `removed` flag.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: 100% of `Cancel` steps that remove one or more bookings have the status `success` in the execution log (today 0 of 165).
- **SC-002**: 100% of `Cancel` steps that find no booking have the status `success` in the execution log.
- **SC-003**: A sequence run in which all steps succeed and the last step is a `Cancel` step that runs shows no step with the status `failure` in the execution log.
- **SC-004**: The message text of each `Cancel` step and the sequence status of each run are the same before and after the fix.
- **SC-005**: The logs stored before the fix show the corrected status with no change to the stored files.

## Assumptions

- The status names of the execution log (`success`, `failure`, `skipped`, `running`, `not_executed`, `no_break`) do not change.
- "Could not run" means an error (exception) during the removal of bookings. A `Cancel` step that has nothing to remove (no booking, no originating queue, or a queue run that is no longer active) ran correctly.
- The web UI shows the status that the service gives. The web UI needs no change, so the feature does not change the web UI and has no phone support statement.
