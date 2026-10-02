# Research: Suppress a failure message only when the last message sent was the same

No item needed clarification. This file records the design decisions.

## R-001: Where to keep the last-sent record

- **Decision**: Keep it in a new class, `NotificationLastSentState`. The worker owns it, as for `NotificationStreakState`.
- **Rationale**: The worker reads all work items one at a time. One owner needs no lock. The guard pattern of the streak state proves the rule in tests.
- **Alternatives considered**: A record in the channel code. Rejected, because the channel does not know the queue or the sequence. A shared static dictionary. Rejected, because tests would share state.

## R-002: Key of the record

- **Decision**: Key by target ID. The value holds queue ID, sequence key, status, and final text.
- **Rationale**: The spec says "for each notification target" (FR-001). An exact match of the final text is the rule (Assumptions).
- **Alternatives considered**: Key by queue and sequence. Rejected, because this is the defect (FR-006). Compare the text only. Rejected, because FR-003 names four fields.

## R-003: The per-target decision

- **Decision**: `Decide` always returns `Failure` for a failure and opens the streak. `StartSend` drops the target when the last message of that target is the same.
- **Rationale**: The streak must open on the first failure, so that "recovered" works (FR-005). The comparison needs the target, which the worker only knows after it lists the targets.
- **Alternatives considered**: Keep the streak check in `Decide` and add an exception. Rejected, because `Decide` has no target.

## R-004: When to record

- **Decision**: Record a message when the worker starts the send, on the worker thread, after the send cap check.
- **Rationale**: FR-008 says a failed send counts and a dropped message does not. The worker thread runs the record in item order, so the next item sees the result at once. The record does not wait for the send.
- **Alternatives considered**: Record after the send succeeds. Rejected, because it needs a lock and breaks FR-008.

## R-005: Alerts and cancelled messages

- **Decision**: Alerts and "cancelled" messages update the record. They are never suppressed.
- **Rationale**: Clarification I2. They are messages in the chat.
- **Alternatives considered**: Ignore them. Rejected by the spec.

## R-006: Restart and removed targets

- **Decision**: No storage. A target that is added later has a new ID and no record.
- **Rationale**: Clarification Q3 and I2. The state is small.
- **Alternatives considered**: Clear records on target delete. Not needed. A stale record cannot match a new target ID.
