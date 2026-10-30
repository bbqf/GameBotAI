# Data Model: End a run at its time limit

## Sequence (`CommandSequence`)

| Field | Type | Default | Notes |
|-------|------|---------|-------|
| `onTimeLimit` | string, `fail` or `end` | `fail` | New. Omitted from the file when `fail` or absent. |
| `watchdogTimeoutMs` | int, optional | none | Not changed (FR-007). |

Validation: any other value is rejected on save (API and repository). The error names the field and lists the
allowed values.

## Time-limit scope (`SequenceTimeLimitScope`, in memory)

| Field | Type | Notes |
|-------|------|-------|
| `TimeLimitMs` | int | Existing. |
| `HasElapsed` | bool | Existing. True when the timer fired and no stop was requested. |
| `EndsRunAtLimit` | bool | New. True when the sequence has `onTimeLimit` = `end`. |

## Execution log entry (no new field)

| Case | `status` | `cancellationReason` | `timeLimitMs` |
|------|----------|----------------------|---------------|
| Limit reached, `onTimeLimit` = `end` | `success` | `sequence_time_limit` | the limit |
| Limit reached, `fail` or absent | `failure` | `sequence_time_limit` | the limit |
| Real fault (any option) | `failure` | none | none |
| Finished before the limit | `success` | none | none |

A reader separates a planned stop from a fault with the filter: reason = `sequence_time_limit` and status =
`success`.

## Queue firing record

For an `end` stop the firing is recorded as `Success`. For all other cases the record is as today.

## State transition

`running` -> (limit elapsed, no stop) -> `success` + reason, when `end`; `failure` + reason, when `fail`.
