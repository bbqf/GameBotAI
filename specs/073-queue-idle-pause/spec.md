# Feature Specification: Idle-Pause the Game During Queue Gaps

**Feature Branch**: `073-queue-idle-pause`
**Created**: 2026-07-23
**Status**: Draft
**Input**: User description: "Idle-pause the game during queue gaps, visibly. When a running queue has no sequence due, back the game out to the device home screen instead of leaving it running, and bring it back exactly when the next sequence is due — with the pause clearly visible in the live monitor so an idle queue is never mistaken for a hung one."

## Clarifications

### Session 2026-07-23

- Q: Where should the per-queue idle-pause setting (enabled + threshold) be stored? → A: On the queue entity, alongside the existing `cycleExecution` flag (set via the queue update API/UI).
- Q: Should idle pauses be recorded in the execution log, or shown only live in the monitor? → A: Monitor-only — a live, transient run state (current-item + resume time); no execution-log entries written.
- Q: What happens to the superseded "PNS Queue Pause 15m" sequence? → A: Remove its entry from the queue template during rollout; keep the sequence definition in the library (no destructive delete).

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Game is backed out while the queue waits (Priority: P1)

An operator runs the daily automation queue. Between scheduled tasks there are gaps of anywhere from a few seconds to tens of minutes. Today the game is left in the foreground doing nothing during those gaps. The operator wants the game backed out to the device home screen whenever the wait until the next task exceeds a short threshold, and brought back to the foreground the moment the next task is due, so the game is never left running idle for more than that threshold.

**Why this priority**: This is the core value — it is the behavior the operator explicitly asked for. Without it, nothing else in the feature matters.

**Independent Test**: Start a queue whose next task is more than the threshold away. Observe that the game is backed to the home screen within one polling interval, and that it is returned to the foreground and the task runs at (approximately) the scheduled time. Fully delivers value on its own.

**Acceptance Scenarios**:

1. **Given** a running queue with idle-pause enabled and the next scheduled firing more than the threshold away, **When** the queue finishes the current task and becomes idle, **Then** the game is backed out to the device home screen within one polling interval.
2. **Given** the game has been backed out during an idle gap, **When** the next scheduled firing becomes due, **Then** the game is returned to the foreground and the due sequence runs, delayed by no more than a small margin beyond its scheduled time.
3. **Given** a running queue with the next scheduled firing at or under the threshold away, **When** the queue becomes idle, **Then** the game is NOT backed out (short gaps are left alone).

---

### User Story 2 - The pause is visible in the live monitor (Priority: P1)

While the game is backed out, the operator watches the live queue monitor. Instead of a frozen/blank "nothing happening" state (which previously looked identical to a hung queue), the monitor clearly shows the queue is intentionally paused and, ideally, when it will resume.

**Why this priority**: The operator's stated goal is transparency — an idle queue must never be mistaken for a hung one. A pause that is invisible would recreate the exact confusion this feature exists to remove.

**Independent Test**: With the game backed out during a gap, open the monitor and confirm it shows an explicit "idle pause" state as the active item, distinct from both a running sequence and a stopped queue. Confirm the expected resume time is shown.

**Acceptance Scenarios**:

1. **Given** the queue is in an idle pause, **When** the operator views the live monitor, **Then** the monitor shows an explicit idle-pause state as the currently-active item for the full duration of the pause.
2. **Given** the queue is in an idle pause, **When** the operator views the live monitor, **Then** the expected resume time (the next scheduled firing) is shown.
3. **Given** the idle pause ends because a task became due, **When** that task starts, **Then** the monitor transitions from the idle-pause state to showing the running task.

---

### User Story 3 - An earlier-arriving schedule still wins (Priority: P2)

During an idle pause, a live/ad-hoc schedule or a self-reschedule fires for a time earlier than the gap the pause was sized to. The pause must yield to it promptly rather than waiting out the originally-computed gap.

**Why this priority**: Correctness guard. Without it, the pause could swallow a task that becomes due earlier than expected, delaying work the operator scheduled on demand.

**Independent Test**: Enter an idle pause with a distant next firing, then inject a live schedule due within a few seconds. Confirm the game is foregrounded and the injected sequence runs at (approximately) its new due time, not at the original distant time.

**Acceptance Scenarios**:

1. **Given** the queue is in an idle pause sized to a distant firing, **When** an earlier live schedule or self-reschedule becomes the soonest due firing, **Then** the pause ends and that sequence runs, delayed by no more than a small margin beyond its new due time.

---

### User Story 4 - Opt-in per queue (Priority: P2)

An operator with multiple queues wants idle-pausing only on the queue where it makes sense. Queues that do not opt in behave exactly as they do today (game left as-is during gaps).

**Why this priority**: Prevents a silent behavior change to existing queues. Important for safety but not required to demonstrate the core value on the one queue that wants it.

**Independent Test**: Run one queue with idle-pause enabled and one without, each with an idle gap. Confirm only the enabled queue backs the game out.

**Acceptance Scenarios**:

1. **Given** a queue with idle-pause disabled (default), **When** it becomes idle with a long gap, **Then** the game is left untouched and no pause state is shown.
2. **Given** a queue with idle-pause enabled, **When** it becomes idle with a gap over the threshold, **Then** the pause behavior of User Story 1 occurs.

---

### Edge Cases

- **Watchdog interaction**: The idle pause may last far longer than the per-sequence watchdog timeout (e.g. a 25-minute gap vs. a 4-minute watchdog). The pause MUST NOT be cancelled or reported as a failure by that watchdog — it is the scheduler intentionally waiting, not a stuck sequence.
- **Rapid churn**: When gaps are consistently just over the threshold, the game may background/foreground every couple of minutes. This is acceptable and MUST NOT be suppressed or rate-limited beyond the threshold itself.
- **Stop during pause**: If the operator stops the queue while it is idle-paused, the queue MUST stop promptly (no waiting out the gap) and MUST NOT leave the game stranded on the home screen in a way that a normal stop would not.
- **Backgrounding fails**: If backing the game out fails, the queue MUST continue to run scheduled tasks normally (the pause is best-effort; a failed background is non-fatal and does not stop the run).
- **Foregrounding fails at resume**: If bringing the game back fails, the due sequence still runs (its own recovery/connect steps handle a game that is not in front); a failed foreground is non-fatal.
- **No next firing at all**: If the queue is genuinely out of pending work (nothing will ever become due), the run ends as it does today rather than idle-pausing forever.
- **Gap shrinks below threshold after entering pause**: Once paused, the pause simply ends when the next firing is due; there is no separate "unpause because the gap got shorter" path beyond normal resume.
- **Cycling queues**: A cycling run that loops with no waiting has no idle gap to detect; idle-pause applies where the run actually waits between firings. This feature targets that waiting state.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: While a queue run is idle (no sequence currently executing and none due), the system MUST compute the time until the next scheduled firing across all pending schedule sources (time-of-day timers, relative-offset timers, self-reschedule timer firings, live/ad-hoc schedules, and any pending next-cycle/once-per-run work).
- **FR-002**: When idle-pause is enabled for the queue and the computed gap to the next firing exceeds the configured threshold, the system MUST back the game out to the device home screen.
- **FR-003**: The system MUST keep the game backed out for the duration of the idle gap, re-evaluating the next-due firing on each polling tick so that an earlier-arriving firing shortens the pause.
- **FR-004**: When the next firing becomes due, the system MUST return the game to the foreground before running the due sequence.
- **FR-005**: When a firing becomes due during a pause, the runtime MUST detect it and begin resuming within one polling interval of the due time. The only additional delay before the sequence runs MUST be the best-effort time to bring the game to the foreground (game resume/relaunch is inherently non-instant). Idle-pause MUST NOT otherwise delay, skip, or reorder scheduled work.
- **FR-006**: The idle-pause wait MUST be exempt from the per-sequence watchdog; a pause longer than the watchdog timeout MUST NOT be cancelled or recorded as a sequence failure.
- **FR-007**: The live monitor MUST display an explicit idle-pause state as the active item for the full duration of the pause, visually distinct from a running sequence and from a stopped queue.
- **FR-007a**: Idle pauses MUST be surfaced only as transient live monitor state; the system MUST NOT write execution-log entries for entering, holding, or leaving an idle pause (no historical log volume added).
- **FR-008**: The monitor's idle-pause display MUST include the expected resume time (the next scheduled firing).
- **FR-009**: Idle-pause MUST be opt-in per queue; queues that have not enabled it MUST behave exactly as before (game untouched during gaps, no pause state shown).
- **FR-010**: The idle threshold MUST default to 30 seconds and MUST be configurable per queue.
- **FR-011**: Backing the game out and bringing it back MUST be best-effort: a failure of either MUST be non-fatal and MUST NOT stop the run or prevent scheduled tasks from executing.
- **FR-012**: Stopping the queue while idle-paused MUST take effect promptly, without waiting out the remaining gap.
- **FR-013**: Idle-pause MUST NOT change which sequences run, their order, or their scheduled times; it only governs whether the game is foregrounded and what the monitor shows between firings.
- **FR-014**: When a gap to the next firing is at or below the threshold, the system MUST NOT back the game out (short gaps are left running).
- **FR-015**: The previously-used fixed-duration "manual pause" sequence approach is superseded by this feature; the system MUST NOT rely on a foreground/background pause implemented as an ordinary scheduled sequence subject to the watchdog.
- **FR-016**: Rollout MUST remove the superseded "PNS Queue Pause 15m" entry from the production queue template; the sequence definition itself is retained in the library (no destructive delete).

### Key Entities *(include if feature involves data)*

- **Idle-pause setting (per queue)**: Whether idle-pausing is enabled for a queue, and the idle threshold (default 30 seconds) that determines the minimum gap that triggers a pause. Stored on the queue entity alongside the existing per-run behavioral toggle (`cycleExecution`), configured via the queue update API/UI.
- **Idle-pause state (per run, transient)**: The runtime condition of a run that is currently backed out and waiting, including the expected resume time. Surfaced to the monitor; not persisted.
- **Next-due firing**: The soonest upcoming scheduled firing across all schedule sources, used both to decide whether to pause and to size/resume the pause.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: On a queue with idle-pause enabled, the game is never left running idle for more than the configured threshold plus one polling interval.
- **SC-002**: When a firing becomes due during an idle pause, the runtime decides to resume within one polling interval of the due time; the scheduled sequence then runs after the game is brought back to the foreground (the game resume/relaunch time is additional and expected, not counted against this bound). No firing is skipped or reordered by idle-pause.
- **SC-003**: For 100% of idle gaps exceeding the threshold, the live monitor shows an explicit idle-pause state with a resume time — an operator viewing the monitor during a gap can always tell the queue is intentionally paused rather than hung.
- **SC-004**: Queues with idle-pause disabled show zero behavioral change versus today (game state during gaps and monitor display are identical to prior behavior).
- **SC-005**: No idle pause is ever cancelled or reported as a failure by the per-sequence watchdog, regardless of gap length.
- **SC-006**: Stopping a queue during an idle pause completes within one polling interval.
- **SC-007**: Idle-pausing adds zero execution-log entries — the number of log entries for a run is unchanged versus the same run without idle-pause, aside from the scheduled sequences themselves.

## Assumptions

- The "device home screen" background action and the "bring game to foreground" action are the existing primitive behaviors already used elsewhere in the system; this feature orchestrates them from the scheduler rather than defining new device actions.
- The primary target is the existing non-cyclic serial queue run, which waits between firings. Cycling runs that loop without waiting have no idle gap and are unaffected except where they, too, wait for a pending firing.
- "Small margin" / "one polling interval" refers to the existing idle re-check cadence of the run loop; this feature does not require a faster cadence.
- The monitor already tracks a notion of the currently-active item per run; the idle-pause state is surfaced through that same mechanism.
- Only one queue currently uses this behavior in production, but the setting is defined generally so any queue can opt in.
