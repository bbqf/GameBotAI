# Contract: reschedule-self Timer with timerTimeOfDay

## Step payload (no change)

```json
{ "type": "reschedule-self", "payload": { "option": "Timer", "timerTimeOfDay": "14:55:00" } }
```

The accepted formats and the validation do not change (issues #226 and #228).

## Booked instant (changed)

| Case | Before | After |
|------|--------|-------|
| `timerTimeOfDay` later than the current local time today | today at that time | today at that time (no change) |
| `timerTimeOfDay` equal to or earlier than the current local time | the current moment | that time on the next local day |

The UTC offset of the booked instant is the offset of the service-local time zone for that local date and time.

## GET /api/queues/{id}/monitor (no shape change)

An item with `scheduleKind: "SelfReschedule"` has `expectedAt` equal to the booked instant. Example after the step ran at 2026-09-24T14:55:41+02:00:

```json
{ "sequenceId": "...", "scheduleKind": "SelfReschedule", "reason": "Rescheduled by a sequence", "expectedAt": "2026-09-25T14:55:00+02:00" }
```

## OpenAPI text

The `reschedule-self` entry of the `PrimitiveAction.payload` description adds: "A timerTimeOfDay that is not later than the current time books that time on the next day."
