# API contract changes (additive only)

## GET /api/execution-logs (list) — `items[]`

```jsonc
{
  "id": "…",
  "executionType": "sequence",
  "finalStatus": "failure",
  "cancellationReason": "sequence_time_limit", // NEW, nullable; only value today
  "timeLimitMs": 240000,                       // NEW, nullable; set iff cancellationReason is set
  "…": "existing fields unchanged"
}
```

## GET /api/execution-logs/{id} (detail)

The top-level response gains the same `cancellationReason` and `timeLimitMs`.

## GET /api/execution-logs/{id}/subtree — every `ExecutionTreeNode`

Each node gains `cancellationReason` and `timeLimitMs`, taken from the recorded execution the node represents. They are
null on primitive step nodes.

## GET /api/sequences/{sequenceId}

```jsonc
{
  "id": "…",
  "watchdogTimeoutMs": null,              // existing: stored override (null = none)
  "effectiveWatchdogTimeoutMs": 240000    // NEW, read-only: override when > 0, else platform default 240000
}
```

Present in all three response shapes (flow, per-step, legacy).

## POST/PUT/PATCH /api/sequences

No behaviour change. `watchdogTimeoutMs` is documented with minimum 1, maximum 1800000 and a description that states
the 240000 ms default. `effectiveWatchdogTimeoutMs` in a write body is ignored and never persisted.

## OpenAPI document (`/swagger/v1/swagger.json`)

- `SequenceUpsertRequest` / `SequencePatchContract` → `watchdogTimeoutMs`: `minimum: 1`, `maximum: 1800000`, description
  mentions `240000`.
- `ExecutionLogEntryDto` and `ExecutionTreeNodeDto` components exist, with `cancellationReason` (description lists
  `sequence_time_limit`) and `timeLimitMs`.
- The `GetSequence` operation description mentions `effectiveWatchdogTimeoutMs` and `240000`.
