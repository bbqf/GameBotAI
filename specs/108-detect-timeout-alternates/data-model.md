# Data Model: Detect does not report a time-limited measurement as an absence

No stored data changes.

## Detection time limit (calculated value)

| Input | Source | Rule |
|-------|--------|------|
| `TimeoutMs` | `Service:Detections:TimeoutMs` (default 500) | A value below 1 counts as 1. |
| `referenceCount` | 1 + number of alternates that loaded | A value below 1 counts as 1. |
| Result | | `TimeoutMs × referenceCount` ms, capped at `int.MaxValue` ms. |

## Error body (`504`)

| Field | Type | Value |
|-------|------|-------|
| `code` | string | `detection_timeout` |
| `message` | string | Text that tells the caller that the service did not measure the screen and that the caller can send the request again. |

## Log entry (warning, EventId 11007)

`Detect time limit expired id={Id} references={ReferenceCount} limitMs={LimitMs} durationMs={DurationMs}`
