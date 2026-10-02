# Data Model: Last sent message for each target

All data is in memory. Nothing is stored on disk.

## LastSentMessage (record)

| Field | Type | Meaning |
| --- | --- | --- |
| QueueId | string | The queue of the message. For an alert, the queue of the alert. |
| SequenceKey | string | The sequence ID. For an alert, the fixed key `alert`. |
| Status | text key | `failure`, `success`, `recovered`, `cancelled`, or `alert`. |
| Text | string | The final message text. |

Two records are equal when all four fields match exactly (ordinal compare).

## NotificationLastSentState

One dictionary: target ID to `LastSentMessage`. One entry for each target that got a message.

| Method | Rule |
| --- | --- |
| IsSame(targetId, message) | True only when the target has a record and the record equals the message. |
| Record(targetId, message) | Replaces the record of the target. |

## State transitions

- No record, then a message starts to send: the record is set.
- Record R, then a message M starts to send: the record becomes M.
- A message that the level filter, the exclude option, or the send cap drops: no change.
- A failure message equal to R for that target: dropped for that target, no change.
- Service restart: all records are lost.

## Failure streak (unchanged data)

The streak set keeps its meaning. It opens at the first failure of a pair. It decides only the "recovered" message.
