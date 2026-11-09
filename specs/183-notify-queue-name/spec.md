# Feature Specification: Show the queue name in notify messages

**Feature Branch**: `183-notify-queue-name`
**Created**: 2026-11-09
**Status**: Implemented
**Input**: User description: "An improved version of notification is needed for an extended notification. Currently the notification may include additional text, however the message doesn't show which queue the notification is for. I want it to be clear which queue is the notification coming for."

## Clarifications

### Session 2026-11-09

- Q: Which message is in scope? → A: Only the plain-text `notify` step message sent to the notification targets. Queue status messages and device alerts are out of scope.
- Q: Where does the queue label go? → A: On the same line, before the author text: `Queue name : text`. No symbol is added.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Know which queue sent a notify message (Priority: P1)

An operator runs many queues. A sequence has a `notify` step with author-written text. Today, the message arrives with only that text. The operator cannot tell which queue ran the step. With this feature, the message shows the queue name before the author text. The operator sees the source queue at once.

**Why this priority**: This is the full request. Without the queue name, the operator must look in the logs to find the source. This wastes time when many queues run.

**Independent Test**: Run one queue that contains a sequence with a `notify` step. Check that the message received by the target shows the queue name and the author text.

**Acceptance Scenarios**:

1. **Given** queue "Farm A" runs a sequence with a `notify` step with the text "Daily reward ready", **When** the step runs, **Then** each enabled target receives one message that shows "Farm A" and "Daily reward ready".
2. **Given** two queues "Farm A" and "Farm B" run the same sequence with the same `notify` text, **When** both steps run, **Then** the two messages show different queue names, and an operator can tell them apart.
3. **Given** the queue has a name, **When** the message is made, **Then** the queue name is the first part of the message, in the same style as the other queue messages (queue name, then separator, then content).

---

### User Story 2 - Clear message when no queue applies (Priority: P2)

A `notify` step can run outside a queue, for example in a manual run of a sequence. In this case no queue exists. The message must not show a wrong or empty queue name.

**Why this priority**: This is a boundary case. It must not break existing use, but it is less common than the queue case.

**Independent Test**: Run a sequence with a `notify` step by hand, outside a queue. Check that the message has only the author text and no queue label.

**Acceptance Scenarios**:

1. **Given** a sequence with a `notify` step runs outside any queue, **When** the step runs, **Then** the message has the author text and no queue name, and no empty label.
2. **Given** a queue has no display name, **When** a `notify` step runs in it, **Then** the message shows the queue ID in place of the name, as the other queue messages do.

---

### Edge Cases

- The queue is renamed while the queue runs. The message uses the name at the time the step runs.
- The author text is empty or blank. The step stays invalid as it is today. This feature does not change the rules for the text.
- The author text is very long. The queue name must still show. The existing length rules for the message stay in force.
- The queue name has special characters. The message stays plain text, so the name cannot break the message.
- Dedupe keys: two queues use the same dedupe key. This feature does not change how the dedupe works. A message sent for one queue must not hide a message for another queue only because of the queue label.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: When a `notify` step runs in a queue, the system MUST add the display name of that queue to the message, before the author text.
- **FR-002**: If the queue has no display name or a blank name, the system MUST show the queue ID in place of the name.
- **FR-003**: When a `notify` step runs outside any queue, the system MUST send the author text as it is today, with no queue label.
- **FR-004**: The queue label MUST use the same style as the other queue messages: the queue name, a space, a colon, a space, then the content. The label is on the same line as the first line of the author text. The system adds no symbol, and it does not change the author text.
- **FR-005**: The message MUST stay plain text. A special character in the queue name or author text MUST NOT break the message.
- **FR-006**: The system MUST use the queue name that is valid at the time the step runs.
- **FR-007**: The system MUST send the message to the same targets as today. The set of targets, the order of messages from one sequence, and the delivery result handling MUST NOT change.
- **FR-008**: The change MUST NOT alter the saved form of a sequence. Existing sequences with `notify` steps MUST show the queue name with no edit by the author.
- **FR-009**: A `notify` step that sends to its own URL (a webhook) already carries the queue ID and queue name as fields. This behavior MUST NOT change.

### Key Entities

- **Notify message**: The text a target receives for a `notify` step. It has an optional queue label and the author text.
- **Queue**: The source of the run. It has an ID and an optional display name.

## Assumptions

- "Extended notification" means the message of a `notify` step, which carries author-written text (confirmed in clarification). The messages for queue status and device alerts already show the queue name, and they are out of scope.
- The queue label is always on for a queue run. No setting turns it off. The operator asked for clear source information, and a setting would add scope without a stated need.
- The label goes before the author text, on the same line, in the style "Queue name : text".
- No change is needed for the webhook form of the payload, because it already has the queue fields.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: In 100% of `notify` messages sent from a queue run, the queue name (or the ID when no name exists) shows in the message.
- **SC-002**: An operator can find the source queue of a message from the message alone, with no log lookup, in a test with two queues that send the same text.
- **SC-003**: In 100% of `notify` messages sent outside a queue, the message has no queue label and no empty label.
- **SC-004**: All existing sequences with `notify` steps keep working with no edit.
