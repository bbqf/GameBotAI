# Contract: Text of a notify message

This contract describes the text that a notification target receives for a `notify` step with no `url`.

## Format

```text
<label> : <message>      when the step runs in a queue
<message>                when the step runs outside a queue
```

- `<message>` is the `message` of the step, not changed.
- `<label>` is the queue name. If the name is blank, the queue does not exist, or the queue read
  fails, `<label>` is the queue ID.
- The text is plain. The channel sends it with no parse mode.

## Examples

| Situation | Text received |
|-----------|---------------|
| Queue "Farm A", message "Daily reward ready" | `Farm A : Daily reward ready` |
| Queue "Farm B", same message | `Farm B : Daily reward ready` |
| Queue with a blank name, ID `q-7` | `q-7 : Daily reward ready` |
| Manual run, no queue | `Daily reward ready` |
| Message with two lines, queue "Farm A" | `Farm A : line one` then `line two` on the next line |

## Unchanged

- The webhook payload (`FailureNotificationEvent`, event type `sequence.notify`) already has
  `queueId` and `queueName`. It has no change and no new `schemaVersion`.
- The `notify` step schema (`message`, `url`, `dedupeKey`, `since`, `within`) has no change.
- The step outcome, the 35 s wait limit, the target list, and the send order have no change.
