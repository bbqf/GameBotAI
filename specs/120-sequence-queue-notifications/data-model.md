# Data Model: Queue Sequence Notifications

## NotificationLevel (enum, Domain)

| Value | JSON text | Use |
|-------|-----------|---------|
| None | `none` | Send no message. Default. |
| Failure | `failure` | Send failure, cancelled and recovered messages. |
| SuccessAndFailure | `successAndFailure` | Send all messages. |

The enum uses a string converter. A queue file without the field reads as `none`.

## ExecutionQueue (changed)

New field:

| Field | Type | Rule |
|-------|------|------|
| `notificationLevel` | NotificationLevel | Default `none`. Set with `PUT /api/queues/{id}/notification-level`. |

No other queue field changes. Old queue files stay valid. A hand edit of the queue file is out of
scope (FR-018).

Keep rules (FR-021):

- A full update, a template link change, a game link change, an entry change and a restart keep the
  saved level.
- A duplicate or a create with no level sets `none`.
- Only the level route (or a create or update request that names a level) writes the field.

## NotificationTarget (new, Domain)

Stored in `data/notifications/targets.json` as a JSON array.

| Field | Type | Rule |
|-------|------|------|
| `id` | string | GUID "N" format. The store makes it on create. Safe-ID pattern `^[A-Za-z0-9_-]+$`. |
| `type` | string | Required. Known value: `telegram`. Case-insensitive on input, stored lower case. |
| `name` | string | Required, 1 to 100 characters after trim. |
| `enabled` | bool | Default `true`. A disabled target gets no message. The test action still works. |
| `settings` | map string to string | Type-specific values. For `telegram`: `botToken` (secret), `chatId`. |
| `createdAt` | timestamp | Set on create. |
| `updatedAt` | timestamp | Set on each change. |

Validation for type `telegram`:

- `botToken` is required on create. It must match `^\d{3,}:[A-Za-z0-9_-]{20,}$` (Telegram token
  shape). On update, an empty token keeps the stored token.
- `chatId` is required. It must be an integer (it may start with `-`) or an `@channelname` value.
- Unknown keys in `settings` are rejected with a 400.

Store behavior (FR-017, FR-018): the store reloads the file when the write time or the length
changes. A corrupt file keeps the last good list.

Each channel class provides its own validation and its list of secret keys, so the store and the
endpoint stay type-neutral.

## NotificationTargetView (API response, not stored)

| Field | Type | Rule |
|-------|------|------|
| `id`, `type`, `name`, `enabled`, `createdAt`, `updatedAt` | as above | |
| `settings` | map | Public keys only (for Telegram: `chatId`). |
| `hasSecret` | bool | True when a secret is stored. |
| `secretHint` | string or null | Last 4 characters of the secret, with a mask prefix. Null when none. |

## NotificationWork (in memory)

The item type of the one channel. It is one of two kinds.

| Kind | Fields | Rule |
|------|--------|------|
| Run job (`QueueNotificationJob`) | `queueId`, `sequenceId`, `status`, `raisedAt` | Written by `Enqueue`. The dispatcher can drop it when 256 jobs wait (with a log line). |
| Reset control message | `queueId` | Written by `ResetStreaks`. Never dropped. The worker removes all streak keys of the queue. |

## QueueNotificationJob (in memory)

| Field | Type | Rule |
|-------|------|------|
| `queueId` | string | Stable queue ID. |
| `sequenceId` | string | Stable sequence ID. |
| `status` | RunOutcome (`success`, `failure`, `cancelled`) | After the watchdog change in research R-008. |
| `raisedAt` | timestamp | For the log only. |

## NotificationMessage (in memory)

| Field | Type | Rule |
|-------|------|------|
| `queueName` | string | Read at send time. Fallback: the queue ID. |
| `sequenceName` | string | Read at send time. Fallback: the sequence ID. |
| `status` | `success`, `failure`, `cancelled`, `recovered` | |

Text format: `<queueName> : <sequenceName> : <circle> <status>`.

| Status | Circle |
|--------|--------|
| success | green (U+1F7E2) |
| recovered | green (U+1F7E2) |
| failure | red (U+1F534) |
| cancelled | yellow (U+1F7E1) |

Example: `Farm-1 : PNS.CollectResources : 🔴 failure`.

The test message is the fixed text `GameBot test message`. It has no circle and no `parse_mode`.

## Failure streak (in memory only)

Key: `(queueId, sequenceId)`. Value: open flag. Owner: the worker of `QueueNotificationDispatcher`.
No other thread reads or writes it (FR-023). No persistence. The state table is in research R-003.

State transitions:

```text
closed --failure (level not None)--> open      [send "failure"]
open   --failure-------------------> open      [send no message]
open   --success (Failure or Success+Failure)--> closed   [send "recovered"]
closed --success (Success+Failure)--> closed   [send "success"]
closed --success (Failure)----------> closed   [send no message]
open   --level change between Failure and Success+Failure--> open   [send no message]
open   --level set to None (ResetStreaks)--> closed   [send no message, at once]
any    --cancelled------------------> same     [send "cancelled" if level allows]
any    --any result (level None)----> same     [send no message, open no streak]
```

Cleanup: `ResetStreaks(queueId)` removes all keys of that queue. The level route calls it after a
save of level None. The delete route calls it after a queue delete.

## NotificationSendResult (in memory)

| Field | Type | Rule |
|-------|------|------|
| `succeeded` | bool | |
| `reason` | string or null | Safe text. Never has a secret. |
