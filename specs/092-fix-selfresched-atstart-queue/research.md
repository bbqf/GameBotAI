# Research: Self-reschedule bookings keep an AtQueueStart-only queue running

## R-001 Root cause

- **Decision**: The defect is the loop-entry gate in `QueueExecutionService.RunAsync`:
  `if (oncePerRunEntries.Count > 0 || everyStepEntries.Count > 0 || timerEntries.Count > 0)`.
  An at-queue-start-only template fails this gate and falls into the empty-template `else` branch. That branch ends the run as "completed full run" without ever draining the handle's self-reschedule registers.
- **Rationale**: The issue's control case matches this exactly. Adding any timer entry makes the gate true, and the in-loop `HasPendingRelativeOrLive()` already includes `handle.HasPendingTimerFirings`, so the run stays alive and later fires the bookings (a4 drain). The self-reschedule coordinator, the booking store and the monitor projection all behave correctly: the monitor listed the bookings.
- **Alternatives considered**: Changing the self-reschedule coordinator to convert bookings into template entries was rejected: it is a larger change, and bookings are ephemeral by design. Always entering the loop was rejected too: an empty or at-queue-start-only template without bookings would then take a cycling loop with no work and busy-spin (breaking FR-006/FR-007 and the `CycleWithEmptyTemplateCompletesWithoutBusyLoop` guarantee).

## R-002 Which pending registers open the gate

- **Decision**: A pending Timer firing (`HasPendingTimerFirings`), a non-empty `PendingNextCycleStart`, a non-empty `PendingOncePerRun` or a non-empty `PendingLiveSchedules` opens the gate. `EveryStepInjections` does not.
- **Rationale**: These three registers are drained only inside the loop (a4, a0 and the once-per-run drain). Every-step injections are already executed by `RunEveryStepPassAsync`, which the pre-pass calls after every at-queue-start firing. Inside the loop they never keep a run alive either, so counting them would only add a pointless extra iteration. It would also turn a cycling run into a spin loop just because an every-step injection was registered.
- **Alternatives considered**: Including `PendingLiveSchedules` was considered. Live schedules are posted by an operator through the API against a running queue, and in an at-queue-start-only run the run could end before any are posted. Including a live schedule that arrived during the pre-pass is consistent and harmless, so it is included too (it is part of `HasPendingRelativeOrLive`'s keep-alive set). Final set: timer firings, next-cycle-start, once-per-run and live schedules.

## R-003 Non-cycling vs cycling inside the loop

- **Decision**: No loop-body change.
- **Rationale**:
  - **Non-cycling**: the first iteration runs a0 (next-cycle-start), a4 (only due timers), the once-per-run pass (empty) plus the once-per-run booking drain, then counts the cycle. After that, `HasPendingRelativeOrLive()` keeps the run alive while a Timer booking is pending, and idle-pause or the poll handles the wait. Once no booking remains, the run breaks and completes.
  - **Cycling**: the loop continues immediately, like today's timer-only cycling template. That spin is #200 and out of scope (spec clarification).
- **Alternatives considered**: Adding a poll delay for cycling runs with no template work was rejected as #200's scope.

## R-004 Cycle count / summary for booking-driven runs

- **Decision**: Accept the loop's normal cycle counting. A booking-driven run counts cycles like the mixed-template control case. A run without bookings still takes the `else` branch (`cycles = 1`), so its summary is unchanged.
- **Rationale**: FR-006 only protects the no-booking case. The booking case is required to behave like the control case (FR-005).

## R-005 Test approach

- **Decision**: Unit tests in `QueueExecutionServiceTests` using the existing `Harness` with `FakeTimeProvider`. A fake sequence runner (the harness's scripted runner) calls `Coordinator` to book a Timer self-reschedule on each firing. Advance the clock past the offset and assert the firing count and that the run is still running. Stop the run and assert the stop reason. Cover cycle execution both off and on, several generations, and a no-booking regression (completes immediately, summary unchanged).
- **Rationale**: This is deterministic and fast, and it exercises the real run loop (constitution II). The existing live-schedule and relative-timer tests already use this pattern.
