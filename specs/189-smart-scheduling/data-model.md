# Data Model: Smart Scheduling

## Stored data

### ExecutionQueue (changed) — `src/GameBot.Domain/Queues/ExecutionQueue.cs`

| Member | Type | Default | JSON | Rule |
|--------|------|---------|------|------|
| `SmartScheduling` | `bool` | `false` | `smartScheduling` | An old queue file has no member and reads as `false`. |
| `SmartSchedulingWindowMinutes` | `int` | `15` | `smartSchedulingWindowMinutes` | 1 to 120. An old queue file reads as 15. The value stays when `SmartScheduling` is `false`. |

The two members are the one exception to "a run reads the queue one time, at its start": each Timer booking reads them from the stored queue (research R-004).

### SmartSchedulingLimits (new) — `src/GameBot.Domain/Queues/SmartSchedulingLimits.cs`

Constants: `DefaultWindowMinutes = 15`, `MinWindowMinutes = 1`, `MaxWindowMinutes = 120`. One static method `IsValidWindow(int minutes)`.

### CommandSequence (changed) — `src/GameBot.Domain/Commands/CommandSequence.cs`

| Member | Type | Default | JSON | Rule |
|--------|------|---------|------|------|
| `IgnoreSmartScheduling` | `bool` | `false` | `ignoreSmartScheduling` | Omitted from JSON when `false`, so an old file stays byte-identical. |

### QueueSettingsDoc (changed) — `src/GameBot.Domain/QueueSettings/SettingsDocument.cs`

Two new members with the same names, types, and defaults as on `ExecutionQueue`. The exporter writes them. The importer reads them and rejects a window out of range with the normal import error of a bad value. The format version stays 1.

## Run-time data (never stored)

### SmartCandidate (new) — `src/GameBot.Domain/Queues/SmartSchedulingRule.cs`

| Member | Type | Meaning |
|--------|------|---------|
| `DueAt` | `DateTimeOffset` | The due time of a pending firing. |
| `SequenceId` | `string` | The sequence of the firing. |
| `Kind` | `SmartTargetKind` | `SelfReschedule`, `TemplateTimer`, `DailyRetry`, or `LiveSchedule`. |

### SmartSchedulingDecision (new)

| Member | Type | Meaning |
|--------|------|---------|
| `BookedAt` | `DateTimeOffset` | The time of the booking. Equal to the requested time when there is no target. |
| `Target` | `SmartCandidate?` | The target firing. Null when the booking is not postponed. |

Invariants (FR-014): `RequestedAt <= BookedAt <= RequestedAt + window`. `Target` is not null only when `BookedAt` is later than `RequestedAt`. A candidate with exactly the requested time is not a target: the booking does not move, so it is not a postponement.

### SmartSchedulingRule (new, pure)

`Resolve(DateTimeOffset requestedAt, TimeSpan window, IReadOnlyList<SmartCandidate> candidates) → SmartSchedulingDecision`

1. Keep each candidate with `requestedAt < DueAt <= requestedAt + window`.
2. If none stays, return `(requestedAt, null)`.
3. Return the candidate with the earliest `DueAt`. For equal times, the first in list order wins.

The comparison uses absolute instants (`DateTimeOffset` compares in UTC), so midnight and a change of daylight saving time have no effect.

### SmartSchedulingRequest (new) — `ISelfRescheduleCoordinator.cs`

`SmartSchedulingRequest(int WindowMinutes)`. The caller gives it only when the stored queue has smart scheduling on and the sequence does not ignore it. Null means: book the requested time.

### SelfRescheduleEntry (changed) — `QueueRunHandle.cs`

New optional member `Postponement` of type `SmartPostponement(DateTimeOffset RequestedAt, string TargetSequenceId, SmartTargetKind TargetKind)`. `FireAt` is the booked time. `Postponement` is null for a booking that is not postponed.

### SelfRescheduleResult (changed)

New optional members `RequestedAt` (`DateTimeOffset?`) and `SmartTarget` (`SmartCandidate?`). `FireAt` is the booked time. `ResolvedTiming` is the booked time as text.

### ActionDispatchResult and the step record of SequenceRunner (changed)

New optional member `SmartScheduling` of type `SmartSchedulingLogInfo(DateTimeOffset RequestedAt, DateTimeOffset BookedAt, string TargetSequenceId, string TargetKind, int WindowMinutes)`, on the pattern of `Removed`.

### QueueRunSchedule (changed)

New read method `SnapshotTimedEntryDueTimes(DateTimeOffset now) → IReadOnlyList<SmartCandidate>`. It gives, under the schedule lock, one candidate for each enabled template Timer entry (time of day: next due time; relative offset: start plus offset, only when the entry did not fire) and one for each armed daily retry of an entry in the plan.

### QueueMonitorItem and QueueMonitorSnapshot (changed)

- Item: new optional member `RequestedAt`.
- Snapshot: new member `SmartScheduling` (`Enabled`, `WindowMinutes`).

## State of a booking

```text
requested (T)
   │  smart scheduling off, or the sequence ignores it, or no target in [T, T + window]
   ├────────────────────────────────────────────────────────────► booked at T
   │  target in (T, T + window]
   └────────────────────────────────────────────────────────────► booked at target time (postponed)

booked ──► fires (as today) │ replaced by a new booking of the sequence │ removed by Cancel │ dropped at run end
```

A booked time never changes. A later change of the target, of the queue settings, or of the sequence setting has no effect on a booking that exists.
