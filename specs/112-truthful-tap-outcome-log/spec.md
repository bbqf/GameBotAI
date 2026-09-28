# Feature Specification: Truthful tap outcome and an execution log for a single step

**Feature Branch**: `claude/resolve-github-issue-whu0sd` (spec number 112)  
**Created**: 2026-09-28  
**Status**: Implemented  
**Input**: GitHub issue #222 (B-021): "a 'not executed' PrimitiveTap outcome must mean that no input went to the device, and POST /api/steps/execute must write an execution log". Full description: see the issue and the feature description that started this spec.

## Background

On 2026-09-23, on `emulator-5556`, the issue owner sent `POST /api/steps/execute` with a `PrimitiveTap` step. The response was `accepted: 0` with the status `skipped_detection_failed` and the reason `detection_failed_after_3_retries`. Approximately 10 to 20 seconds later, a capture showed the screen that a tap on the target button opens. No other client sent input to the device. The issue owner saw this problem one time only, and did not reproduce it.

The issue owner could not find what the service sent, because `POST /api/steps/execute` writes no execution-log entry. `GET /api/execution-logs` for that time range showed nothing.

A code review found one path that can cause this result. The service sends the tap to the session. After that, the service reads back the executed point and the detection confidence. If an error or a cancellation occurs in that code, the error handlers report a "not executed" outcome with `accepted: 0`, but the input already went to the device.

The issue owner uses a "not executed" outcome for safety analysis of taps near controls that spend currency. Thus a "not executed" outcome must be true.

## Clarifications

### Session 2026-09-28

The pipeline ran without a human reviewer. Thus the clarify step selected each answer. Each answer has a rationale.

- Q: Which status does the outcome show when an error or a cancellation occurs after the session accepted the tap input? → A: The status is `executed`. The reason is `executed_then_error` for an error, and `executed_then_cancelled` for a cancellation. `accepted` is the count that the session returned. The outcome has the resolved point, the executed point when the service can read it, and the detection confidence when the service can read it. Rationale: the input went to the device, so `executed` is true. The existing clients, the command status and the execution log already treat `executed` as "input sent". The reason tells the caller about the problem after the dispatch.
- Q: Which status does the outcome show when the session itself fails or is cancelled during the dispatch? → A: A new status `dispatch_unknown`, with the reason `dispatch_error` or `dispatch_cancelled`, `accepted: 0`, and the resolved point. The execution log shows the step outcome `dispatch_unknown`, not `not_executed`. Rationale: the service cannot know whether the device got the input. A "not executed" status would break the contract, and `executed` would claim too much.
- Q: Which component writes the execution-log entry for a single step call, and how does the entry show a timeout? → A: The command executor writes the entry, in the same place for all outcomes, after it resolved and checked the session. The executor, not the endpoint, owns the 10-second limit of the step call, so it can tell a timeout from a cancellation by the caller. For a timeout, the step outcome in the entry has the status `timeout`. For a cancellation by the caller, the status is `cancelled`. The endpoint response for a timeout does not change. Rationale: one writer gives exactly one entry for each call, and the executor has the resolved session id.
- Q: What is the form of the execution-log entry? → A: The execution type is `step`. The object reference has the object type `step`, the object id equal to the session id, and the display name "<StepType> step". The final status is `success` when the step status is `executed`, and `failure` for all other statuses. The entry has one step outcome and one detail item. The detail attributes are `sessionId`, `stepType`, `status`, `reason`, `resolvedX`, `resolvedY`, `executedX`, `executedY`, `detectionConfidence`, `accepted`, `startedAtUtc` and `durationMs`. Rationale: this form uses the existing entry model without a change to the stored format. A caller can filter by `objectType=step` and by the session id.
- Q: What does the service do when the execution-log write fails? → A: The service writes a warning to the service log, and returns the step outcome to the caller without a change. The write uses no cancellation token from the call, so a timeout or a cancellation does not stop the write. Rationale: the log is for diagnosis. A log failure must not hide the result of an input that the service already sent.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - A "not executed" tap outcome is true (Priority: P1)

An operator sends a single `PrimitiveTap` step, or runs a command, sequence or queue that has a `PrimitiveTap` step. When the step outcome says that the tap was not executed, the operator can trust that the service sent no input to the device for that step. When the service sent the input and a problem occurred after that, the outcome says that the input was sent.

**Why this priority**: This is the safety contract that the issue asks for. An operator makes decisions about taps near controls that spend currency from this outcome.

**Independent Test**: With a test double for the session, make the code after the dispatch fail. The outcome shows that the input was sent and `accepted` is the count from the session. With a test double that never finds the image, the session gets no input and the outcome is "not executed".

**Acceptance Scenarios**:

1. **Given** a `PrimitiveTap` step whose image the service finds, **When** an error occurs after the service sent the tap to the session, **Then** the outcome does not have a "not executed" status, `accepted` is the count that the session returned, and the outcome has the resolved point.
2. **Given** a `PrimitiveTap` step whose image the service finds, **When** a cancellation occurs after the service sent the tap to the session, **Then** the outcome does not have a "not executed" status, `accepted` is the count that the session returned, and the outcome has the resolved point.
3. **Given** a `PrimitiveTap` step whose image the service does not find on any attempt, **When** the retries end, **Then** the service sent no input to the session, and the outcome is `skipped_detection_failed` with `detection_failed_after_{n}_retries` and `accepted: 0`.
4. **Given** a `PrimitiveTap` step, **When** the service finds the image and sends the tap with no error, **Then** the outcome is the same as before this feature.

---

### User Story 2 - A single step call writes an execution-log entry (Priority: P1)

An operator sends `POST /api/steps/execute`. Later, the operator reads `GET /api/execution-logs` for that time range and finds one entry for the call. The entry shows the session, the step type, the final outcome, the resolved point, the executed point, the `accepted` count and the time.

**Why this priority**: Without this entry, the operator cannot find later what the service sent. The issue owner could not examine the 2026-09-23 event for this reason.

**Independent Test**: Send `POST /api/steps/execute` against a test session. Read `GET /api/execution-logs` with a time filter that includes the call. The response has one entry for the call with the expected data.

**Acceptance Scenarios**:

1. **Given** a session that runs, **When** the operator sends `POST /api/steps/execute` and the call reaches the step execution, **Then** the service writes one execution-log entry, and `GET /api/execution-logs` with the time filter returns it.
2. **Given** a step call that ends with any outcome (executed, not executed, cancelled), **When** the service writes the entry, **Then** the entry has the session id, the step type, the status, the reason, the resolved point, the executed point, the `accepted` count and the timestamp.
3. **Given** a step call that takes more than 10 seconds, **When** the endpoint returns `status: "timeout"`, **Then** the service also writes one execution-log entry for the call with the timeout status.
4. **Given** a step call that the endpoint rejects before step execution (validation error, no session, a session that does not run), **When** the endpoint returns the error, **Then** the service writes no execution-log entry for the call.

---

### User Story 3 - The API documentation states the contract (Priority: P2)

A client author reads the API documentation for `POST /api/steps/execute`. The text states that a "not executed" status means that the service sent no input, and that each call writes an execution-log entry.

**Why this priority**: Client authors must know the contract to use it. The code change is more important, so this story is P2.

**Independent Test**: Open the Swagger document. The description of the steps execute operation contains the two statements.

**Acceptance Scenarios**:

1. **Given** the Swagger document, **When** a client author reads the steps execute operation, **Then** the text states that a "not executed" status means that no input was sent, and that the call writes an execution-log entry.

---

### Edge Cases

- The session itself fails or is cancelled while it sends the input. The service cannot know whether the device got the input. The outcome must not say "not executed" in this case, because the service cannot promise that no input went to the device. The outcome has the status `dispatch_unknown`.
- The execution-log write fails. The step call must still return its outcome to the caller. The service writes a diagnostic message to the service log.
- The caller cancels the HTTP request. The step call stops. The service still writes the execution-log entry, with the step status `cancelled`, if the call reached step execution.
- A command, sequence or queue runs a `PrimitiveTap` step. The truthful outcome rule applies there too, because these paths use the same code. Their execution-log behavior does not change.
- The step type is not `PrimitiveTap` (for example `KeyInput`, `Swipe`, `WaitForImage`, `EnsureGameRunning`). The step call also writes an execution-log entry. The outcome logic of these step types does not change. The contract of FR-001 is for `PrimitiveTap` only. For a timeout of another step type, the entry and the response show `accepted: 0`, as the response of today does.
- The 10-second limit stops a `PrimitiveTap` step. The step gives its own outcome, as before this feature. Before a dispatch, the outcome is `cancelled`. The endpoint returns the outcome with HTTP 202, and the entry has the same outcome.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The service MUST NOT return a "not executed" status (`skipped_detection_failed`, `cancelled`, or any other `skipped_*` status) with `accepted: 0` for a `PrimitiveTap` step after the service gave the tap input to the session.
- **FR-002**: When an error or a cancellation occurs after the session accepted the tap input, the outcome MUST have the status `executed` and the reason `executed_then_error` or `executed_then_cancelled`. `accepted` MUST be the count that the session returned. The outcome MUST have the resolved point and, when known, the executed point and the detection confidence.
- **FR-003**: When the service does not find the image on any attempt, the service MUST NOT send input to the session for that step.
- **FR-004**: When the session fails or is cancelled during the dispatch itself, the outcome MUST have the status `dispatch_unknown`, the reason `dispatch_error` or `dispatch_cancelled`, `accepted: 0`, and the resolved point. The execution log MUST show the step outcome `dispatch_unknown`, not `not_executed`.
- **FR-005**: Each `POST /api/steps/execute` call that reaches step execution MUST write exactly one execution-log entry through the existing execution-log service. The command executor MUST write the entry after it resolved and checked the session.
- **FR-006**: The execution-log entry MUST have the execution type `step` and the object type `step`, with the session id as the object id. The entry MUST have the session id, the step type, the final step outcome (status, reason, resolved point, executed point), the `accepted` count, the start time, the duration and the timestamp. The final status MUST be `success` for the step status `executed`, and `failure` for all other statuses.
- **FR-007**: `GET /api/execution-logs` MUST return the entry with the existing time filter.
- **FR-008**: The command executor MUST own the 10-second limit of the step call. When the limit stops a step that gives no outcome of its own, the service MUST write one execution-log entry with the step status `timeout`, and the endpoint MUST return `status: "timeout"` as before. A `PrimitiveTap` step that the limit stops gives its own outcome, as before this feature (`cancelled` before a dispatch, `executed` or `dispatch_unknown` after the start of a dispatch). The endpoint returns that outcome, and the entry has it. When the caller cancels the call, the entry MUST have the step status `cancelled`.
- **FR-009**: A failure of the execution-log write MUST NOT change the response of the step call. The service MUST write a warning to the service log. A timeout or a cancellation of the call MUST NOT stop the write.
- **FR-010**: The Swagger text for `POST /api/steps/execute` and the related documentation MUST state that a "not executed" status means that no input was sent, and that the call writes an execution-log entry.
- **FR-011**: The feature MUST NOT change the detection thresholds, the retry count, the retry progression, the tap jitter, or the hold duration.
- **FR-012**: The response shape of `POST /api/steps/execute` MUST NOT change, except for the new status and reason values that FR-002 and FR-004 need.
- **FR-013**: All existing tests MUST continue to pass.

### Key Entities *(include if feature involves data)*

- **Step outcome**: The result of one step. It has the step order, the status, the reason, the resolved point, the executed point, the detection confidence and (for a press and hold) the hold duration.
- **Execution-log entry**: One record in the execution log. For a single step call, it has the execution type `step`, the session id, the step type, the step outcome, the `accepted` count, the final status, the start time, the duration and the timestamp.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: In 100% of the test cases where the service sent a tap to the session, the outcome does not report "not executed" with `accepted: 0`.
- **SC-002**: In 100% of the test cases where the image is not found on any attempt, the session gets no input.
- **SC-003**: For 100% of the single step calls that reach step execution, and also for a timeout, an operator can find one execution-log entry for the call with the time filter.
- **SC-004**: An operator can find the session, the step type, the outcome and the points of a single step call from the execution log only, with no other data.
- **SC-005**: All existing automated tests continue to pass.

## Assumptions

- The issue owner saw the 2026-09-23 event one time only. This feature closes the known code path that can give a false "not executed" outcome, and adds the log that makes the next event possible to examine. It does not prove the cause of that event.
- The existing execution-log service and its storage stay as they are. The feature adds a new entry kind for a single step call, and does not change the storage format of existing entries.
- The feature does not change the execution-log behavior of commands, sequences and queues.
- The feature does not add device-level or emulator-level diagnostics.
- The feature does not examine the unexplained return to the city screen at 20:44:20.
