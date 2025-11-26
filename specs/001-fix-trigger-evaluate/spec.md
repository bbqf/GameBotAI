# Feature Specification: Evaluate-And-Execute Trigger Guard

**Feature Branch**: `001-fix-trigger-evaluate`  
**Created**: 2025-11-26  
**Status**: Draft  
**Input**: User description: "The logic of EvaluateAndExecute must be corrected. It should first evaluate the trigger associated with the command. This has to be covered by unit tests with positive (execution performed when the trigger evaluates successfully) as well as negative (no execution when the trigger is pending)."

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

### User Story 1 - Trigger-Gated Command Execution (Priority: P1)

An automation operator needs Evaluate & Execute to honor the trigger outcome before any command steps run so that bots never act when the triggering condition is pending or failed.

**Why this priority**: Preventing unintended device actions is the core safety requirement and the primary reason customers use trigger-gated commands.

**Independent Test**: Invoke the Evaluate & Execute endpoint with a satisfied trigger and verify the command runs end-to-end without touching the force-execute path directly.

**Acceptance Scenarios**:

1. **Given** a running session and a command whose trigger evaluates as satisfied, **When** the operator calls Evaluate & Execute, **Then** the command executes immediately and returns the count of accepted inputs.
2. **Given** the same command after a cooldown window, **When** Evaluate & Execute is called again, **Then** the trigger is re-evaluated before any action runs and respects the updated trigger state.

---

### User Story 2 - Safe Handling for Pending Triggers (Priority: P2)

A QA engineer needs Evaluate & Execute to short-circuit without sending inputs whenever the trigger is pending so automated tests can assert that no unintended execution occurs.

**Why this priority**: Prevents regressions where pending triggers still run commands, which could corrupt captured test data or devices.

**Independent Test**: Configure a command with a pending trigger (e.g., wait duration not met) and verify Evaluate & Execute returns zero accepted inputs.

**Acceptance Scenarios**:

1. **Given** a pending trigger evaluation result, **When** Evaluate & Execute is called, **Then** zero actions execute and the stored trigger cooldown metadata remains unchanged.

---

### User Story 3 - Deterministic Unit Coverage (Priority: P3)

A developer needs lightweight unit tests that cover both satisfied and pending trigger paths so regressions are caught without relying solely on slower integration suites.

**Why this priority**: Fast unit tests guard the business logic and reduce CI failures caused by environment-specific integration behavior.

**Independent Test**: Run the new unit test class in isolation to confirm it fails if Evaluate & Execute stops evaluating triggers before execution.

**Acceptance Scenarios**:

1. **Given** a mock trigger evaluation that returns satisfied, **When** the unit test runs Evaluate & Execute, **Then** the command execution path is invoked exactly once and its return value propagates.
2. **Given** a mock evaluation that returns pending, **When** the unit test runs Evaluate & Execute, **Then** the command execution path is never called and the method returns zero.

---

[Add more user stories as needed, each with an assigned priority]

### Edge Cases

- Trigger missing or deleted between command creation and evaluation → request should fail with a not-found error before any execution.
- Session no longer running when Evaluate & Execute is called → method should reject with "not_running" to prevent orphaned actions.
- Trigger evaluation returns an error state → log and treat as pending so no execution occurs, surfacing the evaluation failure to the caller.
- Re-entrant or recursive command steps referencing other commands with their own triggers → ensure only the top-level Evaluate & Execute enforces trigger gating (child commands continue to rely on ForceExecute semantics).
- Concurrent Evaluate & Execute calls for the same command/session → cooldown metadata updates must be atomic so only one execution proceeds when satisfied.

## Requirements *(mandatory)*

<!--
  ACTION REQUIRED: The content in this section represents placeholders.
  Fill them out with the right functional requirements.
-->

### Functional Requirements

- **FR-001**: Evaluate & Execute MUST evaluate the command's associated trigger before any action or nested command steps run, even when shortcuts such as cached evaluations exist.
- **FR-002**: When the trigger evaluation state is `Satisfied`, the system MUST persist the updated trigger metadata (last evaluated/fired timestamps) before executing the command to keep cooldown logic consistent.
- **FR-003**: When the trigger evaluation state is `Pending` or `Failed`, the system MUST skip command execution, return zero accepted inputs, and leave trigger state unchanged aside from the last evaluated timestamp.
- **FR-004**: If the command has no trigger, the system MAY bypass evaluation by delegating to the existing forced execution behavior (unchanged baseline) and MUST document this exception in API responses.
- **FR-005**: Evaluate & Execute MUST surface domain-appropriate errors (session not running, command not found, trigger missing) without partially executing steps.
- **FR-006**: The platform MUST provide unit tests that cover the satisfied and pending evaluation paths, asserting that the execution pipeline is invoked or skipped accordingly.
- **FR-007**: Integration tests MUST continue to validate the end-to-end behavior (positive, pending, disabled trigger) without conflicting with the new unit coverage.
- **FR-008**: Telemetry/log entries MUST record each Evaluate & Execute attempt with outcome (executed vs skipped) so support teams can audit trigger gating decisions.

### Key Entities *(include if feature involves data)*

- **Command**: Automation artifact containing ordered steps and a reference to a trigger that determines when it may run.
- **Trigger Evaluation Result**: Captures trigger status (Satisfied, Pending, Failed), evaluation timestamp, and optional metadata such as cooldown windows; used to decide whether execution proceeds.
- **Session**: Represents a live connection to a device/emulator; execution is only allowed when the session is in a running state.

## Success Criteria *(mandatory)*

<!--
  ACTION REQUIRED: Define measurable success criteria.
  These must be technology-agnostic and measurable.
-->

### Measurable Outcomes

- **SC-001**: 100% of Evaluate & Execute invocations record a trigger evaluation outcome before any actions run (validated via logs or instrumentation during testing).
- **SC-002**: Unit tests covering satisfied and pending paths run in under 1 second collectively, enabling fast CI feedback for trigger gating regressions.
- **SC-003**: Integration tests confirm that satisfied triggers execute with a non-zero accepted count and pending triggers return zero in at least 3 consecutive CI runs.
- **SC-004**: No incidents of unintended command execution due to pending triggers are reported in the release following this change (based on support ticket review).

### Assumptions

- Existing trigger evaluation service accurately distinguishes satisfied vs pending; this effort focuses on invocation order and state handling.
- API surface and HTTP contract remain unchanged except for clarified logging/telemetry; no new endpoints are required.
- Force Execute semantics stay as-is for commands without triggers.
