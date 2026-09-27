# Feature Specification: A reschedule-self time of day that has passed books the next day

**Feature Branch**: `claude/resolve-github-issue-uxxlah` (spec number 109)  
**Created**: 2026-09-27  
**Status**: Draft  
**Input**: GitHub issue #227: "a reschedule-self step with a timerTimeOfDay that has already passed today books the sequence for now, not for that time on the next day". Full description: see the issue and the feature description that started this spec.

## Background

A sequence step `reschedule-self` with `option: Timer` and a `timerTimeOfDay` books one more run of the sequence in the queue run that started it. When the time of day is still ahead today, the step books the run for today at that time. This is correct.

When the time of day is not ahead any more, the step books the run for the current moment. The queue runs the sequence again immediately. If the sequence has the same step again, it books the current moment again. Thus the sequence runs again and again until the operator stops the queue.

The probe in the issue shows this. On 2026-09-24 at local time UTC+02:00, the step `timerTimeOfDay: "14:55"` ran at 14:55:41. `GET /api/queues/{id}/monitor` showed the `SelfReschedule` entry at `2026-09-24T14:55:41+02:00`, that is, the current moment. The sequence then ran again approximately every 30 s.

The template entry `Timer` with `timerTimeOfDay` has the correct behavior. After it ran at 14:52, the monitor showed it at 14:52 on the next day.

## Clarifications

### Session 2026-09-27

- Q: What does the step do when the time of day is equal to the current time, to the second? → A: It books the run for the next day. Rationale: the time is "not ahead any more"; a booking for the current moment makes the run occur again immediately, which is the defect.
- Q: Which UTC offset does the next-day booking use when the local clock changes (daylight saving time) between today and the next day? → A: The offset of the service-local time zone on that next day. Rationale: the operator gives a clock time; the run must occur at that clock time on the local clock of the next day.
- Q: What does the step do when the next-day clock time does not exist because the local clock goes forward at that time? → A: The service uses the standard offset of the local time zone for that clock time. The run occurs one hour off on that one day only. Rationale: this is the usual .NET behavior for a time in the gap; it is rare and the next day is correct again.
- Q: Does the fix change `timerRelativeOffset`, `ocrOffset`, the other options, or the template `Timer` entry? → A: No. Rationale: the issue says that these are correct, and the non-goals forbid changes to them.
- Q: Does the step result text change? → A: The text keeps its format. It shows the booked instant, which is now the next-day instant when the time of day has passed. Rationale: the text already shows the resolved instant; only the value changes.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - A daily sequence books its next run at a clock time from its last step (Priority: P1)

An operator has a daily sequence. Its last step is `reschedule-self` with `option: Timer` and `timerTimeOfDay: "11:00"`. The run starts at 11:00 and stops at 11:20. The step books the next run for 11:00 on the next day. The sequence does not run again on the same day.

**Why this priority**: This is the defect in the issue. Without the fix, the sequence runs again and again for the full day.

**Independent Test**: Set the clock after the time of day. Run the step. The booked instant is the same clock time on the next day.

**Acceptance Scenarios**:

1. **Given** a local time of 14:55:41 on 2026-09-24 at UTC+02:00, **When** a `reschedule-self` step with `timerTimeOfDay` 14:55 runs, **Then** the step books the run for `2026-09-25T14:55:00+02:00`.
2. **Given** a local time that is equal to the time of day, to the second, **When** the step runs, **Then** the step books the run for the same time on the next day.
3. **Given** a booking for the next day, **When** the queue loop runs again on the same day, **Then** the queue does not run the sequence again because of this booking.

---

### User Story 2 - A time of day that is still ahead keeps today (Priority: P1)

An operator uses a `reschedule-self` step with a time of day that is still ahead today. The step books the run for today at that time, as before.

**Why this priority**: This case works now. The fix must not change it.

**Independent Test**: Set the clock before the time of day. Run the step. The booked instant is today at that time.

**Acceptance Scenarios**:

1. **Given** a local time of 14:52:11 on 2026-09-24 at UTC+02:00, **When** a step with `timerTimeOfDay` 14:55 runs, **Then** the step books the run for `2026-09-24T14:55:00+02:00`.

---

### User Story 3 - The monitor shows the correct next-day time (Priority: P2)

An operator reads `GET /api/queues/{id}/monitor`. The `SelfReschedule` entry shows the booked instant on the next day.

**Why this priority**: The operator uses the monitor to see the next run. It must agree with the booking.

**Independent Test**: Book a next-day run. Read the monitor. The `SelfReschedule` entry has the next-day instant.

**Acceptance Scenarios**:

1. **Given** a step that booked a time of day that has passed, **When** the operator reads the monitor, **Then** the `SelfReschedule` entry shows that time on the next day and not the current moment.

### Edge Cases

- The time of day is 00:00:00 and the step runs at 00:00:00 or later: the booking is 00:00:00 on the next day.
- The time of day is 23:59:59 and the step runs at 23:59:59: the booking is 23:59:59 on the next day.
- The local clock changes between today and the next day (daylight saving time): the booking uses the local offset of the next day, so the run occurs at the same clock time.
- The next-day clock time is in the gap when the local clock goes forward: the booking uses the standard offset of the local time zone for that clock time.
- A second `reschedule-self` Timer step of the same sequence in the same run replaces the earlier booking, as before.
- The step has `ocrOffset`: the step uses the relative offset from the screen, as before. The time-of-day rule does not apply.
- The queue run stops before the next day: the booking is lost with the run, as before. Bookings apply to the current run only.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: When a `reschedule-self` step with `option: Timer` and `timerTimeOfDay` runs, and the time of day is ahead of the current local time today, the step MUST book the run for today at that time, as before.
- **FR-002**: When the time of day is not ahead of the current local time (equal or earlier), the step MUST book the run for that time on the next local day.
- **FR-003**: The step MUST NOT book a time-of-day run for the current moment when the time of day has passed.
- **FR-004**: The UTC offset of a booked instant MUST be the offset of the service-local time zone for that local date and time.
- **FR-005**: `GET /api/queues/{id}/monitor` MUST show the `SelfReschedule` entry at the booked instant, which includes the next-day instant.
- **FR-006**: The behavior of `timerRelativeOffset`, `ocrOffset`, the options `AtQueueStart`, `OncePerRun` and `EveryStep`, and the template `Timer` entry MUST NOT change.
- **FR-007**: The accepted `timerTimeOfDay` formats and the validation of unknown payload fields MUST NOT change (issues #226 and #228).
- **FR-008**: The OpenAPI description of the `reschedule-self` payload and the changelog MUST tell that a time of day that has passed books the next day.

### Key Entities

- **Self-reschedule Timer booking**: one pending run of a sequence in the current queue run, with the instant when it is due.
- **Time of day**: a local clock time (hours, minutes, seconds) without a date.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: In an automated test with the probe values from the issue, a step at 14:55:41 with time of day 14:55 books `2026-09-25T14:55:00+02:00`.
- **SC-002**: In an automated test, a step at 14:52:11 with time of day 14:55 books `2026-09-24T14:55:00+02:00`, as before.
- **SC-003**: In an automated test, a sequence that books a time of day that has passed does not run again on the same day in the queue run.
- **SC-004**: In an automated test, the monitor shows the `SelfReschedule` entry at the next-day instant.
- **SC-005**: All existing tests for the other options, for `timerRelativeOffset` and for the template `Timer` entry pass with no change to their expected values.

## Assumptions

- "Today" and "next day" are local dates of the service-local time zone, as for the template `Timer` entry.
- The booking applies to the current queue run only, as before. The fix does not keep bookings across a stop and start of the queue.
- The run loop fires a booking when the current instant is equal to or after the booked instant. This does not change.
