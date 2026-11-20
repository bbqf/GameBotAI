# Feature Specification: A booked run uses the current parameter values of its template entry

**Feature Branch**: `196-fix-reschedule-params`
**Created**: 2026-11-20
**Status**: Implemented
**Input**: GitHub issue #409, "B-045: a run that `reschedule-self` books must use the current parameter values of its template entry". Labels: bug, P2. Closes #409.

## Problem *(context)*

A queue run starts a sequence for a template entry. The run gives the sequence the parameter values of that entry. A `reschedule-self` step in the sequence books one more run of the same sequence (a "booking"). Today, the booking keeps a copy of the parameter values that the entry had when the step made the booking. When the booking fires, the booked run uses this copy.

When the operator changes the values of the entry after the booking (pause, `POST /api/queues/{id}/apply-template`, resume), the queue gets the new values, and `template-diff` shows 0 differences. But the pending booking still has the old copy. A booked run also gives its own copy to each booking that it makes, so the old values stay in the chain of bookings until the operator stops and starts the queue.

Measurement from the issue (2026-11-19, service 1.12.27.290, queue PNS.Production):

1. 17:22: a run of PNS.TrainTroops used the entry value `fighterTier` = `pns-troop-tier-max` (an image parameter at that time). The run booked its next run for 20:06 (`reschedule-self`, `keep: earliest`).
2. Approximately 19:00: the sequence changed `fighterTier` to a number parameter (0 to 14). The template entry got `fighterTier` = `0`. `apply-template` put the value into the queue. `template-diff` showed 0 differences.
3. 20:06:46: the booked run failed at step 22: `parameter 'fighterTier' resolved to 'pns-troop-tier-max', which is not a whole number`.
4. The failed run had booked its step-0 retry (15 minutes). Each next firing was a `SelfReschedule` with the old value again, and failed again. A second queue (Exo) showed the same chain.
5. A stop and a start of the queue cleared the bookings. The runs after the start used the template values.

The selected fix is the preferred solution of the issue: **a booked run gets the parameter values of its template entry at the time that it runs**, not at the time of the booking.

## Clarifications

### Session 2026-11-20

Autonomous mode: the pipeline selected each answer. Each answer has a one-line rationale.

- Q: Which values are "the current values of the template entry": the values in the run plan of the running queue, or the values in the stored template? → A: The values in the run plan of the running queue (the values that `apply-template` puts into the queue). Rationale: the other firings of a running queue also use the run plan; a stored template change without `apply-template` does not change a running queue (feature 171), and the issue applies each change with `apply-template`.
- Q: What does a booking do when `apply-template` removed its entry from the run? → A: The booking stays and fires with the values that it kept (the behavior of today). Rationale: the issue keeps when and how often bookings fire out of scope; to drop or change such bookings is a different behavior change.
- Q: Does the rule also apply to an at-queue-start entry that a device liveness hold moved to the next-cycle register (feature 106)? → A: Yes. Such a held firing also gets the values of its entry at the time that it fires. Rationale: it is a firing of a template entry in the same register as the bookings; the same rule makes all entry firings consistent, at a small cost.
- Q: Does the queue monitor or the execution log get new information about which values a booking will use? → A: No. The monitor shows the bookings as today. The execution log of the booked run already shows the resolved parameter values. Rationale: the preferred solution of the issue does not need a monitor change; the monitor part belongs to the alternative solution only.
- Q: How does a booking find its entry when two entries use the same sequence? → A: The booking records the entry of the run that made it (by the stable entry key of the run plan), not the sequence. Rationale: the sequence alone is ambiguous when two entries use the same sequence with different values.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - A pending booking uses the new entry value (Priority: P1)

An operator has a running queue. A task of the queue books its next run with `reschedule-self`. Before the booking fires, the operator changes a parameter value of the template entry of the task and applies the template to the queue. When the booking fires, the run must use the new value.

**Why this priority**: This is the defect of issue #409. With the defect, a parameter change of a task that books itself has no effect until the operator restarts each queue.

**Independent Test**: Start a queue run with one entry that has the value `fighterTier` = `A`. The sequence of the entry books itself with a Timer booking. Pause the run, apply a template change that sets `fighterTier` = `B`, and resume. Let the booking fire. Make sure that the booked run resolves `fighterTier` to `B`.

**Acceptance Scenarios**:

1. **Given** a running queue with a pending `reschedule-self` Timer booking of entry E, made when E had `fighterTier` = `A`, **When** the operator changes E to `fighterTier` = `B` with `apply-template` and the booking fires, **Then** the booked run resolves `fighterTier` to `B`.
2. **Given** the same pending booking, **When** the booking fires and no change was applied, **Then** the booked run resolves `fighterTier` to `A` (the behavior of today does not change).
3. **Given** a pending booking with `keep: earliest`, **When** the operator applies a change of a parameter value of the entry, **Then** the fire time of the booking does not change.

---

### User Story 2 - The bookings that a booked run makes also use the current value (Priority: P1)

A booked run books one more run (for example a step-0 retry of 15 minutes, which the sequence books at its start in case a later step fails). This new booking must also use the current values of the template entry when it fires.

**Why this priority**: In the issue, the chain of bookings kept the old value for each next firing. If only the first booking is fixed, the chain continues to fail.

**Independent Test**: Make a chain: the entry run books run R1. R1 books run R2. Between the booking of R2 and its firing, apply a change of a parameter value of the entry. Make sure that R2 resolves the new value.

**Acceptance Scenarios**:

1. **Given** a booked run R1 of entry E that books run R2, **When** the operator changes a value of E and R2 fires, **Then** R2 resolves the new value.
2. **Given** a booked run that fails at a later step, **When** the step-0 retry that it booked fires after a change of a value of E, **Then** the retry resolves the new value.

---

### User Story 3 - A type change of a parameter does not make pending bookings fail (Priority: P1)

The operator changes the type of a parameter of the sequence (for example from an image parameter to a number parameter) and gives the entry a value that is valid for the new type. A booking that was made before the change must not fail with the old value.

**Why this priority**: This is the failure of the measurement in the issue: the old value `pns-troop-tier-max` is not a whole number, so each booked run failed each 15 minutes.

**Independent Test**: Make a booking while the entry has `fighterTier` = `pns-troop-tier-max`. Apply a template change that sets `fighterTier` = `0`, where the sequence now declares `fighterTier` as a number parameter. Let the booking fire. Make sure that the run does not fail with `not a whole number` and that it resolves `fighterTier` to `0`.

**Acceptance Scenarios**:

1. **Given** a pending booking made with the value `pns-troop-tier-max`, **When** the entry gets the value `0` through `apply-template` and the booking fires, **Then** the parameter step resolves `fighterTier` to `0` and does not fail with `not a whole number`.

---

### User Story 4 - A change of one entry does not touch the bookings of other entries (Priority: P2)

A queue has pending bookings of more than one task. The operator changes the parameter values of one entry. The bookings of the other tasks must stay as they are, with the same fire times and the same values.

**Why this priority**: The workaround of today (stop and start) clears the bookings of all tasks. The fix must not have this side effect.

**Independent Test**: Make pending bookings for entries E1 and E2. Apply a change of a value of E1 only. Make sure that both bookings stay pending with the same fire times, that the booking of E1 resolves the new value, and that the booking of E2 resolves the values of E2.

**Acceptance Scenarios**:

1. **Given** pending bookings of entries E1 and E2, **When** the operator applies a change of a value of E1, **Then** the number of pending bookings and their fire times do not change.
2. **Given** the same state, **When** the booking of E2 fires, **Then** it resolves the values of E2, not of E1.

---

### Edge Cases

- **The entry of a booking is no longer in the plan** (the operator removed the entry with `apply-template`): the booked run uses the values that the booking kept (the behavior of today). The fix does not remove or move the booking.
- **The entry of a booking is disabled** with `apply-template`: the booked run uses the current values of the disabled entry. The fix does not change whether a booking of a disabled entry fires.
- **The entry had no parameter values at the time of the booking and gets values later**: the booked run uses the new values.
- **The entry had parameter values and the operator removes all of them**: the booked run uses no entry values (the sequence defaults apply).
- **A booking that does not come from a template entry** (for example a booking that a run made from a live schedule, which has no entry): the booked run uses the values that the booking kept (the behavior of today).
- **Two entries use the same sequence with different values**: each booking uses the current values of the entry whose run made the booking, not the values of the other entry.
- **A booking fires while the queue is paused**: it does not fire (the behavior of today). After the resume, it uses the values that the entry has at the time of the firing.
- **Other booking kinds** (`OncePerRun`, `EveryStep`, `AtQueueStart` of `reschedule-self`): they use the same rule as the Timer booking.
- **An at-queue-start entry that a device liveness hold moved to the next-cycle register** (feature 106): when it fires, it uses the values that its entry has at that time.
- **A booking that a run-next run of entry E made** (feature 184): the run-next run itself uses the values of the time of the request (no change). The booking that it made uses the values that E has when the booking fires.
- **The stored template changes, but the operator does not apply it to the running queue**: the bookings use the values of the run plan, which did not change.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: A booking that a `reschedule-self` step makes MUST record which template entry of the run the booking belongs to, by the stable key of the entry in the run plan (not by the sequence).
- **FR-002**: When a booking fires, the booked run MUST get the parameter values that its template entry has in the run plan at that time, not the values at the time of the booking.
- **FR-003**: A booking that a booked run makes MUST belong to the same template entry as the booked run, so that FR-002 applies to each booking of the chain (for example the step-0 retry).
- **FR-004**: When the template entry of a booking is no longer in the run plan, or the booking belongs to no template entry, the booked run MUST use the values that the booking kept (the behavior of today).
- **FR-005**: A change of the parameter values of an entry MUST NOT add, remove, or move a booking, and MUST NOT change the fire time or the `keep` rule of a booking.
- **FR-006**: A change of the parameter values of one entry MUST NOT change the values that the bookings of other entries use.
- **FR-007**: The operator MUST NOT have to stop and start the queue to make a parameter change of an entry have an effect on its pending bookings.
- **FR-008**: The fix MUST NOT change the stop and start behavior, the parameter types, the validation of number parameters, or when and how often `reschedule-self` books runs (`keep: earliest`, the retry delay).
- **FR-009**: An at-queue-start entry that a device liveness hold moved to the next-cycle register MUST also get the values that its entry has in the run plan when it fires.
- **FR-010**: The fix MUST NOT change the queue monitor or the web UI. The execution log of a booked run MUST continue to show the parameter values that the run resolved.
- **FR-011**: A booking that a run-next run of an entry makes MUST belong to that entry, so that FR-002 applies to it. The run-next run itself MUST continue to use the values of the time of the request (feature 184, FR-005).

### Key Entities

- **Template entry**: one entry of the queue template. It has a stable ID, a sequence, a schedule, and parameter values. The run plan holds the entries of the run. `apply-template` swaps the plan.
- **Booking**: one pending additional run of a sequence that a `reschedule-self` step made. It has a fire time (Timer) or a register (`OncePerRun`, `EveryStep`, `AtQueueStart`), and the parameter values of the run that made it. The kept values also tell which template entry the booking belongs to (new), so that the booked run can get the current values of that entry.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: In a test of the scenario of the issue (booking with an image value, then a change to a number value with `apply-template`), 100% of the booked runs and retries after the change resolve the new value, and 0 runs fail with `not a whole number`.
- **SC-002**: After a parameter change of one entry, 0 bookings of other entries are removed, moved, or changed.
- **SC-003**: The operator does 0 stop and start actions to make a parameter change have an effect on pending bookings.
- **SC-004**: All tests of the queue run and of `reschedule-self` that pass before the change also pass after the change.

## Assumptions

- The operator changes the values of a running queue only with `apply-template` (feature 171). A change of the stored template alone does not change the run plan of a running queue; this is the behavior of today and is out of scope.
- The bookings live only in the memory of the run. A stop clears them (non-goal: no change).
- A "run next" request (feature 184) fixes its values at the time of the request by design (feature 184, FR-005). This fix does not change it.
- The fix does not change the web UI. The queue monitor shows the bookings as today (FR-010).
