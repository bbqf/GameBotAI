# Contract: `notify` step with dedupe

The step is part of a sequence step with `action.type` = `notify`. The service reads the fields from `action.payload`.

## Request fields

```json
{
  "type": "notify",
  "payload": {
    "message": "Daily task timed out",
    "url": "https://example.test/hook",
    "dedupeKey": "daily-timeout",
    "since": "02:05"
  }
}
```

Use `within` ("00:30:00") instead of `since` for a sliding window. Do not use both.

## Save-time errors (HTTP 400)

Each error message starts with `Step '<label>' notify action` and names the field.

| Case | Message part |
|------|--------------|
| `dedupeKey` with no window | `'dedupeKey' requires 'since' or 'within'` |
| `since` or `within` with no key | `'since' and 'within' require 'dedupeKey'` |
| `since` and `within` together | `use either 'since' or 'within', not both` |
| Blank `dedupeKey` | `'dedupeKey' must not be blank` |
| Key longer than 100 characters | `'dedupeKey' must be at most 100 characters` |
| `since` not `HH:mm` | `'since' must be a time of day in HH:mm format` |
| `within` not `HH:mm:ss` | `'within' must be a duration in HH:mm:ss format` |
| `within` zero or over 24 hours | `'within' must be from 00:00:01 to 24:00:00` |

## Run outcome

| Case | Step status | Outcome | Message |
|------|-------------|---------|---------|
| No dedupe fields | `success` | `success` (unchanged) | unchanged |
| Alert delivered | `success` | `sent` | `notification delivered` |
| Alert skipped | `success` | `suppressed` | `notification suppressed: sent at <time> in this window` |
| Delivery failed | `success` | `success` | `notification not delivered: <error>` |

## OpenAPI

The `payload` description of `PrimitiveActionRequest` lists `dedupeKey`, `since`, `within`, and the outcome values `sent` and `suppressed` in the `notify` line.

## Step-through preview

`would send a notification: '<message>'` or `would suppress the notification: sent at <time> in this window`. No send. No record.
