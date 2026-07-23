# Contract: Queue Idle-Pause Config & Monitor Item

Mirrors the existing `cycleExecution` flow. All JSON is camelCase.

## 1. Queue config wire additions

Applies to queue **create** (`POST /api/queues`), **update** (`PUT /api/queues/{id}`), and the queue
**response** (list/detail).

### Request (`CreateQueueRequest`, `UpdateQueueRequest`)

```jsonc
{
  "name": "PNS Daily 5558",
  "cycleExecution": false,
  "pauseWhenIdle": true,        // NEW — optional; default false
  "idleThresholdSeconds": 30    // NEW — optional; default 30; values < 1 coerced to 30
}
```

- `pauseWhenIdle` absent → `false`.
- `idleThresholdSeconds` absent or < 1 → `30`.
- `create` also takes `emulatorSerial` (unchanged); `update` does not (binding immutable).

### Response (`QueueResponse`)

```jsonc
{
  "id": "a0debe91c5784f15a38277badd5e480a",
  "name": "PNS Daily 5558",
  "emulatorSerial": "emulator-5558",
  "cycleExecution": false,
  "pauseWhenIdle": true,          // NEW
  "idleThresholdSeconds": 30,     // NEW
  "status": "Running",
  "entryCount": 17,
  "linkedTemplateId": "…",
  "linkedGameId": "…",
  "linkedGameName": "PNS"
}
```

**Back-compat**: queues stored before this feature report `pauseWhenIdle: false`,
`idleThresholdSeconds: 30`.

## 2. Monitor item: `scheduleKind: "IdlePause"`

`GET /api/queues/{id}/monitor` — when the run is idle-paused, `current` is a synthetic idle-pause item
(no real sequence). All other monitor fields are unchanged.

```jsonc
{
  "queueId": "…",
  "name": "PNS Daily 5558",
  "running": true,
  "cycleExecution": false,
  "runStartedAt": "2026-07-23T11:21:32+02:00",
  "current": {
    "sequenceId": "",
    "sequenceName": "Idle Pause",
    "stale": false,
    "scheduleKind": "IdlePause",              // NEW enum value
    "reason": "Game paused — resumes at 11:52",
    "expectedAt": "2026-07-23T11:52:00+02:00", // resume time
    "relativeLabel": "paused",
    "repeats": false,
    "order": 0
  },
  "upcoming": [ /* unchanged: the due firing appears here as the soonest item */ ],
  "nothingScheduled": false,
  "lastOutcome": null
}
```

- `scheduleKind` gains the value `"IdlePause"` (existing values:
  `AtQueueStart|OncePerRun|EveryStep|TimerTimeOfDay|TimerRelative|LiveSchedule|SelfReschedule`).
- Emitted **only** while paused; when a sequence is actually executing, `current` is that sequence as
  today. The idle-pause item is never written to the execution log (transient only).

## 3. web-ui (`queues.ts`) — REST/web-ui surface only

> **No MCP arm.** Unlike the historical `cycleExecution` plumbing, these fields are **not** added to any
> MCP tool schema: the project's MCP server is being deleted in this same branch (FR-020, research R8).
> The config surface stops at the REST API + web-ui.

- web-ui queue types gain the two fields; the queue editor exposes a "Pause game when idle" toggle and a
  threshold input; `QueueMonitor` renders `scheduleKind === "IdlePause"` as a distinct paused state with
  the resume time.

## 4. Contract tests

- Create with `pauseWhenIdle:true, idleThresholdSeconds:45` → response echoes both; persisted and
  re-read identically.
- Update toggling `pauseWhenIdle` and changing threshold round-trips.
- Omitted fields default (`false` / `30`); `idleThresholdSeconds:0` coerces to `30`.
- Back-compat: a queue JSON without the fields reads as `false` / `30`.
- Monitor response serializes `scheduleKind: "IdlePause"` with `expectedAt` = resume and
  `relativeLabel: "paused"` when the run handle is idle-paused.
