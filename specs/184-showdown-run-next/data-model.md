# Data Model: Run Next for Unscheduled Tasks

All new data is in memory and belongs to one run. Nothing is saved to a file. A restart ends the run and
clears the data.

## PendingRunNext (run-scoped, internal)

The API request type is `RunNextRequest`. It is a different type. Do not confuse the two.

| Field | Type | Rule |
|-------|------|------|
| EntryId | string | Stable ID of the template entry. Not empty. Unique in the register. |
| SequenceId | string | Sequence of the entry, copied from the run plan at request time. |
| Scope | ParameterScope | The scope of the entry (queue scope plus the entry values). Fixed at request time. |
| RequestedAt | DateTimeOffset | Local clock at the request. Used for order and for the monitor. |

- **Register**: an ordered list on the run handle, guarded by a lock.
- **Order**: first in, first out. The loop fires the requests in the order of `RequestedAt`.
- **Uniqueness**: at most one request for each `EntryId`.
- **Lifecycle**: Created by an accepted call. Removed when the entry fires (success or failure). Put back
  when a device-liveness hold stops the firing (feature 106 rule). Cleared with the handle when the run
  ends for any reason (FR-007).

## EntryState (computed, per enabled entry of the run plan)

| Value | Meaning |
|-------|---------|
| `running` | The entry fires now. |
| `queued` | A request for the entry waits in the register. |
| `scheduled` | The run still has a normal firing for the entry (see research R-004). |
| `idle` | Enabled, with no pending firing. **Run next** is available. |

A disabled entry and an entry that is not in the run plan have no state. They are not in the map.

### State of an entry by schedule type

| Schedule type | `scheduled` when | `idle` when |
|---------------|------------------|-------------|
| OncePerRun | The step is not done in this cycle. | The step is done in this cycle. A non-cycling run keeps it done. |
| Timer, time of day | The timer is not fired today, or a retry is pending. | The timer fired today and no retry is pending. |
| Timer, relative | The timer did not fire in this run. | The timer fired in this run. |
| AtQueueStart | The entry did not run in this run. | The entry ran once in this run. |
| BeforeEachRun | The entry did not run in this run. | The entry ran once in this run. |
| EveryStep | The entry did not run in this run. | The entry ran once in this run. |

- A pending self-reschedule or live booking for the same sequence makes the entry `scheduled`.
- `running` and `queued` have priority over `scheduled` and `idle`.
- The rule is the same for a cycling and a non-cycling run. A cycling run resets a once-per-run
  step at the start of each cycle, so the step is `scheduled` again.

### State transitions

```text
idle --(Run next accepted)--> queued --(loop starts the firing)--> running --(firing ends)--> idle | scheduled
scheduled --(normal firing starts)--> running
queued --(run ends)--> (cleared)
```

## Monitor changes

- `QueueMonitorResponse.entryStates`: map from entry ID to the state text. Empty when the queue does not
  run. Added without a version change (a new field; old readers ignore it).
- New `ScheduleKind` value `RunNext`. The `upcoming` list shows a queued request as an item with the
  reason "Run next" and the label "next". The queued item comes before the other upcoming items.

## Validation rules (call)

| Rule | Result |
|------|--------|
| Queue does not exist | 404 `not_found` |
| `entryId` missing or empty | 400 `invalid_request` |
| Queue has no active run | 409 `not_running` |
| Entry is not in the run plan, or is disabled | 409 `entry_not_available` |
| Entry state is `running` | 409 `entry_running` |
| Entry state is `scheduled` | 409 `entry_scheduled` |
| Entry state is `queued` | 200 with `alreadyQueued: true` |
| Entry state is `idle` | 200 with `alreadyQueued: false` |
