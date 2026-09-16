# Feature Specification: Self-reschedule bookings keep an AtQueueStart-only queue running

**Feature Branch**: `092-fix-selfresched-atstart-queue`  
**Created**: 2026-09-16  
**Status**: Draft  
**Input**: GitHub issue #198 (https://github.com/bbqf/GameBotAI/issues/198) — B-014: a queue template with only AtQueueStart entries ignores pending reschedule-self bookings and the queue ends as 'completed'. Closes #198.

## Background

A queue whose template contains **only** at-queue-start entries runs those entries once, then ends its run as "completed full run", even though the sequences it just ran booked themselves again with a `reschedule-self` step. The run log reports every booking as successful and the queue monitor lists the bookings as upcoming (`scheduleKind: "SelfReschedule"`). Seconds later the queue is `Stopped` and neither booking ever runs. This happens whether cycle execution is on or off.

Control case: the same sequences inside a template that also holds timer entries keep the queue running on their own bookings indefinitely, including re-running the at-queue-start entry from its own booking.

Impact: the natural "run at every start, then reschedule yourself" roster quietly becomes a one-shot run, and nothing reports an error. The current workaround (a zero-offset timer instead of at-queue-start) is unverified.

## Clarifications

### Session 2026-09-16

- Q: Should a cycling at-queue-start-only queue that now enters the scheduling loop get its own anti-spin delay? → A: No. It inherits the cycling loop's existing behaviour, as a timer-only cycling template does today. The spin rate is #200's scope, and fixing it in only one template shape would diverge the two cases. (Auto-resolved: the issue's non-goals exclude #200.)
- Q: Which pending self-reschedule registers count as "pending work" that makes the run enter the scheduling loop? → A: Timer bookings, next-cycle-start bookings and once-per-run bookings. An operator live schedule already pending when the pass ends also counts, as it does in the loop's existing keep-alive set. Every-step injections do not count, because they already run after every firing and every-step work never keeps a run alive. (Auto-resolved: matches the loop's existing keep-alive semantics.)
- Q: Is new logging or monitor output required? → A: No. The existing per-firing log entries and monitor projection already cover booked firings. (Auto-resolved: the issue reports those as already correct.)

## User Scenarios & Testing *(mandatory)*

### User Story 1 - "Run at start, then reschedule yourself" roster runs indefinitely (Priority: P1)

A bot author builds a queue template in which every entry runs at queue start. Each sequence's first step books the same sequence again after a delay. When the queue starts, each sequence runs once. The queue then stays running, runs each booking when it comes due, and keeps going as long as the sequences keep booking themselves.

**Why this priority**: This is the defect. Without it a production queue stops after its first pass with no error.

**Independent Test**: Start a non-cycling queue whose template holds only at-queue-start entries that book themselves for a short delay. Advance time past the delay. Check that each booked sequence runs again and the queue is still running.

**Acceptance Scenarios**:

1. **Given** a non-cycling queue with only at-queue-start entries whose sequences book themselves for a timer offset, **When** the queue starts and the at-queue-start pass finishes, **Then** the queue stays running while those bookings are pending and does not record "completed full run".
2. **Given** that queue with a pending booking, **When** the booked time arrives, **Then** the booked sequence runs, and it counts toward the run's executed total.
3. **Given** a booked firing that books itself again, **When** it runs, **Then** the queue stays running for the new booking (repeatable indefinitely).
4. **Given** the same template with cycle execution enabled, **When** the queue starts, **Then** the bookings are likewise honoured and fire when due.

---

### User Story 2 - Waiting on a booking behaves like the mixed-template case (Priority: P2)

While an at-queue-start-only queue waits for its next booking, it behaves exactly like a queue whose template also has timer entries. In particular, idle-pause (when enabled) backs the game out for a long gap and brings it back when the booking is due, and the monitor shows the booking as upcoming.

**Why this priority**: Operators rely on idle-pause to keep the emulator quiet between firings. The fix must not create a second, different wait mode.

**Independent Test**: Start an at-queue-start-only queue with idle-pause enabled and a booking further out than the idle threshold. Check that the run enters the same idle-pause hold used for other pending firings, and fires the booking afterwards.

**Acceptance Scenarios**:

1. **Given** idle-pause is enabled and the next booking is further out than the idle threshold, **When** the queue is waiting, **Then** it uses the same idle-pause wait as any other pending firing.
2. **Given** the queue is waiting on a booking, **When** an operator stops the queue, **Then** it stops promptly and records a manual stop, as for any other waiting queue.

---

### User Story 3 - Unchanged completion when nothing is booked (Priority: P3)

A queue whose template has only at-queue-start entries, none of which book anything, still finishes its run immediately after the at-queue-start pass, with the same summary as today. The other template shapes (timers, once-per-run, every-step, empty) keep their current completion behaviour.

**Why this priority**: This is a regression guard. The fix must extend the run only when there is real pending work.

**Independent Test**: Start an at-queue-start-only queue with no reschedule steps. Check that it completes at once with "completed full run" and the same executed count.

**Acceptance Scenarios**:

1. **Given** an at-queue-start-only template with no self-reschedule steps, **When** the queue starts, **Then** it runs each entry once and ends as "completed full run".
2. **Given** an empty template, **When** the queue starts (cycling or not), **Then** its behaviour is unchanged (one empty cycle, no busy loop).

### Edge Cases

- **Non-timer bookings**: a sequence books itself with a non-timer option (next cycle start, once-per-run, every step) from an at-queue-start-only template. Any booking still pending when the at-queue-start pass ends must be honoured the same way the mixed-template case honours it. It must not be silently dropped.
- **Every-step injections**: an every-step injection is the only thing registered after the pass. It runs in the every-step passes that already follow each firing. On its own it does not keep a non-cycling run alive, which is today's rule for every-step work.
- **Failed booked firing**: a booked firing that fails is non-fatal. It is counted as failed and the run continues, as for other firings.
- **Booking that never books again**: once its last booking has fired and nothing else is pending, a non-cycling run ends as "completed full run".
- **Cycling with only bookings**: with cycle execution on, the run loops the same way as a cycling timer-only template. The empty-cycle spin rate of that loop is tracked separately (#200) and is out of scope.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: A queue run MUST continue past its at-queue-start pass whenever self-reschedule work is pending, whatever schedule types the template contains. Pending work means a pending timer booking, a next-cycle-start booking, a once-per-run booking, or an operator live schedule that is already pending.
- **FR-002**: A pending self-reschedule booking in an at-queue-start-only run MUST fire when due. It counts toward the executed total and a failure is non-fatal, exactly as the same booking does in a template that also has timer entries.
- **FR-003**: A booked firing that books itself again MUST keep the run alive for the new booking, so the roster can repeat indefinitely until an operator stops it.
- **FR-004**: FR-001 to FR-003 MUST hold with cycle execution both on and off.
- **FR-005**: While waiting for a booking, the run MUST use the existing wait behaviour for pending firings: idle-pause when enabled and the gap exceeds the threshold, otherwise the regular poll. It MUST remain promptly stoppable.
- **FR-006**: A run whose template contains only at-queue-start entries and has no pending self-reschedule work after the pass MUST end as it does today: "completed full run", with the same executed and failed counts and the same cycle count.
- **FR-007**: Completion behaviour for every other template shape MUST NOT change. That covers empty templates and templates with timer, once-per-run or every-step entries.
- **FR-008**: An automated test MUST reproduce the reported case: an at-queue-start-only template whose sequence books itself with a timer offset, checked both with cycle execution on and with it off. The test MUST fail before the fix and pass after it.

### Key Entities

- **Queue template entry**: a sequence plus its schedule type (at queue start, once per run, every step, timer). The template shape is fixed when the run starts.
- **Self-reschedule booking**: an ephemeral, run-scoped firing that a sequence registers for itself. It has an option (timer / next cycle / once-per-run / every step) and, for timers, a fire time. It is never persisted and is discarded when the run ends.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: In the reported scenario (three at-queue-start entries, each booking itself +90 s), 100% of the bookings fire at their booked time instead of 0%, and the queue is still running afterwards.
- **SC-002**: A self-rebooking at-queue-start-only queue completes at least 3 successive booking generations in an automated test, for both cycle-execution settings.
- **SC-003**: The existing queue-execution test suite passes unchanged. No completion or summary regression for templates without bookings.

## Assumptions

- The fix belongs in the queue run's decision about whether to enter its scheduling loop. The self-reschedule step, the booking store and the monitor projection already behave correctly: the monitor lists the bookings.
- A pending every-step injection on its own does not extend a non-cycling run. This matches today's rule, where every-step work never keeps a run alive.
- Out of scope: the empty-cycle spin rate for cycling queues with only scheduled work (#200), `health.paused` reporting (#199), new schedule types (#202), restart on service start (#203), and verifying the zero-offset timer workaround.
