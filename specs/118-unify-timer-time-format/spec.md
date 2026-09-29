# Feature Specification: Unify the timerTimeOfDay format

**Feature Branch**: `118-unify-timer-time-format`  
**Created**: 2026-09-30  
**Status**: Draft  
**Input**: User description: "Fix issue #226 (B-023, bug, P3): the queue-template validator and the sequence validator accept different formats for the field `timerTimeOfDay`. Closes #226."

## Clarifications

### Session 2026-09-30

- Q: Which rule for `timerTimeOfDay`: accept `HH:mm:ss` in both validators, or reject it in both? → A: Both validators accept exactly two strict forms: `HH:mm` and `HH:mm:ss` (24-hour, `00:00` to `23:59:59`). Reason: the sequence validator already accepts `HH:mm:ss`. Its error text already says so. If it rejected `HH:mm:ss`, stored sequences would break. The template validator accepts fewer strings. Widening it to also accept `HH:mm:ss` is not a breaking change.
- Q: Must the sequence validator keep its not-strict forms (for example `11:00 PM`, single-digit hour)? → A: No. Both validators accept only the two strict forms. No document names the not-strict forms. This is a deliberate, stricter rule. `HH:mm` and `HH:mm:ss` values keep working.
- Q: Are values with a space at the start or end, or `24:00`, accepted? → A: No. Both validators reject them.
- Q: How does the template response show a stored time of day that has non-zero seconds? → A: It shows the seconds (`HH:mm:ss`). When the seconds are zero, it keeps the `HH:mm` form. Existing clients see no change for existing values.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Copy a time of day between a sequence and a template (Priority: P1)

An author writes a `timerTimeOfDay` value in a `reschedule-self` step of a sequence. The platform accepts the value. The author copies the value into a Timer entry of a queue template. Today the template save fails with a 400 for a value that the platform accepted before. After the fix, the two places accept the same values and reject the same values.

**Why this priority**: This is the whole bug. One field name must follow one format rule.

**Independent Test**: Send the same list of `timerTimeOfDay` strings to the queue-template endpoint and to the sequence dry-run endpoint. Compare the accept/reject result for each string. The two results must be equal for every string.

**Acceptance Scenarios**:

1. **Given** a `timerTimeOfDay` string in the `HH:mm` form, **When** the author sends it to the template endpoint and to the sequence endpoint, **Then** both accept it.
2. **Given** a `timerTimeOfDay` string in the `HH:mm:ss` form, **When** the author sends it to the template endpoint and to the sequence endpoint, **Then** both accept it.
3. **Given** a string that is not a time of day, **When** the author sends it to both endpoints, **Then** both reject it.

---

### User Story 2 - Get an error message that names the accepted format (Priority: P2)

An author sends a wrong `timerTimeOfDay` value. The error message names the format that the platform accepts. The author can then fix the value at once.

**Why this priority**: Today the sequence message names one format and the template message names another format. The messages must be true and must agree.

**Independent Test**: Send an invalid value to each endpoint. Read the error text. It names the accepted format. The format matches the behavior.

**Acceptance Scenarios**:

1. **Given** an invalid `timerTimeOfDay`, **When** the author saves a template, **Then** the error names the accepted format.
2. **Given** an invalid `timerTimeOfDay`, **When** the author validates a sequence, **Then** the error names the accepted format. It is the same format as in the template error.

---

### User Story 3 - Read the same rule in the API description (Priority: P3)

An integrator reads the OpenAPI description of `timerTimeOfDay` for the template entry and for the `reschedule-self` step. The two descriptions state the same rule.

**Why this priority**: The documentation must match the behavior. It does not change the behavior.

**Independent Test**: Read the generated OpenAPI document. Both descriptions of the field state the same format.

**Acceptance Scenarios**:

1. **Given** the OpenAPI document, **When** the integrator reads `timerTimeOfDay` for a template entry and for a `reschedule-self` step, **Then** both state the same accepted format.

---

### Edge Cases

- A stored template or sequence that has an `HH:mm` value keeps loading and running with no change.
- Both validators reject a value with a space at the start or end. They reject a 12-hour form (for example `11:00 PM`), a single-digit hour, and an hour above 23.
- The value `24:00` or `24:00:00` is rejected by both validators.
- An empty or missing value keeps its current behavior. The existing "exactly one of timerTimeOfDay or timerRelativeOffset" rule applies.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The system MUST accept exactly the same set of `timerTimeOfDay` strings in the queue-template endpoint and in the sequence validator, and MUST reject exactly the same set.
- **FR-002**: Both validators MUST accept `HH:mm` and `HH:mm:ss`. The time range is `00:00` to `23:59:59`.
- **FR-003**: Both validators MUST reject every other string. Examples are 12-hour forms, single-digit hours, values with spaces, and `24:00`.
- **FR-004**: The error message of each validator MUST name the format that the validator accepts. The two messages MUST name the same format.
- **FR-005**: The OpenAPI description of `timerTimeOfDay` MUST state the accepted format. This applies to the template entry and to the `reschedule-self` step.
- **FR-006**: Existing `HH:mm` values in stored templates and sequences MUST keep working with no migration.
- **FR-007**: A regression test MUST fail before the fix and pass after it. The test MUST cover both endpoints with `HH:mm` and `HH:mm:ss` values.
- **FR-008**: The change MUST NOT alter `timerRelativeOffset`, other schedule fields, how schedules run, or the `nextDay` unknown-field behavior (issue #228).
- **FR-009**: The template response MUST show seconds only when they are not zero.

### Key Entities

- **timerTimeOfDay**: A service-local wall-clock time of day. It is set on a Timer entry of a queue template and on a `reschedule-self` step of a sequence. It has one accepted string format.

## Assumptions

- The web UI already sends `HH:mm`. No UI feature is added.
- The template response keeps the `HH:mm` output for a time with zero seconds. It shows `HH:mm:ss` when the seconds are not zero.
- The response format drops sub-second ticks, as it does today.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: For every string in a test list of at least 10 values (valid and invalid, both formats), the template endpoint and the sequence validator give the same accept or reject result (0 differences).
- **SC-002**: 100% of `HH:mm` values in the strict two-digit `HH:mm` form that were valid before the fix stay valid after it.
- **SC-003**: An author who copies a `timerTimeOfDay` value from a valid sequence to a template gets no 400 for that value.
- **SC-004**: Each error message for an invalid `timerTimeOfDay` names the accepted format, and the two messages name the same format.
