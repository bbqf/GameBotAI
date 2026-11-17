# Feature Specification: Trace Noise Cleanup

**Feature Branch**: `190-trace-noise-cleanup`  
**Created**: 2026-11-17  
**Status**: Implemented  
**Input**: User description: "cleanup waste from the service traces, the slow image match is spamming the traces and was never useful up till now. Cleanup this, but also check what other messages appear in the traces regularly but do not convey any real message"

## Background

The service writes its traces to its console window. The default trace level is Warning. Thus, at the default level, each Warning message is visible to the operator.

An audit of the trace messages found these facts:

- **"Slow image match for '{ReferenceImageId}': {ElapsedMs}ms"** (Warning). The service writes this message each time an image-match evaluation takes more than 50 ms. A template match on a full screen frame almost always takes more than 50 ms. Thus the message comes for almost each evaluation, many times each minute while a queue runs. The message tells of no fault, and the operator can do nothing about it. The user confirms that the message was never useful.
- **"PrimitiveTap step {StepOrder}: target not detected after {TotalCycles} retry cycles."** (Warning, event 6012). A tap step that does not find its target is a normal result in a sequence that taps a dialog only when the dialog shows. The execution log already records this result for the step (`skipped_detection_failed`). Thus the Warning repeats data that the execution log has, and it comes regularly during healthy runs.
- **"No cached session for command {CommandId}: game {GameId}, device {AdbSerial}."** (Warning, event 6007). No code writes this message. It is a dead definition. Two more definitions in the same file also have no caller: "Using cached session for command …" (Information, event 6006) and "EvaluateAndExecute bypassed trigger for command …" (Information, event 6002).

The audit found no other message at Warning level or higher that comes regularly in a healthy run. All other Warning messages tell of a real fault. Examples are a device that does not answer, a damaged file, a failed notification, and a sequence that exceeded its time limit. Messages at Information and Debug level do not show at the default level, and they stay as they are.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - No image-match duration messages in the traces (Priority: P1)

An operator runs one or more queues and looks at the service console to find problems. Today, the "Slow image match" messages fill the console, and the operator cannot see the messages that tell of real problems. After this change, the service does not write a message for the duration of an image match.

**Why this priority**: This is the message that the user names. It causes most of the noise.

**Independent Test**: Run a queue with sequences that use image-match triggers, at the default trace level, for 10 minutes. Count the trace lines that tell of image-match duration. The count must be 0.

**Acceptance Scenarios**:

1. **Given** the default trace level and a queue that evaluates image-match triggers, **When** an image-match evaluation takes more than 50 ms, **Then** the service writes no trace line about the duration.
2. **Given** an operator who set the trace level to Debug or Trace, **When** an image-match evaluation takes a long time, **Then** the service also writes no trace line about the duration.
3. **Given** any image-match evaluation, **When** the evaluation completes, **Then** its result (satisfied or not, similarity, reason) is the same as before this change.

---

### User Story 2 - Normal "target not found" tap results do not show as warnings (Priority: P2)

An operator runs sequences that tap a target only when it shows on the screen. When the target does not show, the step is skipped. This is normal. After this change, the service does not write a Warning for this normal result. The execution log continues to show the step result.

**Why this priority**: The message comes regularly in healthy runs, but less often than the image-match message.

**Independent Test**: Run a PrimitiveTap step with retries whose target is not on the screen, at the default trace level. Make sure that no Warning line comes for the step and that the execution log shows the step as `skipped_detection_failed`.

**Acceptance Scenarios**:

1. **Given** the default trace level, **When** a PrimitiveTap step does not find its target after all retry cycles, **Then** the service writes no Warning line for this result.
2. **Given** the trace level is Debug, **When** a PrimitiveTap step does not find its target after all retry cycles, **Then** the service writes one Debug line with the step order and the number of retry cycles.
3. **Given** a PrimitiveTap step that does not find its target, **When** the step ends, **Then** the execution log records the step result as before this change.

---

### User Story 3 - Remove trace message definitions that no code uses (Priority: P3)

A developer reads the trace message definitions to know what the service can write. After this change, the list has no definition that no code uses.

**Why this priority**: This does not change what the operator sees. It only removes code that can cause confusion.

**Independent Test**: Search the source code. The definitions of events 6002, 6006, and 6007 are not in the source code.

**Acceptance Scenarios**:

1. **Given** the source code after this change, **When** a developer searches for the "No cached session", "Using cached session", and "bypassed trigger" messages, **Then** the message definitions are not there.

---

### Edge Cases

- An image match that is really slow (for example, more than 1 second) because the computer is overloaded. The service does not write a message for this. Other signals (a step time limit, a sequence watchdog, a queue liveness check) already tell of the effect when it causes a problem.
- An operator who uses the trace level to find a problem with a PrimitiveTap step. At Debug level, the operator continues to see each retry cycle and the final "not detected" result.
- A logging policy file that is on the computer now and sets a component to Warning or to Information. The change must not need an edit to this file.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The service MUST NOT write a trace message about the duration of an image-match evaluation, at any trace level.
- **FR-002**: The result of an image-match evaluation (status, similarity, reason, time of evaluation) MUST stay the same as before this change.
- **FR-003**: The service MUST write the "target not detected after all retry cycles" message for a PrimitiveTap step at Debug level, not at Warning level.
- **FR-004**: The text and the data of the PrimitiveTap message (step order, number of retry cycles) MUST stay the same.
- **FR-005**: The execution log entry for a PrimitiveTap step that does not find its target MUST stay the same as before this change.
- **FR-006**: The source code MUST NOT contain the message definitions for events 6002 ("bypassed trigger"), 6006 ("Using cached session"), and 6007 ("No cached session"), because no code writes them.
- **FR-007**: All other trace messages MUST keep their level and their text.
- **FR-008**: Automated tests MUST make sure that the image-match evaluator writes no trace line for a slow evaluation, and that the PrimitiveTap "not detected after all retry cycles" message is at Debug level.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: In a 1-hour queue run with image-match triggers and no faults, at the default trace level, the console shows 0 lines about image-match duration (today: many lines each minute).
- **SC-002**: In the same run, each Warning line that shows tells of a fault that the operator can examine. A sequence step that skips because its target is not on the screen causes 0 Warning lines.
- **SC-003**: The results of all current image-match and PrimitiveTap tests stay the same.

## Assumptions

- "Service traces" means the console output of the service. The service writes no trace file.
- The default trace level is Warning, as set by the logging policy. Thus the cleanup is about messages at Warning level or higher that come in healthy runs.
- No operator uses the "Slow image match" message for diagnosis. The user confirms this.
- A PrimitiveTap step that does not find its target is a normal result, because the execution log records it and the sequence continues. If the step must find its target, other parts of the sequence (a condition or a later step) show the failure.
- Messages at Information or Debug level that come often are not in scope, because they do not show at the default level and an operator turns them on only to find a problem.
- The audit covers only the messages that GameBot code defines. It does not cover framework messages (for example, `Microsoft.AspNetCore` at Warning), because the live console was not available. The code has no HTTPS redirection, so the usual repeated framework warning about the HTTPS port cannot occur. If the live console shows other repeated framework messages, add them with `/speckit-clarify`.
- This feature does not change the web UI. Thus it has no phone support level.

## Out of Scope

- A new trace file, a trace viewer, or a change to how the console output is formatted.
- A change to the default trace level or to the logging policy file.
- Rate limits for repeated messages, or one summary line for a group of repeated messages.
