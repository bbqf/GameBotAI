# Sequence Logic Blocks (Loops & Conditionals)

**Feature Branch**: `001-sequence-logic`  
**Created**: 2025-12-17  
**Status**: Draft  
**Input**: Sequence blocks for loops and conditions: repeat steps N times, repeat until image/text detected, if/then/else; consider additional control blocks if needed

## Overview
Enable richer sequence orchestration using standard control blocks: repeat steps a fixed number of times, repeat-until a condition is met (e.g., image/text detection), and conditional branching (if/then/else). This expands existing Command Sequences into structured flows that can express retries, polling, and decision-making without custom code.

## Assumptions
- Conditions leverage existing detection capabilities (image/text) and trigger evaluation semantics.
- Infinite loops are prevented via required safeguards (max iterations or timeout).
- Blocks can group multiple steps and apply delays/gating within the group.
- Execution results include per-block/per-step telemetry (iterations, condition outcomes, durations).

Nested blocks are allowed without a fixed maximum depth. Authors should prefer shallow nesting for readability; safeguards apply at every level to prevent unbounded execution.

Loop control supports both `break` (exit loop) and `continue` (skip to next iteration) when declared within loop blocks.

Conditions may reference image/text detection or trigger status by `triggerId`.

## Clarifications

### Session 2025-12-17

- Q: For repeat-until/while loop iteration ordering, when is the condition evaluated relative to executing steps? → A: Check before steps; execute only if not satisfied.
- Q: When should `breakOn`/`continueOn` be evaluated within loop iterations? → A: Evaluate at start and between steps; `breakOn` at start or between steps; `continueOn` only between steps.
- Q: What telemetry granularity should loops capture? → A: Compact per-iteration summaries: index, decision (normal/break/continue), duration.
- Q: If both `timeoutMs` and `maxIterations` are provided, how should they be enforced? → A: Enforce both; stop on the first safeguard hit.

## Actors
- Operator: Authors sequences and reviews results.
- Automation Client: Calls sequence endpoints to create and execute.
- Service: Executes blocks, evaluates conditions, enforces timeouts/safety.

## User Scenarios & Testing *(mandatory)*

<!--
  IMPORTANT: User stories should be PRIORITIZED as user journeys ordered by importance.
  Each user story/journey must be INDEPENDENTLY TESTABLE - meaning if you implement just ONE of them,
  you should still have a viable MVP (Minimum Viable Product) that delivers value.
  
  Assign priorities (P1, P2, P3, etc.) to each story, where P1 is the most critical.
  Think of each story as a standalone slice of functionality that can be:
  - Developed independently
  - Tested independently
  - Deployed independently
  - Demonstrated to users independently
-->

### User Story 1 - Repeat N Times (Priority: P1)

As an Operator, I define a block to run steps exactly `N` times.

**Why this priority**: Fixed-count loops are foundational and widely used.

**Independent Test**: Execute a sequence with `repeatCount: 3` over one step and assert exactly 3 executions.

**Acceptance Scenarios**:

1. **Given** a step and `repeatCount: 5`, **When** executing, **Then** the step runs 5 times.
2. **Given** `repeatCount: 0`, **When** executing, **Then** the block is skipped and telemetry reflects 0 iterations.

---

### User Story 2 - Repeat Until Detected (Priority: P1)

As an Operator, I define a block that polls image/text presence until success or timeout.

**Why this priority**: Poll-until-success is critical for UI readiness gates.

**Independent Test**: Execute until an image appears; assert stop-on-success. Inject absence to assert timeout fails.

**Acceptance Scenarios**:

1. **Given** `repeatUntil` on `image: Present`, **When** detection succeeds, **Then** the loop exits and the next step runs.
2. **Given** `repeatUntil` with `timeoutMs: 2000`, **When** detection fails within timeout, **Then** sequence status is `Failed`.

---

### User Story 3 - If/Then/Else Branch (Priority: P2)

As an Operator, I define a condition referencing a detection target; if `Present`, run branch A else branch B.

**Why this priority**: Decision-making enables divergent flows without code.

**Independent Test**: Mock present/absent outcomes and assert the chosen branch.

**Acceptance Scenarios**:

1. **Given** `if` with `Present`, **When** condition holds, **Then** branch A executes and B is skipped.
2. **Given** `if` with `Absent`, **When** condition does not hold, **Then** branch B executes.

### User Story 4 - Loop Control (Priority: P2)

As an Operator, I declare `break` or `continue` within a loop to control execution.

**Why this priority**: Provides precise control during iteration without altering conditions.

**Independent Test**: Configure a loop with `continue` on a non-critical failure and `break` on a critical error; assert iteration counts and exit behavior.

**Acceptance Scenarios**:

1. **Given** a loop with `continue` on a transient detection miss, **When** the miss occurs, **Then** the iteration skips remaining steps and proceeds to the next iteration.
2. **Given** a loop with `break` on a critical condition, **When** the condition occurs, **Then** the loop exits immediately and subsequent steps do not run.

---

[Add more user stories as needed, each with an assigned priority]

### Edge Cases

- Loop with both `maxIterations` and `timeoutMs`: ensure stop occurs on first safeguard hit.
- Cadence too low/high: enforce bounds and return validation error on out-of-range.
- Condition flip-flop: ensure `while` exits correctly and does not oscillate.
- Nested blocks (if supported): ensure clear evaluation order and result aggregation.

## Requirements *(mandatory)*

<!--
  ACTION REQUIRED: The content in this section represents placeholders.
  Fill them out with the right functional requirements.
-->

### Functional Requirements

- **FR-01**: Support `repeatCount` loops over a group of steps; executes exactly N iterations.
- **FR-02**: Support `repeatUntil` loops that evaluate a condition at a fixed cadence until success or timeout.
- **FR-03**: Support `while` loops based on condition `Present`/`Absent` with cadence and safety bounds.
- **FR-04**: Support `if` with optional `else` around step groups; condition sources align with detection gates.
- **FR-05**: Require at least one safeguard (`timeoutMs` or `maxIterations`) per loop.
- **FR-06**: Conditions support image/text targets, trigger status by `triggerId`, confidence threshold, and optional region/language.
- **FR-07**: Polling cadence defaults to 100ms (configurable; bounds 50–5000ms).
- **FR-08**: Record per-block metrics: `evaluations`, `iterations`, `branchTaken`, `durationMs`, `appliedDelayMs`.
- **FR-09**: On condition timeout, set sequence status to `Failed` and stop execution.
- **FR-10**: Delay precedence: `delayRangeMs` overrides `delayMs` when present.
- **FR-11**: Validate and persist blocks via API; invalid configs return `400` with reason codes.
- **FR-12**: Execute blocks deterministically per declared order; results conform to contracts.
- **FR-13**: Emit structured logs for block start/end, condition checks, branch decisions, and loop iteration counts.
- **FR-14**: Enforce bearer auth on non-health endpoints (`401` on missing/invalid token).
- **FR-15**: Backward compatibility for sequences without blocks.
- **FR-16**: Support loop control: `break` to exit the loop; `continue` to skip to the next iteration.
- **FR-17**: Loop semantics (gate-first): For `repeatUntil` and `while`, evaluate the condition at the beginning of each iteration; if satisfied, exit the loop without executing steps for that iteration (no-op when already satisfied).
- **FR-18**: Loop control evaluation order: Evaluate `breakOn` at the start of each iteration and between steps; evaluate `continueOn` only between steps to skip remaining steps in the current iteration and proceed to the next.
- **FR-19**: Telemetry detail: Emit compact per-iteration summaries for loop blocks including `iterationIndex`, `decision` ("normal" | "break" | "continue"), and `durationMs`; avoid full per-step, per-iteration payloads by default.
- **FR-20**: Combined safeguards: If both `timeoutMs` and `maxIterations` are configured, enforce both and terminate the loop on whichever threshold is reached first (first-hit wins).

### Key Entities *(include if feature involves data)*

- **Sequence**: Declarative container of steps and blocks.
- **Block**: `repeatCount`, `repeatUntil`, `while`, `ifElse`; parameters include safeguards and cadence.
- **Condition**: Target (`image`/`text`) or `triggerId`, `present`/`absent`, confidence threshold, optional region/language.
- **Telemetry**: Iterations, evaluations, branch decisions, durations, applied delays, status per block.

## Success Criteria *(mandatory)*

<!--
  ACTION REQUIRED: Define measurable success criteria.
  These must be technology-agnostic and measurable.
-->

### Measurable Outcomes

- **SC-01**: Authors complete a tutorial to create loop/conditional sequences in under 30 minutes.
- **SC-02**: 95% of executions produce correct branch/loop counts across 100-run tests.
- **SC-03**: Typical loop exits occur within configured timeout windows and cadence; no unbounded loops.
- **SC-04**: Validation prevents invalid configurations; error messages enable correction within 1 iteration.
