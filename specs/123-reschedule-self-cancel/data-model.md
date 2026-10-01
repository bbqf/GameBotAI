# Data Model: Reschedule-Self Cancel

No persisted data changes. Bookings stay in memory in the run handle. A booking does not live past a stop of the queue.

## SelfRescheduleOption (enum)

| Value | Meaning | Change |
|-------|---------|--------|
| AtQueueStart | Fire at the next cycle start | none |
| OncePerRun | Append to the current cycle | none |
| Timer | Fire once at a resolved instant | none |
| EveryStep | Fire after each normal step | none |
| Cancel | Remove the one-time bookings of this sequence | new |

The wire value is the case-insensitive string "Cancel" in the `option` field of the payload.

## Reschedule-self payload (wire)

| Field | Type | Rule for Cancel |
|-------|------|-----------------|
| option | string | "Cancel" |
| timerTimeOfDay | string | Not allowed. 400 names the field. |
| timerRelativeOffset | string | Not allowed. 400 names the field. |
| ocrOffset | object | Not allowed. 400 names the field. |
| any other key | any | Not allowed (existing rule, issue #228). |

## Pending booking (in memory, per queue run handle)

A queue run is one start-to-stop run of the queue. The run handle holds each booking as a
`SelfRescheduleEntry` (entry id, sequence id, option, fire time, scope):

| Register | Content | Cancel removes |
|----------|---------|----------------|
| Timer list | One entry for each sequence at most (the last one wins) | Entry of this sequence |
| `PendingOncePerRun` | OncePerRun entries and the non-cycling AtQueueStart fallback | Entries of this sequence |
| `PendingNextCycleStart` | AtQueueStart entries for the next cycle, and template entries that a device hold moved here (id prefix `at-queue-start:`) | Entries of this sequence, except those with the prefix |
| `EveryStepInjections` | One standing injection for each sequence | Nothing (kept) |
| `PendingLiveSchedules` | Entries from the live-schedule endpoint | Nothing (kept) |

Cancel never changes an entry of another sequence or a queue template entry.

### In-flight drain copy (FR-005, FR-015)

When the run loop fires the OncePerRun bookings, it moves them from `PendingOncePerRun` into a local list (the
drain copy). The handle holds two more in-memory members for this:

| Member | Content | Life |
|--------|---------|------|
| In-flight list | Reference to the drain copy | Set by `BeginOncePerRunDrain`, cleared by `EndOncePerRunDrain` |
| Cancelled-id set | Entry ids of in-flight bookings that a Cancel step stopped | Filled by `RemovePendingBookings`, read and emptied by `TryConsumeCancelled` and `EndOncePerRunDrain` |

The loop checks `TryConsumeCancelled(entry.Id)` before each firing and skips a cancelled booking. A booking that
a later step makes has a new entry id, so it is not cancelled. The Timer list has one entry for each sequence,
so the Timer drain needs no such set. The owner of the Cancel step is the sequence that owns the step (the same
id that a Timer step uses), also in a nested or called sequence.

## ActionDispatchResult

| Member | Type | Change |
|--------|------|--------|
| Outcome | string | new value `cancelled` |
| Message | string? | none |
| Removed | bool? | new, optional, default null. Set only by the Cancel option. |

## CancelSelf result (internal)

| Member | Type | Meaning |
|--------|------|---------|
| Outcome | enum | `Cancelled`, `NothingPending`, `NotRunning` |
| RemovedCount | int | Number of entries removed |

## Step outcome (API and execution log)

| Case | Step status | Outcome | removed |
|------|-------------|---------|---------|
| At least one booking removed | success | cancelled | true |
| No booking of this sequence | success | noop | false |
| No queue started the run | success | noop | false |
| Queue run no longer active | success | noop | false |
| Only in-flight drain bookings stopped | success | cancelled | true |

The step result and the execution log item of a reschedule step carry `removed` (boolean) and a short message.
Other options leave the field out.

## State transitions

- Pending booking exists, Cancel runs: booking removed, outcome `cancelled`, `removed` true.
- No pending booking: outcome `noop`, `removed` false.
- No originating queue, or run no longer active: outcome `noop`, `removed` false.
- Run ends before Cancel (named failure, time-limit cancel, operator cancel): booking stays.
- Cancel runs, then a later step books a new Timer: the new booking stays (the last booking wins).
