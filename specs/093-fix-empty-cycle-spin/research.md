# Research: A cycling queue with nothing due waits instead of spinning empty cycles

## R-001 — Root cause

**Finding**: In `QueueExecutionService.RunAsync`, a cycling run always runs the once-per-run block (`if (queue.CycleExecution || !schedule.OncePerRunPassDone)`). That block counts a cycle and seals it in the ledger even when it ran nothing. The loop then ends with `if (queue.CycleExecution) continue;`, which skips the idle-pause and poll wait that sits below it. A template holding only at-queue-start and timer entries therefore loops with no awaited delay: every `await` in the iteration completes synchronously. `cyclesCompleted` climbs by about 450k a second, and `IdlePauseHoldAsync` is unreachable.

**Why timers still fired on time**: every iteration re-evaluates timers against the clock, so the spin polls them continuously.

## R-002 — How to detect "no sequence ran in this iteration"

**Decision**: Compare the run-local firing counter `index` before and after the firing evaluation.

**Rationale**: Every `RunOneSequenceAsync` call site uses `++index`. That covers the at-queue-start pass, next-cycle bookings, time-of-day timers, daily retries, relative timers, live schedules, self-reschedule timers, once-per-run entries, every-step passes, every-step injections and once-per-run bookings. The counter is therefore an exact "a sequence ran" signal with no new state. `executed` is not usable, because time-of-day timers and every-step firings do not count toward it.

**Alternatives considered**:
- A boolean set at each call site: it duplicates `index` and is easy to miss when a new firing source is added.
- Deciding up front from the template shape (no once-per-run or every-step entries → "scheduled-only"): a scheduled-only cycling queue would still spin when a booking or live schedule is queued but not due, and the decision would need its own keep-alive rules. The per-iteration signal handles every shape uniformly.

## R-003 — Whether the once-per-run block should run in an empty cycling iteration

**Decision**: Skip the block's bookkeeping (`BeginCycle`, `cycles++`, `CompleteOpen`, failure policy) when all of these hold: the run is cycling, nothing ran earlier in the iteration, and the block would itself run nothing (no once-per-run entries, no every-step entries, and `handle.PendingOncePerRun` empty). The block still runs whenever it has work.

**Rationale**: FR-002 says empty iterations are not cycles. The failure policy evaluates only sealed cycles, so a skipped empty cycle cannot affect its consecutive-failure counts, which stay correct. `MarkOncePerRunPassDone` is idempotent, and `Cycling` makes `RemainingOncePerRun` ignore it, so skipping it changes nothing for the monitor.

An iteration in which a timer fired but no once-per-run entry exists still runs the block and counts one cycle (FR-005). The ledger records that firing, and the cycle's outcome reflects it.

## R-004 — What happens to the open ledger cycle of an empty iteration

**Decision**: Add `QueueCycleLedger.DiscardOpenIfEmpty()`, which sets `_open = null` only when the open cycle has no recorded entries. Call it when an empty iteration is detected.

**Rationale**: `EnsureOpen` is idempotent. Without the discard, the next cycle that actually runs would reuse a cycle opened when the wait began, and its `StartedAt` would include the whole idle gap (FR-007). The helper is a void observer mutator, consistent with the ledger's documented contract.

**Alternatives considered**: moving `EnsureOpen` below the firing evaluation. Rejected, because firings earlier in the iteration record their outcomes against the open cycle, so it must be open first.

## R-005 — The wait itself

**Decision**: Reuse the existing tail of the non-cycling path unchanged: `schedule.ComputeNextDue`, the idle-pause threshold check, `IdlePauseHoldAsync`, or `Task.Delay(RelativeTimerPollInterval)`. The only difference is that a cycling run skips `if (!HasPendingRelativeOrLive()) break;`.

**Rationale**: FR-003 asks for exactly the non-cycling wait. `ComputeNextDue` already covers time-of-day timers (next eligible), relative timers, daily retries, live schedules and self-reschedule bookings. When it returns null (nothing pending), the idle-pause branch is not taken and the run polls every 250 ms (FR-004). The pause gate (`WaitIfPausedAsync`) stays at the top of the next iteration, and the cancellation token is honoured by both waits (FR-008).

## R-006 — Existing tests affected

**Finding**: Cycling tests with once-per-run entries (`CycleExecutionRepeatsWithoutReloadingTemplate`, `TimerFiresAtMostOncePerCalendarDayAcrossCycles`, `AtQueueStartRunsOncePerRunOnCyclingQueue`, `EveryStepAlsoRunsInCyclicMode`) run a sequence every iteration and are unaffected. `AtQueueStartOnlyTimerRescheduleFiresInCyclingRun` (feature 092) now waits at the poll interval instead of spinning, and it still passes, because it advances the fake clock and waits on real time for the firing. `CycleWithEmptyTemplateCompletesWithoutBusyLoop` and `OnlyAtQueueStartTemplateCompletesEvenWhenCycling` never enter the loop and are unchanged. Integration and contract cycle tests use once-per-run templates.

## R-007 — Measuring the spin in a unit test

**Decision**: Test the observable counters rather than CPU. Start a cycling queue with one at-queue-start entry and a relative timer (+10 min) on a frozen `FakeTimeProvider`. After a short real-time wait (~500 ms), assert that `handle.Cycles.SnapshotHealth().CyclesCompleted` is 0 and that the run is still `Running`. Before the fix the count is in the hundreds of thousands. Then advance the clock past the offset, wait for the timer firing, and assert exactly one completed cycle whose `StartedAt` is at or after the advanced time. The idle-pause variant asserts `handle.IsIdlePaused` and `IdlePausedUntil`, following the existing idle-pause tests.
