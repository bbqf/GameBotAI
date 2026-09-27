# Feature Specification: Log each command step under its own step ID (B-020)

**Feature Branch**: `claude/resolve-github-issue-6ldqb3` (spec number 107)  
**Created**: 2026-09-27  
**Status**: Implemented  
**Input**: GitHub issue #221 (B-020): "the execution log names a command step by the stepId of the first step that uses the same command". Full description: see the issue and the feature description that started this spec.

## Background

A sequence can have two or more steps that run the same command. Each of these steps has its own `stepId`. `GET /api/sequences/{id}` returns these IDs correctly.

When the sequence runs, the service writes one command node in the execution log for each command step that ran. Today, the service finds the step for a command node by the command ID. When two or more steps use the same command, the service always finds the first of these steps. Thus every command node for that command has the `stepId` of the first step, in `message` and in `deepLink.stepId`.

Example from the issue: a sequence has seven `If` steps. The body of each `If` has one step, and all seven body steps run `PNS.PaceShort`. Four bodies ran. `GET /api/execution-logs/{id}/subtree` shows all four command nodes as `body-if-false`. `body-if-false` is the body of the first `If`, and its condition was false, so that step did not run.

Result: a client that counts `(stepId, status)` pairs from the log gets incorrect counts. A step that did not run appears to have run. A shared failure command or a shared tap command hides which step fired.

## Clarifications

### Session 2026-09-27

- Q: Which name does the `message` of a command node use when the step has a `label`? → A: The same rule as today: the step `label`, else the `stepId`. The fix changes only which step the service finds. Rationale: the issue does not ask to change the message format, and the non-goals forbid it.
- Q: Does the fix also apply to steps in an `If` else branch (`elseBody`)? → A: Yes. The service must find a command step in `body` and in `elseBody` at all nesting levels. Rationale: an else-branch step has the same problem, and today the service does not look in `elseBody` at all.
- Q: Does the response of `POST /api/sequences/{id}/execute` get a new field? → A: No. The run result keeps the ID of the step that ran for internal use only; the JSON response does not change. Rationale: the non-goals forbid API shape changes.
- Q: What does the service do for a command node when the run result has no step ID (for example, a step with no `stepId`, or the legacy flow-graph path)? → A: It uses the current lookup by command ID. Rationale: there is no better source of the step, and the current result stays the same for these cases.
- Q: Which test level covers the regression? → A: An integration test that creates a sequence through the API, runs it, and reads `GET /api/execution-logs/{id}/subtree`, plus a unit test of the run result. Rationale: the acceptance criteria name the subtree endpoint.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Find the step that ran a shared command (Priority: P1)

An operator runs a sequence where several steps use the same command. The operator reads the execution log. Each command node names the step that ran it.

**Why this priority**: This is the defect in the issue. Verification counts and diagnosis depend on correct step IDs.

**Independent Test**: Create a sequence with three top-level steps that run the same command. Run it. Read the subtree. Each command node has the `stepId` of its own step.

**Acceptance Scenarios**:

1. **Given** a sequence with top-level steps `a`, `b` and `c` that all run command `X`, **When** the sequence runs, **Then** the subtree has three command nodes with `deepLink.stepId` `a`, `b` and `c`, in that order.
2. **Given** the same run, **When** the operator reads the `message` of each command node, **Then** the message is "Step '<name>' ran command '<command name>' with outcome '<outcome>'.", and `<name>` is the label or the `stepId` of that same step.

---

### User Story 2 - Steps in `If` and `Loop` bodies (Priority: P1)

An operator runs a sequence where steps in `If` bodies and `Loop` bodies run the same command.

**Why this priority**: The issue shows the defect with `If` bodies. A step that did not run must not appear as run.

**Independent Test**: Create a sequence with `If` steps whose bodies run the same command. Some conditions are true and some are false. Run it. Read the subtree.

**Acceptance Scenarios**:

1. **Given** two `If` steps whose bodies (`body-false` and `body-true`) run command `X`, and the first condition is false and the second condition is true, **When** the sequence runs, **Then** the subtree has one command node for `X`, with `deepLink.stepId` `body-true`, and no command node has `deepLink.stepId` `body-false`.
2. **Given** an `If` step whose `elseBody` has step `else-step` that runs command `X`, and a step before it that also runs `X`, **When** the else branch runs, **Then** the command node of the else branch has `deepLink.stepId` `else-step`.
3. **Given** a `Loop` step whose body has step `loop-step` that runs command `X`, and a top-level step `first` that also runs `X`, **When** the loop runs, **Then** each command node from the loop body has `deepLink.stepId` `loop-step`.
4. **Given** any of the sequences above, **When** the operator reads the `If` and `Loop` nodes, **Then** they have their own IDs, as before.

### Edge Cases

- A step that has no `stepId`: the service uses the current lookup by command ID. The result is the same as before.
- A sequence that runs through the legacy flow graph (`entryStepId` and `flowSteps`): no change.
- A command step that the step guard (`condition`) skipped: the node has the `stepId` of that step, not of the first step with the same command.
- A command step that failed or dispatched nothing (`failed`, `not_executed`) or that a dry run skipped: the node has the `stepId` of that step.
- Two steps with the same command and different labels: each node uses the label of its own step.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The sequence runner MUST record, in the run result of each command step, the `stepId` of the step that ran.
- **FR-002**: When the service writes the execution log, it MUST find the sequence step for a command node by the recorded `stepId`, when the `stepId` is set and a step with that ID exists.
- **FR-003**: The service MUST find steps at all nesting levels: top level, `If` `body`, `If` `elseBody`, and `Loop` `body`.
- **FR-004**: When the run result has no `stepId`, or no step with that ID exists, the service MUST use the current lookup by command ID.
- **FR-005**: The `message`, `deepLink.stepId`, `stepId` and `stepLabel` of a command node MUST come from the step found by FR-002 to FR-004.
- **FR-006**: The service MUST NOT write a command node for a step that did not run. (The runner writes no result for such a step. This requirement records that the fix keeps this.)
- **FR-007**: The service MUST NOT change the log nodes of `If`, `Loop`, reschedule-self, condition and wait-for-image steps.
- **FR-008**: The fix MUST NOT change the JSON shape of `POST /api/sequences/{id}/execute` or of any execution-log endpoint.
- **FR-009**: The fix MUST NOT change which steps run.
- **FR-010**: Automated tests MUST cover several steps that share one command, at the top level, in `If` bodies (true and false conditions), in an `If` `elseBody`, and in a `Loop` body.

### Key Entities

- **Step result**: one entry in the run result for one step that ran. It has the command ID, the status and the outcome. The fix adds the `stepId` of the step.
- **Command node**: one entry of `stepType` `command` in the execution log. It has `message`, `stepId`, `stepLabel` and a `deepLink` with `stepId`.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: For a sequence of the same type as in the issue (several `If` steps whose bodies run one shared command, some conditions true and some false), the subtree has one command node for each true condition. The `deepLink.stepId` values of these nodes are the step IDs of the bodies whose conditions were true.
- **SC-002**: In all new tests, 100% of command nodes have `deepLink.stepId` equal to the `stepId` of the step that ran.
- **SC-003**: All existing tests pass with no change to their assertions.

## Assumptions

- The operator reads the correct step IDs from `GET /api/sequences/{id}`.
- The execution-log node already takes `deepLink.stepId` from the `stepId` detail attribute. Thus a correct `stepId` detail gives a correct deep link.
