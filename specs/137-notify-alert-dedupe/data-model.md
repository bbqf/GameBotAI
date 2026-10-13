# Data Model: Notify alert dedupe window

## Notify payload (existing, extended)

Stored in `SequenceActionPayload.Parameters` of a step with type `notify`.

| Field | Type | Required | Rule |
|-------|------|----------|------|
| `message` | string | yes | Existing rule. At most 1000 characters. |
| `url` | string | no | Existing rule. Absolute http or https URL. |
| `dedupeKey` | string | no | Not blank. At most 100 characters after trim. Needs `since` or `within`. |
| `since` | string `HH:mm` | no | Valid time of day. Not allowed with `within`. Needs `dedupeKey`. |
| `within` | string `HH:mm:ss` | no | From `00:00:01` to `24:00:00`. Not allowed with `since`. Needs `dedupeKey`. |

`NotifyPayload` gets the read-only properties `DedupeKey` (string or null), `Since` (TimeOnly or null) and `Within` (TimeSpan or null), and the constant `MaxDedupeKeyLength = 100`.

## Sent-alert record

One record for each delivered alert scope.

| Field | Type | Note |
|-------|------|------|
| `queueId` | string | Queue identity, or `adhoc` for a run outside a queue. |
| `key` | string | The trimmed `dedupeKey`. |
| `sentAt` | DateTimeOffset | Time of the last delivered alert. |

The record is unique for (`queueId`, `key`). A new commit replaces `sentAt`.

File: `<dataRoot>/notify-alert-dedupe/sent-alerts.json`

```json
{ "schemaVersion": 1, "records": [ { "queueId": "q1", "key": "timeout", "sentAt": "2026-10-12T02:05:10+02:00" } ] }
```

## Reservation (memory only)

| Field | Type | Note |
|-------|------|------|
| `token` | opaque | Returned by `TryReserve`. |
| `queueId`, `key` | string | The scope. |
| `reservedAt` | DateTimeOffset | A reservation older than 5 minutes is dead. |

## State transitions for one (queue, key) slot

```text
Free --TryReserve(no record in window)--> Reserved
Free --TryReserve(record in window)-----> Suppressed (no state change)
Reserved --TryReserve--------------------> Suppressed (no state change)
Reserved --Commit------------------------> Free with record sentAt = now
Reserved --Release (delivery failed)-----> Free (record unchanged)
Reserved --5 minutes pass----------------> Free (dead reservation)
```

## Interface

```text
ISentAlertStore
  TryReserveAsync(queueId, key, windowStart, now, ct) -> reservation token or null
  CommitAsync(token, sentAt, ct)
  ReleaseAsync(token)
  WasSentSinceAsync(queueId, key, windowStart, ct) -> DateTimeOffset? (read-only, for preview)
  HasLiveReservationAsync(queueId, key, now, ct) -> bool (read-only, for preview)
  DeleteQueueAsync(queueId, ct)
```

Preview shows "would suppress" when `WasSentSinceAsync` returns a time or `HasLiveReservationAsync` returns true.

`TryReserveAsync` returns null when the alert is suppressed.

## Retention and clean-up

- Commit removes records with `sentAt` older than 48 hours.
- `DeleteQueueAsync` removes all records and reservations of the queue. The queue delete endpoint calls it.
- Null store: when `SequenceExecutionService` has no `ISentAlertStore`, a step with dedupe fields always sends.
