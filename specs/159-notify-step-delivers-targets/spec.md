# Feature Specification: Notify step delivers to the notification targets

**Feature Branch**: `159-notify-step-delivers-targets`
**Created**: 2026-10-22
**Status**: Implemented
**Input**: GitHub issue #335 "FR-032: the notify step should deliver to the configured notification targets". Closes #335.

## Clarifications

### Session 2026-10-22

- Q: What does the target receive as text? → A: The `message` of the step, without a queue or sequence prefix. Rationale: the issue wants the author text to reach the operator, and the queue notice already has its own format.
- Q: Is the step outcome changed when no enabled target exists or a target fails? → A: No. The step stays `success` and the fault is only logged. Rationale: this matches the current behaviour of notification delivery and the non-goals.
- Q: What if the list of targets cannot be read? → A: Treat it as "no enabled target" and apply FR-006. Rationale: a read fault must not fail the run.
- Q: What is the time limit for the step to wait for delivery? → A: The step waits at most the existing send time limit of the notification worker plus a short fixed margin. After that, the result is "not delivered" and the step stays `success`. Rationale: a stuck target must not hold the run.
- Q: What is the result when only some targets accept the message? → A: The result is "delivered" when at least one target accepts it. The log names how many targets failed. The step stays `success`. Rationale: the operator got the message on at least one target.
- Q: Does a step run send more than one message to a target? → A: No. One message per enabled target per step run. Rationale: a repeat would spam the operator.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Operator gets the notify message on the enabled targets (Priority: P1)

An operator writes a sequence with a `notify` step. The step has a `message` and has no `url`. The operator has at least one enabled notification target (for example a `telegram` target). When the step runs, the message goes to each enabled target. The operator reads the message on the target and can tell, for example, which quest is available.

**Why this priority**: This is the defect in the issue. Today the step says "success" but the message does not reach the operator. The operator cannot use `notify` to learn that a quest matched.

**Independent Test**: Create one enabled notification target. Run a sequence that has a `notify` step with a `message` and no `url`. Check that the target receives the message text.

**Acceptance Scenarios**:

1. **Given** one enabled `telegram` target and a `notify` step with `message` "Quest A is ready" and no `url`, **When** the step runs, **Then** the target receives a message that contains "Quest A is ready".
2. **Given** two enabled targets, **When** a `notify` step without `url` runs, **Then** both targets receive the message.
3. **Given** one enabled target and one disabled target, **When** a `notify` step without `url` runs, **Then** only the enabled target receives the message.
4. **Given** at least one enabled target, **When** a `notify` step without `url` runs, **Then** the log does not contain "notification not delivered: no destination configured".

---

### User Story 2 - The `url` field still overrides the default (Priority: P2)

An author sets `url` on a `notify` step. The step sends the message to that `url`, as before. The step does not send the message to the notification targets.

**Why this priority**: This keeps current behaviour for existing sequences. It is a guard, not new value.

**Independent Test**: Run a `notify` step with a `url` while an enabled target exists. Check that only the `url` receives the message.

**Acceptance Scenarios**:

1. **Given** an enabled target and a `notify` step with a `url`, **When** the step runs, **Then** the message goes to the `url` and not to the target.

---

### User Story 3 - The API documentation names the default destination (Priority: P2)

An author reads the OpenAPI text of the `notify` action. The text states that, when `url` is not set, the message goes to the enabled notification targets. The text says where the targets are managed.

**Why this priority**: The old text did not say where the default destination comes from. This caused the confusion in the issue.

**Independent Test**: Read the `notify` text in the OpenAPI document. Check that it names the enabled notification targets as the default destination.

**Acceptance Scenarios**:

1. **Given** the published OpenAPI document, **When** an author reads the description of `notify` and of its `url` field, **Then** the text states that the default destination is the set of enabled notification targets.

---

### Edge Cases

- No enabled target exists and no `url` is set: the step keeps its current behaviour and logs that no destination is configured. The step does not fail the run.
- The list of targets cannot be read: the step treats this as "no enabled target".
- One target fails to accept the message and another accepts it: the accepting target still gets the message. A delivery fault does not fail the sequence step, as with other notification delivery.
- The `message` is empty or missing: the existing validation of the `notify` step applies. This feature does not change it.
- A step-through (dry run) of a `notify` step: the step does not send a message, as today.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: A `notify` step with a `message` and no `url` MUST send the message to every enabled notification target.
- **FR-002**: The message that reaches a target MUST contain the `message` text of the step, so the operator can read it.
- **FR-003**: A `notify` step without `url` MUST NOT send to a disabled target.
- **FR-004**: When at least one enabled target exists, the step MUST NOT log "notification not delivered: no destination configured".
- **FR-005**: When `url` is set, the step MUST send to that `url` as before. The meaning of `url` MUST NOT change.
- **FR-006**: When no enabled target exists and no `url` is set, the step MUST keep its current behaviour.
- **FR-007**: A delivery fault on one target MUST NOT stop delivery to the other enabled targets.
- **FR-008**: The OpenAPI text of `notify` MUST state that the default destination is the set of enabled notification targets.
- **FR-009**: The queue-level notification level behaviour and its message format MUST NOT change.
- **FR-010**: This feature MUST NOT add a notification target type, and MUST NOT change how targets are created or managed.
- **FR-011**: The text sent to a target MUST be the step `message`, with no queue or sequence prefix. Each enabled target MUST get one message per step run.
- **FR-012**: The step outcome MUST stay `success` when no target exists or a delivery fails. The fault is only logged.
- **FR-013**: The work MUST start with a regression test for "no `url`, one enabled target, the message reaches the target". The test MUST be written first, and MUST be shown to fail on the current code, before the fix is made.
- **FR-014**: In step-through (dry run) mode, a `notify` step MUST NOT use the target path and MUST NOT send a message. A test MUST cover this.
- **FR-015**: When the work is done, the implementation MUST set the Status of this spec to Implemented and MUST update `specs\STATUS.md`. The implementation MUST also set the Status of spec 137 as FR-018 states.
- **FR-016**: The step MUST wait at most the existing send time limit of the notification worker plus a short fixed margin. A test MUST show that a target which does not answer in this time gives the result "not delivered", and that the step stays `success`.
- **FR-017**: When at least one target accepts the message, the result MUST be "delivered". When some targets fail, the log MUST name how many targets failed. A test MUST cover this case.
- **FR-018**: The implementation MUST set the Status of spec 137 to "Implemented (iterated by 159)" and MUST update the row of spec 137 in `specs\STATUS.md` to match. This is not conditional.
- **FR-019**: Before the implementer writes the dry-run test of FR-014, the implementer MUST confirm the step-through code path and outcome name for `notify` in `StepThroughActionPreview`.

### Key Entities

- **Notify step**: A sequence step of type `notify`. It has a `message` and an optional `url`.
- **Notification target**: A configured destination (for example `telegram`) with an enabled flag. The queue notification level already uses the enabled targets.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: In a test with one enabled target, 100% of `notify` steps with a `message` and no `url` deliver the message text to that target.
- **SC-002**: In a test with N enabled targets, each of the N targets receives the message once per step run.
- **SC-003**: A `notify` step with `url` set sends nothing to the notification targets.
- **SC-004**: The OpenAPI text of `notify` names the enabled notification targets as the default destination.
- **SC-005**: Existing tests for the queue notification level pass with no change to their expected output.

## Assumptions

- "Enabled notification targets" are the same targets that the queue notification level uses and that `GET /api/notifications/targets` lists.
- The step `message` reaches the target as the message text, with no queue or sequence prefix.
- A step with no enabled target and no `url` is not an error. This keeps the current behaviour.
- This feature iterates spec 137 (notify alert dedupe window), which last changed the `notify` step. It also relates to spec 087 (queue failure policy and outbound notification), which first set up notification delivery.
- The dry-run result of a `notify` step is the result that the existing step-through preview returns for `notify`. This feature adds no new outcome.
