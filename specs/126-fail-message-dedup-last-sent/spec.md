# Feature Specification: Suppress a failure message only when the last message sent was the same

**Feature Branch**: `126-fail-message-dedup-last-sent`
**Created**: 2026-10-02
**Status**: Implemented
**Input**: User description: "Suppress a failure message only when the last message sent was the same (FR-016). Source: GitHub issue #269. Closes #269."

## Background

Spec 120 (sequence queue notifications), requirement FR-012, keeps one failure streak for each pair of queue and sequence. The first failure of a pair sends a "failure" message and opens the streak. Later failures of the same pair send nothing. The first success sends "recovered" and closes the streak.

The purpose of this rule: a sequence that fails every hour must not fill the chat with the same text.

The rule looks at the pair, not at the chat. On 2026-10-01, queue `PNS.Production (Exo)` (level `successAndFailure`) showed the problem:

| Time | Event | Message |
| --- | --- | --- |
| 19:24:33 | `PNS.CollectResources` fails (`verify-cluster-found`) | sent, the streak opens |
| 20:24:35 | `PNS.EnsureCityScreen` succeeds | sent |
| 20:24:46 | `PNS.CollectResources` fails again | not sent |
| 21:24:54 | `PNS.EnsureCityScreen` succeeds | sent |
| 21:25:05 | `PNS.CollectResources` fails again | not sent |
| 22:25:19 | `PNS.CollectResources` fails again | not sent |

The operator saw two successes and no failure. The failure was real and lasted three hours. After the first message, the other messages hid it.

## Clarifications

### Session 2026-10-02

- Q: Does "the last message sent" count only messages for one target, or all messages? A: Only messages sent through the same notification target. Rationale: the operator reads one chat for each target. The issue says "same notification target".
- Q: Does the first failure of a streak always send? A: Yes, unless the last sent message on the target is equal to it. Rationale: this is one rule for every failure. The streak only decides "recovered".
- Q: Is the record of the last message kept after a restart? A: No, it is in memory only. Rationale: the issue gives no rule for storage. A new message after a restart is harmless.

### Session 2026-10-02 (loop 2, from analyze findings)

- Q: Which messages must the 2026-10-01 timeline give? (U2) Answer: SC-005 now lists them. The last failure at 22:25:19 stays silent.
- Q: Does a failed send count? (I1) Answer: Yes. The edge case now says so.
- Q: Which test must change? (U1) Answer: the existing test with a failure, a "cancelled" message, a failure, and a success. It now expects a second failure message.

### Session 2026-10-02 (loop 1, from analyze findings)

- Q: Does a failed send count as "last sent"? (I1) A: Yes. The service records a message when it starts to send it. If the send to a target fails, the next equal failure for that target is not sent. Rationale: the service cannot know which messages the chat shows. This limit is accepted. In FR-001, "last message sent" means "last message that the service started to send".
- Q: Do device alerts and "cancelled" messages count as the last message? (I2) A: Yes. Any message that the service starts to send through the target counts. A message that the level filter, the exclude option, or the send cap drops does not count. The record is for each target. A target that is added later starts with no record.
- Q: Does this change spec 120 FR-012? (I3) A: Yes, in part. A later failure of one pair is silent only while the last sent message on the target is the same. Spec 120 FR-012 keeps its meaning for "recovered".
- Q: Which word names the sequence? (I4) A: Use "sequence ID" in prose.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - See a repeated failure again after other messages (Priority: P1)

An operator reads the notification chat. A failure message must stay visible. If other messages came after it, the next failure of the same sequence is sent again.

**Why this priority**: This is the defect. Without it, a real failure stays hidden.

**Independent Test**: Feed the notification service the sequence failure (A), success (B), failure (A). Count the messages that it sends.

**Acceptance Scenarios**:

1. **Given** level Success+Failure, **When** failure (A) and then failure (A) occur with no other message between them, **Then** the service sends one message.
2. **Given** level Success+Failure, **When** failure (A), success (B), failure (A) occur, **Then** the service sends three messages.
3. **Given** two queues that share one notification target, **When** failure (A, queue 1), failure (B, queue 2), failure (A, queue 1) occur, **Then** the service sends three messages.

---

### User Story 2 - Keep the "recovered" message (Priority: P2)

The first success after a failure still sends "recovered".

**Why this priority**: Existing behavior that this change must not break.

**Independent Test**: Feed failure (A), failure (A), success (A). The service sends "failure", then "recovered".

**Acceptance Scenarios**:

1. **Given** level Success+Failure, **When** failure (A), failure (A), success (A) occur, **Then** the service sends "failure" and then "recovered".

---

### Edge Cases

- The last sent message is a failure of the same sequence and queue, but with different text (for example, a different failure reason): the service sends the new message.
- The last sent message is for the same sequence but a different queue: the service sends the failure message.
- The level filter, the exclude option, or the send cap drops a message: it does not count as "the last message sent". A send that starts and then fails still counts.
- No message was sent through the target yet (service start): the service sends the failure message.
- The service restarts between two equal failures: the service has no record of the last message and sends the failure message. This is acceptable.
- Two queues share one target: the last message is the last one for that target, from any queue.
- A queue reset or a level change sends no message and does not change the last-message record.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: For each notification target, the service MUST remember the last message that it sent through that target.
- **FR-002**: The service MUST compare each failure message with the last message sent through the same target.
- **FR-003**: The service MUST send the failure message unless the last sent message has the same sequence, the same queue, the same status, and the same text.
- **FR-004**: A message of any other kind between two equal failures (for example a success of another sequence, or a failure of another pair) MUST make the next failure message new, so the service sends it.
- **FR-005**: The first success after a failure MUST still send the "recovered" message. This change MUST NOT change the "recovered" rule.
- **FR-006**: The rule MUST work on the messages that the service sent. It MUST NOT work on the queue or on the sequence alone.
- **FR-007**: The change MUST NOT add new scope, new settings, or a workaround beyond the rule above.
- **FR-008**: The service MUST update the last-message record of a target for each message that it starts to send through that target. This includes a job message, a device alert, and a "cancelled" message. A message that the level filter, the exclude option, or the send cap drops MUST NOT update the record. A failed send still counts as sent.
- **FR-009**: This change replaces spec 120 FR-012 in part: a later failure of one pair is silent only if the last sent message on the target is the same. The "recovered" part of FR-012 stays.
- **FR-010**: When the work ends, the project MUST update its living docs and status tables (constitution Principle V). The Status line of this spec MUST change to "Implemented". The Status line of spec 120 MUST change to "Implemented (iterated by 126)". The row of spec 120 in `specs/STATUS.md` MUST show the same text. `specs/STATUS.md` MUST have a row for this spec. The "Last reviewed" date in `docs/architecture.md` MUST change.

### Key Entities

- **Notification target**: The place where the service sends messages. Many queues can share one target.
- **Last sent message**: For one target, the sequence, queue, status, and text of the most recent message that the service sent.
- **Failure streak**: The existing record of a failure for one pair of queue and sequence. It keeps its meaning for "recovered".

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Feed failure (A), failure (A): exactly 1 message is sent.
- **SC-002**: Feed failure (A), success (B), failure (A): exactly 3 messages are sent.
- **SC-003**: Feed failure (A), failure (A), success (A): the messages are "failure" then "recovered", in that order.
- **SC-004**: Feed failure (A, queue 1), failure (B, queue 2), failure (A, queue 1) on one target: exactly 3 messages are sent.
- **SC-005**: In the 2026-10-01 timeline, the service sends: failure 19:24, success 20:24:35, failure 20:24:46, success 21:24:54, failure 21:25:05. The service sends no message for the failure at 22:25:19, because the last sent message is the same failure.

## Assumptions

- "Same text" means an exact match of the final message text.
- The record of the last sent message is kept in memory only. It is not kept across a service restart.
- Tracker row FR-016 and spec 120 FR-012 are the references. This specification describes behavior only. It does not set code or API details.
