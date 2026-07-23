# Contract: Queue Monitor Endpoint

## `GET /api/queues/{id}/monitor`

Returns a read-only snapshot of what a queue's current run is doing now and will do next. Safe to poll
(the web-ui monitor calls it every ~2.5s while open). No side effects.

### Path parameters

| Name | Type | Description |
|------|------|-------------|
| `id` | string | Queue id. |

### Responses

- **200 OK** — `QueueMonitorResponse` (below), for both running and not-running queues.
- **404 Not Found** — `{ "error": { "code": "not_found", "message": "Queue not found" } }` when the
  queue id does not exist.

The endpoint intentionally returns **200 with `running:false`** (not 404/409) when the queue exists but
is not running, so the client can render the "not running / run ended" state and the last outcome.

### `QueueMonitorResponse`

```jsonc
{
  "queueId": "q-123",
  "name": "PNS Daily 5558",
  "running": true,
  "cycleExecution": false,
  "runStartedAt": "2026-07-23T09:15:02.51+02:00",   // null when not running
  "current": {                                        // null when idle or not running
    "sequenceId": "seq-help-all",
    "sequenceName": "Alliance Help All",
    "stale": false,
    "scheduleKind": "OncePerRun",
    "reason": "Once per run",
    "expectedAt": null,
    "relativeLabel": "now",
    "repeats": false,
    "order": 0
  },
  "upcoming": [
    {
      "sequenceId": "seq-gift",
      "sequenceName": "Collect Gifts",
      "stale": false,
      "scheduleKind": "OncePerRun",
      "reason": "Once per run",
      "expectedAt": null,
      "relativeLabel": "up next",
      "repeats": false,
      "order": 1
    },
    {
      "sequenceId": "seq-chest",
      "sequenceName": "Daily Chest",
      "stale": false,
      "scheduleKind": "TimerTimeOfDay",
      "reason": "At 03:30",
      "expectedAt": "2026-07-24T03:30:00+02:00",
      "relativeLabel": null,
      "repeats": false,
      "order": 2
    },
    {
      "sequenceId": "seq-adhoc",
      "sequenceName": "One-off Scan",
      "stale": false,
      "scheduleKind": "LiveSchedule",
      "reason": "Scheduled live",
      "expectedAt": "2026-07-23T09:20:00+02:00",
      "relativeLabel": null,
      "repeats": false,
      "order": 3
    }
  ],
  "nothingScheduled": false,
  "lastOutcome": null                                 // { "status": "...", "summary": "..." } when not running
}
```

### Field semantics

| Field | Meaning |
|-------|---------|
| `running` | Whether a run is currently registered for the queue. |
| `cycleExecution` | Queue repeats its OncePerRun steps each cycle. |
| `runStartedAt` | Local-clock instant the run loop started (anchor for relative timers). |
| `current` | The sequence executing right now (sequence-level; per-step detail stays in Execution Logs). `relativeLabel:"now"`. |
| `upcoming[]` | Ordered next items: OncePerRun spine (template order) → EveryStep (once, "After Every Step") → timed/live/self-reschedule firings sorted by `expectedAt`. Excludes `current`. |
| `scheduleKind` | One of `AtQueueStart`, `OncePerRun`, `EveryStep`, `TimerTimeOfDay`, `TimerRelative`, `LiveSchedule`, `SelfReschedule` (string enum). |
| `reason` | Human-readable schedule reason for display. |
| `expectedAt` | Absolute expected time when known; `null` for spine steps with no wall-clock time. Best-effort for far-future timers (next-eligible). |
| `relativeLabel` | Hint when there is no absolute time: `now` / `next` / `up next` / `waiting`. |
| `repeats` | Item recurs every cycle (cycling queues). |
| `nothingScheduled` | Running but nothing to execute (no template/entries, no pending firings). |
| `lastOutcome` | Best-effort last finalized run (`status`, `summary`) when `running:false`; `null` otherwise. |

### Time zone

All instants are ISO-8601 **with offset**, using the service local clock (consistent with the queue
engine's `GetLocalNow()`), so the client renders one unambiguous local time.

### Notes

- Read-only. No start/stop/schedule controls are exposed by this endpoint (those remain the existing
  `POST {id}/start`, `POST {id}/stop`, `POST {id}/live-schedule`).
- The projection reflects the same schedule semantics the run executes; ordering and reasons are exact,
  far-future timer instants are approximate (next-eligible).
