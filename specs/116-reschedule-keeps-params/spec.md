# Feature Specification: Keep the parameter values of a queue entry on a run that reschedule-self starts

**Feature Branch**: `116-reschedule-keeps-params`
**Created**: 2026-09-29
**Status**: Draft
**Input**: GitHub issue #249 (B-031): "a run started by reschedule-self loses the parameterValues of the template entry, so a required parameter resolves from no scope". Labels: bug, P1.

Closes #249

## Background

A sequence can book its next run with a `reschedule-self` step. A queue template entry can give values to the sequence parameters with `parameterValues`. When the service starts a run from a `reschedule-self` booking, that run does not get the `parameterValues` of the queue template entry. A required sequence parameter then resolves from no scope, and the first step that uses the parameter fails.

Reproduction (from the issue; service 1.4.0.215, 2026-09-29, emulator-5558):

1. Sequence `ZZZ.PNS.NovaInteract` has the parameter `novaOptionImage` (required, no default). Step 0 books `reschedule-self` Timer `00:15:00`. An If step `tap-option` has a condition child `imageVisible` with `imageId` = `{{novaOptionImage}}`. A later step books `reschedule-self` Timer with `ocrOffset`.
2. Queue template entry: `Timer`, `timerRelativeOffset` `00:00:00`, `parameterValues` `novaOptionImage` = `pns-nova-option-affinity`. The read-back of the entry has `effectiveParameters` with `originLayer` `entry`.
3. Start the queue.

Observed result: the run of the template entry resolves the parameter and makes a booking. The booked run fails at `tap-option` with this message: "If 'tap-option' condition evaluation failed: Step 'tap-option': parameter 'novaOptionImage' used by field 'if.condition.children[2].imageId' could not be resolved from any scope." The retry booking of step 0 of that run starts the next run in the same way, thus the chain can never recover.

## Clarifications

### Session 2026-09-29

The pipeline runs with no user. The answers below are the most reasonable choices for the spec and the codebase.

- Q: Which scope does a booking keep: the full scope that the service gave the run that booked it, or only the queue layer and the entry layer? → A: The full scope that the service gave the run that booked it. Rationale: this is the scope that resolved the values in that run, so the booked run resolves each value from the same layer; for a template entry run it is the queue layer plus the entry layer.
- Q: When the template changes while the queue runs, does a booked run use the new `parameterValues`? → A: No. The booking keeps the scope of the run that made it. Rationale: a running queue already uses the template entries from its start time; a change needs a queue restart.
- Q: An EveryStep booking of a sequence that is already booked comes from a run with a different scope. Which scope does the register keep? → A: The scope of the most recent booking. The same rule applies to a Timer booking, because the Timer register also keeps only the most recent booking for each sequence. Rationale: the EveryStep register and the Timer register already replace the booking for each sequence; the scope goes with the booking.
- Q: Must the execution log or the API show where the booked run got its scope? → A: No. No new log field or API field. Rationale: FR-006 and the non-goals of the issue forbid new fields; the current error message for an unresolved parameter stays.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - A booked run resolves the parameters of the run that booked it (Priority: P1)

An author runs one sequence that has parameters from a queue template entry that gives `parameterValues`. The sequence books its next run with `reschedule-self`. The booked run must use the same parameter values as the run that booked it.

**Why this priority**: This is the defect. Without the fix, every booked run of a sequence that has parameters fails, and you cannot use `reschedule-self` and `parameterValues` together. It blocks a feature in the PNS authoring repository. No workaround is available.

**Independent Test**: Make a queue with one Timer entry that gives a value to a required parameter. The sequence books a `reschedule-self` Timer run and uses the parameter in a step field. Start the queue. Make sure that the booked run resolves the parameter to the entry value and does not fail with "could not be resolved from any scope".

**Acceptance Scenarios**:

1. **Given** a queue template entry with `parameterValues` `novaOptionImage` = `pns-nova-option-affinity` for a sequence with a required parameter `novaOptionImage` and a `reschedule-self` Timer step, **When** the entry run books a run and the booked run starts, **Then** the booked run resolves `novaOptionImage` to `pns-nova-option-affinity`.
2. **Given** the reproduction of issue #249, **When** the booked run starts, **Then** it evaluates the `tap-option` condition with `imageId=pns-nova-option-affinity` and does not fail with "could not be resolved from any scope".
3. **Given** a `reschedule-self` booking with each of the four booking options (five variants, because Timer has a relative offset and a time of day: Timer with a relative offset, Timer with a time of day, OncePerRun, EveryStep, AtQueueStart), **When** the booked run starts, **Then** it resolves each parameter to the same value as the run that booked it.

---

### User Story 2 - A chain of bookings keeps the values (Priority: P1)

A booked run books a new run, and that run books a new run, and so on. Each run in the chain must resolve the parameters to the same values as the first run of the chain.

**Why this priority**: The use case books the next run every 5 minutes from an OCR countdown. If only the first booked run gets the values, the chain stops after one step.

**Independent Test**: Use a sequence that books itself with `reschedule-self` Timer each time it runs. Let the queue run three or more booked runs. Make sure that each booked run resolves the parameter to the entry value.

**Acceptance Scenarios**:

1. **Given** a booked run that got the entry values, **When** it books a new run, **Then** the new run resolves each parameter to the same value.
2. **Given** a sequence with two booking steps (the Timer booking of step 0 with `00:15:00`, and a later Timer booking with `ocrOffset`), **When** the run makes both bookings, **Then** the Timer register keeps only the most recent booking, and the run that this kept booking starts resolves the parameter to the entry value. **When** the run stops before the later step, **Then** the step-0 booking starts a run that also resolves the parameter to the entry value.

---

### User Story 3 - No change for other runs (Priority: P2)

Runs that the queue start, the template timers, the daily clock, the daily retry, the every-step pass, the before-each-run pass, and the live schedules start must resolve parameters as they do now.

**Why this priority**: The fix must not change the resolution order or the scope layers of the runs that work now.

**Independent Test**: Run the current queue and parameter tests. Make sure that they all pass with no change.

**Acceptance Scenarios**:

1. **Given** a template entry with `parameterValues`, **When** the queue start, a template timer, or the daily clock starts the entry, **Then** the run resolves each parameter as it does before this change.
2. **Given** a sequence with no parameters and a `reschedule-self` step, **When** the booked run starts, **Then** it runs as it does before this change.

---

### Edge Cases

- A sequence that the queue did not start from a template entry (for example, a live schedule that runs with the queue scope only) books a `reschedule-self` run: the booked run gets the same scope as the run that booked it, which is the queue scope. This is the same result as before this change.
- An EveryStep booking or a Timer booking of a sequence that is already booked: the EveryStep register keeps one booking for each sequence (loop safety), and the Timer register also keeps one booking for each sequence. The kept booking has the scope of the most recent run that booked it.
- A booking that the liveness gate holds and fires later: the held booking keeps its scope.
- A sequence that a before-each-run pass or a template every-step pass runs books a `reschedule-self` run: the booked run gets the scope that the pass gave the run that booked it. For a template entry of the pass, this is the queue layer plus the entry layer of that entry (`EntryScope`). For a self-reschedule EveryStep booking that the every-step pass fires, this is the kept scope of that booking, or the queue scope when the booking has no kept scope.
- An entry with no `parameterValues` books a run: the booked run gets the queue scope, as before.
- A booking of a run when no queue run is active (the run ended): the booking is a no-op, as before. No scope is kept.
- Values that a step of the booking run sets at run time (for example step outputs) are not part of the parameter scope. They do not move to the booked run.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: When a queue run books a `reschedule-self` run, the service MUST keep the parameter scope of the run that booked it with the booking.
- **FR-002**: When the service starts a booked run, the service MUST run it with the kept parameter scope. The booked run MUST resolve each parameter to the same value, from the same scope layer, as the run that booked it.
- **FR-003**: FR-001 and FR-002 MUST apply to all four `reschedule-self` booking options (five variants, because Timer has a relative offset and a time of day): Timer (relative offset and time of day), OncePerRun, EveryStep, and AtQueueStart.
- **FR-004**: A run that a booked run books MUST get the same kept scope (the chain keeps the values for each generation).
- **FR-005**: The service MUST NOT change the parameter resolution order or the scope layers for runs that the queue start, the template timers, the daily clock, the daily retry, the every-step pass, the before-each-run pass, or the live schedules start.
- **FR-006**: The fix MUST NOT add a new API field or a new payload field on `reschedule-self`, and MUST NOT change the persisted queue template format.
- **FR-007**: When a booking has no kept scope (for example a code path that has no scope to give), the service MUST use the queue scope, as before this change.
- **FR-008**: The kept scope MUST be the full scope that the service gave the run that booked it, as it was at the time of the booking. A change to the queue template while the queue runs MUST NOT change the kept scope.
- **FR-009**: The service MUST NOT add a new execution log field or API field for the kept scope.
- **FR-010**: An EveryStep or Timer booking that replaces a booking of the same sequence MUST keep the scope of the most recent booking.

### Key Entities

- **Self-reschedule booking**: a run-scoped, not persisted record that a `reschedule-self` step makes. It has the sequence, the booking option, the fire time (for Timer), and, after this change, the parameter scope of the run that made it.
- **Parameter scope**: the layered set of parameter values that a run resolves from (queue layer, entry layer, and the layers under them).

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: In the reproduction of issue #249, 100% of booked runs resolve `novaOptionImage` to `pns-nova-option-affinity`, and 0 booked runs fail with "could not be resolved from any scope".
- **SC-002**: A chain of at least three booked runs keeps the entry values in each run.
- **SC-003**: All four booking options (five variants, because Timer has a relative offset and a time of day) keep the scope of the run that booked it (one automated test for each variant, or one test that covers each variant).
- **SC-004**: All current queue, self-reschedule, and parameter tests pass with no change to their expected results.

## Assumptions

- The parameter scope of the run that books is the correct scope for the booked run. For a run that a template entry started, this is the queue layer plus the entry layer, which is the scope the issue expects ("the parameter values of the run that booked it (or of the template entry of that sequence)").
- The bookings stay in memory for the life of the queue run only, as before. A service restart does not keep them.
- The fix is in the service. No change to the web UI is necessary.
