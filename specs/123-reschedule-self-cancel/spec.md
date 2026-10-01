# Feature Specification: Reschedule-Self Cancel

**Feature Branch**: `123-reschedule-self-cancel`  
**Created**: 2026-10-01  
**Status**: Implemented  
**Input**: User description: "Let a sequence remove its own pending booking (reschedule-self cancel). GitHub issue #264 (FR-015)."

## Clarifications

### Session 2026-10-01

- Q: Which booking does Cancel remove: only one made in the current run, or the pending booking of the sequence in the queue? → A: The pending booking of the sequence in the queue that started the run, whichever run made it. Rationale: a sequence has at most one pending booking, so no other reading gives a different safe result. The issue wants the pending booking gone after success.
- Q (analyze I1): What does "queue run" mean in FR-001? → A: One start-to-stop run of a queue. The pending bookings of that queue run are kept in memory for that run only. A booking from an earlier run of the same sequence in the same queue run is removed. A booking from before a queue stop does not exist after the restart, so Cancel has nothing to remove for it. Rationale: this matches how the bookings are stored and how the issue words it.
- Q (analyze I2): Does Cancel remove an EveryStep injection? → A: No. Cancel removes only the one-time bookings of the sequence: Timer, OncePerRun and AtQueueStart bookings. An EveryStep injection is a standing schedule, not a pending booking. FR-011 keeps EveryStep unchanged. Rationale: the issue speaks about one pending booking, and removing a standing schedule would be a surprise.
- Q (analyze I3): Does "pending booking" in FR-005 include live schedules? → A: No. It means a reschedule-self booking only. Rationale: live schedules are not made by the step.
- Q (analyze I6): Are the outcome names part of the spec? → A: Yes. The outcome is "cancelled" with removed true, or "noop" with removed false. Rationale: clients can test one stable value.
- Q (loop 2, analyze C1): A OncePerRun drain copies all queued bookings before it fires them. Does a Cancel step stop the other bookings of the same sequence that are already in that copy? → A: Yes. After Cancel, no booking of the sequence fires, also not one that is already in the copy that the queue is about to fire. Rationale: SC-001 needs 0 retry wakes. A test MUST cover two OncePerRun bookings of one sequence with Cancel in a firing that comes from the copy.
- Q (loop 2, analyze C3): Is the validate call part of FR-010? → A: The validate call only checks a saved sequence and cannot receive a new payload. The test for it proves that a saved sequence with a valid Cancel step is valid. The PATCH call is also covered. Rationale: this is what the existing endpoints allow.
- Q (loop 2, analyze C4): Which terms does the text use? → A: "Booking" for the item, "firing" for one run of a booking, and "wake" only in the user stories. Rationale: one term for one thing.
- Q (loop 2, analyze C5): Which docs does FR-013 name? → A: The OpenAPI description, the API docs in `docs/architecture.md`, the changelog, and the tracker row if the file exists. Rationale: traceability.
- Q (loop 2, analyze C7): Which sequence id does a Cancel step in a nested or called sequence use? → A: The id of the sequence that owns the step, the same id that a Timer step of that sequence uses. Rationale: Cancel must remove what the same sequence booked.
- Q (analyze C1): Does the web UI change? → A: Yes. The sequence editor lists the Cancel option and hides the timer fields for it. Rationale: an author who uses the editor must be able to pick the option.
- Q: Does Cancel remove a Timer entry of the queue template? → A: No. It removes only a booking that a reschedule-self step created. Rationale: the template is operator configuration, not a booking of the sequence.
- Q: How does the step outcome state the result? → A: With a boolean field "removed" (true or false) in the outcome of the step, and a short text that says the same. Rationale: a client reads a field more reliably than text.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Remove the retry booking when the work succeeds (Priority: P1)

An author writes a daily task. Step 0 books a retry 30 minutes later, because step 0 is the only safe place for the booking: it stays in place after a named failure and after a time-limit cancel. When all work succeeds, the retry is not necessary. Today the queue wakes 30 minutes later, runs the establisher sequences, runs the task again, skips all work, and uses the emulator for about 15 seconds. The author adds a final step that removes the pending booking. After this, the queue does not wake for the retry.

**Why this priority**: This is the core request. Every daily task and every self-booking task has the empty wake today.

**Independent Test**: Run a sequence with step 0 "Timer +30 min" and a final "Cancel" step in a queue. After the run, the queue monitor shows no pending booking for the sequence, and the queue does not wake 30 minutes later.

**Acceptance Scenarios**:

1. **Given** a sequence in a queue run books a Timer entry at step 0, **When** the final step is the Cancel option, **Then** the step succeeds, the step outcome states that a booking was removed, and the queue monitor shows no pending booking for the sequence.
2. **Given** the same run, **When** the run ends and 30 minutes pass, **Then** the queue does not wake for this sequence.
3. **Given** the sequence fails by name before the final Cancel step, **When** the run ends, **Then** the booking of step 0 stays and the queue wakes 30 minutes later.

---

### User Story 2 - Cancel is safe when there is nothing to remove (Priority: P1)

An author puts the Cancel step at the end of a sequence that shares code paths. In some runs no booking is pending. In some runs the sequence does not run from a queue. The step must not fail the run in these cases.

**Why this priority**: A failing Cancel step would turn a success into a failure. The author could not use the step on common paths.

**Independent Test**: Run a sequence with only a Cancel step, first in a queue with no booking, then outside a queue. Both runs succeed.

**Acceptance Scenarios**:

1. **Given** a sequence in a queue run has no pending booking, **When** the Cancel step runs, **Then** the step succeeds and the step outcome states that no booking was removed.
2. **Given** a sequence runs outside a queue, **When** the Cancel step runs, **Then** the step succeeds, changes nothing, and the step outcome states that no booking was removed.

---

### User Story 3 - The author can read the result and the documentation (Priority: P2)

An author uses the API docs and the OpenAPI description to find the new option. An invalid payload for the step gives a clear 400 message, not a server error.

**Why this priority**: The option is used through the API only. Wrong or missing docs cause authoring errors. The project rules require living documentation.

**Independent Test**: Send a sequence with an invalid reschedule-self payload to the validation and save endpoints. Each call returns 400 with a message that names the problem. Read the OpenAPI description and find the Cancel option.

**Acceptance Scenarios**:

1. **Given** a reschedule-self payload with option "Cancel" and an extra field (for example a timer field), **When** the author saves the sequence, **Then** the API returns 400 and names the field.
2. **Given** a payload with an unknown option, **When** the author saves the sequence, **Then** the 400 message lists "Cancel" in the known options.
3. **Given** the published OpenAPI description, **When** the author reads the reschedule-self payload, **Then** the Cancel option and its no-op rules are described.

---

### Edge Cases

- A Cancel step runs, then a later step in the same run books a new Timer entry. The new booking is valid and stays: the last booking of a run wins (existing rule).
- The booking was made by a different sequence in the same queue. Cancel removes only the booking of the current sequence.
- The booking was made in an earlier run of the same sequence, but in the same queue run. The booking is still the pending booking of this sequence, so Cancel removes it. If none exists, the step reports that nothing was removed.
- The queue template has a daily Timer entry for the same sequence. Cancel does not remove that entry. It removes only a booking that a reschedule-self step created.
- Two Cancel steps run in one run. The first removes the booking. The second succeeds and reports that nothing was removed.
- The run is cancelled (operator cancel, time limit) before the Cancel step. The booking stays. This is the intended safety behavior.
- A Cancel step runs in a nested or called sequence. It uses the id of the sequence that owns the step, the same id that a Timer step of that sequence uses. It removes the booking of that sequence, with the same rules as a top-level step.
- Two OncePerRun bookings of one sequence are in the drain copy, and the first firing runs a Cancel step. The second booking does not fire.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The reschedule-self step MUST accept a new option "Cancel" that removes the one-time bookings (Timer, OncePerRun, AtQueueStart) that the current sequence has pending in the current queue run (one start-to-stop run of the queue). An EveryStep injection is not a pending booking and stays.
- **FR-002**: The Cancel step MUST succeed with no change when the sequence has no pending booking.
- **FR-003**: The Cancel step MUST succeed with no change when the sequence did not start from a queue.
- **FR-004**: The step outcome MUST state whether a booking was removed, with a boolean field "removed" and a short text. The outcome name is "cancelled" when a booking was removed and "noop" when not.
- **FR-005**: After a Cancel step removes a booking, the queue monitor MUST NOT show a pending reschedule-self booking for the sequence (live schedules are out of scope), and no firing of it MUST occur. After a Cancel step, no booking of the sequence MUST fire. This also applies to a booking that is already in the copy of the OncePerRun bookings that the queue is about to fire (the drain copy). See FR-015.
- **FR-006**: A run that ends before the Cancel step (named failure, time-limit cancel, operator cancel) MUST keep the earlier booking.
- **FR-007**: The Cancel option MUST remove only the booking of the current sequence. It MUST NOT change the booking of any other sequence or any other queue entry.
- **FR-008**: The Cancel option MUST NOT take timer fields. The validator MUST reject each timer field (`timerTimeOfDay`, `timerRelativeOffset`, and `ocrOffset`) when the option is Cancel. A payload with option "Cancel" and any one of these fields MUST return 400 and name the field. This is a firm required behavior. Unit tests MUST prove it for each of the three fields.
- **FR-009**: The validator MUST list "Cancel" with the other options in its "unknown option" message.
- **FR-010**: An invalid reschedule-self payload MUST return 400 on every API path that accepts a sequence, never 500. A contract test MUST prove this. The contract test MUST cover the create call, the update call, and the PATCH call. The validate call checks a saved sequence and cannot receive a new payload. Its test MUST prove that a saved sequence with a valid Cancel step is valid. The save path in the sequence repository MUST also reject an invalid payload with 400, not 500. The planner MUST confirm which of these endpoints exist before it writes the tests.
- **FR-011**: The existing options AtQueueStart, OncePerRun, Timer, and EveryStep, and the rule "the last booking of a run wins", MUST NOT change.
- **FR-012**: Every place in the backend that lists the sequence action types or the schedule options MUST include the new option.
- **FR-013**: These items MUST describe the new option: the OpenAPI description, the API docs in `docs/architecture.md`, the changelog, and the tracker row FR-015 in `docs/api-feature-requests.md` (if that file exists in this repository).
- **FR-014**: The sequence editor in the web UI MUST list the Cancel option and MUST hide the timer fields for it.
- **FR-015**: When the queue fires the OncePerRun bookings from a drain copy, a Cancel step in one of these firings MUST stop each other booking of the same sequence that is in the same copy. A cancelled booking MUST NOT fire. A test MUST cover two OncePerRun bookings of one sequence with a Cancel step in a firing that comes from the drain copy.

### Out of scope

- More than one pending booking for each sequence (issue #257, FR-013).
- Changes to PNS sequences or queue templates.
- The behavior of a final `timerTimeOfDay` booking with the daily Timer entry of a template.

### Key Entities

- **Pending booking**: A one-time wake entry that a sequence creates in its queue with a reschedule-self step. A queue monitor view lists it. One sequence has at most one pending booking in a queue run.
- **Reschedule-self step**: A sequence step with an option that sets or (new) removes the booking of the sequence.
- **Step outcome**: The result data of a step. For Cancel it states whether a booking was removed.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: In a queue run where all work succeeds and the final step is Cancel, the number of retry wakes for the sequence is 0.
- **SC-002**: In a queue run that fails by name before the Cancel step, the retry wake still occurs at the booked time (1 wake).
- **SC-003**: The Cancel step never changes the result of a run from success to failure in the no-booking case and in the non-queue case (0 failures in the tests for both cases).
- **SC-004**: 100% of invalid reschedule-self payloads in the contract tests return 400 and none return 500.
- **SC-005**: An author finds the Cancel option in the OpenAPI description and in the API docs without reading source code.

## Assumptions

- "Pending booking" means the booking that the existing reschedule-self step creates and that the queue monitor shows.
- The option name "Cancel" follows the example in the issue.
- The scope of Cancel is the current sequence in the current queue run, as the issue states.
