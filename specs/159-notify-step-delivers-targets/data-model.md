# Data Model: Notify Step Delivers to the Notification Targets

No stored data changes. No new file. The types below are in memory only, in `GameBot.Service.Services.Notifications`.

## TextSend (new, internal record)

A request to send one plain text to all enabled targets.

| Field | Type | Rule |
|-------|------|------|
| Text | string | The step `message`. Not empty. No prefix. At most 1000 characters (the step validation limit). |
| ScopeKey | string | Originating queue id, or `adhoc`. Used for the send order key. |
| SequenceId | string | Used for the send order key and logs. |
| Completion | TaskCompletionSource&lt;TextSendOutcome&gt; | The worker sets it one time. The task runs its continuations asynchronously. |

## TextSendOutcome (new, internal record)

| State | Meaning | Step effect |
|-------|---------|-------------|
| NoTarget | No enabled target exists, or the target list cannot be read | The step uses the old notifier path (`url` empty). |
| Delivered | At least one target accepted the message (the log names the count of failed targets, FR-017) | Step message "notification delivered". |
| Failed(reason) | Targets exist and none accepted, the send could not start, or the guard time limit (`SendTimeout` + `TextSendGuardMargin`, 35 s) expired | Step message "notification not delivered: reason". The reason is safe text and has no secret. |

State rules: `Completion` is set exactly one time, also on a fault. A fault on one target does not change the other sends (FR-007).

## NotificationWork (edit)

Gets a fourth kind, `Text`, with a factory `ForText(TextSend)`. The existing kinds (`Job`, `ResetQueueId`, `Alert`) do not change.

## INotificationDispatcher (edit)

New member: `Task<TextSendOutcome> SendText(string text, string scopeKey, string sequenceId, CancellationToken ct)`. It returns at once with a task, never throws, and never blocks the caller thread.

## Existing entities used, not changed

- `NotificationTarget` (Id, Name, Type, Enabled, fields): read through `INotificationTargetStore.List()`.
- `INotificationChannel`: `SendAsync(target, text, ct)`.
- `FailureNotificationResult`: still returned by the old path and by the step code to the record and dedupe code.
