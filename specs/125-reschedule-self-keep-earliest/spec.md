# Feature Specification: Reschedule-Self Keep Earliest

**Feature Branch**: `125-reschedule-self-keep-earliest`  
**Created**: 2026-10-01  
**Status**: Implemented  
**Input**: User description: "Give the reschedule-self action an option `keep: earliest` (GitHub issue #257). Closes #257."

## Clarifications

### Session 2026-10-01

- Q: Which schedule options does `keep` apply to? → A: Only `Timer`. Only Timer bookings are limited to one pending booking for each sequence; the other options queue or inject entries and are not replaced. Reason: `keep: earliest` has no meaning for the other options.
- Q: What does the validator do with `keep` on an option other than `Timer`? → A: It rejects the payload with its own error ("keep is only valid when option is Timer"), the same rule as `timerTimeOfDay` and `ocrOffset` on other options. Reason: a silent no-op hides an author mistake.
- Q: What does "earlier" compare? → A: The absolute fire times of the two bookings. For an `ocrOffset` booking, this is the fire time that the resolver gives after it applies `min`, `max` and the fallback. Reason: the offsets are measured at different moments in a run.
- Q: What is "the same run" for FR-002 and FR-003? → A: One execution of the sequence by the queue. An earlier execution or the engine (for example the step-0 retry) can make a pending booking. The first booking of the new run replaces it. Reason: a stale booking must not block the new timers.
- Q: Does a `keep: earliest` booking that loses (later or equal) log anything? → A: Yes. The run logs that the booking was kept back, with both fire times. The step still counts as successful. Reason: the author can see why the booking did not change.
- Q: What does the step result report for a losing booking? → A: Success. The booking was valid and the pending booking is the earlier one. Reason: a failed step would abort the run.
- Q: Is the value `earliest` the only value? → A: Yes. Any other value, an empty string, or a non-string value gives a 400 error that names `earliest`. Reason: FR-006.

### Session 2026-10-01 (analyze loop 1)

- Q: (I1) Does a booking without `keep` count as a booking of its run? → A: Yes. Each Timer booking that a run makes records the run, with or without `keep`. A later `keep: earliest` booking of the same run compares with it. Reason: the mix rule in the Edge Cases needs this. Added to FR-002.
- Q: (C3a) How does a booking in the past compare? → A: The same rule applies. The compare uses the absolute fire times only. A past fire time is earlier than a future fire time, so it wins.
- Q: (C3b) For an `ocrOffset` booking, which fire time is compared? → A: The fire time after the resolver applies `min`, `max` and the fallback (see Session 2026-10-01). A test MUST cover one `ocrOffset` case (FR-012).

### Session 2026-10-01 (analyze loop 2)

- Q: (U1) How does the engine retry booking become a booking with no run? → A: When the engine puts a held Timer booking back into the register (the re-arm path), the stored booking gets no run. The fire time does not change. Reason: a re-armed booking is not a booking of the new run, so the first booking of the new run replaces it (FR-003). This is the only change to a booking source; it is part of the FR-003 ordering rule.
- Q: (U3) What is the run key? → A: The root execution of the sequence. A booking made inside a child sequence execution uses the run key of the root execution. Reason: the queue starts one root execution for each run.
- Q: (U2) At which level does the losing-booking message log? → A: Information level, with the pending fire time and the new fire time. Reason: this is a normal event, not an error.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Keep the earliest booking of a run (Priority: P1)

A sequence author has one sequence that must claim three pools. Each pool has its own free-claim timer, and the sequence books the next run with `reschedule-self` after each pool. Today a queue keeps one pending booking for each sequence, and the last booking of a run wins. In the probe, one run booked 15, 50, 30, and 40 minutes, and only the 40 minute booking stayed pending. The 15 minute booking was lost, so the earliest pool timer is missed. The author sets `keep: earliest` on the `reschedule-self` steps. Then the earliest booking of the run stays pending.

**Why this priority**: This is the observed gap. Without it, the author must keep one copy of the sequence for each pool.

**Independent Test**: Run a sequence with four `reschedule-self` steps with `keep: earliest` that book 15, 50, 30, and 40 minutes. Check that the one pending booking is 15 minutes ahead.

**Acceptance Scenarios**:

1. **Given** a run with `keep: earliest` that books 15, 50, 30, and 40 minutes in this order, **When** the run ends, **Then** the one pending booking of the sequence is 15 minutes ahead.
2. **Given** a pending booking and a new booking with `keep: earliest` that is later than the pending booking, **When** the new booking is made, **Then** the pending booking stays and the new booking is dropped.
3. **Given** a pending booking and a new booking with `keep: earliest` that is earlier than the pending booking, **When** the new booking is made, **Then** the new booking replaces the pending booking.
4. **Given** a pending booking and a new booking with `keep: earliest` at the same time as the pending booking, **When** the new booking is made, **Then** the pending booking stays (the booking time is the same).

---

### User Story 2 - The retry booking of step 0 does not block the run (Priority: P1)

A queue can book a retry of the sequence at step 0 of a run. The first booking of the run replaces this retry booking, also when `keep: earliest` is set and the retry booking is earlier than the new booking. Only the bookings made later in the same run compete with each other.

**Why this priority**: Without this, an old retry booking could hide the real timer of the run.

**Independent Test**: Make a pending retry booking that is earlier than the first booking of a run. Run the sequence with `keep: earliest`. Check that the first booking of the run replaces the retry booking.

**Acceptance Scenarios**:

1. **Given** a step-0 retry booking that is 5 minutes ahead and a run whose first booking has `keep: earliest` and is 20 minutes ahead, **When** the first booking is made, **Then** the pending booking is 20 minutes ahead.
2. **Given** the first booking of the run is made (20 minutes), **When** a second booking with `keep: earliest` of 10 minutes is made in the same run, **Then** the pending booking is 10 minutes ahead.

---

### User Story 3 - Keep the default behavior unchanged (Priority: P1)

A sequence author has sequences without the `keep` option. These sequences must book as before: the last booking of a run wins.

**Why this priority**: A regression here changes the booking of sequences that work today.

**Independent Test**: Run the existing booking tests. All results stay the same. Run a sequence that books 15, 50, 30, and 40 minutes without `keep` and check that the pending booking is 40 minutes ahead.

**Acceptance Scenarios**:

1. **Given** a run that books 15, 50, 30, and 40 minutes without `keep`, **When** the run ends, **Then** the pending booking is 40 minutes ahead.
2. **Given** a payload with no `keep` key, **When** the payload is validated, **Then** the payload is valid, as before.

---

### User Story 4 - Reject an invalid `keep` value with 400 (Priority: P2)

A sequence author sends a `reschedule-self` payload with an unknown `keep` value, for example `keep: latest`. The API rejects the sequence with a 400 response and a clear message. It does not return a 500 response and does not drop the value.

**Why this priority**: A silent drop or a server error hides the mistake of the author.

**Independent Test**: Save a sequence with `keep: latest` through the API and check for a 400 response with a message that names the allowed values.

**Acceptance Scenarios**:

1. **Given** a payload with `keep: latest`, **When** the sequence is saved through the API, **Then** the response is 400 and the message names the accepted value `earliest`.
2. **Given** a payload with `keep: earliest`, **When** the sequence is saved through the API, **Then** the response is a success and the value is stored and returned.
3. **Given** a payload with `keep` in a different case, such as `Earliest`, **When** the sequence is saved, **Then** the payload is accepted, like the other option values of this action.

---

### User Story 5 - Find the option in the API documentation (Priority: P3)

A sequence author reads the published API documentation of the `reschedule-self` payload. The documentation describes `keep` and its allowed value.

**Why this priority**: The option is only useful if authors can find it.

**Independent Test**: Read the published schema description of the `reschedule-self` payload and check that it names `keep` and `earliest`.

**Acceptance Scenarios**:

1. **Given** the published API documentation, **When** an author reads the `reschedule-self` payload, **Then** the documentation describes `keep`, the value `earliest`, and the default behavior without `keep`.

---

### Edge Cases

- A `reschedule-self` step with `keep: earliest` when no booking is pending: the new booking is made as normal.
- A run that mixes steps with and without `keep`: each booking follows its own rule. A booking without `keep` always replaces the pending booking. A booking with `keep: earliest` replaces the pending booking only if it is earlier. The author is responsible for the mix.
- `keep` with a schedule option other than `Timer` (for example `Cancel` or `OncePerRun`): the validator rejects the payload with a 400 error (FR-006a).
- A booking that lies in the past: it follows the same ordering rule as other bookings.
- An earlier run of the same sequence made the pending booking: the new run starts fresh. Its first booking replaces the pending booking, as for the step-0 retry booking (see FR-003).
- Two sequences in one queue: a booking of one sequence never changes the booking of another sequence.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The `reschedule-self` payload MUST accept an optional field `keep` with the value `earliest`.
- **FR-002**: When a booking has `keep: earliest` and the sequence already has a pending booking that was made earlier in the same run, the new booking MUST replace the pending booking only if the new booking time is earlier than the pending booking time. If the new booking time is later than or equal to the pending booking time, the pending booking MUST stay. Each Timer booking that a run makes MUST record the run, with or without `keep`, so a `keep: earliest` booking compares with an earlier booking of the same run that has no `keep`.
- **FR-003**: The first booking of a run MUST replace the pending booking of the sequence that was made before the run (for example the step-0 retry booking), also when `keep: earliest` is set and the pending booking is earlier. A booking that the engine puts back into the register (the re-arm path) MUST lose its run, so the first booking of the next run replaces it.
- **FR-004**: A booking without `keep` MUST behave as before this feature: it replaces the pending booking, and the last booking of a run wins.
- **FR-005**: The queue MUST still keep at most one pending booking for each sequence.
- **FR-006**: An unknown value of `keep` MUST be rejected with a 400 response and a message that names the accepted value. It MUST NOT cause a 500 response and MUST NOT be dropped silently.
- **FR-006a**: A `keep` field on a payload whose `option` is not `Timer` MUST be rejected with a 400 response and a message that says `keep` is only valid when option is Timer.
- **FR-006b**: A booking that loses to the pending booking MUST be logged with both fire times, and the step MUST still count as successful.
- **FR-007**: The value of `keep` MUST be case-insensitive, and the stored value MUST come back unchanged in API responses for the sequence.
- **FR-008**: Every place that validates, stores, or lists the fields of the `reschedule-self` payload MUST accept `keep`, so a sequence with `keep: earliest` is not rejected or changed in one place and accepted in another.
- **FR-009**: The published API documentation of the `reschedule-self` payload MUST describe `keep`, its allowed value, and the default behavior.
- **FR-010**: The change MUST NOT key bookings by the template entry, MUST NOT add a `key` field, and MUST NOT allow more than one pending booking for each sequence.
- **FR-011**: The change MUST NOT alter the schedule options (`AtQueueStart`, `OncePerRun`, `Timer`, `EveryStep`, `Cancel`) or the other booking sources, except for the ordering rule of FR-002 and FR-003.
- **FR-012**: Automated tests MUST cover: the 15/50/30/40 example, a later booking, an earlier booking, an equal booking, a booking in the past (tested at the coordinator level too, not only in the booking store), one `ocrOffset` booking, the logged message and the successful step result of a losing booking, the step-0 retry replacement (with the real engine retry path, which makes a booking with no run), the default behavior without `keep`, a 400 response for an invalid value, and round-trip storage of `keep`.
- **FR-013**: The change MUST update `docs/architecture.md` (the self-reschedule section, with the `keep: earliest` rule, the same-run rule, and the "Last reviewed" date), set the `Status` line of this spec, update `specs/STATUS.md`, and add an entry to `CHANGELOG.md` (Constitution Principle V and the Definition of Done).

### Key Entities

- **Pending booking**: The one next run that a queue has booked for a sequence through `reschedule-self`. A queue keeps at most one for each sequence.
- **Run**: One execution of a sequence. A run can make many bookings. A booking made before the run (for example the step-0 retry) is not part of the run.
- **`keep` option**: An optional field of the `reschedule-self` payload. The value `earliest` makes the booking replace the pending booking of the same run only if it is earlier.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A run that books 15, 50, 30, and 40 minutes with `keep: earliest` leaves one pending booking of 15 minutes. Before this feature it left 40 minutes.
- **SC-002**: A run that books the same times without `keep` still leaves 40 minutes pending.
- **SC-003**: The Noah's Tavern route can use one sequence for the three pools. The PNS authoring repository then needs one file for the route instead of three. This is checked after deploy, no task.
- **SC-004**: 100% of the existing booking and validation tests pass with no change to their expected values.
- **SC-005**: An invalid `keep` value gives a 400 response in 100% of the tested save paths, and gives no 500 response.
- **SC-006**: The published API documentation names `keep` and `earliest` for the `reschedule-self` payload.

## Assumptions

- `keep` is a sibling field of `option` in the `reschedule-self` payload. It is not part of the `ocrOffset` object.
- `keep: earliest` applies to the `Timer` option only (see Clarifications).
- "Earlier" compares the absolute booking times of the two bookings (the time at which the run will start), not the offsets.
- "The same run" means the bookings made by one execution of the sequence. A booking that exists when the run starts belongs to an earlier time and is replaced by the first booking of the run.
- The booking store change and the coordinator change are one unit of work in the same code files. Tasks order them in sequence, not in parallel.
- Code and tests live in the existing projects: `src\GameBot.Domain`, `src\GameBot.Service`, `tests\unit`, `tests\integration`, `tests\contract`. The plan must not name other directories.
- A task marked as parallel (`[P]`) must not share a file with another task in the same phase.
- A test that fails to compile because a new signature does not exist yet counts as a red test. The task order adds the signature in the same phase.
- The repository has a changelog at `CHANGELOG.md` in the repository root. FR-013 requires an entry in it.
- The PNS authoring repository and its sequences are out of scope.
- The other two shapes in issue #257 (a booking for each template entry, and a `key` field) are out of scope. The user chose only `keep: earliest`.
