# Feature Specification: Press and hold at a detected point (anchored long press)

**Feature Branch**: `claude/resolve-github-issue-fr-009-3y4vaw` (spec number 111)  
**Created**: 2026-09-28  
**Status**: Draft (active)  
**Input**: GitHub issue #235 (FR-009): "press and hold at a detected point (anchored long press)". Full description: see the issue and the feature description that started this spec.

## Background

In the game, a press and hold of approximately 0.5 s to 1 s on a claim button takes all free claims. The game stops before the first paid claim. A single tap takes only one claim.

Today, a press and hold is possible only as a `Swipe` step with the same start point and end point and a `durationMs`. A `Swipe` step takes fixed coordinates only. Thus the hold cannot be anchored on the control that it is for. The authoring rules forbid a fixed-coordinate input near a control that spends currency, because the screen can change between the check and the input.

A `PrimitiveTap` step finds a reference image on the screen and taps the detected point (plus the offsets). The step contract has only `detectionTarget`. On 2026-09-28, `POST /api/commands` with a `PrimitiveTap` step that had `holdMs: 700` returned 201, but the read-back had no `holdMs`. The service dropped the field.

## Clarifications

### Session 2026-09-28

- Q: Which design does this feature use: an optional `holdMs` on `PrimitiveTap`, or a `detectionTarget` on `Swipe`? → A: An optional `holdMs` on `PrimitiveTap`. Rationale: it is the smaller change, it agrees with the acceptance text of the issue, and it does not change the `Swipe` step. The feature does not build the other design.
- Q: How does the service send a press and hold to the device, and what occurs to the tap of today? → A: The service sends one swipe input with the same start point and end point at the calculated point, with a duration equal to `holdMs`. When `holdMs` is absent or 0, the service sends the tap of today without change (a swipe to the same point with a duration of 200 ms). The tap-point jitter of the session applies to a press and hold in the same way as to the tap of today. Rationale: this is the input that the issue names, and the tap of today already uses it. A change to the jitter would change the tap and the `Swipe` step too.
- Q: What does the read-back return for `holdMs: 0` and for an absent `holdMs`? → A: The service keeps the value that the author sent. `holdMs: 0` comes back as 0. An absent `holdMs` stays absent on read-back and in storage. Rationale: the read-back agrees with the request, and stored commands from before this feature do not change.
- Q: Can `holdMs` take a parameter placeholder (a `fieldTemplates` entry such as `primitiveTap.holdMs`)? → A: No, not in this feature. `holdMs` is a literal integer. A `fieldTemplates` entry `primitiveTap.holdMs` is rejected at save time with the current rule for an unsupported key. Rationale: a placeholder needs a run-time range check. The issue does not ask for it.
- Q: How do the execution log and the step outcome show a press and hold? → A: The execution log detail of the step has the kind `tap`, the text "Press and hold at (x,y) for N ms." (or "Press and hold targeted (x,y), executed at (x2,y2) for N ms." when the jitter moved the point), and the attributes of the tap of today plus `holdMs`. The step outcome that the execute endpoints return gets a `holdMs` field. The service writes `holdMs` only for a press and hold, so the log detail and the outcome of a tap without a hold do not change. Rationale: the acceptance text asks for the point and the duration. The operator can find the hold in the text and in the attributes.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - A command step presses and holds at a detected point (Priority: P1)

An author adds a `PrimitiveTap` step to a command and sets a hold duration, for example 700 ms. When the command runs, the service finds the reference image, calculates the point (the detected point plus the offsets), and presses and holds at that point for the hold duration.

**Why this priority**: This is the request in the issue. Without it, the author must use a fixed-coordinate hold near a control that spends currency, and the authoring rules forbid that.

**Independent Test**: Save a command with a `PrimitiveTap` step that has a hold duration of 700 ms. Run the command against a device (or a test double). The device gets one press and hold at the calculated point for 700 ms.

**Acceptance Scenarios**:

1. **Given** a command with a `PrimitiveTap` step with a hold duration of 700 ms, **When** the command runs and the service finds the reference image, **Then** the device gets a press and hold at the calculated point for 700 ms.
2. **Given** a command with a `PrimitiveTap` step with a hold duration of 700 ms, **When** the command runs and the service does not find the reference image after all retries, **Then** the device gets no input, and the step outcome is the same as for a tap that does not find its image.
3. **Given** a command with a `PrimitiveTap` step without a hold duration, or with a hold duration of 0, **When** the command runs, **Then** the device gets the same single tap as before this feature.

---

### User Story 2 - The hold duration is kept on save and read-back (Priority: P1)

An author saves a command with a `PrimitiveTap` step that has a hold duration. When the author reads the command back, the step shows the same hold duration. The service rejects a hold duration outside the permitted range with a clear error.

**Why this priority**: Today the service drops the field silently. The author cannot know that the hold does not operate.

**Independent Test**: Send `POST /api/commands` with a `PrimitiveTap` step with `holdMs: 700`. Read the command with `GET /api/commands/{id}`. The step has `holdMs: 700`. Send `holdMs: 5001` and `holdMs: -1`. Each request gets HTTP 400.

**Acceptance Scenarios**:

1. **Given** a create request with a `PrimitiveTap` step with a hold duration of 700 ms, **When** the author reads the command back, **Then** the step has a hold duration of 700 ms.
2. **Given** an update request that changes the hold duration to 1000 ms, **When** the author reads the command back, **Then** the step has a hold duration of 1000 ms.
3. **Given** a create or update request with a hold duration less than 0 or more than 5000, **When** the service gets the request, **Then** the service rejects it with HTTP 400 and a message that names the field and the permitted range.
4. **Given** a `PrimitiveTap` step without a hold duration, **When** the author reads the command back, **Then** the step has no hold duration, as before this feature.

---

### User Story 3 - The execution log shows the point and the hold duration (Priority: P2)

An operator reads the execution log of a command run. The entry for a press and hold shows the calculated point, the executed point, and the hold duration.

**Why this priority**: The acceptance text of the issue asks for it. The operator must see that the hold occurred at the correct point for the correct time.

**Independent Test**: Run a command with a `PrimitiveTap` step with a hold duration of 700 ms. Read the execution log entry. The detail for the step shows the point and "700 ms".

**Acceptance Scenarios**:

1. **Given** a `PrimitiveTap` step with a hold duration of 700 ms that ran, **When** the operator reads the execution log, **Then** the detail for the step shows the calculated point, the executed point, and the hold duration of 700 ms.
2. **Given** a `PrimitiveTap` step without a hold duration that ran, **When** the operator reads the execution log, **Then** the detail for the step is the same as before this feature.

---

### User Story 4 - The API document and the authoring UI show the hold duration (Priority: P3)

An author reads the OpenAPI document and sees the hold duration field on the `PrimitiveTap` step configuration, with its range. An author who edits a `PrimitiveTap` step in the web UI can set the hold duration.

**Why this priority**: Authors find the field without a probe of the service. The UI is a secondary path; the main authors use the API.

**Independent Test**: Read the OpenAPI document. `PrimitiveTapConfigDto` has `holdMs` with minimum 0 and maximum 5000. Open a `PrimitiveTap` step in the command editor of the web UI. The editor has a hold duration input. Save; the saved step has the value.

**Acceptance Scenarios**:

1. **Given** the OpenAPI document, **When** an author reads `PrimitiveTapConfigDto`, **Then** it has an optional integer `holdMs` with the range 0 to 5000 and a description.
2. **Given** the command editor in the web UI, **When** an author sets a hold duration on a `PrimitiveTap` step and saves, **Then** the saved step has the hold duration.

---

### Edge Cases

- A hold duration of exactly 0 is permitted and gives a single tap, the same as no hold duration.
- A hold duration of exactly 5000 is permitted.
- A hold duration that is not an integer (for example `700.5` or `"abc"`) is not saved. The service returns the same error status as for a non-integer value in another integer field of a step. This feature does not change that behavior.
- A hold duration on a step of a type other than `PrimitiveTap` has no effect, because the service reads the `primitiveTap` object only for a `PrimitiveTap` step.
- A command that the service stored before this feature has no hold duration. It runs as a single tap.
- The single-step execute endpoint (`POST /api/steps/execute`) with a `PrimitiveTap` step with a hold duration presses and holds, the same as a saved command.
- A cancel during the hold stops the step with the same outcome as a cancel during a tap.
- A backup and a restore of the command data keep the hold duration.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The `PrimitiveTap` step configuration MUST accept an optional integer hold duration in milliseconds (`holdMs`).
- **FR-002**: The service MUST reject a hold duration less than 0 or more than 5000 with HTTP 400. The error message MUST name the field and the permitted range.
- **FR-003**: The service MUST keep the hold duration on create, on update, and in storage, and MUST return it on read-back. A value of 0 MUST come back as 0. An absent value MUST stay absent.
- **FR-004**: When the hold duration is more than 0, the step MUST press and hold at the calculated point (the detected point plus the offsets) for the hold duration, in one input: a swipe with the same start point and end point and a duration equal to the hold duration. The tap-point jitter of the session MUST apply as for the tap of today.
- **FR-005**: When the hold duration is absent or 0, the step MUST send the same input as before this feature (a swipe to the same point with a duration of 200 ms).
- **FR-006**: The detection, the retries, the offsets, the screen-bounds check, and the outcome of a step that does not find its image MUST not change.
- **FR-007**: The execution log detail of a `PrimitiveTap` step with a hold duration more than 0 MUST show the calculated point, the executed point, and the hold duration. The detail kind is `tap`. The text is "Press and hold at (x,y) for N ms.", or "Press and hold targeted (x,y), executed at (x2,y2) for N ms." when the jitter moved the point. The attributes are the attributes of the tap of today plus `holdMs`. The detail of a step without a hold duration MUST not change.
- **FR-008**: The step outcome that the execute endpoints return MUST show the hold duration (`holdMs`) for a step that pressed and held. The outcome of other steps MUST not get a `holdMs` field.
- **FR-009**: The OpenAPI document MUST show `holdMs` on `PrimitiveTapConfigDto`, with the range 0 to 5000 and a description.
- **FR-010**: The command editor of the web UI MUST let the author set, change, and clear the hold duration of a `PrimitiveTap` step.
- **FR-011**: The `Swipe` step MUST not change. This feature MUST not add `detectionTarget` to `Swipe`.
- **FR-012**: `holdMs` MUST be a literal integer. This feature MUST not add `primitiveTap.holdMs` to the supported `fieldTemplates` keys, so the current save-time rule rejects that key.

### Key Entities

- **PrimitiveTap step configuration**: The configuration of a step that finds a reference image and taps it. It has a detection target (reference image, confidence, offsets, selection strategy) and, with this feature, an optional hold duration in milliseconds.
- **Step outcome**: The result of one step in a command run. For a `PrimitiveTap` step it has the calculated point, the executed point, the detection confidence, and, with this feature, the hold duration of a press and hold.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: An author can make a command step that presses and holds at a detected point for a duration from 1 ms to 5000 ms, with no fixed coordinates.
- **SC-002**: 100% of saved hold durations in the range 0 to 5000 come back unchanged on read-back.
- **SC-003**: 100% of hold durations outside the range 0 to 5000 get a rejection with a message that names the field.
- **SC-004**: For each press and hold that runs, the execution log shows the point and the hold duration.
- **SC-005**: All commands that exist before this feature run with the same input as before.

## Assumptions

- The issue offers two designs. This spec uses the first design: an optional `holdMs` on `PrimitiveTap`. It is the smaller change, and it agrees with the acceptance text of the issue.
- The device receives a press and hold as a swipe with the same start point and end point and a duration. The tap of today already uses this input with a fixed duration of 200 ms.
- The permitted range 0 to 5000 comes from the issue.
- Sequences do not have a `PrimitiveTap` action type. A sequence step gets an anchored press and hold when it runs a command that has a `PrimitiveTap` step with a hold duration.
- Issue #237 (unknown step fields are dropped silently) is out of scope.
