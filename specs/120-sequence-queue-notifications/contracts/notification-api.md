# Contract: Notification API

All routes need the normal bearer token. Errors use the current shape:
`{ "error": { "code": "...", "message": "...", "hint": "..." } }`. A bad body gives 400, never 500.

## Targets

Route base: `/api/notifications/targets`.

### GET /api/notifications/targets

Returns 200 with an array of `NotificationTargetView`. The secret is never in the body.

```json
[
  {
    "id": "3f2a...",
    "type": "telegram",
    "name": "My phone",
    "enabled": true,
    "settings": { "chatId": "-1001234567890" },
    "hasSecret": true,
    "secretHint": "••••Xy9z",
    "createdAt": "2026-09-30T10:00:00+02:00",
    "updatedAt": "2026-09-30T10:00:00+02:00"
  }
]
```

### POST /api/notifications/targets

Request:

```json
{
  "type": "telegram",
  "name": "My phone",
  "enabled": true,
  "settings": { "chatId": "-1001234567890" },
  "secrets": { "botToken": "123456:ABC..." }
}
```

- 201 with `NotificationTargetView` and a `Location` header.
- 400 `invalid_request` when the type is unknown, the name is empty, a required value is absent, a
  value has a wrong format, or a key is unknown. The message names the field. It never repeats the
  secret value.

### PUT /api/notifications/targets/{id}

Same body as POST. Rules:

- The `type` cannot change. A different type gives 400.
- A absent or empty `secrets.botToken` keeps the stored token.
- A non-empty `secrets.botToken` replaces it.
- 200 with `NotificationTargetView`. 404 `not_found` when the ID does not exist.

### DELETE /api/notifications/targets/{id}

204 on success. 404 when the ID does not exist. The UI target list uses GET, PUT and DELETE for the
list, the edit and the delete actions (FR-013).

### POST /api/notifications/targets/{id}/test

Sends one test message to this target. It ignores the target `enabled` flag, the queue levels, and
the failure streaks. The text is `GameBot test message`. No body.

The target must be saved. The UI action "Save and test" (FR-020) calls POST or PUT first, then this
route. If the save fails, the UI does not call this route.

Response 200 (the HTTP call worked, the send result is in the body):

```json
{ "ok": false, "reason": "Telegram answered 400: Bad Request: chat not found" }
```

- `ok: true` and `reason: null` when the target accepted the message.
- 404 when the ID does not exist.
- The action has a 15 s total limit. On a limit, `ok` is false and `reason` is
  `The target did not answer in time.`.

### GET /api/notifications/types

Returns the known target types and the fields of each type, so the UI can build a form without
type-specific code in the API.

```json
[
  {
    "type": "telegram",
    "displayName": "Telegram",
    "fields": [
      { "key": "botToken", "label": "Bot token", "secret": true, "required": true },
      { "key": "chatId", "label": "Chat ID", "secret": false, "required": true }
    ]
  }
]
```

## Queue level

### PUT /api/queues/{id}/notification-level

Request: `{ "level": "none" | "failure" | "successAndFailure" }`.

- 200 with the queue response (`QueueResponse`).
- 400 `invalid_request` when the level is absent or unknown.
- 404 when the queue does not exist.
- The new level applies to the next sequence that finishes in the queue.
- When the new level is `none`, the handler calls `INotificationDispatcher.ResetStreaks(queueId)`
  after the save. The worker closes each open streak of the queue at once, with no message
  (FR-012, FR-023).
- `DELETE /api/queues/{id}` also calls `ResetStreaks(queueId)` after the delete (FR-023).

### Changed responses

`QueueResponse` and `QueueDetailResponse` get a new field `notificationLevel` (string, one of the
three values). The value is `none` for a queue that never had a level.

`CreateQueueRequest` and `UpdateQueueRequest` accept an optional `notificationLevel`. When absent,
create uses `none` and update keeps the stored value (FR-021). A bad value gives 400.
`POST /api/queues/{id}/duplicate`, the template link route, the game link route and the entry routes
keep the stored level, except that a duplicate gets `none`.

### Store file (hand edit)

`data/notifications/targets.json` is a JSON array of stored targets. The service reads a change
without a restart (FR-018). A bad file keeps the last good list. A hand edit of the queue file is
out of scope. Only the UI and the API set the queue level.

## Message contract (to a target)

The text is one line: `<queue name> : <sequence name> : <circle> <status>`. See the data model for
the circle symbols. A channel that supports color can render the status in color instead of the
circle. The channel must keep the text readable as plain text.

The system sends one message to all enabled targets in parallel. Each message has one total limit of
30 s. If a target does not answer in this time, the system drops the message for that target and
writes one log line. Other targets are not affected. The test message text is
`GameBot test message`. It has no `parse_mode`.

## Dispatcher contract (for a developer)

```csharp
public interface INotificationDispatcher {
  void Enqueue(QueueNotificationJob job);   // never blocks, never throws
  void ResetStreaks(string queueId);        // control message, never dropped
}
```

- Both methods write to the one channel that the worker reads. The worker owns the streak state.
- `QueuesEndpoints` gets this interface from DI.

## Channel contract (for a developer)

```csharp
public interface INotificationChannel {
  string Type { get; }
  string DisplayName { get; }
  IReadOnlyList<NotificationField> Fields { get; }
  string? Validate(NotificationTarget target);          // null when valid, else a safe error text
  Task<NotificationSendResult> SendAsync(NotificationTarget target, string text, CancellationToken ct);
}
```

- `SendAsync` MUST NOT throw. It returns a failed result with a safe reason.
- `SendAsync` MUST NOT put a secret in the reason or in a log line.
- A new channel needs one class and one DI registration. It needs no change to levels, streaks, or
  message creation.
