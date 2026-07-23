# Phase 1 Data Model: Live Queue Monitor View

No persisted entities are added. This feature introduces **in-memory / wire-only** shapes: two fields on
the existing run handle, and the projection/DTO types returned by the monitor endpoint. Times are
local-clock `DateTimeOffset` (service-local offset) on the wire (ISO-8601 with offset).

## 1. `QueueRunHandle` additions (in-memory, not persisted)

| Member | Type | Purpose |
|--------|------|---------|
| `CurrentSequenceId` | `volatile string?` | Sequence id currently executing; `null` between firings. Set at the top of `RunOneSequenceAsync`, cleared in its `finally`. Drives the sequence-level "now" indicator. |
| `CurrentSequenceStartedAt` | `DateTimeOffset?` (guarded) | When the current firing started (local clock); for "running for Ns". Set/cleared alongside `CurrentSequenceId`. |
| `SnapshotPendingTimerFirings()` | `IReadOnlyList<SelfRescheduleEntry>` | Read accessor returning a copy (under the existing `_timerLock`) of pending self-reschedule Timer firings, each with `SequenceId` + `FireAt`. |

Existing already-usable members: `QueueId`, `CycleExecution`, `RunStartedAt`, `RootExecutionId`,
`PendingLiveSchedules` (`sequenceId → fireAt`), `EveryStepInjections`, `PendingOncePerRun`,
`PendingNextCycleStart`.

## 2. `QueueMonitorSnapshot` (internal projection result)

Produced by `IQueueMonitorService.BuildAsync(queueId, ct)`. Pure function of
(linked `QueueTemplate`, `QueueRunHandle` snapshot, `now = TimeProvider.GetLocalNow()`,
best-effort last-outcome).

| Field | Type | Notes |
|-------|------|-------|
| `QueueId` | `string` | |
| `Name` | `string` | Queue name. |
| `Running` | `bool` | From `IQueueRunRegistry.IsRunning`. When `false`, `Current`/`Upcoming` are empty. |
| `CycleExecution` | `bool` | Drives the "repeats" marker on OncePerRun items. |
| `RunStartedAt` | `DateTimeOffset?` | Handle anchor; `null` when not running. |
| `Current` | `QueueMonitorItem?` | The now-executing sequence, or `null` when idle/not running. |
| `Upcoming` | `IReadOnlyList<QueueMonitorItem>` | Ordered list (see ordering rules below). |
| `NothingScheduled` | `bool` | `true` when running but no template/entries and no pending firings. |
| `LastOutcome` | `RunOutcome?` | Best-effort last finalized run (`Status`, `Summary`); `null` if none/running. |

### `QueueMonitorItem`

| Field | Type | Notes |
|-------|------|-------|
| `SequenceId` | `string` | |
| `SequenceName` | `string?` | Resolved from the sequence repository; `null`/`Stale` if unresolved. |
| `ScheduleKind` | enum (`AtQueueStart`/`OncePerRun`/`EveryStep`/`TimerTimeOfDay`/`TimerRelative`/`LiveSchedule`/`SelfReschedule`) | Drives the human-readable reason label. |
| `Reason` | `string` | Operator-facing label, e.g. "Once per run", "After Every Step", "At 08:30", "+00:10:00 after start", "Scheduled live", "Rescheduled by a sequence". |
| `ExpectedAt` | `DateTimeOffset?` | Absolute expected time when known (live/self-reschedule exact; time-of-day next-eligible; relative = anchor+offset). `null` for spine steps with no wall-clock time. |
| `RelativeLabel` | `string?` | Human hint when there is no absolute time, e.g. "next", "up next", "waiting". |
| `Repeats` | `bool` | `true` for OncePerRun/EveryStep on a cycling queue. |
| `Order` | `int` | Stable position within `Upcoming`. |

### `RunOutcome`

| Field | Type | Notes |
|-------|------|-------|
| `Status` | `string` | e.g. "success" / "failure" (from the finalized log entry). |
| `Summary` | `string` | The queue-run summary text already produced by `QueueExecutionService.BuildSummary`. |

## 3. Ordering rules for `Upcoming`

1. **OncePerRun spine** in template order (the "playlist" backbone). `ExpectedAt = null`,
   `RelativeLabel = "next"` for the first, `"up next"` thereafter; `Repeats = CycleExecution`.
2. **EveryStep** entries appended once each, labeled "After Every Step" (not interleaved per step);
   `Repeats = CycleExecution`.
3. **Timed firings** — the union of template time-of-day timers (next-eligible), pending relative
   timers (`RunStartedAt + offset`, only if not yet elapsed or shown as "due"), live schedules
   (exact `fireAt`), and self-reschedule Timer firings (exact `FireAt`) — sorted ascending by
   `ExpectedAt`.
4. **AtQueueStart** items are normally already past once a run is underway; they are omitted from
   `Upcoming` after the start pre-pass (the projection does not resurrect them).

`Current` is excluded from `Upcoming`.

## 4. Wire DTOs (`Contracts/Queues`)

- **`QueueMonitorResponse`** — camelCase serialization of `QueueMonitorSnapshot`:
  `queueId, name, running, cycleExecution, runStartedAt, current, upcoming[], nothingScheduled,
  lastOutcome`.
- **`QueueMonitorItemResponse`** — camelCase of `QueueMonitorItem`:
  `sequenceId, sequenceName, stale, scheduleKind, reason, expectedAt, relativeLabel, repeats, order`.
- `scheduleKind` serializes as a string (JsonStringEnumConverter), consistent with `ScheduleType`.

## 5. Validation / edge behavior

- **Not running** → `running:false`, empty `current`/`upcoming`, `lastOutcome` best-effort. (Endpoint
  returns 200, not 404, so the UI can render the "ended/not running" state.)
- **Running, no template/entries, no pending firings** → `nothingScheduled:true`, empty lists.
- **Stale sequence reference** (name unresolved) → item still listed with `sequenceName:null`,
  `stale:true`, consistent with existing `ProjectEntry` behavior.
- **Timer already fired today** → shown as next-eligible (tomorrow) rather than imminent, per best-effort
  projection (satisfies the edge case without exposing run-loop fired-state).
