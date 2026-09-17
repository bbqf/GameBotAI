# Contract: `resumeOnServiceStart` queue option

Additive, backward compatible. Omitting the field in a request means `false`, the same as `pauseWhenIdle`.

## POST /api/queues

Request body gains:

```json
{ "name": "PNS Farm", "emulatorSerial": "emulator-5558", "resumeOnServiceStart": true }
```

Response `201` (`QueueResponse`) includes `"resumeOnServiceStart": true`.

## PUT /api/queues/{id}

Request body gains `resumeOnServiceStart` (bool). Existing rule unchanged: `409 queue_running` while the queue is Running. Response `200` echoes the stored value.

## POST /api/queues/{id}/duplicate

The duplicate copies `resumeOnServiceStart` from the source (part of the 1:1 configuration copy). Request body unchanged.

## GET /api/queues, GET /api/queues/{id}

Every `QueueResponse` / `QueueDetailResponse` item includes `resumeOnServiceStart` (bool). Queues stored before this feature report `false`.

## OpenAPI

`CreateQueueRequest`, `UpdateQueueRequest` and `QueueResponse` schemas list the boolean property `resumeOnServiceStart`.

## Service behaviour (not an HTTP surface)

On service start, after the host is ready, every queue that has `resumeOnServiceStart: true` and was Running when the service stopped is started as if `POST /api/queues/{id}/start` had been called. Service log events:

| EventId | Level | Message |
|---------|-------|---------|
| 7300 | Information | Queue {QueueId} was running when the service stopped; resume outcome: {Outcome}. |
| 7301 | Information | Queue {QueueId} was running when the service stopped but does not opt in to resume; leaving it stopped. |
| 7302 | Information | Queue {QueueId} was recorded as running but no longer exists; discarding the record. |
| 7303 | Error | Queue {QueueId} could not be resumed after the service restart. |
| 7304 | Warning | Resume pass could not read the running-queue record; no queues resumed. |
