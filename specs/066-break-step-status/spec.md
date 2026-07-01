# Feature Specification: Break Step Success/Failure Execution Statuses

**Feature Branch**: `066-break-step-status`  
**Created**: 2026-07-01  
**Status**: Draft  
**Input**: User description: "adjusting success/failure execution statuses of break steps. Break step can be success of failure in itself, depending whether the condition is true or false, but the "failed" break step means there is NO break, so failed break step must not influence the execution."

## Overview

Loops can be broken in two ways: a discrete **break step** placed inside the loop body, and a
loop-level **break condition** attached to a while-style loop (evaluated before/between
iterations). In both cases the break either fires (ending the loop) or does not fire (the loop
continues). Today the execution log represents a break step that did **not** fire as `Skipped`,
which does not clearly communicate that the break was evaluated and simply chose not to break;
the loop-level break condition, meanwhile, has no explicit success/"no break" representation at
all.

This feature redefines how a break's own outcome is reported, for both the discrete break step
and the loop-level break condition:

- A break that **fires** (its condition is true, or an unconditional "Always break" step) is a
  **success** — it did its job and broke the loop.
- A break that **does not fire** (its condition is false) "failed to break" — but this is surfaced
  as a distinct, neutral **"No break"** outcome, never the red "Failed" status used for genuine
  failures.

The critical invariant is that a non-firing break is *local to the break*: it must **not**
influence the execution — it must not mark the loop or the sequence/run as failed, and it must not
stop, skip, or otherwise alter the flow of subsequent steps or iterations.

To keep normal loop iterations from looking alarming, a non-firing break is shown with its
own distinct, neutral **"No break"** indicator in the execution log — visually separate from
a genuine red "Failed" result — rather than reusing the failure indicator used for real
failing steps.

## Clarifications

### Session 2026-07-01

- Q: When a break step's condition cannot be evaluated at runtime (a genuine error, not simply "false"), how should it behave? → A: Treat the error exactly like a false condition — a non-influential "no break" outcome; continue execution and do not fail the run.
- Q: How should a non-firing break step ("no break") be shown in the execution log? → A: Use a distinct, neutral "No break" badge, visually separate from a genuine red "Failed" indicator.
- Q: Which "break" mechanism does this feature cover? → A: Both — the discrete break step in a loop body AND the loop-level break condition (`breakOn`) on a while-style loop.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Break step reflects whether it broke (Priority: P1)

A sequence author reviewing an execution log wants each break step to show, at a glance,
whether it actually broke the loop. A break step that fired should read as a success; a
break step whose condition was false should read as a distinct "No break" outcome.

**Why this priority**: This is the core observable change. Without it, authors cannot tell
from the log whether a break fired, which is the whole purpose of the feature.

**Independent Test**: Author a loop containing a single conditional break step. Run it once
with the condition satisfied and once with the condition not satisfied. Confirm the break
step is reported as a success in the first run and as a "No break" outcome in the second.

**Acceptance Scenarios**:

1. **Given** a loop with a conditional break, **When** the break condition evaluates to true, **Then** the break step is recorded as a success, the loop iteration ends (break fires), and the condition detail is retained in the log.
2. **Given** a loop with a conditional break, **When** the break condition evaluates to false, **Then** the break step is recorded with the distinct "No break" outcome, and the loop continues to the next step/iteration.
3. **Given** a loop with an unconditional ("Always break") step, **When** the step is reached, **Then** the break step is recorded as a success and the loop iteration ends.

---

### User Story 2 - A non-firing break never taints the run (Priority: P1)

When a break step does not fire, the user expects the loop and the overall sequence/run to
be evaluated purely on the merits of the real work — the non-firing break must never turn a
healthy run red or halt it.

**Why this priority**: A conditional break that stays false across many iterations is the
normal, expected path. If each "no break" counted as a real failure, virtually every looped
sequence would be misreported as failing, making run status meaningless.

**Independent Test**: Author a loop that iterates several times with the break condition
false until a separate exit condition ends the loop. Confirm the loop and the sequence/run
both report success even though the break step reported "No break" on every iteration.

**Acceptance Scenarios**:

1. **Given** a loop whose break condition is false on every iteration, **When** the loop completes via its normal exit/iteration limit, **Then** the loop status is not failed solely because of the non-firing break.
2. **Given** a sequence containing such a loop and no other failing steps, **When** the sequence completes, **Then** the overall run status is success.
3. **Given** a non-firing break step, **When** it is evaluated, **Then** all subsequent steps in the same iteration and all remaining iterations run exactly as they would have before this change (no early stop, no skipped work caused by the break's "No break" outcome).
4. **Given** a run summary that counts failed steps or raises alerts on failures, **When** a break step reports "no break," **Then** that outcome is excluded from those failure counts/alerts.

---

### Edge Cases

- **Break condition cannot be evaluated (runtime error):** A genuine error while evaluating a break condition (e.g., a malformed or unresolvable condition) is treated exactly like a condition that evaluates to false — the break step is recorded as a non-firing "no break" failure, execution continues, and the run is not marked failed. This changes today's behavior, where an evaluation error fails the whole sequence.
- **Unconditional break:** Has no false path, so it can only ever be a success; it has no failure representation.
- **Break inside nested loops:** The non-influence guarantee applies to the immediately enclosing loop and every ancestor loop/sequence — a non-firing break must not fail any level of the hierarchy.
- **Break as the last step of an iteration vs. followed by more body steps:** In both cases a non-firing break must allow normal continuation (next body step, then next iteration).
- **Loop-level break condition evaluated multiple times per iteration:** A while-style break condition may be checked more than once per iteration (e.g., before and mid-iteration). Every false/error check is a non-influential "No break" outcome; the eventual true check is the break success that ends the loop. The exact granularity of how these repeated "No break" checks are surfaced in the log is left to planning, but none of them may influence the run status.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: When a break step fires (its condition evaluates to true, or it is unconditional), the system MUST record the break step's own status as a **success** and end the current loop iteration (break behavior unchanged).
- **FR-002**: When a conditional break step's condition evaluates to false, the system MUST record the break step's own outcome as a non-firing **"No break"** result. This outcome MUST be presented with a distinct, neutral "No break" indicator that is visually separate from the "Failed" indicator used for genuinely failing steps.
- **FR-002a**: When a conditional break step's condition cannot be evaluated because of a runtime error, the system MUST treat it as a non-firing "No break" outcome per FR-002 — recording the outcome (with the error detail retained per FR-007), continuing execution, and NOT marking the loop, sequence, or run as failed. This supersedes the prior behavior of failing the whole sequence on a break-condition evaluation error.
- **FR-003**: The system MUST no longer represent a non-firing conditional break as `Skipped`; the "No break" outcome defined in FR-002 replaces it.
- **FR-004**: A break step recorded with the "No break" outcome per FR-002 MUST NOT cause the enclosing loop to be marked failed.
- **FR-005**: A break step recorded with the "No break" outcome per FR-002 MUST NOT cause the enclosing sequence/run (or any ancestor loop) to be marked failed.
- **FR-006**: A break step recorded with the "No break" outcome per FR-002 MUST NOT stop, skip, or otherwise alter the execution of subsequent steps in the current iteration or of any remaining iterations.
- **FR-007**: The system MUST retain, for both the success and "No break" outcomes, the descriptive detail of the evaluation (condition type, evaluated result, and a human-readable message) so authors can understand why the break did or did not fire.
- **FR-008**: A non-firing break step's "No break" outcome MUST be excluded from any run-level failure counts, health summaries, or failure-triggered alerts.
- **FR-009**: The change MUST apply consistently across every loop construct that supports break steps (fixed-count, conditional/while, and any other loop variants that host break steps).
- **FR-010**: The success / "No break" semantics and the non-influence guarantees (FR-004 through FR-008, and the FR-002a error handling) MUST also apply to a loop-level break condition (a while-style loop's break-on condition): when it evaluates true the loop ends and this is represented as a break success. While it evaluates false — or cannot be evaluated — the loop MUST continue, and this MUST never mark the loop, sequence, or run as failed. Representation of the loop-level break condition is at the loop/block level (the loop's ended-by-break outcome); individual false evaluations need not be recorded as separate log entries, but none of them may influence run status.

### Key Entities *(include if feature involves data)*

- **Break step**: A step within a loop body that optionally carries a condition. It fires (breaks the loop) when unconditional or when its condition is true; otherwise it does not fire.
- **Loop-level break condition**: A condition attached to a while-style loop (evaluated before/between iterations) that ends the loop when it becomes true. It has the same fire / "No break" outcome vocabulary as a break step, but belongs to the loop rather than being a body step.
- **Break step outcome**: The recorded result of evaluating a break step for one iteration — a **success** when the break fired, or a distinct **"No break"** result when a conditional break did not fire (whether the condition was false or could not be evaluated) — along with retained condition detail.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: In 100% of runs, a break step that fires is reported as a success and a conditional break step that does not fire is reported with the distinct neutral "No break" indicator (never the genuine "Failed" indicator, and never the old `Skipped` representation).
- **SC-002**: A loop that runs to completion with a break condition that is false on every iteration reports the loop and the overall run as successful in 100% of such runs (assuming no other failing work).
- **SC-003**: Enabling this change produces zero difference in the sequence of actions performed and the number of iterations executed compared with the prior behavior for the same inputs (execution flow is unchanged).
- **SC-004**: Run-level failure counts and failure alerts show zero contributions from non-firing break steps.

## Assumptions

- A break that fires reuses the execution log's existing "success" indicator; a non-firing break uses a new distinct, neutral "No break" indicator rather than the existing "Failed" indicator (per Clarifications, 2026-07-01).
- The existing break-firing behavior (which iteration/loop ends when a break fires) is unchanged; only the *reporting* of the break step's own outcome and the *non-influence* guarantee for non-firing breaks are in scope.
- Unconditional ("Always break") steps are always successes and need no failure representation.
- The overall run is still marked failed by genuinely failing real steps; this feature only ensures a *non-firing break* is not one of those causes.
