# API Changes: `onTimeLimit`

## Request (POST /api/sequences, PUT and PATCH /api/sequences/{id})

New optional member `onTimeLimit` (string). Allowed values: `fail` (default), `end`.

| Verb | Member absent | Member present |
|------|---------------|----------------|
| POST | `fail` | the value |
| PUT | reset to `fail` (PUT replaces the sequence) | the value |
| PATCH | saved value is kept | the value |

Error (400), for an unknown value, a non-string value, or JSON null:

```json
{ "message": "Invalid sequence payload", "errors": ["onTimeLimit must be one of: fail, end."] }
```

The repository check on save uses the same message text as the API (one shared constant).

The check runs before any other change, on each body shape. A dry run (`dryRun`) applies the same check.

## Response (GET, POST, PUT, PATCH)

Each response shape adds `onTimeLimit` (`fail` or `end`). The value `fail` is always written in a response, so a
client need not know the default.

## Execution log (GET /api/execution-logs and detail)

No new field. Documented change: `cancellationReason` = `sequence_time_limit` can now be on an entry with the
status `success` (the sequence has `onTimeLimit` = `end`). With the status `failure` it means the limit ended a run
with `onTimeLimit` = `fail`. A fault has no reason. The summary text of an `end` stop SHOULD say that the run ended at its time limit by
choice; the contract is the structured fields (status and reason).

## OpenAPI

- Request and response schemas describe `onTimeLimit`, the allowed values, and the default.
- The description of `cancellationReason` states both cases above.
- `watchdogTimeoutMs` and `effectiveWatchdogTimeoutMs` are not changed.
