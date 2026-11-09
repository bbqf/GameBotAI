# Research: Show the queue name in notify messages

## R-001: Where to add the label

**Decision**: Add the label in `QueueNotificationWorker.HandleText`, with a new method
`NotificationMessageFormatter.FormatNotifyText`.

**Rationale**: The worker already reads the queue repository for the status messages and the
device alerts. The formatter already holds the `name : content` style. The send request already
has the queue ID in `ScopeKey`. The dedupe and the step logic in `SequenceExecutionService` do not
see the label, so the dedupe stays the same (FR-007).

**Alternatives considered**:
- Add the label in `SequenceExecutionService.SendToTargetsAsync`. Rejected: the service would need
  a queue read and a second copy of the style rule. The worker has both.
- Add a queue name field to `TextSend`. Rejected: it changes the `SendText` contract and all test
  doubles. The worker can read the name from the ID.

## R-002: How to know that a step runs outside a queue

**Decision**: Treat `ScopeKey == NotifyPayload.AdhocScope` as "no queue". Send the text with no label.

**Rationale**: `SendToTargetsAsync` sets `ScopeKey` to `adhoc` when `originatingQueueId` is empty.
No other code path writes this value as a queue ID. A queue with the ID `adhoc` is not possible
in practice, because queue IDs are generated.

**Alternatives considered**: Add a nullable queue ID to `TextSend`. Rejected for the reason in R-001.

## R-003: Name rules

**Decision**: Use the queue name when it is not blank. Use the queue ID when the name is blank,
when the queue does not exist (deleted during the run), or when the read fails.

**Rationale**: This follows `Format` and `FormatAlert`, which replace a blank name with the ID
(spec FR-002). A deleted queue or a failed read MUST NOT drop the message (FR-007). The read
happens when the worker handles the item, so a rename during the run gives the new name (FR-006).

**Alternatives considered**: Send with no label when the read fails. Rejected: the reader would
lose the source with no sign of the error. The ID is still a valid source.

## R-004: Label format and multi-line text

**Decision**: The result is `{label} : {text}`. The text is not changed. A text with many lines
keeps its lines. The label is on the same line as the first line (clarification Q2).

**Rationale**: Same style as the other queue messages. No symbol, as the author controls the text.

**Alternatives considered**: A label on its own line, or a bell symbol. Both were rejected in
clarification.

## R-005: Webhook path

**Decision**: No change. The `FailureNotificationEvent` already has `QueueId` and `QueueName`.

**Rationale**: Spec FR-009. A change to the payload would need a new `SchemaVersion`.

## R-006: Existing tests

**Decision**: Tests that send `SendText` with the queue ID `q1` and expect the text `Plain text 1`
change their expected value. Add a queue with the ID `q1` to the test repository, or expect the ID
as the label. Add tests for each rule in R-002 and R-003.

**Rationale**: These tests check the exact text. The label is an intended change in this text.
