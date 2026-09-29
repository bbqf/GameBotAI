# Feature Specification: Reject an unknown field in a reschedule-self payload

**Feature Branch**: `119-reject-unknown-reschedule-field`
**Created**: 2026-09-30
**Status**: Implemented
**Input**: User description: "Issue #228 (B-025): the reschedule-self validator accepts an unknown payload field (nextDay) as valid. Closes #228."

## Clarifications

### Session 2026-09-30

- Q: Does the check also reject an unknown field inside the nested `ocrOffset` object? → A: No. Only the top-level payload is checked. Rationale: the issue names top-level fields; a nested check has a higher risk to reject a payload that is valid today, and it is a different request.
- Q: Does the error name all unknown fields or only the first? → A: All unknown fields, in one error, with the list of known fields. Rationale: the author fixes the payload in one round.
- Q: Where does the check run? → A: In the step validation used by create, update, and dry run. It does not run in the load path or the run-time reader. Rationale: a stored sequence must load and run as before.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Author sees an error for a field that does not exist (Priority: P1)

A sequence author writes a `reschedule-self` step. The author guesses a field name, for example `nextDay`, to book the next day. Today the platform says the step is valid, and the sequence ignores the field with no sign. The author must get an error that names the unknown field.

**Why this priority**: This is the whole request. An accepted but ignored field makes the author think the sequence does something that it does not do.

**Independent Test**: Send a create request (also with `dryRun: true`) that has a `reschedule-self` step with the payload `{ "option": "Timer", "timerTimeOfDay": "11:00", "nextDay": true }`. The response is 400 and the error text contains `nextDay`.

**Acceptance Scenarios**:

1. **Given** a `reschedule-self` step whose payload has the unknown field `nextDay`, **When** the author posts the sequence with `dryRun: true`, **Then** the response is 400 and an error names `nextDay`.
2. **Given** the same step, **When** the author posts the sequence without `dryRun`, **Then** the response is 400, an error names `nextDay`, and the platform does not store the sequence.
3. **Given** a payload that has two unknown fields, **When** the author posts the sequence, **Then** the error names each unknown field.

---

### User Story 2 - A valid payload stays valid (Priority: P1)

An author uses only the known fields: `option`, `timerTimeOfDay`, `timerRelativeOffset`, and `ocrOffset` (with its nested fields `region` with `x`, `y`, `width`, `height`, and `fallback`, `min`, `max`). The platform must continue to accept these payloads as before.

**Why this priority**: The new check must not break sequences that work today.

**Independent Test**: Post a `reschedule-self` step for each known option and field combination that is valid today. Each response stays valid.

**Acceptance Scenarios**:

1. **Given** a payload with only known fields, **When** the author posts the sequence, **Then** the response is valid as before.
2. **Given** a payload that writes a known field name in a different letter case (for example `TimerTimeOfDay`), **When** the author posts the sequence, **Then** the response is valid as before, because the payload reader matches names without regard to case.

---

### User Story 3 - The wrong option check does not change (Priority: P2)

A payload with `option: "Bogus"` must continue to return 400 with the same message as today.

**Why this priority**: This is a guard against a regression.

**Independent Test**: Post a step with `option: "Bogus"`. The response is 400 and the message is "is not a known schedule option (expected one of AtQueueStart, OncePerRun, Timer, EveryStep)".

**Acceptance Scenarios**:

1. **Given** a payload with `option: "Bogus"`, **When** the author posts the sequence, **Then** the response and message are the same as before this change.

---

### Edge Cases

- A payload has an unknown field and a wrong `option` value: the response is 400 and the error names the option problem. The platform reports the unknown field after the author fixes the option.
- An unknown field is inside the nested `ocrOffset` object or inside `ocrOffset.region`: this is out of scope. Only the top-level payload is checked.
- A stored sequence that has an unknown field: the platform does not reject it when it loads the sequence. Only the create and update validation changes.
- A stored sequence with an unknown field still loads and runs. The next PUT or PATCH of that sequence gets a 400 until the author removes the unknown field.
- Other action types keep their current behavior for unknown payload fields.
- Two keys that differ only in letter case (for example `nextDay` and `NextDay`) are both unknown. The error lists each key as the author wrote it.
- An update (`PUT`) that has an unknown field gets 400. The stored sequence does not change.
- A field name that differs from a known name only in letter case is a known field.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The platform MUST return 400 for a `reschedule-self` step whose payload has a top-level field that is not `option`, `timerTimeOfDay`, `timerRelativeOffset`, or `ocrOffset`.
- **FR-002**: The error message MUST name each unknown field.
- **FR-003**: The check MUST apply to a create request (`POST /api/sequences`), with or without `dryRun`. It MUST also apply to an update request (`PUT` and `PATCH /api/sequences/{id}`). These endpoints use the same step validation.
- **FR-004**: The platform MUST continue to accept every payload that has only known fields and is valid today, as before.
- **FR-005**: The platform MUST match field names without regard to letter case, as the payload reader does today.
- **FR-006**: The platform MUST NOT change the check and message for a wrong `option` value.
- **FR-007**: The platform MUST NOT change the value checks of the known fields.
- **FR-008**: The platform MUST NOT change the validation of other action types.
- **FR-009**: The platform MUST NOT reject a stored sequence at load time because of an unknown field.
- **FR-010**: The platform MUST NOT add a `nextDay` field or another way to book the next day.

### Key Entities

- **reschedule-self payload**: The parameter set of a `reschedule-self` step. Known fields: `option`, `timerTimeOfDay`, `timerRelativeOffset`, `ocrOffset`.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: 100% of `reschedule-self` payloads with an unknown top-level field and a valid `option` get a 400 response whose message names the field (a payload with a wrong `option` shows the option error first). Keys that differ only in letter case are each named.
- **SC-002**: 0 payloads that are valid today become invalid, as shown by the existing tests for `reschedule-self` that all stay green.
- **SC-003**: The response and message for `option: "Bogus"` are the same as before the change.
- **SC-004**: Automated tests cover the unknown field (rejected on create, on PUT, and on PATCH), the known fields (accepted), and the wrong option (rejected).

## Assumptions

- The exact field names are the four keys in the payload reader: `option`, `timerTimeOfDay`, `timerRelativeOffset`, `ocrOffset`.
- The scope is the top-level payload only (see Clarifications).
- The check runs in the sequence step validation, not in the payload reader that the runner uses at run time, so a stored sequence loads and runs as before.
