# Feature Specification: Command Sequences

**Feature Branch**: `001-command-sequences`  
**Created**: 2025-12-09  
**Status**: Draft  
**Input**: User description: "Sequences of Commands. I need to be able to create a sequence of Commands. It should support 0..n Commands in the sequence, with optional delays in between. Delays should be specified either in milliseconds or a range of x to y milliseconds, meaning there will be a random delay between x and y milliseconds during run time. It should also be supported to continue to the next step only if an image will be detected or a configurable timeout (15s default) occurs. If a timeout occurs, the sequence execution should be aborted and an appropriate warning message logged."

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

### User Story 1 - Configure and run a command sequence (Priority: P1)

Create a reusable sequence that runs a list of existing Commands with optional delays between them.

**Why this priority**: Delivers the core value of chaining multiple Commands into a single runnable unit.

**Independent Test**: Create a sequence with 2 Commands and fixed delays; run it; verify both Commands execute in order and total duration includes delays.

**Acceptance Scenarios**:

1. Given two existing Commands A and B, When a sequence [A, B] with a 500 ms delay is executed, Then A completes, a ~500 ms delay occurs, and B completes; result shows both steps accepted.
2. Given an empty sequence, When executed, Then the system returns success with zero steps executed and no errors.

---

### User Story 2 - Randomized delay ranges (Priority: P2)

Support specifying delay as either a fixed millisecond value or a range x–y milliseconds, choosing a random value per step at runtime.

**Why this priority**: Adds variability needed to mimic human-like timing or avoid detection in game UIs.

**Independent Test**: Create a sequence with a delay range (1000–2000 ms) between steps; run it and assert actual delay falls within range and steps still execute in order.

**Acceptance Scenarios**:

1. Given a sequence with delayRange (1000, 2000), When executed, Then measured inter-step delay is within [1000, 2000] ms.

---

### User Story 3 - [Brief Title] (Priority: P3)

Allow a step to proceed only if an image is detected before a timeout; otherwise abort the sequence with a warning.

**Why this priority**: Essential for sequences that depend on screen state transitions.

**Independent Test**: Create a sequence where step B is gated by detecting image X with a 15s timeout. Run twice: one with X present (continues), one without (aborts at timeout and logs warning).

**Acceptance Scenarios**:

1. Given a gating image present, When executing the gated step with timeout 15s, Then the sequence continues and completes.
2. Given the gating image absent, When executing with timeout 15s, Then after 15s the sequence aborts and logs a warning message.

---

[Add more user stories as needed, each with an assigned priority]

### Edge Cases

<!--
  ACTION REQUIRED: The content in this section represents placeholders.
  Fill them out with the right edge cases.
-->

- Zero commands in sequence: execution returns success immediately; no delays applied.
- Delay specified both as fixed and range: [NEEDS CLARIFICATION: precedence or mutual exclusivity].
- Invalid delay values (negative, range min > max): validation rejects with descriptive error.
- Detection gating with zero timeout: treated as immediate check (proceed only if detected right away).
- Sequence aborts mid-way: already executed steps remain executed; subsequent steps are not attempted.

## Requirements *(mandatory)*

<!--
  ACTION REQUIRED: The content in this section represents placeholders.
  Fill them out with the right functional requirements.
-->

### Functional Requirements

- **FR-001**: System MUST allow creating a "CommandSequence" entity containing 0..n references to existing Commands, each with optional delay configuration.
- **FR-002**: System MUST support delay specified as either a fixed millisecond value or a range [minMs, maxMs]; at runtime, if range is specified, choose a random integer in the inclusive range.
- **FR-003**: System MUST support per-step "continueWhenDetected" gating with a target image id and a timeout (default 15s) before proceeding; if detection fails to occur before timeout, abort the sequence and log a warning.
- **FR-004**: System MUST provide validation: fixed delay >= 0; range values >= 0 and min <= max; timeout in seconds within [1, 300] unless overridden by configuration.
- **FR-005**: System MUST record execution results per step (accepted count, start/end timestamps, applied delay, gating outcome) and an overall sequence status (Completed | AbortedByTimeout | Failed).
- **FR-006**: System MUST log at warning level when a step aborts due to detection timeout, including step index, gating image id, and elapsed time.
- **FR-007**: System SHOULD allow configuring a default detection timeout at runtime; if unspecified on a step, use the default (15s).
- **FR-008**: System SHOULD allow configuring a default delay range or fixed delay at sequence level which can be overridden per step.
- **FR-009**: System MUST be idempotent in storage: creating or updating a sequence persists without duplicating referenced Commands.

*Example of marking unclear requirements:*

- **FR-006**: System MUST authenticate users via [NEEDS CLARIFICATION: auth method not specified - email/password, SSO, OAuth?]
- **FR-007**: System MUST retain user data for [NEEDS CLARIFICATION: retention period not specified]

### Key Entities *(include if feature involves data)*

- **CommandSequence**: name, description?, steps[], createdAt/updatedAt.
- **SequenceStep**: commandRefId, delayMs?, delayRangeMinMs?, delayRangeMaxMs?, gateDetectionImageId?, gateTimeoutSeconds?.
- **SequenceExecutionResult**: stepsResults[], overallStatus, startedAt, endedAt.
- **StepExecutionResult**: commandRefId, accepted, appliedDelayMs, gateDetected (bool), gateElapsedMs, startedAt, endedAt.

## Success Criteria *(mandatory)*

<!--
  ACTION REQUIRED: Define measurable success criteria.
  These must be technology-agnostic and measurable.
-->

### Measurable Outcomes

- **SC-001**: Users can configure and run a sequence of up to 20 Commands with optional delays without errors.
- **SC-002**: Randomized delay range selection completes within the specified bounds 99% of runs.
- **SC-003**: Detection-gated steps either proceed within timeout or abort with a warning; no step continues after timeout (100% of cases).
- **SC-004**: Sequence execution telemetry shows accurate delays and gating outcomes; logs contain clear warning messages for timeouts.

## Assumptions

- Default detection timeout is 15 seconds unless overridden by configuration or per-step.
- Delay range and fixed delay are mutually exclusive per step; if both provided, range takes precedence. [NEEDS CLARIFICATION]
- Image detection semantics reuse existing detection capabilities and configured selection strategies.
