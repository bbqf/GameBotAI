# Feature Specification: A cycling queue with nothing due waits instead of spinning empty cycles

**Feature Branch**: `093-fix-empty-cycle-spin`  
**Created**: 2026-09-17  
**Status**: Draft  
**Input**: GitHub issue #200 (https://github.com/bbqf/GameBotAI/issues/200) — B-016: cycleExecution:true with only scheduled entries spins ~450,000 empty cycles/s and never idle-pauses. Closes #200.

## Background

A queue with cycle execution on, whose template holds only scheduled entries (at-queue-start and timer entries), spins empty cycles as fast as the service can go, and the idle pause never starts.

Reproduction from the issue: a throwaway queue on `emulator-5558` with `pauseWhenIdle:true` and `idleThresholdSeconds:30`. Its entries are one at-queue-start entry (3 s wait), `Timer +00:01:00` and `Timer +00:02:30`. After the start, `health.cyclesCompleted` went from 4,544,546 (11 s) to 41,088,589 (~89 s). That is about 450,000 cycles a second, with `lastCycleStartedAt` and `lastCycleCompletedAt` about a microsecond apart. `/monitor` never showed `IdlePause`. The timers still fired on time.

Impact: one wrong flag turns a scheduled production queue into a CPU hot loop on the host that runs every other queue. It also makes the `cyclesCompleted` health signal (#180) meaningless for that queue. The workaround in force is `cycleExecution:false` on every scheduled production queue.

## Clarifications

### Session 2026-09-17

- Q: What makes a loop iteration an "empty cycle"? → A: An iteration in which no sequence ran at all: no timer, retry, live-schedule, self-reschedule, once-per-run or every-step firing. A cycling template with once-per-run or every-step entries always runs something, so its cycles are never empty and keep today's behaviour. (Auto-resolved: "nothing executed" is the issue's own wording, and it is the only definition that leaves every working cycling roster untouched.)
- Q: What does a cycling queue do when nothing at all is pending (no timers, bookings or live schedules)? → A: It stays running and waits at the regular poll interval, so a live schedule added later can still fire. It does not end the run. (Auto-resolved: a cycling run ends only when an operator stops it today, and changing that is outside the issue.)
- Q: Should `cycleExecution:true` be rejected at the API for templates without once-per-run entries? → A: No. The runtime fix makes the flag harmless for those templates, so rejecting it would only break stored queues. (Auto-resolved: the issue names rejection only as a fallback, and the feature description says not to reject unless the runtime fix proves infeasible.)

## User Scenarios & Testing *(mandatory)*

### User Story 1 - A cycling scheduled-only queue sits quietly between firings (Priority: P1)

A bot author turns on cycle execution for a queue whose template holds only at-queue-start and timer entries. Between firings the queue waits for the next due firing. It does not busy-loop, and it does not count cycles while nothing runs.

**Why this priority**: This is the defect. Without it one flag turns a production queue into a CPU hot loop that starves every other queue on the host.

**Independent Test**: Start a cycling queue whose template holds only an at-queue-start entry and a relative timer entry, on a controllable clock. Before the timer is due, check that the loop does not iterate continuously and the completed-cycle count does not grow. Advance past the timer's offset and check that it fires.

**Acceptance Scenarios**:

1. **Given** a cycling queue with only at-queue-start and timer entries, **When** the at-queue-start pass has finished and no timer is due, **Then** the run waits and the completed-cycle count stays unchanged.
2. **Given** that queue waiting, **When** a timer entry comes due, **Then** it fires on time and the iteration that fired it counts as one completed cycle.
3. **Given** that queue waiting, **When** an operator stops it, **Then** it stops promptly and records a manual stop.

---

### User Story 2 - The idle pause applies to a cycling queue that is waiting (Priority: P1)

The queue opts in to idle pause, and the gap to its next firing is longer than the idle threshold. While it waits, the queue backs the game out and shows `IdlePause` in the monitor, exactly as a non-cycling scheduled queue does.

**Why this priority**: Operators rely on idle pause to keep the emulator quiet between firings. It is the second observed failure in the issue.

**Independent Test**: Start a cycling scheduled-only queue with idle pause enabled and a next firing beyond the threshold. Check that the run enters the idle-pause hold and the monitor reports it, then advance the clock and check that the firing runs.

**Acceptance Scenarios**:

1. **Given** idle pause is enabled and the next firing is further out than the threshold, **When** the cycling queue is waiting, **Then** it enters the idle-pause hold and the monitor reports the idle pause with the resume time.
2. **Given** idle pause is disabled, or the next firing is within the threshold, **When** the cycling queue is waiting, **Then** it waits at the regular poll interval, as a non-cycling queue does.

---

### User Story 3 - Cycling rosters that do real work each cycle are unchanged (Priority: P2)

A cycling queue with once-per-run or every-step entries keeps looping straight into its next cycle, and counts a cycle per pass, exactly as today.

**Why this priority**: This is a regression guard for the production cycling rosters.

**Independent Test**: Run the existing cycling-queue tests unchanged, plus a cycling queue with a once-per-run entry: check that it completes successive cycles with no wait between them.

**Acceptance Scenarios**:

1. **Given** a cycling queue with a once-per-run entry, **When** it runs, **Then** each pass counts one cycle and the next pass starts without waiting.
2. **Given** a cycling queue with only every-step entries, **When** it runs, **Then** each pass runs the every-step entries and counts one cycle, as today.

### Edge Cases

- **Nothing pending at all**: a cycling queue whose at-queue-start entries booked nothing and which has no timers or live schedules stays running. It waits at the regular poll interval and counts no cycles. It never spins.
- **Empty template**: an empty template keeps its current one-empty-cycle behaviour, cycling or not.
- **Pending self-reschedule work**: queued next-cycle-start or once-per-run bookings are due at once. The iteration that drains them runs sequences, so it is not an empty cycle.
- **Daily retry armed**: an armed retry of a failed time-of-day firing counts as pending. The wait wakes when the retry is due.
- **Failure-policy pause**: the failure-policy pause gate still applies at the top of each iteration. A wait never skips it.
- **Cycle timestamps**: waiting does not stretch the next published cycle's start time back to when the wait began. A published cycle starts at the iteration that actually ran work.
- **Non-cycling queues**: completion and cycle counting for non-cycling runs do not change.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: A cycling queue run MUST NOT start its next loop iteration at once after an iteration in which no sequence ran. It MUST wait for the next due firing instead.
- **FR-002**: An iteration in which no sequence ran MUST NOT count as a completed cycle. It MUST NOT be published to the cycle ledger or health, and it MUST NOT trigger the queue's failure-policy evaluation.
- **FR-003**: While waiting, a cycling run MUST use the same wait behaviour as a non-cycling run with pending firings. If idle pause is enabled and the gap to the next firing is longer than the idle threshold, it enters the idle-pause hold, which the monitor reports. Otherwise it waits one regular poll interval.
- **FR-004**: When nothing is pending at all, a cycling run MUST stay running and wait at the regular poll interval. It MUST NOT end the run and MUST NOT spin.
- **FR-005**: Timer (time-of-day and relative), daily-retry, live-schedule, at-queue-start and self-reschedule firings MUST still fire when due in a cycling run. The iteration that fires them counts as one completed cycle.
- **FR-006**: A cycling run whose iterations run sequences (for example a template with once-per-run or every-step entries) MUST keep today's behaviour: one completed cycle per pass and no wait between passes.
- **FR-007**: A published cycle's start time MUST NOT include time spent waiting in earlier empty iterations.
- **FR-008**: A waiting cycling run MUST stay promptly stoppable, and it MUST still honour the failure-policy pause gate.
- **FR-009**: Behaviour for non-cycling runs and for empty templates MUST NOT change.
- **FR-010**: Automated tests MUST reproduce the reported case: a cycling template with only at-queue-start and timer entries. They MUST show that the cycle count does not grow while nothing is due, that the timer still fires, and that idle pause is entered when enabled. The tests MUST fail before the fix and pass after it.

### Key Entities

- **Loop iteration**: one pass of the queue run's scheduling loop. It evaluates due firings and, for a cycling run, the once-per-run pass.
- **Completed cycle**: a loop iteration counted in the run summary and published in queue health (`cyclesCompleted`, `lastCycleStartedAt`/`lastCycleCompletedAt`) and the cycle history.
- **Next due firing**: the earliest pending firing across template timers, daily retries, live schedules and self-reschedule bookings. Waiting and idle pause are timed against it.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: In the reported scenario, `cyclesCompleted` grows by at most the number of iterations that actually ran a firing, instead of about 450,000 a second.
- **SC-002**: A waiting cycling scheduled-only queue makes at most about 4 loop wake-ups a second (the regular poll interval), or none beyond the idle-pause hold's own polling, instead of running continuously.
- **SC-003**: With idle pause enabled and a next firing beyond the threshold, the monitor reports the idle pause for a cycling queue in 100% of test runs.
- **SC-004**: The existing queue-execution, cycle-ledger, failure-policy and monitor test suites pass unchanged.

## Assumptions

- The fix belongs in the queue run's loop decision after an iteration: whether to continue at once or wait. The idle-pause hold, the next-due projection and the monitor already behave correctly for non-cycling runs, and they are reused.
- "No sequence ran in the iteration" is observable inside the loop without new persisted state.
- Out of scope: `health.paused` reporting (#199), new schedule types (#202), restart on service start (#203), rejecting `cycleExecution:true` at the API, and OpenAPI changes.
