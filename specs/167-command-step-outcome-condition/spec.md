# Feature Specification: Sequence condition on a step outcome inside a command

**Feature Branch**: `167-command-step-outcome-condition`  
**Created**: 2026-10-28  
**Status**: Implemented  
**Input**: User description: "Let a sequence condition read the outcome of a step inside a command (GitHub issue #352, FR-037). Example: step 12 (a PrimitiveTap) of the command that step send-1 ran was executed, or was skipped (detection failed)."

Issue: #352 - https://github.com/bbqf/GameBotAI/issues/352 (labels: enhancement, P2). Closes #352.

## Background

A command can make a conditional action without a condition. A tap on an image that is not on the screen is skipped, and the command goes on. Feature 023 (`PNS.GatherResources`) uses this for the recall on an attack warning.

After a send, the command `PNS.GatherSend` waits up to 30 s for a warning banner. Only when the banner shows, it taps Recall, "Buy & Use" (400 gems), and Confirm. The banner shows for about 1.5 s, so the recall must stay inside the command. A sequence condition after the command is too late.

After the command, the sequence cannot tell if the recall ran. The existing `commandOutcome` condition reads one outcome for the whole command step. This outcome is `success` in both cases. No sign of the recall stays on the screen. The banner is gone and the recalled march is home in seconds. The task must:

- end the run after a recall, so that the account does not spend another 400 gems in a contested area, and
- use a shield and fail by name when the recall fails (the game then shows "Incorrect troop status" for about 1.5 s).

Neither is possible today. Evidence is in `evidence/023-world-resource-gathering/runs/gt7/result.md` and `evidence/023-world-resource-gathering/detections/r010-r011-alert-recall.md` of the PNS repository.

Each step that a command runs already has an outcome state. The force-execute result of a command lists these states for each step by order. The states are `executed`, `skipped_detection_failed` (with the reason `detection_failed_after_3_retries`), `completed_timeout`, and `image_detected`. This feature lets a sequence condition read one of these states.

## Design decision

The issue offers two ways: (A) a condition that reads the outcome state of one command step, or (B) a command step that sets a run-scoped flag, and a condition that reads the flag. This feature builds **A only**, as a new condition type `commandStepOutcome`. Reasons: A needs no change to how a command runs its steps (a non-goal), and it uses the outcome states that exist already. B would add a new step action and a new run-scoped store. B is out of scope. The existing `commandOutcome` condition does not change.

## Clarifications

### Session 2026-10-28

- Q: A new condition type, or an extension of `commandOutcome`? → A: A new type `commandStepOutcome`. Rationale: `commandOutcome` must not change for existing sequences, and its states (`success`, `failed`, ...) are a different set from the command step states.
- Q: How does the condition find the step? → A: By `stepRef` (the id of a prior sequence step that runs a command) and `commandStepOrder` (the `order` of a step in that command). Rationale: this is the form in the issue.
- Q: Which `expectedState` values are allowed? → A: `executed`, `skipped_detection_failed`, `completed_timeout`, `image_detected`. The reason code `detection_failed_after_3_retries` is not a state. It is not an allowed value. Rationale: it is the reason text of `skipped_detection_failed`. One value for one state is clear.
- Q: What if the referenced command step did not run (the command stopped before it, or the sequence step was skipped)? → A: The condition has no state to read. It is false (before `negate`), the run log says that no outcome was recorded, and the run does not fail. Rationale: "the recall did not run" must be a normal case to test for.
- Q: What if the same sequence step runs its command more than once (a loop)? → A: The condition reads the state of the latest run of that sequence step. Rationale: this is the rule that `commandOutcome` uses for a step in a loop.
- Q: Does the condition work inside `all`, `any`, `none`, `if`, loop, and break positions? → A: Yes, in each position where `commandOutcome` works. Rationale: the same condition model.
- Q: Does a `commandStepOutcome` condition change the stored outcome of the sequence step (`commandOutcome` state)? → A: No. The two conditions read two separate records. Rationale: the non-goal says `commandOutcome` stays as it is.
- Q: Can `stepRef` name a step that runs a command inside a loop body or an `if` branch? → A: Yes, if it is before the condition in the same order rule that `commandOutcome` uses. Rationale: one reference rule for both conditions.
- Q: Does a step with a state that is not one of the four (for example a failed dispatch) give a readable state? → A: No. Only the four states are readable; any other result counts as "no outcome recorded". Rationale: the issue limits the states to the force-execute set.
- Q: (Analyze U1) What does `commandStepOrder` mean when a command step runs another command? → A: It refers to a top-level step of the command that the sequence step runs. Steps of a nested command are not readable. A test MUST show that a nested step with the same order does not change the result. Rationale: step orders repeat in a nested command. Only the top level is unique.
- Q: (Analyze C3) What occurs when the command cannot be loaded at save or at run? → A: The save check of the order is skipped. At run, the condition gives "no outcome recorded". A test MUST cover both. Rationale: this follows FR-007.
- Q: (Analyze P1) Which states does SC-001 cover? → A: SC-001 covers the end-run flow for `executed` and `skipped_detection_failed`. SC-002 covers all four states. Rationale: the Feature 023 case uses only these two states.
- Q: (Analyze C1) Does the run dry run check the order against the command? → A: No. The order check against the command runs only in the save validation and the save dry run. The run dry run and the run check only the `stepRef` rules; a missing order at run time gives "no outcome recorded". Rationale: the Domain validators have no command repository, and FR-007 puts this check at save.
- Q: (Analyze C5) What does a later step do to an older recorded outcome? → A: A new command run of the sequence step replaces the older entry, even if the new run has no readable states. A step that is skipped, fails, or does not run in a dry run keeps the older entry. Rationale: only a new command run is new information about the command steps.
- Q: (Analyze C7) What is the lowest allowed `commandStepOrder` in the API schema? → A: 1. The schema MUST show this minimum. Rationale: step orders start at 1.
- Q: Does the web UI offer the condition? → A: Yes, in the sequence editor with the same condition fields as `commandOutcome`, plus the step order and the four states. Phone support level: reduced (the same as the other condition fields of the editor). Rationale: the issue names the web UI.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Branch on a step inside a command (Priority: P1)

A sequence author has a sequence step `send-1` that runs the command `PNS.GatherSend`. Step 12 of the command taps Confirm, and it is skipped when the banner is not on the screen. After `send-1`, the author adds a condition `commandStepOutcome` with `stepRef` `send-1`, `commandStepOrder` 12, and `expectedState` `executed`. The condition is true only when the Confirm tap ran.

**Why this priority**: This is the request of the issue. It makes "end the run after a recall" possible.

**Independent Test**: Run a sequence with a command whose step 12 runs, and a second run where step 12 is skipped. The condition is true in the first run and false in the second run.

**Acceptance Scenarios**:

1. **Given** step 12 of the command ended in `executed`, **When** a later condition reads `commandStepOutcome` with `expectedState` `executed`, **Then** the condition is true.
2. **Given** step 12 ended in `skipped_detection_failed`, **When** the condition expects `executed`, **Then** the condition is false.
3. **Given** step 12 ended in `skipped_detection_failed`, **When** the condition expects `skipped_detection_failed`, **Then** the condition is true.
4. **Given** the condition has `negate` true and step 12 ended in `executed` with `expectedState` `executed`, **When** the condition is evaluated, **Then** the condition is false.
5. **Given** the condition is the `if` condition of a block, **When** it is true, **Then** the block runs the "then" branch (for example, a step that ends the run).

---

### User Story 2 - All four states and the run log (Priority: P1)

An author needs to match each of the four states, and needs to see in the run log which state the condition read.

**Why this priority**: Acceptance criteria 2 and 3 of the issue. Without the log, a wrong branch is hard to find.

**Independent Test**: For each of the four states, run a command that gives the state and check the condition and the log line.

**Acceptance Scenarios**:

1. **Given** a command step that ended in `completed_timeout` (a wait that timed out), **When** the condition expects `completed_timeout`, **Then** the condition is true.
2. **Given** a command step that ended in `image_detected` (a wait that found the image), **When** the condition expects `image_detected`, **Then** the condition is true.
3. **Given** any evaluated `commandStepOutcome` condition, **When** the run log records the evaluation, **Then** the log shows the `stepRef`, the order, the expected state, and the state that the condition read.
4. **Given** the referenced command step has no recorded outcome, **When** the condition is evaluated, **Then** the condition is false before `negate`, and the log shows that no outcome was recorded.

---

### User Story 3 - Dry run rejects a bad reference (Priority: P2)

An author makes a mistake in the `stepRef` or in the order. The save validation and the save dry run give a clear error before the sequence runs. The run dry run and the run check only the `stepRef` rules.

**Why this priority**: Acceptance criterion 4 of the issue. Early errors save run time on a game account.

**Independent Test**: Save a sequence with each wrong reference and check the error code and message.

**Acceptance Scenarios**:

1. **Given** `stepRef` names a sequence step that is not a command step (for example a tap or a wait), **When** the sequence is validated, **Then** validation gives an error that names the step and says it must run a command.
2. **Given** `stepRef` names an unknown step, or a step that is not before the condition, **When** the sequence is validated, **Then** validation gives the same error as `commandOutcome` gives.
3. **Given** `commandStepOrder` is 99 and the command has no step with order 99, **When** the sequence is saved or the save dry run runs, **Then** validation gives an error that names the command and the order.
4. **Given** `expectedState` is empty or not one of the four states, **When** the sequence is validated, **Then** validation gives an error that lists the four states.
5. **Given** `commandStepOrder` is missing or less than 1, **When** the sequence is validated, **Then** validation gives an error.

---

### User Story 4 - The Feature 023 case (Priority: P2)

A task author uses the condition to end the run after a recall, and to fail by name when the recall fails.

**Why this priority**: Acceptance criterion 5 of the issue. It shows the feature solves the real case.

**Independent Test**: Use a test sequence that has a send step, a condition on the Confirm tap, and an end-run step. Run it with a command that skips the Confirm tap and with a command that runs it.

**Acceptance Scenarios**:

1. **Given** the Confirm tap ran, **When** the sequence reaches the condition, **Then** the run ends with the end-run step and no further send runs.
2. **Given** the Confirm tap was skipped, **When** the sequence reaches the condition, **Then** the run goes on to the next send.
3. **Given** a shield step fails after a recall, **When** the author uses the condition to choose a named failure step, **Then** the run fails with that name.

---

### User Story 5 - Edit the condition in the web UI (Priority: P3)

An author picks `commandStepOutcome` in the sequence editor, picks a prior command step, enters the order, and picks one of the four states. The editor shows the same errors as the service.

**Why this priority**: The condition must be usable without a hand-edited file. Lower than the service behaviour.

**Independent Test**: In the editor, add the condition, save, and load the sequence again. The fields keep their values and a bad reference shows an error.

**Acceptance Scenarios**:

1. **Given** the editor, **When** the author picks the type `commandStepOutcome`, **Then** the editor shows fields for `stepRef`, order, and state.
2. **Given** a saved sequence with the condition, **When** the author opens it, **Then** the fields show the saved values.
3. **Given** an empty order or no state, **When** the author saves, **Then** the editor shows an error and does not send the request.

### Edge Cases

- The command step with the given order is a step that has no outcome (a step type that records no state): the condition is false and the log says that no outcome was recorded.
- The sequence step `stepRef` was skipped by its own condition: no command ran, so no outcome exists. The condition is false before `negate`.
- A command that runs again in a loop: the latest run counts (see Clarifications).
- A dry-run execution that does not run commands: the condition is false before `negate` and the log says that the step did not run (the same as `commandOutcome` for a dry-run-skipped step).
- A command that is deleted or changed after the sequence is saved: run time gives the "no outcome recorded" result; a new validation gives the error of User Story 3.
- Existing sequences that use `commandOutcome` do not change and their saved files still load.
- A new command run of a sequence step replaces the older recorded outcome, even if the new run has no readable states. A step that is skipped, fails, or does not run in a dry run keeps the older entry.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The system MUST support a new step condition type `commandStepOutcome` with the fields `stepRef`, `commandStepOrder`, `expectedState`, and `negate`.
- **FR-002**: The condition MUST be true when the command step with the order `commandStepOrder`, in the latest command run of the sequence step `stepRef`, ended in `expectedState`. Otherwise it MUST be false. `negate` MUST invert the result.
- **FR-003**: `expectedState` MUST accept `executed`, `skipped_detection_failed`, `completed_timeout`, and `image_detected`, with a case-insensitive match. The system MUST reject other values.
- **FR-004**: When no outcome is recorded for the command step, the condition MUST be false before `negate` and MUST NOT fail the run.
- **FR-005**: The run log MUST show, for each evaluation, the `stepRef`, the order, the expected state, and the state that the condition read (or that none was recorded).
- **FR-006**: The save validation, the save dry run, the run dry run, and the run MUST reject a `stepRef` that is unknown, not before the condition, or not a command step, with a clear error that names the step.
- **FR-007**: The save validation and the save dry run MUST reject a `commandStepOrder` that the referenced command does not have, with an error that names the command and the order. When the command cannot be loaded, the check MUST be skipped at save and MUST give the "no outcome recorded" result at run time.
- **FR-008**: The condition MUST work in each place that `commandOutcome` works: a step condition, an `all`/`any`/`none` child, an `if` condition, a loop condition, and a break condition.
- **FR-009**: The system MUST record the outcome state of each command step that a sequence step runs, so the condition can read it. This MUST NOT change how a command runs its steps or how a skipped step works.
- **FR-009a**: `commandStepOrder` MUST refer to a top-level step of the command that the sequence step runs. Steps of a nested command MUST NOT be readable. The only allowed change to the command run path is an additive field that marks the nesting depth of a step outcome.
- **FR-010**: The `commandOutcome` condition MUST keep its present behaviour, and saved sequences MUST load without change.
- **FR-011**: The API schema (OpenAPI) and the schema filters MUST list the new condition type and its fields.
- **FR-012**: The web-UI sequence editor MUST let the author create and edit the condition, and its validation MUST give the same errors as the service. Phone support level: reduced, per `docs/mobile-ui-principles.md`.
- **FR-013**: The feature MUST NOT add recall logic, shield logic, or a run-scoped flag to the product, and MUST NOT change the `PNS.GatherResources` or `PNS.GatherSend` data.

### Key Entities

- **Command step outcome**: the state (`executed`, `skipped_detection_failed`, `completed_timeout`, `image_detected`) of one step of a command, keyed by the order of the step, for the latest command run of one sequence step.
- **`commandStepOutcome` condition**: a step condition with `stepRef`, `commandStepOrder`, `expectedState`, and `negate`.

## Assumptions

- The force-execute result of a command already gives a state for each step by order. The sequence runner can get the same list from the command run.
- A step type with no state in the force-execute result gives no outcome to read.
- The latest-run rule for loops is the same as the rule for `commandOutcome`.
- The additive depth field on the step outcome is allowed. It marks the nesting depth of a step outcome. It does not change how a command runs (FR-009a). The field is added in the command execution code (`CommandExecutor.cs` and the step outcome type). The plan lists this file.
- Unit tests for the evaluator are in separate test files for each user story. Tasks that write them can run in parallel.
- The lists of files to change (validators, runner, evaluator, endpoints, schema filters, and web-UI files) are in the Project Structure section of `plan.md`.
- Tasks that edit the same file are not marked parallel. Each task has a concrete done condition.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: An author can make a sequence that ends the run after a recall, with one condition and no change to the command, and a test run shows the right branch in 100% of the cases of the states `executed` and `skipped_detection_failed`.
- **SC-002**: For each of the four states, a condition that expects it is true, and a condition that expects another state is false, in 100% of the test cases.
- **SC-003**: Each evaluation of the condition leaves one run-log line that shows the state read, so an author finds the cause of a branch from the log without a debugger.
- **SC-004**: Each wrong reference in User Story 3 gives an error before the sequence runs, in 100% of the test cases.
- **SC-005**: All existing sequences with `commandOutcome` load and run with the same results as before.
