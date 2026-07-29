# Internal Behavior Contract: Timer self-reschedule dedup

**Feature**: 075-auto-reschedule-dedup

This feature exposes **no external API, CLI, or schema surface** — self-reschedule Timer firings are internal, in-memory, run-scoped state. The only contract is the behavioral contract of the affected internal method. It is documented here so the change is testable against a fixed specification.

## Method: `QueueRunHandle.AddTimerFiring(SelfRescheduleEntry entry)`

**Before (current)**
- Under `_timerLock`: `_pendingTimerFirings.Add(entry)`. Always appends; same-sequence firings accumulate.

**After (this feature)**
- Under `_timerLock`, atomically:
  1. Remove every existing entry `x` in `_pendingTimerFirings` where `x.SequenceId == entry.SequenceId`.
  2. Add `entry`.

### Guarantees

| ID | Guarantee |
|----|-----------|
| C1 | After the call, exactly one entry in `_pendingTimerFirings` has `SequenceId == entry.SequenceId`, and it is `entry` (same `FireAt`). |
| C2 | Entries with a different `SequenceId` are neither removed nor reordered relative to each other. |
| C3 | The whole remove+add happens under `_timerLock`, so a concurrent `DrainDueTimerFirings` / `SnapshotPendingTimerFirings` / `HasPendingTimerFirings` never observes a state with two entries for the same sequence, nor a state missing the sequence entirely. |
| C4 | The call performs no sequence execution and no draining; it only mutates the pending register. |

### Reader methods — unchanged contracts (regression guard)

- `DrainDueTimerFirings(now)`: returns and removes entries with `FireAt <= now`. With C1 there is at most one per sequence, so a due sequence is drained exactly once.
- `SnapshotPendingTimerFirings()`: returns a copy; now contains at most one entry per sequence.
- `HasPendingTimerFirings`: true iff any entry remains.

## Test contract

Unit tests (see quickstart.md) assert C1–C4 directly against `AddTimerFiring`, and via `SelfRescheduleCoordinator.ScheduleSelf(..., Timer, ...)` at the coordinator layer. Existing tests for the other registers and for single-firing Timer cases stand as the FR-004 regression guard.
