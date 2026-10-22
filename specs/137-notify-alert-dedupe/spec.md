# Feature Specification: Notify alert dedupe window

**Feature Branch**: `137-notify-alert-dedupe`
**Created**: 2026-10-12
**Status**: Implemented (iterated by 159)
**Input**: GitHub issue #238 - "FR-010: send an alert at most once per window (notify dedupe key, or a stepRan condition)".

## Chosen shape

The issue gives two shapes. This feature uses **shape 1 only**: the `notify` step gets the optional fields `dedupeKey` and `since` or `within`. Shape 2 (a `stepRan` condition) is **not** built.

Reason: the problem is that a task cannot record that it sent an alert. Only the service can record a send, and shape 1 puts the record and the check in one place, in the step that sends. Shape 2 adds a new condition type, a new run record for each step, and new reference-scope rules. Shape 2 also cannot stop two sends that run at the same time. Shape 1 is the smaller change that meets every acceptance rule.

## Clarifications

### Session 2026-10-12

- Q: Is the dedupe key shared by all sequences in one queue, or is it per sequence? → A: Shared by all sequences in the queue. The scope is (queue, key). Rationale: the issue says "same key and same queue"; an author who wants separate scopes uses separate keys.
- Q: How can FR-007 (record only on success) and FR-013 (at most one send for concurrent runs) both hold? → A: The service reserves the (queue, key) slot before it delivers, and it releases the reservation if delivery fails. Concurrent runs see the reservation and report `suppressed`. Rationale: this is the only way to keep both rules.
- Q: Does a suppressed step start or extend a window? → A: No. Only a delivered alert sets the window start for later checks. Rationale: a suppressed step that extended the window would hide alerts for ever.
- Q: What does a `within` window use as its limit? → A: Any duration from one second up to 24 hours (`24:00:00` is the maximum). Longer values give 400. Rationale: this bounds the record store (FR-009).
- Q: (From analyze, loop 1) What do reservation expiry, queue delete, record retention, and a missing store do? → A: Reservation expires after 5 minutes; queue delete removes its records; records live 48 hours; a missing store means always send (see Assumptions and FR-009). Rationale: closes gaps C2, C3, C5, C6, C7.
- Q: (Loop 1 analyze, resolved in loop 2) How does preview resolve scope, and does a live reservation show in preview? → A: Same scope rule as a real run; a live reservation shows "would suppress" (FR-014). Rationale: preview must predict the real result.
- Q: (Loop 1 analyze) Which tests must exist for gaps in FR-013, FR-015 and the reservation rule? → A: A concurrent-dispatch test through the notify step (FR-013); a test that a run during a live reservation is `suppressed` and a run after release sends; the existing `lastRun`, notify and channel tests run unchanged as the FR-015 check. Rationale: closes coverage findings.
- Q: What is the outcome text and status of a suppressed step? → A: The step status stays `success` (a notify step never fails a run). The outcome value is `suppressed`; for a send it is `sent`. A step without the new fields keeps its current outcome text. Rationale: FR-010 and the existing "always reports success" rule.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - One alert per window across retries (Priority: P1)

A sequence author writes a `notify` step with a `dedupeKey` and a `since` time of day (for example `02:05`). A daily task fails by time-limit cancellation. Each retry starts and reaches the same `notify` step. Only the first retry in the window sends the alert. Each later retry in the same window skips the alert and reports `suppressed`.

**Why this priority**: This is the whole purpose of the issue. Without it, a chain of time-outs sends one alert every 30 minutes.

**Independent Test**: Run one sequence with a `notify` step that has `dedupeKey` and `since` 10 times in the same window in the same queue (SC-001). Count the alerts that the notifier receives. The count is 1. The step outcomes are 1 `sent` and 9 `suppressed`. A second case starts at least 2 runs of the step at the same time: the notifier receives 1 alert (FR-013).

**Acceptance Scenarios**:

1. **Given** a `notify` step with `dedupeKey` "timeout" and `since` "02:05", and no alert with that key in the queue since the last 02:05, **When** the step runs, **Then** the alert is sent and the step outcome is `sent`.
2. **Given** the same step and an alert with key "timeout" that the same queue sent after the last 02:05, **When** the step runs again, **Then** no alert is sent and the step outcome is `suppressed`.
3. **Given** the same step and a new window (the clock passes the next 02:05), **When** the step runs, **Then** the alert is sent again and the outcome is `sent`.
4. **Given** a `notify` step with `within` "00:30:00" and an alert with the same key sent 10 minutes ago in the same queue, **When** the step runs, **Then** the outcome is `suppressed`. After 30 minutes it is `sent` again.

---

### User Story 2 - Scope by queue and key (Priority: P2)

Two queues run the same sequence, or one sequence has two `notify` steps with different keys. An alert in one scope must not hide an alert in another scope.

**Why this priority**: A wrong scope hides real alerts. This is a safety rule, but it follows after the main flow.

**Independent Test**: Send an alert with key "a" in queue Q1. Run the same step in queue Q2, and run a step with key "b" in Q1. Both send.

**Acceptance Scenarios**:

1. **Given** key "a" was sent in queue Q1 in the window, **When** a `notify` step with key "a" runs in queue Q2, **Then** it sends and reports `sent`.
2. **Given** key "a" was sent in queue Q1 in the window, **When** a `notify` step with key "b" runs in Q1, **Then** it sends and reports `sent`.

---

### User Story 3 - Existing steps and invalid input (Priority: P2)

Existing `notify` steps have none of the new fields. They must send every time, as they do now. A step with an invalid new field must be rejected when the sequence is saved.

**Why this priority**: Backward compatibility and input safety are acceptance rules of the issue.

**Independent Test**: Run an old `notify` step twice: both send. Save a sequence with each invalid combination: each gives a 400 response with a message that names the problem.

**Acceptance Scenarios**:

1. **Given** a `notify` step with only `message` (and optional `url`), **When** it runs two times, **Then** it sends two times.
2. **Given** a `notify` step with `dedupeKey` and no `since` and no `within`, **When** the sequence is saved, **Then** the response is 400.
3. **Given** a `notify` step with both `since` and `within`, or with `since` or `within` and no `dedupeKey`, **When** the sequence is saved, **Then** the response is 400.
4. **Given** a `notify` step with a blank or too long `dedupeKey`, a `since` that is not `HH:mm`, or a `within` that is not `HH:mm:ss` or is zero, **When** the sequence is saved, **Then** the response is 400.

### Edge Cases

- The step runs in a run that is not in a queue (ad hoc run): the dedupe scope is "no queue". Sends in ad hoc runs share one scope per key.
- The alert delivery fails: the step is not a failed step (same as today). A failed delivery does NOT count as a sent alert, so a later run can try again in the same window.
- The service restarts inside the window: the record of the sent alert survives the restart, so the window still holds.
- The `since` time does not exist or occurs two times on a daylight-saving change day: use the same window rule as `lastRun`.
- A dedupe record is older than any window that can use it: the service removes old records so the record store does not grow without limit.
- A `notify` step in step-through preview mode: preview must not send and must not write a record.
- Two runs reach the same step at the same time: at most one of them sends.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The `notify` step MUST accept an optional text field `dedupeKey`.
- **FR-002**: The `notify` step MUST accept an optional `since` field (time of day, `HH:mm`) or an optional `within` field (duration, `HH:mm:ss`), and MUST NOT accept both.
- **FR-003**: When `dedupeKey` and a window are set, the service MUST send the alert only if no alert with the same key and the same queue was sent in the window. Otherwise the service MUST NOT send it.
- **FR-004**: For a step with `dedupeKey`, the step outcome MUST say `sent` when the alert was sent and `suppressed` when the service skipped it.
- **FR-005**: The `since` window MUST start at the most recent occurrence of that local time of day, at or before now, with the same service-local time and daylight-saving rules as `lastRun`. The `within` window MUST be the time span that ends now.
- **FR-006**: The dedupe scope MUST be the pair (queue, key). An alert in one queue or with one key MUST NOT hide an alert in another queue or with another key.
- **FR-007**: The service MUST record a send only when delivery succeeded. A failed delivery MUST NOT start a window; the service releases any reservation it made before delivery (see FR-013).
- **FR-008**: The record of sent alerts MUST survive a service restart.
- **FR-009**: The service MUST remove records that are too old for any window (48 hours), and MUST remove the records of a queue when that queue is deleted.
- **FR-010**: A `notify` step without `dedupeKey`, `since`, and `within` MUST behave as before this feature: it always sends, and its outcome text does not change.
- **FR-011**: Saving a sequence MUST reject, with a 400 response and a message that names the field: `dedupeKey` with no window; a window with no `dedupeKey`; both `since` and `within`; a blank `dedupeKey`; a `dedupeKey` longer than 100 characters; a `since` that is not a valid `HH:mm`; a `within` that is not a valid `HH:mm:ss` or is shorter than one second (this includes zero) or longer than 24 hours.
- **FR-012**: The new fields and the two outcome values MUST be in the OpenAPI document.
- **FR-013**: Concurrent runs that reach the same (queue, key) step in the same window MUST send at most one alert. The service reserves the slot before delivery and releases it if delivery fails. A suppressed step MUST NOT start or extend a window. The step status stays `success` in all cases.
- **FR-014**: Step-through preview MUST show whether the alert would be sent or suppressed, and MUST NOT send an alert or write a record. Preview uses the same scope rule as a real run (the queue identity of the stepped run, or `adhoc`). A live reservation shows "would suppress".
- **FR-015**: This feature MUST NOT change the `lastRun` condition, the `notify` payload fields that exist today (`queueName`, `emulatorSerial`, `failedSequenceName`, `message`), the alert channels, or the retry and time-limit behaviour of the engine.

### Key Entities

- **Dedupe window**: The time span in which an earlier alert hides a new one. It comes from `since` or `within` of the step.
- **Sent-alert record**: A record of one delivered alert: queue, key, and time sent. It is the source for the check in FR-003.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A chain of 10 retries in one window, each cancelled by the time limit, sends exactly 1 alert for each queue and key.
- **SC-002**: 100% of existing `notify` steps without the new fields send on every run, with no change in behaviour.
- **SC-003**: Every invalid combination in FR-011 gives a 400 response and none gives a 500 response.
- **SC-004**: The window start for `since` equals the `lastRun` window start for the same time of day, including on daylight-saving change days.
- **SC-005**: After a service restart inside a window, the next run of the same step reports `suppressed`.

## Assumptions

- The key is scoped per queue by the queue identity, not by the queue name.
- The maximum `dedupeKey` length is 100 characters.
- The longest window is 24 hours for `within`, and a `since` window is at most about one day (plus a daylight-saving margin). The service keeps a sent-alert record for 48 hours and then removes it.
- A reservation (FR-013) expires after 5 minutes. A run that sees a live reservation reports `suppressed`, even if that delivery later fails. After the reservation expires or is released, a later run can send.
- When a queue is deleted, the service removes the sent-alert records of that queue.
- If no sent-alert store is available (a null store in a test or tool build), a step with dedupe fields always sends, and the preview shows "would send".
- The `since` value uses the service-local time zone, as `lastRun` does.
- The scope input (the queue identity, or `adhoc` for a run outside a queue) is part of the base dedupe flow in User Story 1. The service builds it together with that flow, not later, because the dedupe check in the notify dispatch needs the scope to work.
- Unit-level tests cover each invalid combination in FR-011. Contract-level tests cover a representative subset of them. These tests check status 400 and a message that names the field.
- The shape 2 `stepRan` condition is a possible future change and is out of scope.
- The save-time validation uses the existing `NotifyPayload.TryRead` call. The file `src\GameBot.Domain\Services\SequenceStepValidationService.cs` changes only if a message needs it. The plan source tree lists this file with that note.
- Tasks that edit the same test file are not marked parallel [P].
- The preview work depends on the store registration and on the scope rule of the base dispatch (User Story 1). The tasks must order it after them.
