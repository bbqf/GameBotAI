# Contract: `sequenceStats` on the queue read

**Feature**: 105-sequence-run-statistics | **Requirements**: FR-001 to FR-006, FR-013, FR-014

## Endpoint

`GET /api/queues/{id}`: no change to the route, the status codes or the other fields. The response gets one new field, `sequenceStats`.

The same field is in every response that returns a queue detail: `PUT /api/queues/{id}/entries`, `PUT /api/queues/{id}/template` and `PUT /api/queues/{id}/game`. `GET /api/queues` (the list), `GET /api/queues/{id}/monitor` and `GET /api/queues/{id}/cycles` do not change.

## Shape

```json
{
  "id": "q-7f3c",
  "name": "PNS Daily 5558",
  "status": "Stopped",
  "entries": [ "..." ],
  "health": null,
  "sequenceStats": {
    "seq-daily-train": {
      "sequenceName": "Daily Training",
      "lastRunStartedAt": "2026-09-24T12:00:03+02:00",
      "lastRunEndedAt": "2026-09-24T12:01:10+02:00",
      "lastRunStatus": "failure",
      "lastSuccessAt": "2026-09-24T11:30:40+02:00",
      "successCount": 2,
      "failureCount": 1,
      "cancelledCount": 0
    }
  }
}
```

| Field | Type | Description |
|---|---|---|
| `sequenceStats` | object | Keyed by sequence ID. Always present. `{}` when the queue has no recorded run. Keys are in ordinal order. |
| `.sequenceName` | string or null | The name from the sequence store. Null when the sequence no longer exists. |
| `.lastRunStartedAt` | date-time (service-local, with offset) | The start of the last completed run. |
| `.lastRunEndedAt` | date-time | The end of the last completed run. |
| `.lastRunStatus` | `success` \| `failure` \| `cancelled` | The status of the last completed run. |
| `.lastSuccessAt` | date-time or null | The end of the last successful run. Null when no run succeeded. |
| `.successCount` | integer | The total number of successful runs since the first record. |
| `.failureCount` | integer | The total number of failed runs. |
| `.cancelledCount` | integer | The total number of cancelled runs. |

## Rules

- **What is recorded**: each sequence run that the queue starts and that completes. This is true for all schedule types: at-start, once-per-run, every-step, before-each-run, timer, relative, live-schedule, self-reschedule and retry. Each sequence has its own entry, guard sequences too.
- **Key**: the sequence ID. Two template entries of the same sequence share one entry.
- **Status**:
  - `success`: the run completed and did not fail. This includes a run that a Break step ends.
  - `failure`: the run ended with an error or a failed step.
  - `cancelled`: the queue stopped the run (a stop by hand, a failure-policy stop, or the sequence time limit (watchdog)).
- **Not recorded**:
  - a run in progress,
  - a run that a service stop interrupts,
  - an ad-hoc run (`POST /api/sequences/{id}/execute`),
  - a dry-run,
  - a nested sequence run. At this time, no step type runs another sequence. If a later feature adds nested runs, the queue records only the outer run.
- **Persistence**: the values stay the same after a queue stop and start, and after a service restart. A queue restart does not change how `reschedule-self` time slots or template schedules apply (FR-014).
- **Template change**: a sequence that is no longer in the queue keeps its entry.
- **Queue delete**: `DELETE /api/queues/{id}` deletes the statistics of the queue.
- **Queue duplicate**: `POST /api/queues/{id}/duplicate` does not copy the statistics. Its response is a `QueueResponse`, which has no `sequenceStats` field. A later `GET /api/queues/{newId}` returns `sequenceStats: {}`.
- **Damaged file**: the queue read returns `{}` for that queue, the service logs a warning, and the read does not fail.

## OpenAPI

- Schema `QueueSequenceStatsResponse` with a description for each field, and an `enum` for `lastRunStatus`.
- `QueueDetailResponse.sequenceStats` is an object with `additionalProperties` of `QueueSequenceStatsResponse`, and a description of the key and of the rules above.
- The `GET /api/queues/{id}` operation description names `sequenceStats`, and the example shows one entry.
