# Quickstart: Deduplicate Self-Rescheduled Sequence Firings

**Feature**: 075-auto-reschedule-dedup | **Date**: 2026-07-29

## What changes

One method: `QueueRunHandle.AddTimerFiring` in
`src/GameBot.Service/Services/QueueExecution/QueueRunHandle.cs`.

It becomes most-recent-wins per `SequenceId` (like `PendingLiveSchedules`), instead of always appending.

## The change

```csharp
/// <summary>
/// Adds a resolved Timer firing (fires once at/after its <see cref="SelfRescheduleEntry.FireAt"/>).
/// Most-recent-wins per sequence: any pending Timer firing already queued for the same
/// <see cref="SelfRescheduleEntry.SequenceId"/> is replaced, so a self-rescheduling sequence
/// never stacks duplicate future firings (feature 075). Other registers still accumulate.
/// </summary>
public void AddTimerFiring(SelfRescheduleEntry entry) {
  lock (_timerLock) {
    _pendingTimerFirings.RemoveAll(x => string.Equals(x.SequenceId, entry.SequenceId, StringComparison.Ordinal));
    _pendingTimerFirings.Add(entry);
  }
}
```

Also update the block comment at `QueueRunHandle.cs:54-58` so it no longer claims *all four* registers accumulate — the Timer register is now most-recent-wins per sequence.

## How to verify

### Unit tests (new)

`tests/unit/Queues/QueueRunHandleTimerFiringTests.cs` (new file):

- **Replaces same sequence**: add Timer firing for `seq-A` at T1, then another for `seq-A` at T2 → `SnapshotPendingTimerFirings()` has one entry, `SequenceId == "seq-A"`, `FireAt == T2` (C1, FR-002/FR-006).
- **Different sequences independent**: add `seq-A` then `seq-B` → snapshot has two entries (C2, FR-003).
- **First add unchanged**: single add → one entry (FR-007).

`tests/unit/Queues/SelfRescheduleCoordinatorTests.cs` (add):

- **Timer most-recent-wins via coordinator**: `ScheduleSelf("q1","seq-A",Timer,null,10min)` then `ScheduleSelf("q1","seq-A",Timer,null,30min)` → `DrainDueTimerFirings(now+10min)` is empty; `DrainDueTimerFirings(now+30min)` returns a single firing (the second wins).
- Mirror the existing `TwoOncePerRunReschedulesAccumulate` intent in reverse for Timer to lock the new contract.

### Regression (must stay green, FR-004)

```bash
dotnet test --filter "FullyQualifiedName~Queues"
```

Existing tests that guard the untouched behavior: `TwoOncePerRunReschedulesAccumulate`, `EveryStepIsIdempotentPerSequence`, the single-firing Timer tests in `SelfRescheduleCoordinatorTests`, `QueueMonitorServiceTests` self-reschedule projections, and `SelfRescheduleRunIntegrationTests`.

### Full gate

Per memory `web-ui-quality-gate`, the real green gate for this backend-only change is the .NET build + test:

```bash
dotnet build GameBot.sln
dotnet test
```

## Manual sanity (optional, live)

On the live "PNS Daily 5558" queue, a sequence that self-reschedules on a cooldown (e.g. Tavern Basic recruit, Alliance donations +8h) should, after running twice in one run, show only one upcoming self-reschedule firing in the queue monitor rather than two.
