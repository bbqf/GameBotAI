# Data Model: Show the queue name in notify messages

This feature adds no stored data. It does not change a saved sequence, a queue, or a target.

## Notify message (text sent to a target)

| Part | Source | Rule |
|------|--------|------|
| Label | Queue display name | Present only when the step runs in a queue. |
| Separator | Fixed | ` : ` (space, colon, space). |
| Body | `message` of the `notify` step | Not changed. Can have many lines. |

### Label rules

1. The step runs outside a queue (`ScopeKey` is `adhoc`): no label and no separator. The text is the body.
2. The step runs in a queue and the queue has a non-blank name: the label is the name.
3. The queue has a blank name, the queue does not exist, or the read fails: the label is the queue ID.

### State

The message has no state. The worker makes it at the time it handles the send. The sends of one
sequence keep their order (order key `notify:<sequenceId>`).

## Queue (read only)

| Field | Use |
|-------|-----|
| `Id` | Fallback label. Key to read the queue. |
| `Name` | Label. |

## Unchanged

- `TextSend` (text, scope key, sequence ID, completion).
- `FailureNotificationEvent` (the webhook payload) and its `SchemaVersion`.
- The dedupe records of `FileSentAlertStore`.
