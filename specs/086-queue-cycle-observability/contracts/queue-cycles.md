# Contract: Queue Health and Cycle Records

**Feature**: 086-queue-cycle-observability | **Date**: 2026-09-14

Two read paths. Both are safe to poll, both work while the queue is running, and neither has any
effect on the run (FR-007, FR-020).

## 1. `GET /api/queues/{id}` — additive `health` block

Everything already returned is unchanged (FR-017). One nullable field is added.

### Running queue

```json
{
  "id": "q-7f3a",
  "name": "PNS.Production",
  "status": "Running",
  "cycleExecution": true,
  "entries": [ "…unchanged…" ],
  "health": {
    "runStartedAt": "2026-09-12T10:53:04+02:00",
    "cyclesCompleted": 417,
    "lastCycleStartedAt": "2026-09-14T19:12:40+02:00",
    "lastCycleCompletedAt": "2026-09-14T19:18:02+02:00",
    "lastCycleStatus": "failure",
    "consecutiveFailedCycles": 39,
    "currentEntryIndex": 2,
    "currentSequenceId": "seq-alliance-help"
  }
}
```

This is the 2026-09-12 outage as it would now read: `status: Running`, but 39 consecutive failed
cycles and a failing last cycle. That is the signal the platform did not have.

### Not-running queue

```json
{ "id": "q-7f3a", "status": "Stopped", "health": null }
```

`health` is **null**, never a zeroed object (FR-008). A caller must not be able to mistake a stopped
queue for a live one that has completed zero cycles.

**Liveness is a conjunction** (FR-008a): `health` is present exactly when this same response reports
`status: "Running"` *and* a run handle exists. A response can therefore never say `Stopped` while
carrying a health block, and `GET {id}/cycles` uses the identical gate for its `running` flag, so the
two endpoints never disagree about whether a run is in progress. See research R8 for why the two
underlying stores would otherwise briefly diverge at run start and run end.

### Running, before the first cycle completes

```json
{
  "health": {
    "runStartedAt": "2026-09-14T19:12:40+02:00",
    "cyclesCompleted": 0,
    "lastCycleStartedAt": null,
    "lastCycleCompletedAt": null,
    "lastCycleStatus": null,
    "consecutiveFailedCycles": 0,
    "currentEntryIndex": 0,
    "currentSequenceId": "seq-boot"
  }
}
```

The three `lastCycle*` fields are null together, exactly until the first cycle completes.

## 2. `GET /api/queues/{id}/cycles` — new

### Request

| Parameter | In | Type | Default | Notes |
|---|---|---|---|---|
| `id` | path | string | — | Queue id |
| `limit` | query | int | `20` | Clamped to 1–50; out-of-range values are clamped, never rejected (FR-014) |

### `200 OK` — running

Newest first (FR-014).

```json
{
  "queueId": "q-7f3a",
  "running": true,
  "cycles": [
    {
      "ordinal": 417,
      "startedAt": "2026-09-14T19:12:40+02:00",
      "completedAt": "2026-09-14T19:18:02+02:00",
      "status": "failure",
      "entries": [
        { "sequenceId": "seq-boot",          "sequenceName": "Ensure Game Running", "status": "failure" },
        { "sequenceId": "seq-alliance-help", "sequenceName": "Alliance Help All",   "status": "failure" }
      ]
    },
    {
      "ordinal": 416,
      "startedAt": "2026-09-14T19:07:11+02:00",
      "completedAt": "2026-09-14T19:12:39+02:00",
      "status": "failure",
      "entries": [
        { "sequenceId": "seq-boot",          "sequenceName": "Ensure Game Running", "status": "failure" },
        { "sequenceId": "seq-alliance-help", "sequenceName": "Alliance Help All",   "status": "failure" }
      ]
    }
  ]
}
```

Reading down the `entries` of successive cycles is how an operator identifies *which* entry is
failing without stopping the queue (SC-004).

### `200 OK` — known queue, not running

```json
{ "queueId": "q-7f3a", "running": false, "cycles": [] }
```

Not a 404 and not a 409. This mirrors `GET /api/queues/{id}/monitor`, which made the same choice so a
polling client can render a state instead of handling an error (FR-016).

### `404 Not Found` — unknown queue id

```json
{ "code": "not_found", "message": "queue not found" }
```

The endpoint's existing error convention; unchanged vocabulary (Constitution III).

## Field semantics

| Field | Rule |
|---|---|
| `ordinal` | 1-based within the **current run**. Keeps counting past the 50-record retention bound, so `cycles[0].ordinal` can exceed `cycles.length` |
| `status` (cycle) | `"failure"` iff at least one entry in the cycle failed; a cycle with no entries is `"success"` |
| `status` (entry) | `"success"` / `"failure"` — the same distinction the engine already makes per sequence |
| `sequenceName` | Resolved when the response is built; `null` if the sequence has since been deleted |
| timestamps | Local-clock offsets, consistent with the rest of the queue API |
| retention | At most 50 completed cycles per run; oldest discarded first (FR-015, SC-005) |
| scope | All values describe the **current run**. A restarted queue begins again at `cyclesCompleted: 0` with an empty cycle list (FR-009) |

## Invariants

- Neither endpoint mutates run state or influences scheduling (FR-020, SC-007).
- `health != null` on `GET {id}` and `running: true` on `GET {id}/cycles` hold under exactly the same
  condition, so the two endpoints agree with each other and with `status` (FR-008a).
- Neither writes to the execution log; the root and terminating run records are untouched (FR-018).
- A cycle interrupted by a stop, a connection loss, or host shutdown never appears — only completed
  cycles are published.
- A non-cycling queue reports `cyclesCompleted: 1` and exactly one cycle record for its single pass
  (FR-019).
