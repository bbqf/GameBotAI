# Feature Specification: Triggered Profile Execution

**Feature Branch**: `001-profile-triggers`  
**Created**: 2025-11-08  
**Status**: Draft  
**Input**: User description: "The application should allow for Profiles to be executed based on Triggers. A Trigger can be Timebased (delay or absolute time), certain part of the Screenshot containing a predefined image (fuzzy matching), or a OCR-ed text being found or not found at a specified location on screen."

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Run Profile when image appears (Priority: P1)

As an automation user, I can attach a trigger to a profile that fires when a reference image is detected within a defined screen region with a minimum similarity threshold, so the profile starts as soon as the in-game UI state appears.

**Why this priority**: Detecting visual states is the most common gating condition for game automation and enables reliable hands-free execution.

**Independent Test**: Upload a reference image and define a region; when the image is displayed in that region, the profile starts within the expected detection window and only once per cooldown.

**Acceptance Scenarios**:

1. Given a trigger with region [x=0.40,y=0.30,w=0.20,h=0.10] and threshold 0.85, When the screen contains the image in that region, Then the profile starts within 2 seconds and the trigger enters cooldown.
2. Given a trigger with threshold 0.85, When the image similarity is 0.80, Then the profile does not start and the trigger remains pending.

---

### User Story 2 - Run Profile after a delay or at a scheduled time (Priority: P2)

As an automation user, I can schedule a profile to start after a delay (e.g., 30 seconds) or at a specific date/time, so it runs without manual interaction.

**Why this priority**: Time-based automation enables predictable sequences and coordination with other tasks.

**Independent Test**: Create a delay trigger for 10 seconds and verify the profile starts after the delay; create an absolute time trigger and verify start occurs within the window.

**Acceptance Scenarios**:

1. Given a delay trigger of 10 seconds, When enabled, Then the profile starts 10±1 seconds later and the trigger completes.
2. Given an absolute time trigger at 2025-11-09T10:00:00Z, When the current time reaches the scheduled time, Then the profile starts within 2 seconds and the trigger completes.

---

### User Story 3 - Run Profile when text appears or disappears (Priority: P3)

As an automation user, I can configure a trigger that fires when specified text is found (or not found) within a screen region with a minimum confidence, so the profile runs when the UI transitions occur (e.g., when "Loading" disappears).

**Why this priority**: Text-based conditions are common where UI states are signaled by labels rather than distinct graphics.

**Independent Test**: Configure a region and target text; when the text appears (or disappears), the profile starts within the detection window and respects cooldown.

**Acceptance Scenarios**:

1. Given a text-found trigger with target "Victory" and confidence 0.80, When the word "Victory" appears in the region, Then the profile starts within 2 seconds.
2. Given a text-not-found trigger with target "Loading" and confidence 0.80, When "Loading" is no longer present for two consecutive evaluations, Then the profile starts and the trigger completes.

---

### Edge Cases

- Region is outside screen bounds or has zero area → Trigger is invalid and cannot be enabled until corrected.
- Multiple partial matches (image or text) within the region → Use the best match; must still meet threshold.
- Rapidly flickering UI states → Debounce by requiring condition to hold for at least one full evaluation cycle; respects cooldown after firing.
- Different device resolutions/aspect ratios → Regions are normalized (0..1); behavior must be consistent across resolutions.
- Device not available or screen capture fails → Trigger evaluation is skipped and retried; no firing occurs.
- Overlapping triggers for the same profile → Only the first satisfied trigger causes execution; others remain pending and will not re-fire during cooldown.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The system MUST support attaching one or more triggers to a profile.
- **FR-002**: Supported trigger types MUST include: delay (relative time), schedule (absolute date/time), image-match (fuzzy match), and text-match (OCR found/not-found).
- **FR-003**: Trigger combination logic MUST be OR: if any enabled trigger is satisfied, the profile starts; remaining triggers for that profile are not evaluated again until cooldown elapses.
- **FR-004**: Each trigger MUST support an Enabled flag and a Cooldown period (seconds) to prevent re-firing within the cooldown window (default: 60 seconds).
- **FR-005**: Image-match trigger MUST accept a reference image, a normalized region (x,y,w,h in [0..1]) and a similarity threshold [0..1] (default: 0.85). The trigger is satisfied when the best match in the region meets or exceeds the threshold.
- **FR-006**: Text-match trigger MUST accept target text (exact or regex), a normalized region (x,y,w,h in [0..1]), a confidence threshold [0..1] (default: 0.80), and a mode of either "found" or "not-found". The trigger is satisfied when the condition is met at or above the confidence threshold.
- **FR-007**: Delay trigger MUST start the profile after the specified duration in seconds since the trigger was enabled; once fired, it completes and does not repeat unless re-enabled.
- **FR-008**: Schedule trigger MUST start the profile at the specified ISO 8601 timestamp (including timezone). If the scheduled time is in the past when enabled, it MUST not fire.
- **FR-009**: The system MUST evaluate active triggers continuously and detect satisfied conditions within 2 seconds under normal operating conditions.
- **FR-010**: The system MUST expose a way to "Test Trigger" that evaluates the condition immediately and reports the current result (satisfied/not satisfied and measured similarity/confidence).
- **FR-011**: The system MUST provide trigger state visibility including: status (pending, satisfied, cooldown), last evaluation time, last result details, and last fired time.
- **FR-012**: Invalid configurations (e.g., region out of bounds, missing image, negative thresholds) MUST be rejected with clear validation errors prior to enabling the trigger.
- **FR-013**: Regions MUST be specified as normalized coordinates (0..1) relative to the current screen; behavior MUST be resolution-independent.
- **FR-014**: When multiple triggers become satisfied simultaneously, the profile MUST start once, and a single firing MUST be recorded with attribution to the first evaluated trigger.
- **FR-015**: After a profile starts due to a trigger, additional firings for that profile MUST be suppressed until the configured cooldown has elapsed.

### Key Entities *(include if feature involves data)*

- **ProfileTrigger**: Associates a trigger with a Profile; attributes: id, profileId, type (delay | schedule | image-match | text-match), enabled, cooldownSeconds, lastFiredAt, lastEvaluatedAt, lastResult.
- **Region**: Normalized rectangle with x, y, width, height in [0..1].
- **ImageMatchParams**: referenceImage (identifier), region (Region), similarityThreshold [0..1].
- **TextMatchParams**: target (string or regex), region (Region), confidenceThreshold [0..1], mode (found | not-found).
- **TimeParams**: For delay: seconds (int). For schedule: timestamp (ISO 8601 with timezone).

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: 95% of satisfied conditions result in the profile starting within 2 seconds of the condition becoming true.
- **SC-002**: False-positive firings are ≤ 5% for image and text triggers when thresholds are at or above defaults.
- **SC-003**: 90% of users are able to configure and successfully test a trigger end-to-end within 5 minutes without external documentation.
- **SC-004**: During any cooldown window, no more than one firing occurs per trigger (0 duplicate starts per cooldown per trigger).
- **SC-005**: 90% of "Test Trigger" evaluations complete in ≤ 2 seconds.
- **SC-006**: 100% of invalid trigger configurations are blocked with clear error messages before enabling.
