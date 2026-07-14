# Feature Specification: OCR-Parsed Dynamic Reschedule Offset

**Feature Branch**: `068-reschedule-ocr-offset`
**Created**: 2026-07-13
**Status**: Draft
**Input**: User description: "Add an OCR-parsed dynamic relative offset to the `reschedule-self` sequence action (extends feature 065), so a self-rescheduling sequence can read an on-screen countdown timer and reschedule itself by that duration."

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Self-pace off a live on-screen countdown (Priority: P1)

A daily automation performs an action that starts a variable cooldown displayed on screen as a countdown timer (the driving case: answering the PNS Radio Quiz, whose cooldown grows with each answer). Immediately after acting, the same screen shows the new cooldown (e.g. `00:05:42`). The sequence's final step reschedules itself to fire again after exactly that displayed duration, so the next run lands right when the next action is available — without polling and without hard-coding an interval that would be wrong as the cooldown changes.

**Why this priority**: This is the core capability and the only reason the feature exists. Without it, self-rescheduling sequences must use a fixed interval, which is either wasteful (too frequent) or laggy (too infrequent) whenever the real interval is dynamic.

**Independent Test**: Configure a `reschedule-self` (Timer) step with an OCR-offset region pointing at a known countdown timer, run the sequence, and verify the next firing is scheduled at approximately `now + <displayed duration>`.

**Acceptance Scenarios**:

1. **Given** a sequence whose `reschedule-self` step is configured to read its offset from a screen region, **When** that region legibly shows `00:05:42`, **Then** the next firing is scheduled at approximately `now + 5m42s` and the execution log records that the OCR-parsed value was used.
2. **Given** the same step, **When** the region shows `01:20` (mm:ss form), **Then** the next firing is scheduled at approximately `now + 1m20s`.
3. **Given** the same step, **When** the region shows a value surrounded by OCR noise (e.g. leading/trailing non-digit characters), **Then** the duration is still extracted and used.

### User Story 2 - Never stall when the timer can't be read (Priority: P1)

The automation runs unattended for days. Occasionally OCR fails — the region is momentarily blank, the timer is absent (the control shows a button instead of a countdown), or recognition returns garbage. The sequence must still reschedule itself using a required static fallback offset, so the self-rescheduling chain never dies and never spins in a tight loop.

**Why this priority**: A self-rescheduling task that stops rescheduling silently breaks the whole automation until a human notices. Robust fallback is as important as the happy path.

**Independent Test**: Point the OCR-offset region at an area with no readable duration, run the sequence, and verify it still reschedules using the configured fallback offset and logs that the fallback was used.

**Acceptance Scenarios**:

1. **Given** an OCR-offset step with a fallback of `00:06:00`, **When** the region yields no parseable duration, **Then** the next firing is scheduled at `now + 6m` and the log records that the fallback was used (with the reason).
2. **Given** an OCR-offset step, **When** OCR raises an error or returns empty text, **Then** the reschedule still occurs via fallback (the step does not fail the sequence or skip the reschedule).

### User Story 3 - Reject implausible reads (Priority: P2)

A misread could yield an absurd duration (e.g. an extra digit → hours instead of minutes, or `00:00:00`). Using such a value directly would either park the sequence far in the future or make it fire immediately in a hot loop. Parsed durations outside a sane, configurable range are treated as a failed parse and fall back to the static offset.

**Why this priority**: Prevents rare OCR errors from causing pathological scheduling; important for safety but secondary to the happy path and basic fallback.

**Independent Test**: Feed the parser a value below the minimum (e.g. `00:00:00`) and above the maximum (e.g. `99:59:59`) and verify both fall back to the static offset.

**Acceptance Scenarios**:

1. **Given** a min bound of 1 second, **When** the region reads `00:00:00`, **Then** the parsed value is rejected and the fallback offset is used.
2. **Given** a max bound of 24 hours, **When** the region reads a duration exceeding it, **Then** the parsed value is rejected and the fallback offset is used.

### Edge Cases

- Timer already elapsed / control shows an actionable button instead of a countdown → no parseable duration → fallback (kept short so the sequence retries soon).
- Recognized text has digit-confusable characters (e.g. `O`/`0`, `l`/`1`) → tolerated where feasible; otherwise fallback.
- Region coordinates fall partly/entirely outside the captured frame, or the capture is unavailable → fallback.
- Parsed `00:00:00` (zero) → below minimum → fallback.
- A `reschedule-self` step configured for OCR-offset but with a non-Timer option, or missing the required region or fallback → rejected at validation time with a clear message.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The `reschedule-self` action (option = Timer) MUST support deriving its relative offset by reading text from a caller-specified region of the current emulator screen at execution time, instead of using only a static offset.
- **FR-002**: The OCR-offset configuration MUST specify the screen region to read, expressed as x, y, width, height in the same captured-screen pixel space used by image-match detection.
- **FR-003**: The system MUST parse the recognized text into a duration, supporting at least `mm:ss` and `hh:mm:ss` countdown formats and tolerating surrounding non-duration characters (OCR noise).
- **FR-004**: On a successful, in-bounds parse, the next firing MUST be scheduled at `now + parsed duration`, reusing the existing Timer relative-offset resolution.
- **FR-005**: An OCR-offset configuration MUST include a required static fallback offset. If OCR fails, returns empty, or the text cannot be parsed into a plausible duration, the system MUST reschedule using the fallback offset rather than failing the step or skipping the reschedule.
- **FR-006**: The parsed duration MUST be validated against configurable minimum and maximum bounds; a value outside the bounds MUST be treated as a failed parse and fall back to the static offset.
- **FR-007**: The execution log MUST record which offset source was used for each reschedule (OCR-parsed vs. fallback); on the OCR path it MUST include the recognized text and the resulting duration, and on the fallback path the reason.
- **FR-008**: When no OCR-offset is configured, `reschedule-self` MUST behave exactly as today for all options (AtQueueStart, OncePerRun, EveryStep, Timer with static `timerRelativeOffset` / `timerTimeOfDay`).
- **FR-009**: Payload validation MUST reject an OCR-offset configuration that is missing the region or the fallback offset, has a non-positive region size, or is combined with a non-Timer option, returning a clear, human-readable error.

### Key Entities *(include if feature involves data)*

- **OCR-offset spec**: an optional part of a `reschedule-self` Timer payload describing how to derive the offset at runtime. Attributes: the screen region (x, y, width, height); the expected duration format(s); a required static fallback offset (duration); optional minimum and maximum plausible-duration bounds (with sensible defaults).
- **Reschedule outcome (log detail)**: the recorded result of a reschedule — the offset source (OCR vs. fallback), the effective duration used, and, where relevant, the recognized text and/or fallback reason.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: When the configured region legibly shows a `mm:ss` or `hh:mm:ss` duration, the next firing is scheduled within 2 seconds of `now + that duration`.
- **SC-002**: When the region yields no parseable in-bounds duration, the sequence still reschedules 100% of the time, using the configured fallback offset (no dropped reschedules, no tight loops).
- **SC-003**: A parsed duration outside the configured `[min, max]` window never determines the next firing — it is always replaced by the fallback.
- **SC-004**: Every reschedule via this feature produces an execution-log entry that unambiguously states whether the OCR value or the fallback was used.
- **SC-005**: Sequences that do not configure an OCR-offset exhibit no behavioral change versus the prior release (verified by existing self-reschedule tests continuing to pass).

## Assumptions

- The target timer text is legible to the existing OCR engine at the specified region; recognition tuning (e.g. page-segmentation settings) is handled by existing OCR configuration and is out of scope here.
- Region coordinates are provided in the captured-screen pixel space already used by image cropping/detection.
- Default plausibility bounds are reasonable for game cooldown timers (minimum on the order of 1 second, maximum on the order of 24 hours) and are configurable per step where needed.
- The driving Radio Quiz workflow supplies the timer region; authoring the specific Radio Quiz sequence/queue entry is a separate task and not part of this feature.

## Non-Goals

- No general variable/expression system for sequences; this is scoped to the `reschedule-self` offset only.
- No changes to the other `reschedule-self` options or to `timerTimeOfDay`.
- Not building the Radio Quiz correct-answer bank (tracked separately).
