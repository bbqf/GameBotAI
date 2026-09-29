# Feature Specification: A Break outcome when its If branch did not run

**Feature Branch**: `117-break-outcome-untaken-branch`  
**Created**: 2026-09-29  
**Status**: Draft  
**Input**: GitHub issue #250 (B-032): "commandOutcome on a Break inside an If branch that did not run fails with condition-evaluation-error instead of no_break". Full description: `C:\Users\anton\AppData\Local\Temp\claude\C--src-GameBot\bc0c2e7c-1414-4f87-b9ff-5ee89a758031\scratchpad\feature-description.md`. Closes #250.

## Background

A `commandOutcome` condition can name a Break step with `stepRef`. The save validation (and `dryRun`) accepts a reference to a Break that is in an If branch. The description of `CommandOutcomeCondition.stepRef` says that this reference is valid.

At run time, when the If branch did not run, the Break has no recorded outcome. The condition then fails with `condition-evaluation-error` and the message "Step '<step>' commandOutcome reference '<ref>' is unavailable". Thus the save accepts a reference that the run cannot read. The save contract and the run contract do not agree.

Reproduction (service 1.4.0.215, 2026-09-29):

- Loop `book` (count 3). Body: If `if-empty` { `book-reset`, Break `brk-empty` }, If `if-wait` { `book-wait`, Break `brk-wait` }, `settle`.
- After the loop: Action `fail-no-booking` with condition `all(commandOutcome brk-empty break negate:true, commandOutcome brk-wait break negate:true)` and `requireDispatch: true`.
- `POST /api/sequences` with `dryRun: true` gives `valid: true`.
- A run in which neither branch runs fails on `fail-no-booking` with `conditionTrace.failureReason` `condition-evaluation-error`.

## Clarifications

### Session 2026-09-29

- Q: Which of the two results in the issue does this feature select? → A: Evaluate a Break that did not run as `no_break` at run time. Rationale: it keeps the save contract that `dryRun` already reports, and it does not reject sequences that authors already saved.
- Q: Does the `no_break` default apply to step types other than Break (for example an Action in an If branch that did not run)? → A: No. Only Break steps get the default. Rationale: the issue is only about Break; a default for other types is a separate decision.
- Q: In a Loop body, the branch runs in one iteration and not in a later iteration. Which outcome does a condition after the loop read? → A: The outcome that the Break recorded last. An iteration in which the Break does not run does not erase or change a recorded outcome. Rationale: this is the current recording behaviour for steps that ran.
- Q: Must the condition trace show that the value came from the default and not from a recorded outcome? → A: The trace shows the evaluated value (true/false) as for any leaf. A separate marker is optional and is not required. Rationale: minimal change; the run log already shows that the Break did not run.
- Q: How does the run know that a `stepRef` names a Break, when the Break has no recorded outcome? → A: From the sequence definition (the step type of the named step). A reference that names no step in the sequence still fails. Rationale: FR-004 keeps unknown references as errors.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Read the outcome of a Break whose If branch did not run (Priority: P1)

A sequence author puts a Break in an If branch in a Loop body. After the loop, a step uses `commandOutcome <break> break negate:true` to find out that the loop did not stop at that Break. When the If branch never runs, the author expects the Break to count as "did not fire" (`no_break`). The step condition must then evaluate, and the step must run.

**Why this priority**: This is the defect. It blocks the PNS Nova daily automation, and the save already tells the author that the pattern is valid.

**Independent Test**: Save the reproduction sequence, run it with no If branch taken, and make sure that `fail-no-booking` runs and does not fail with `condition-evaluation-error`.

**Acceptance Scenarios**:

1. **Given** the reproduction sequence and a run in which no If branch runs, **When** the runner evaluates the condition of `fail-no-booking`, **Then** the condition is true and the step runs. The step does not fail with `condition-evaluation-error`.
2. **Given** a Break in an If branch that did not run, **When** a condition reads `commandOutcome <break> no_break`, **Then** the condition is true.
3. **Given** a Break in an If branch that did not run, **When** a condition reads `commandOutcome <break> break`, **Then** the condition is false.

---

### User Story 2 - Keep the outcome of a Break that ran (Priority: P1)

When the If branch runs, the Break records `break` (it fired) or `no_break` (its condition kept the loop going). This behaviour must not change.

**Why this priority**: The fix must not break the working cases, which include the documented workaround pattern.

**Independent Test**: Run sequences in which the branch runs and the Break fires, and in which the branch runs and the Break does not fire. Make sure that the conditions give the same results as before the fix.

**Acceptance Scenarios**:

1. **Given** an If branch that runs and its Break fires, **When** a later condition reads `commandOutcome <break> break`, **Then** the condition is true.
2. **Given** an If branch that runs and its Break does not fire, **When** a later condition reads `commandOutcome <break> no_break`, **Then** the condition is true.
3. **Given** the workaround pattern (one Break directly in the loop body, and `commandOutcome brk-booked no_break` after the loop), **When** the sequence runs, **Then** the result is the same as before the fix.

---

### User Story 3 - The save and the run agree (Priority: P2)

A reference that the save (`POST /api/sequences`, with and without `dryRun`) accepts must not fail at run time only because the If branch that contains the Break did not run.

**Why this priority**: This prevents the same class of defect for the other position in which a Break can be and not run: a Loop body that ran zero iterations. The save accepts a Break only in a Loop body (directly, or in an If branch in a Loop body). Thus these two positions are all the positions that the API can make.

**Independent Test**: Save two sequences, with `dryRun` and without it: (a) a Break in an If branch in a Loop body, and (b) a Break directly in a Loop body with count 0. All saves give a valid result. Then run (a) with the branch not taken and run (b). Make sure that the condition that reads the Break evaluates in both runs.

**Acceptance Scenarios**:

1. **Given** a Break in an If branch in a Loop body, **When** the branch does not run and a later condition reads that Break, **Then** the condition evaluates as `no_break` and does not fail.
2. **Given** a Break in a Loop body that ran zero iterations, **When** a later condition reads that Break, **Then** the condition evaluates as `no_break` and does not fail.
3. **Given** a `stepRef` that names a step that does not exist in the sequence, **When** the author saves the sequence, **Then** the save still rejects it as before.

Note: a Break in an If branch at the top level (not in a Loop body) cannot come through the API, because the save rejects a Break that is not in a Loop body. The runner does not validate, so a runner unit test covers this form as a guard only. It is not an acceptance scenario.

### Edge Cases

- A Break in an If branch in a Loop body, where the branch runs in one iteration and not in a different iteration: the outcome is the outcome that the Break recorded last. A later iteration in which the branch does not run does not erase a recorded outcome.
- A Break in a Loop body that ran zero iterations (for example a count-0 Loop): the Break did not run, so a later condition reads it as `no_break` (FR-001, FR-003).
- A bare leaf condition in a `while`/`repeatUntil` slot (the D-006 exception): the save behaviour does not change. If this condition reads a Break in the body of its own Loop, then before the Break runs for the first time, the condition reads `no_break`. Before this feature, the loop failed with "reference is not available". This is the FR-001 behaviour, and it is not a regression.
- A `commandOutcome` reference to a step that is not a Break (for example an Action) in an If branch that did not run: the behaviour does not change in this feature (see Assumptions).
- A `stepRef` that is empty or that names no step: the run still fails with `condition-evaluation-error`, as before.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: When a `commandOutcome` condition names a Break step that has no recorded outcome at evaluation time, because the Break did not run (its If branch did not run, or its Loop body ran zero iterations), the system MUST evaluate the Break outcome as `no_break`.
- **FR-002**: A Break that ran MUST keep the outcome that it recorded (`break` or `no_break`). FR-001 applies only when there is no recorded outcome.
- **FR-003**: The system MUST apply FR-001 to a Break in an If branch in a Loop body when the branch did not run, and to a Break in a Loop body that ran zero iterations. These are the positions that the save accepts. The runner also applies FR-001 to a Break in an If branch at the top level, but the save rejects that form, so it is a runner-level guard only.
- **FR-004**: A `commandOutcome` reference to a step that does not exist in the sequence MUST stay a save error. An empty or unknown reference at run time MUST still fail with `condition-evaluation-error`.
- **FR-005**: The behaviour for a reference to a step that is not a Break MUST NOT change.
- **FR-006**: The system MUST NOT add a new `expectedState` value. The allowed values stay `success|failed|skipped|break|no_break`.
- **FR-007**: The save validation and `dryRun` results for these references MUST NOT change. The save continues to accept a reference to a Break in an If branch.
- **FR-008**: The condition trace of a step MUST show the evaluated value for the Break reference, the same as for a recorded outcome.
- **FR-009**: The documentation of `CommandOutcomeCondition.stepRef` (the API schema description) MUST say that a Break that did not run evaluates as `no_break`.

### Key Entities

- **Step outcome**: The recorded result of a step in a run (`success`, `failed`, `skipped`, `break`, `no_break`). A `commandOutcome` condition reads it by `stepRef`.
- **Break step**: A step that stops the loop that contains it when its condition is true. It records `break` when it fires and `no_break` when it does not fire.
- **commandOutcome condition**: A leaf condition with `stepRef`, `expectedState`, and `negate`.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A run of the reproduction sequence in which no If branch runs completes `fail-no-booking` with 0 `condition-evaluation-error` failures.
- **SC-002**: All five cases have automated tests that pass: (1) a Break in an If branch in a Loop body, when the branch did not run; (2) a Break that ran and fired; (3) a Break that ran and did not fire; (4) a Break in an If branch at the top level, when the branch did not run (runner-level guard only, because the save rejects this form); (5) a Break in a Loop body that ran zero iterations.
- **SC-003**: For 100% of references that the save accepts to a Break in an If branch, the run evaluates the reference and does not fail because the branch did not run.
- **SC-004**: All existing sequence and condition tests continue to pass with no change to their expected results.

## Assumptions

- The selected result is "evaluate the Break as `no_break` at run time", not "reject on save". Reason: it keeps the authoring pattern of the issue, and it keeps the save contract that `dryRun` and the `stepRef` description already report. Rejecting on save would break sequences that authors already saved.
- `no_break` is the correct value for a Break that did not run, because it did not stop the loop. This agrees with the issue.
- The fix is only for Break references. A reference to a different step type that did not run keeps the current behaviour, because the issue is only about Break steps and a default for other types (for example `skipped`) is a different decision.
- The PNS authoring repository and its evidence files are not changed.

## Non-goals

- No change to a `stepRef` that names a step that does not exist in the sequence.
- No change to the D-006 exception.
- No new `expectedState` value.
- No change to the documented workaround pattern.
- No change to the PNS authoring repository.
