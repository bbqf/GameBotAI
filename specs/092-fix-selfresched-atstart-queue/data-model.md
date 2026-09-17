# Data Model: Self-reschedule bookings keep an AtQueueStart-only queue running

No persisted model changes. The fix only reads existing in-memory, run-scoped state.

## QueueRunHandle (existing, in-memory, one per active run)

| Member | Kind | Role in this fix |
|--------|------|------------------|
| `HasPendingTimerFirings` | bool | A pending self-reschedule Timer booking opens the scheduling-loop gate |
| `PendingNextCycleStart` | queue of `SelfRescheduleEntry` | A non-empty queue opens the gate (drained at a0) |
| `PendingOncePerRun` | queue of `SelfRescheduleEntry` | A non-empty queue opens the gate (drained by the once-per-run drain) |
| `PendingLiveSchedules` | map sequenceId → fireAt | A non-empty map opens the gate (drained at a3) |
| `EveryStepInjections` | map | **Does not** open the gate (research R-002) |
| **new** `HasPendingSelfRescheduleWork` | bool (computed) | OR of the four rows above. Evaluated once, after the at-queue-start pre-pass |

## Run lifecycle (at-queue-start-only template)

```text
Start → at-queue-start pre-pass
      ├─ no pending work → empty-cycle branch → "completed full run" (unchanged)
      └─ pending work    → scheduling loop (same as mixed template)
                             ├─ non-cycling: wait (idle-pause/poll) while a Timer/live booking is pending; fire when due; break when none remain
                             └─ cycling: loop until stopped
```
