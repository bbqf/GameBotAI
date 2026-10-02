# Data Model: Reschedule-Self Keep Earliest

## Payload field (stored in the action payload dictionary)

| Field | Type | Rule |
|-------|------|------|
| `keep` | string, optional | Only value: `earliest` (case-insensitive). Valid only when `option` is `Timer`. Stored as the author wrote it. JSON null counts as absent. |

Errors (all give a 400 on save):
- Invalid value: `keep '<value>' is not valid; the only accepted value is 'earliest'`.
- Non-Timer option: `Step '<label>' reschedule-self keep is only valid when option is Timer.`

## Domain types

- `SelfRescheduleKeep` enum: `None`, `Earliest`.
- `SelfReschedulePayload`: new `Keep` (`SelfRescheduleKeep`) and `HasKeep` (true when the key is present with a valid value).

## Service types

- `SelfRescheduleEntry`: new optional member `string? RunId` (default null). Every Timer booking that a run makes sets it, with or without `keep` (FR-002). A booking that the engine makes has no run id.
- `SelfRescheduleResult`: new optional members `bool KeptPending` (default false) and `DateTimeOffset? PendingFireAt` (default null).
- `ISelfRescheduleCoordinator.ScheduleSelf`: new optional arguments `keep` and `runId`.
- `QueueRunHandle.AddTimerFiring(entry, keepEarliest)` returns `TimerBookingResult` (`Added`, `Replaced`, `KeptPending`, plus the pending `FireAt`).

## Decision table for AddTimerFiring

| keepEarliest | Pending entry of the sequence | Same non-null RunId | New FireAt vs pending | Result |
|--------------|-------------------------------|---------------------|-----------------------|--------|
| false | none | - | - | Added |
| false | exists | - | - | Replaced (old behavior) |
| true | none | - | - | Added |
| true | exists | no (other run, no run id, or step-0 retry) | any | Replaced |
| true | exists | yes | earlier | Replaced |
| true | exists | yes | equal or later | KeptPending |

A fire time in the past is earlier than a fire time in the future, so it wins.

## RearmTimerFiring rule

`QueueRunHandle.RearmTimerFiring(entry)` adds the entry only when the register has no Timer entry for the sequence. It stores the entry with `RunId = null` and the same `FireAt`. So the first booking of the next run replaces a re-armed booking (FR-003).

## Run key

`RunId` is the root execution id of the sequence execution. A booking made in a child sequence execution uses the root execution id of its parent context.

## State rule

The register keeps at most one Timer entry for each sequence (FR-005). A dropped booking is not stored. A booking of one sequence never changes the booking of another sequence.
