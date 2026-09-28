# Contract: `POST /api/steps/execute` (feature 112)

## Request

No change.

## Response 202 (step ran)

The shape does not change:

```json
{
  "accepted": 1,
  "stepOutcomes": [
    {
      "stepOrder": 0,
      "status": "executed",
      "stepType": null,
      "reason": "executed_then_error",
      "detectionConfidence": 0.97,
      "resolvedPoint": { "x": 420, "y": 910 },
      "executedPoint": null,
      "targetSwipe": null,
      "executedSwipe": null
    }
  ]
}
```

### Outcome rules for a `PrimitiveTap` step

| Status | Reason | `accepted` | Input sent to the device? |
|--------|--------|-----------|---------------------------|
| `executed` | `null` or `detected_after_{n}_retries` | count | Yes |
| `executed` | `executed_then_error` / `executed_then_cancelled` | count | Yes. A problem occurred after the dispatch. |
| `dispatch_unknown` | `dispatch_error` / `dispatch_cancelled` | 0 | Not known |
| `skipped_detection_failed` | `detection_failed_after_{n}_retries` / `primitive_tap_exception` | 0 | No |
| `skipped_invalid_config` | (current reasons) | 0 | No |
| `cancelled` | `cancelled_during_retry_{n}` | 0 | No |

Contract: a "not executed" status (`skipped_*` or `cancelled`) means that the service sent no input to the device for the step.

## Response 200 (timeout)

No change:

```json
{ "accepted": 0, "stepOutcomes": [ { "stepOrder": 0, "status": "timeout", "stepType": null, "reason": "Step execution timed out after 10 seconds" } ] }
```

## Errors

No change: 400 for a validation error or no session, 503 for a session that the service cannot find or that does not run. These errors write no execution-log entry.

## Execution log

Each call that passes the session check writes exactly one entry. Read it with:

```
GET /api/execution-logs?fromUtc=<start>&toUtc=<end>&objectType=step&objectId=<sessionId>
```

Entry fields: see [data-model.md](../data-model.md), section "ExecutionLogEntry for a single step call". The step outcome of the entry is `executed`, `dispatch_unknown`, `timeout` or `not_executed`.
