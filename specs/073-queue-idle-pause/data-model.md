# Phase 1 Data Model: Idle-Pause the Game During Queue Gaps

## 1. Persisted config (queue entity)

**`ExecutionQueue`** (`src/GameBot.Domain/Queues/ExecutionQueue.cs`) — two new durable fields:

| Field | Type | Default | Meaning |
|-------|------|---------|---------|
| `PauseWhenIdle` | `bool` | `false` | When true, the run backs the game out during idle gaps over the threshold and resumes it when the next firing is due. Opt-in; false = today's behavior. |
| `IdleThresholdSeconds` | `int` | `30` | Minimum gap (seconds) to the next scheduled firing that triggers an idle pause. Gaps at or below this are left running. |

**Validation**:
- `IdleThresholdSeconds` MUST be ≥ 1; a non-positive value submitted via the API is treated as the
  default (30). Enforced at `QueuesEndpoints` on create/update.
- No dependency between the two fields; `IdleThresholdSeconds` is ignored while `PauseWhenIdle` is false.

**Persistence / back-compat**: serialized by `FileQueueRepository` via System.Text.Json. Property
initializers mean queues stored before this feature (fields absent) deserialize to
`PauseWhenIdle=false`, `IdleThresholdSeconds=30`. No migration.

**Lifecycle**: set on create or update through the queue config API/UI. Read once per run by
`QueueExecutionService.RunAsync` from the resolved `ExecutionQueue`. Changing it on a running queue
takes effect on the next run/restart (config is read at run start), consistent with other queue config.

## 2. Transient run-scoped state (in-memory, not persisted)

**`QueueRunHandle`** (`src/GameBot.Service/Services/QueueExecution/QueueRunHandle.cs`) — new idle-pause
register, alongside the existing current-sequence tracking (feature 072):

| Member | Type | Meaning |
|--------|------|---------|
| `IdlePausedUntil` | `DateTimeOffset?` | Resume instant while the run is idle-paused; `null` when not paused. Set when the hold begins, cleared when it ends. Read concurrently by the monitor. |
| `IsIdlePaused` | `bool` (derived) | `IdlePausedUntil is not null`. |
| `EnterIdlePause(DateTimeOffset resumeAt)` | method | Sets `IdlePausedUntil` (guarded like current-sequence). |
| `ClearIdlePause()` | method | Resets `IdlePausedUntil` to `null`. |

Thread-safety: mirror the existing `_currentLock`/volatile pattern used for `CurrentSequenceId` /
`CurrentSequenceStartedAt`. The monitor reads this on a different thread than the run loop that writes it.

Invariant: while `IsIdlePaused` is true, `CurrentSequenceId` is `null` (nothing executes during a pause).
The monitor's current-item projection prefers a real `CurrentSequenceId` when present.

## 3. Next-due value (computed, transient)

A small internal helper result used only inside the run loop:

| Concept | Type | Meaning |
|---------|------|---------|
| next-due instant | `DateTimeOffset?` | Earliest upcoming firing across all pending sources; `null` when nothing is pending (run would end). |

Sources folded (see research R3): time-of-day timers (next-eligible, unfired-today), unfired relative
offsets (`runStartedAt + offset`), pending self-reschedule timer firings (min `FireAt`), live schedules
(min value), and any queued once-per-run/next-cycle self-reschedules (due-now). Not persisted; not on
the wire.

## 4. Monitor projection additions

**`ScheduleKind`** (`QueueMonitorSnapshot.cs`) — new member `IdlePause`.

**`QueueMonitorItem`** — reused as-is for the synthetic idle-pause "current" item:

| Field | Idle-pause value |
|-------|------------------|
| `SequenceId` | `""` (no real sequence) |
| `SequenceName` | `"Idle Pause"` |
| `Stale` | `false` |
| `ScheduleKind` | `IdlePause` |
| `Reason` | `"Game paused — resumes at HH:mm"` |
| `ExpectedAt` | resume instant (`IdlePausedUntil`) |
| `RelativeLabel` | `"paused"` |
| `Repeats` | `false` |
| `Order` | `0` |

## 5. Superseded data (rollout)

The "PNS Queue Pause 15m" template entry is removed from the `PNS Daily 5558` queue template on
rollout (FR-016). The sequence definition `d0484641e79243668c6442848e85a7bf` is retained in the library
(no delete). This is an operational data change, not a schema change.
