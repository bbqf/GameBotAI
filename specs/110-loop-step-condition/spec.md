# Feature Specification: A condition on a Loop step is a guard that the runtime obeys

**Feature Branch**: `claude/resolve-github-issue-wp4ek5` (spec number 110)  
**Created**: 2026-09-28  
**Status**: Implemented  
**Input**: GitHub issue #232 (B-026): "a condition on a Loop step is accepted and ignored at run time". Full description: see the issue and the feature description that started this spec.

## Background

A sequence step can have a `condition`. On an `Action` step, the condition is a guard: when the condition is false, the runtime skips the step and the execution log shows the condition result.

`POST /api/sequences` (also with `dryRun: true`) accepts a `condition` on a top-level `Loop` step and returns `{ "valid": true }`. But the runtime ignores the condition. The loop runs when the condition is true and when it is false. The execution log shows no condition result for the `Loop` node ("Loop 'leave-if-stuck' true after 4 iterations").

The issue shows a live run on 2026-09-27. A `WaitForImage` step found `pns-sim-final-dialog`. 0.4 s later the next step, the `Loop` `leave-if-stuck`, ran and pressed BACK three times, but its condition was `all(none(lastRun ...), imageVisible(pns-sim-final-dialog, negate: true))`, which was false. The guards of four loops in the `PNS.SimTraining` sequence did not operate. The workaround is a `Break` step on the guard as the first step of each loop body.

## Selected result and reason

The issue permits two results: (1) the runtime skips a `Loop` step when its `condition` is false, or (2) the validator rejects a `condition` on a `Loop` step with HTTP 400.

This spec selects **result 1: the runtime obeys the condition on a `Loop` step as a guard.** The reasons are:

- The authors of `PNS.SimTraining` wrote the condition as a guard, and they expect it to operate. Result 1 gives them this behavior. Result 2 removes the capability and keeps the `Break` workaround in each loop body. A `Break` workaround also does not help a `Loop` that must run zero times: a `Break` step stops the body but the loop still starts.
- Result 1 uses the same rule as a conditioned `Action` step. The author does not have to know a special rule for one step type.
- Result 1 adds no new field. The `condition` field is already on the step contract, and `GET /api/sequences/{id}` already writes the `condition` of each step.
- Result 1 does not make a sequence that saves today fail to save, when its loop condition is correct.

## Clarifications

### Session 2026-09-28

- Q: Which result does the fix use: skip on false, or 400? → A: Skip on false (result 1). Rationale: see "Selected result and reason" above.
- Q: When is the condition of a `Loop` step evaluated? → A: One time, before the first iteration, at the position of the `Loop` step in the sequence. It is not evaluated again before each iteration. Rationale: this is the behavior of a guard on an `Action` step. A condition for each iteration is the job of a `while` or `repeatUntil` loop configuration.
- Q: What does the execution log show for a `Loop` step that the guard skips? → A: One `Loop` entry with status `Skipped`, zero iterations, the condition type, the condition result `false`, and a message that tells that the condition was false. When a composite condition decides the result, the message names the child that decided it, as for an `Action` step. Rationale: the issue asks for the same condition trace as for `If` and conditioned `Action` nodes.
- Q: What does the execution log show for a `Loop` step with a true guard? → A: The usual `Loop` entry, and in addition the condition type and the condition result `true`. Rationale: the issue asks for the condition result for the `Loop` node in all cases.
- Q: What occurs when the evaluation of the guard fails (for example, the image evaluator is not available)? → A: The same as for an `Action` step: the `Loop` step fails with condition result `error`, and the sequence stops. Rationale: same rule as a conditioned `Action` step.
- Q: Does save-time validation check the condition of a `Loop` step? → A: Yes, with the same rules as for the condition of an `Action` step (a `commandOutcome` reference must name a prior step, an `imageVisible` leaf needs an `imageId`, a composite must be correct, a referenced image must exist). A `commandOutcome` reference to a step in the body of the same loop is not a prior step. Rationale: a condition that has an effect must be correct at save time; else the run fails later.
- Q: What does a later step read as the `commandOutcome` of a `Loop` step that the guard skipped? → A: `skipped`. Rationale: the same value as for a skipped `Action` step.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - A Loop step with a false guard does not run (Priority: P1)

An author puts a `condition` on a top-level `Loop` step, for example "the final dialog is not visible". When the condition is false at that position in the run, the runtime does not run the loop body. The run continues with the next step.

**Why this priority**: This is the defect in the issue. Without the fix, the loop presses keys or swipes when it must do no work.

**Independent Test**: Run a sequence with a `Loop` step whose `condition` is false. The body steps do not run. The next top-level step runs.

**Acceptance Scenarios**:

1. **Given** a top-level `Loop` step with a `condition` that is false, **When** the sequence runs, **Then** no body step of the loop runs, and the next top-level step runs.
2. **Given** a top-level `Loop` step with a composite `condition` (for example `all(none(lastRun ...), imageVisible(..., negate: true))`) that is false, **When** the sequence runs, **Then** no body step runs.
3. **Given** a top-level `Loop` step with a `condition` that is true, **When** the sequence runs, **Then** the loop runs as before, with the same number of iterations.
4. **Given** a top-level `Loop` step without a `condition`, **When** the sequence runs, **Then** the loop runs as before.

---

### User Story 2 - The execution log shows the condition result of a Loop step (Priority: P1)

An operator reads the execution log of a run. The `Loop` entry shows the condition type and the condition result, as the entries of `If` steps and conditioned `Action` steps do.

**Why this priority**: Without the condition result, the operator cannot see why a loop ran or did not run. The issue reports this gap.

**Independent Test**: Run a sequence with a guarded `Loop` step, one time with a false guard and one time with a true guard. Read the `Loop` entry of the execution log.

**Acceptance Scenarios**:

1. **Given** a `Loop` step with a false guard, **When** the sequence runs, **Then** the `Loop` entry has status `Skipped`, zero iterations, the condition type, the condition result `false`, and a message that tells that the condition was false.
2. **Given** a `Loop` step with a true guard, **When** the sequence runs, **Then** the `Loop` entry has the condition type and the condition result `true`, beside its usual status and iteration count.
3. **Given** a `Loop` step without a condition, **When** the sequence runs, **Then** the `Loop` entry has no condition type and no condition result, as before.

---

### User Story 3 - The condition of a Loop step is stored and checked at save time (Priority: P2)

An author saves a sequence with `POST /api/sequences`. The service keeps the `condition` of the `Loop` step, returns it on read, and checks it with the same rules as the condition of an `Action` step.

**Why this priority**: The guard can only operate when the service stores it. A wrong guard must fail at save time and not at run time.

**Independent Test**: Save a sequence with a guarded `Loop` step and read it back. Save a sequence with an incorrect guard on a `Loop` step and get a 400.

**Acceptance Scenarios**:

1. **Given** a `Loop` step with a correct `condition`, **When** the author saves the sequence and reads it again, **Then** the `Loop` step has the same `condition`.
2. **Given** a `Loop` step with a `commandOutcome` condition that names an unknown step or a later step, **When** the author sends `POST /api/sequences` (with or without `dryRun: true`), **Then** the response is 400 and the error names the step and the `commandOutcome` reference.
3. **Given** a `Loop` step with an `imageVisible` condition that has no `imageId`, **When** the author sends `POST /api/sequences`, **Then** the response is 400.

### Edge Cases

- The guard is false and the `Loop` step is a `count` loop with a count of zero: the `Loop` entry shows `Skipped` with the condition result `false`. The body does not run.
- The guard refers to the `commandOutcome` of an earlier step that was skipped: the guard evaluates this as for an `Action` step.
- The guard uses `lastRun`: it evaluates the same run history as for an `Action` step.
- The guard evaluation fails: the `Loop` step fails with condition result `error`, and the sequence stops.
- A later step has a `commandOutcome` condition on a skipped `Loop` step: the state of that `Loop` step is `skipped`.
- A dry run (`dryRun: true` on a run request): an `imageVisible` leaf is false in a dry run, as for an `Action` step, so a guard on an image can skip the `Loop` step.
- A `Loop` step inside an `If` branch or inside a loop body is not permitted, as before. The fix does not change this rule.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The service MUST keep the `condition` of a `Loop` step that `POST /api/sequences` (and the other sequence save endpoints that use the same step contract) receives, and MUST return it on read.
- **FR-002**: Before a top-level `Loop` step starts its first iteration, the runtime MUST evaluate the `condition` of that step one time, with the same evaluator and the same rules as for the `condition` of an `Action` step.
- **FR-003**: When that condition is false, the runtime MUST NOT run any body step of the loop and MUST continue with the next top-level step.
- **FR-004**: When that condition is true, or when the `Loop` step has no condition, the loop MUST run as before.
- **FR-005**: When the evaluation of that condition fails, the `Loop` step MUST fail with condition result `error`, and the sequence MUST stop, as for an `Action` step.
- **FR-006**: The execution log entry of a guarded `Loop` step MUST contain the condition type and the condition result (`true`, `false` or `error`). A skipped `Loop` step MUST have status `Skipped`, zero iterations, and a message that tells that the condition was false; when a composite condition decides the result, the message MUST name the child that decided it.
- **FR-007**: The run result of a skipped `Loop` step MUST give the state `skipped` to a later `commandOutcome` condition.
- **FR-008**: Save-time validation MUST check the `condition` of a `Loop` step with the same rules as the `condition` of an `Action` step, and MUST reject an incorrect condition with HTTP 400 and an error that names the step.
- **FR-009**: The behavior of `If` steps, of conditioned `Action` steps, of `Break` steps, and of the loop configurations `count`, `while` and `repeatUntil` MUST NOT change.
- **FR-010**: The OpenAPI description of the step `condition` field, the architecture document and the changelog MUST tell that a `condition` on a `Loop` step is a guard that is evaluated one time before the loop starts.

### Key Entities

- **Loop step guard**: the optional `condition` of a top-level `Loop` step. It decides one time if the loop runs.
- **Loop execution log entry**: the log entry for a `Loop` step, with status, iteration count, exit reason, and now the condition type and condition result.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: In an automated test, a `Loop` step with a false guard runs zero body steps, and the next top-level step runs. This test fails before the fix.
- **SC-002**: In an automated test, a `Loop` step with a false guard gives a run result with status `Skipped`, condition result `false`, and zero iterations.
- **SC-003**: In an automated test, a `Loop` step with a true guard runs the same number of iterations as the same loop without a guard, and its run result has condition result `true`.
- **SC-004**: In an automated test, the execution log entry of a guarded `Loop` step contains the condition type and the condition result.
- **SC-005**: In an automated test, a sequence with a guarded `Loop` step keeps the `condition` after a save and a read.
- **SC-006**: In an automated test, `POST /api/sequences` with `dryRun: true` returns 400 for a `Loop` step with a `commandOutcome` guard that names an unknown step.
- **SC-007**: All existing tests for `If`, `Action`, `Break` and loop steps pass with no change to their expected values.

## Assumptions

- Only top-level steps can be `Loop` steps. Loops inside loop bodies and inside `If` branches are not permitted, and this does not change.
- The guard is evaluated at the position of the `Loop` step, after the prior step completes, at the same point in the step flow as the guard of an `Action` step.
- Before this fix, the save endpoints did not keep the `condition` of a `Loop` step. Thus a stored sequence has such a condition only when a person wrote it into the stored file. The guard then operates, which is the intended behavior.
- The web authoring UI does not need a change for this fix. A web UI editor for the guard is not in scope. The web sequence editor does not send the `condition` of a `Loop` step, so a save in the web UI removes the guard. The changelog tells this limit.
