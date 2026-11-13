# Contract: REST API Changes

All changes are additive. A client from before this feature works with no change.

## Queues

### `POST /api/queues` and `PUT /api/queues/{id}` (request)

| Member | Type | Rule |
|--------|------|------|
| `smartScheduling` | boolean, optional | Create: absent or null gives `false`. Update: absent or null keeps the stored value. |
| `smartSchedulingWindowMinutes` | integer, optional | 1 to 120. Create: absent or null gives 15. Update: absent or null keeps the stored value. |

Error: a window out of range gives `400` with the code `invalid_request` and the message `smartSchedulingWindowMinutes must be from 1 to 120`. The check applies also when `smartScheduling` is `false`.

A saved change applies to each `reschedule-self` Timer booking that a running queue makes after the save. It does not move a booking that exists.

### `GET /api/queues`, `GET /api/queues/{id}`, and each response that returns a queue

| Member | Type | Rule |
|--------|------|------|
| `smartScheduling` | boolean | Always present. |
| `smartSchedulingWindowMinutes` | integer | Always present. 15 for a queue from before this feature. |

### `POST /api/queues/{id}/duplicate`

The copy gets `smartScheduling` and `smartSchedulingWindowMinutes` of the source queue. The request has no new member.

### Settings document (export and import)

Each queue in the document has `smartScheduling` and `smartSchedulingWindowMinutes`. A document with no such members reads as `false` and 15. The format version stays 1.

## Sequences

The member `ignoreSmartScheduling` follows the contract of `excludeFromSuccessNotifications`, with one difference: on `PUT`, an absent member keeps the stored value.

| Route | Rule |
|-------|------|
| `POST /api/sequences` | Optional boolean. Absent gives `false`. |
| `PUT /api/sequences/{id}` | Optional boolean. Absent keeps the stored value. This is different from `excludeFromSuccessNotifications`, where absent gives `false`. |
| `PATCH /api/sequences/{id}` | Optional boolean. Absent keeps the stored value. |
| `GET /api/sequences`, `GET /api/sequences/{id}` | Always present. |

Error: a value that is not `true` or `false` gives `400` with the message `ignoreSmartScheduling must be true or false.`

OpenAPI description: "When true, the bookings of this sequence always keep the requested time. Smart scheduling of a queue does not postpone them. Use it for a time-critical sequence."

## Queue monitor: `GET /api/queues/{id}/monitor`

| Member | Type | Rule |
|--------|------|------|
| `smartScheduling` | object | `{ "enabled": boolean, "windowMinutes": integer }`. From the stored queue. Present for a running queue and for a stopped queue. |
| `upcoming[].requestedAt` | date-time, optional | Present only for a self-reschedule firing that smart scheduling postponed. It is the requested time. `expectedAt` is the booked time. |
| `upcoming[].reason` | string | For a postponed firing: `Rescheduled by a sequence, postponed from HH:mm (smart scheduling)`. For all other items: as today. |

Example of a postponed item:

```json
{
  "sequenceId": "46baa304d46445a9ae214ffbcf416755",
  "sequenceName": "PNS.RadioQuiz",
  "stale": false,
  "scheduleKind": "SelfReschedule",
  "reason": "Rescheduled by a sequence, postponed from 12:00 (smart scheduling)",
  "expectedAt": "2026-11-13T12:10:00+01:00",
  "requestedAt": "2026-11-13T12:00:00+01:00",
  "relativeLabel": null,
  "repeats": false,
  "order": 3
}
```

## Schedule rows of the queue detail (web UI)

The rows come from `buildScheduleRows` in `queueActivity.ts`. The phone view and the large screen use the same function. For a monitor item with `requestedAt`, the timing text of the row gets the suffix ` · postponed from HH:mm`. Example: `Scheduled · 12:10 · postponed from 12:00`. A row with no `requestedAt` has the text of today.

## Edit page of a running queue (web UI)

The note near Save is: `Settings apply at the next start. Smart scheduling applies to the next booking.`

## Execution log of a `reschedule-self` step

For a postponed booking, the detail item of the step has these added attributes:

| Attribute | Type | Meaning |
|-----------|------|---------|
| `requestedFireAt` | date-time | The requested time. |
| `bookedFireAt` | date-time | The booked time. |
| `smartSchedulingTarget` | string | The name of the target sequence (the ID when the name is not known). |
| `smartSchedulingTargetKind` | string | `SelfReschedule`, `TemplateTimer`, `DailyRetry`, or `LiveSchedule`. |
| `smartSchedulingWindowMinutes` | integer | The window that the rule used. |

The message of the step is:

`rescheduled this sequence (option Timer, <booked time>); postponed from <requested time> to the firing of '<target>' (smart scheduling, window <N> min); applies to the current run only`

A booking that is not postponed has no added attribute and has the message of today.
