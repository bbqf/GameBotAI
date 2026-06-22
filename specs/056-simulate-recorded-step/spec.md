# Feature Specification: Simulate Recorded Step

**Feature Branch**: `056-simulate-recorded-step`  
**Created**: 2026-06-05  
**Status**: Implemented
**Input**: User description: "When recording a command I want to run the just recorded command step in order to simulate step by step execution of a command and be able to adjust the steps accordingly."

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Run Last Recorded Step (Priority: P1)

A user is in the command recorder building a sequence of steps. After recording a step (tap, swipe, or key input), they want to immediately execute just that step against the emulator to confirm it does what they intended — without having to close the recorder, save the command, and run the full sequence manually.

**Why this priority**: This is the core value: tight record-then-verify feedback loop. Without this, users must exit the recording flow, run the full command, and re-enter the recorder to make corrections — which is slow and disruptive.

**Independent Test**: Open the recorder, record a single tap step, press "Run step", and observe the emulator performs the tap. Delivers the full feedback loop as an MVP.

**Acceptance Scenarios**:

1. **Given** the recorder is open with at least one recorded step, **When** the user clicks "Run step" on the most recently added step, **Then** the emulator executes that single step and the recorder remains open
2. **Given** the recorder is open and a step was just run, **When** the step completes successfully, **Then** the recorder displays a success indicator and the step list remains editable
3. **Given** a step execution fails (e.g., emulator unreachable), **When** the failure occurs, **Then** the recorder shows an inline error message and the step list remains fully editable

---

### User Story 2 - Run Any Individual Step (Priority: P2)

A user reviews their recorded step list and wants to verify or re-test a specific earlier step — not just the latest — by running it individually against the current emulator state.

**Why this priority**: Builds on P1 by extending run capability to any step, not just the last. Valuable when earlier steps need adjustment after the emulator state has changed during recording.

**Independent Test**: Record three steps, then click "Run step" on the first step in the list. Emulator performs only that step.

**Acceptance Scenarios**:

1. **Given** the recorder has multiple recorded steps, **When** the user clicks "Run step" on any step in the list, **Then** only that step is executed against the emulator
2. **Given** a step is currently executing, **When** the user attempts to run another step, **Then** the run action is disabled (not double-fired) until the current execution completes

---

### User Story 3 - Run All Recorded Steps in Sequence (Priority: P3)

A user has finished recording a full set of steps and wants to preview the entire recorded sequence end-to-end before confirming and adding the steps to the command.

**Why this priority**: Completes the simulation workflow — after verifying individual steps, users naturally want a dry-run of the full sequence before committing it.

**Independent Test**: Record three steps, click "Run all", and observe the emulator executes them in order. User can then accept or adjust before confirming.

**Acceptance Scenarios**:

1. **Given** the recorder has two or more recorded steps, **When** the user clicks "Run all", **Then** each step is executed against the emulator in recorded order, with a brief visual indicator of the currently running step
2. **Given** "Run all" is in progress, **When** a step fails, **Then** execution stops at that step, the failed step is highlighted, and the list remains editable

---

### Edge Cases

- What happens if the emulator is not running when the user clicks "Run step"? → Show an inline error; do not close or reset the recorder.
- What happens if the step list is empty when the user attempts to run? → The run action is disabled / not shown.
- What happens if the user edits (deletes/reorders) steps while a run is in progress? → Edit actions are disabled during execution to prevent conflicting state.
- What happens if a tap step references coordinates outside the current emulator screen bounds? → Report an execution error on that step; recorder remains open.
- What happens if a step execution hangs? → A fixed 10-second timeout applies; the step is marked failed with a timeout error and the recorder is unlocked.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: Within the command recorder, each recorded step MUST have a "Run step" action that executes only that step against the connected emulator.
- **FR-002**: The recorder MUST remain open and fully editable after a single step is run.
- **FR-002a**: A step that has already been run (success or error) MUST be re-runnable at any time when no other execution is in progress.
- **FR-002b**: When a step is removed and replaced (re-recorded), the replacement step's execution status MUST start as idle with no prior success or error indicator.
- **FR-003**: The recorder MUST display a visible status indicator on a step while it is executing (e.g., spinner or highlight).
- **FR-004**: The recorder MUST display an inline success or error indicator on a step after execution completes.
- **FR-005**: The recorder MUST prevent new run actions on any step while another step is currently executing.
- **FR-006**: The recorder MUST provide a "Run all" action that executes all recorded steps in order against the emulator.
- **FR-007**: "Run all" execution MUST stop at the first failed step and highlight that step.
- **FR-008**: All editing actions (delete, reorder) on the step list MUST be disabled while any step execution is in progress.
- **FR-009**: If the emulator is unavailable when a run is attempted, the recorder MUST display an error message without closing or resetting the step list.
- **FR-010**: Run actions MUST be disabled (or hidden) when the recorded step list is empty.
- **FR-011**: Step execution MUST time out after a fixed duration (10 seconds); the step MUST be marked as failed with a timeout error message if no response is received within that window.

### Key Entities

- **RecordedStep**: A single recorded action (tap, swipe, or key input) that can be individually executed. Gains an `executionStatus` field (idle / running / success / error) and an optional `errorMessage`.
- **StepExecutionResult**: The outcome returned when a step is executed — success or failure with an error description.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A user can record a step and confirm whether it works correctly without leaving the recorder, reducing the verify-and-adjust loop from multiple navigation steps to a single button press.
- **SC-002**: Single-step execution completes and the result (success or error) is visible to the user within 3 seconds under normal emulator response times.
- **SC-003**: 100% of run actions respect the "no concurrent execution" constraint — double-clicks or rapid sequential presses never trigger two simultaneous executions.
- **SC-004**: The recorder step list remains fully intact and editable immediately after any step execution completes (success or failure).

## Assumptions

- The emulator already exposes an API or mechanism to execute individual primitive actions (tap at coordinates, swipe, key input). This feature adds the UI trigger; it does not define a new emulator protocol.
- Step execution reuses the existing `CommandExecutor` infrastructure; a thin new HTTP route wraps it. No new emulator protocol or execution logic is introduced.
- Execution is fire-and-done for each step (no streaming progress). The status changes to success or error when the call returns.
- "Run all" executes steps sequentially (one at a time), not in parallel, to reflect how the command would actually execute in production.
- Only one recorder session is active at a time (no concurrent multi-window concerns).

## Clarifications

### Session 2026-06-05

- Q: Does "run step" reuse existing execution infrastructure or require a new dedicated endpoint? → A: Reuse existing command/step execution path, passing a single-step payload.
- Q: What happens if step execution hangs indefinitely? → A: Fixed 10-second timeout; step marked as failed with a timeout error message.
- Q: Can a step be re-run after showing a result, and does editing reset the result? → A: Re-run always allowed; result resets to idle when the step is removed and re-recorded.
