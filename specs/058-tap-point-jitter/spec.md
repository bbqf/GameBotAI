# Feature Specification: Tap-Point Jitter

**Feature Branch**: `058-tap-point-jitter`
**Created**: 2026-06-10
**Status**: Implemented
**Input**: User description: "I want to have tap-point jitter (e.g., randomizing X/Y within a small radius of the target). This should be applied automatically to all taps and swipes. Default \"small radius\" should be +/-5 pixels of the target. and should be configurable via configuration parameter. Make sure that the configuration is properly stored/retrieved, documented and included in the UI Configuration variables list, but no specific UI option should be created for this in any of the authoring or execution UIs"

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Every executed tap and swipe lands near, not exactly on, the target (Priority: P1)

As an automation operator, when a sequence executes a tap or swipe against the device, I want the actual point touched to vary slightly from run to run instead of always landing on the exact same pixel, so that repeated executions look more natural and are more resilient to UI elements that respond slightly differently to pixel-perfect repeated input.

**Why this priority**: This is the core behavior requested — without it, nothing changes. It must apply automatically, with no per-step setup, to deliver value immediately to every existing and future sequence.

**Independent Test**: Configure a sequence with a primitive tap step at a fixed coordinate and run it multiple times. Observe (e.g., via execution logs or device input trace) that the coordinates actually sent to the device vary slightly between runs while remaining close to the configured target.

**Acceptance Scenarios**:

1. **Given** a primitive tap step targeting coordinates (X, Y), **When** the step executes, **Then** the coordinates sent to the device are within the configured jitter radius of (X, Y) and are not guaranteed to equal (X, Y) exactly.
2. **Given** a primitive swipe step with start point (X1, Y1) and end point (X2, Y2), **When** the step executes, **Then** both the start and end coordinates sent to the device are independently jittered within the configured radius of their respective targets.
3. **Given** the same tap step executed multiple times in a row, **When** each execution runs, **Then** the actual coordinates sent to the device vary across executions (are not always identical).
4. **Given** a tap target at or very near the edge of the screen (e.g., X=2), **When** jitter is applied, **Then** the resulting coordinates are never negative.

---

### User Story 2 - Operator tunes or disables jitter via configuration (Priority: P2)

As an operator/administrator, I want to control how large the random offset can be — including turning it off entirely — by changing a configuration value, without editing any sequence, command, or code.

**Why this priority**: Different games/UIs have different tolerances for off-target taps (e.g., small buttons vs. large areas). Operators need to tune this without redoing authored content, and need an escape hatch (disable) for precise testing or troubleshooting.

**Independent Test**: Change the jitter radius configuration value to 0 and confirm taps/swipes land exactly on target. Change it to a larger value (e.g., 20) and confirm the observed variation increases accordingly.

**Acceptance Scenarios**:

1. **Given** the jitter radius configuration is set to 0, **When** any tap or swipe executes, **Then** the coordinates sent to the device exactly match the configured target (no offset applied).
2. **Given** the jitter radius configuration is set to a custom positive value, **When** any tap or swipe executes, **Then** the maximum possible offset on each axis matches that configured value (rather than the default of 5).
3. **Given** no value has been configured, **When** the system runs, **Then** the jitter radius defaults to 5 pixels.
4. **Given** an invalid value (e.g., negative number) is configured, **When** the system loads configuration, **Then** the system falls back to the default radius rather than failing or applying a nonsensical negative radius.

---

### User Story 3 - Jitter setting is visible alongside other configuration values (Priority: P3)

As an operator reviewing system configuration, I want to see the jitter radius setting listed in the same configuration view as other tunable parameters (with its current value and where it comes from), so I understand and can adjust system behavior in one place.

**Why this priority**: Discoverability and consistency. This does not change runtime behavior but ensures the new setting isn't "hidden" relative to other configuration values, and keeps configuration documentation/tooling accurate.

**Independent Test**: Open the general configuration view and confirm the jitter radius parameter appears in the list with its current effective value, default, and source (default/file/environment), consistent with how other parameters (e.g., ADB retry count) are presented. Confirm no dedicated jitter control exists in the command authoring or execution screens.

**Acceptance Scenarios**:

1. **Given** the general configuration variables list, **When** an operator views it, **Then** the tap jitter radius parameter is present with its name, current effective value, and source.
2. **Given** the command authoring UI and the execution UI, **When** an operator browses tap or swipe step options, **Then** no jitter-specific control is present (jitter is not configurable per-step).
3. **Given** project documentation of configuration parameters, **When** an operator looks up the jitter radius parameter, **Then** its purpose, default value, and how to override it are documented.

---

### Edge Cases

- What happens when the jitter radius is configured as 0? Jitter is disabled; coordinates pass through unchanged.
- What happens when the jittered coordinate would be negative (target near the top-left edge of the screen)? The coordinate is clamped to 0 rather than sent as a negative value.
- What happens when the jitter radius is configured with an invalid value (negative, non-numeric)? The system falls back to the default radius (5) rather than erroring.
- What happens for swipes where start and end points are jittered independently? The effective swipe distance/direction may vary slightly between executions; this is expected and acceptable.
- Does jitter apply to taps/swipes regardless of how the target coordinates were determined (explicitly authored, computed from image detection, or replayed from a recording)? Yes — jitter is applied uniformly to every tap and swipe sent to the device, regardless of origin.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: System MUST apply a small random offset to the X and Y coordinates of every tap action before it is sent to the device.
- **FR-002**: System MUST apply a small random offset, independently for the start point and the end point, to every swipe action before it is sent to the device.
- **FR-003**: For each tap or swipe endpoint, the offset applied to the X coordinate and the offset applied to the Y coordinate MUST each be independently randomized within the range [-radius, +radius], where "radius" is the configured jitter radius in pixels.
- **FR-004**: System MUST provide a configuration parameter that controls the jitter radius (in pixels), with a default value of 5.
- **FR-005**: System MUST treat a configured jitter radius of 0 as "jitter disabled" — coordinates are passed through unchanged.
- **FR-006**: System MUST reject/ignore invalid configured radius values (e.g., negative numbers) and fall back to the default value (5) in that case.
- **FR-007**: System MUST clamp jittered coordinates so they are never negative, regardless of the configured radius.
- **FR-008**: The jitter radius configuration parameter MUST be persisted and retrieved using the same configuration storage/override mechanism as other system configuration parameters (i.e., default → saved configuration file → environment variable override precedence).
- **FR-009**: The jitter radius configuration parameter MUST appear in the general UI configuration variables list, showing its current effective value, default, and source — consistent with how existing configuration parameters are presented.
- **FR-010**: The jitter radius configuration parameter MUST be documented in the project's configuration/environment variable reference documentation, including its purpose, default, and valid range.
- **FR-011**: System MUST NOT introduce any per-step or per-command UI control for jitter in the authoring UI or the execution UI — the only way to view/change the jitter radius is via the general configuration variables list.
- **FR-012**: Jitter MUST be applied automatically to all taps and swipes sent to the device — there is no per-step opt-in or opt-out.
- **FR-013**: For executed tap and swipe steps, the execution log/step outcome MUST report both the original (pre-jitter) target coordinates and the actual jittered coordinates sent to the device.

### Key Entities

- **Tap Jitter Radius**: A single configuration value (in pixels) representing the maximum random offset applied independently to each axis (X and Y) of every tap or swipe coordinate sent to the device. Default 5; 0 disables jitter; negative values are invalid and fall back to the default.
- **Executed Point**: The actual post-jitter coordinates sent to the device for a tap or swipe (or each endpoint of a swipe), recorded alongside the existing pre-jitter target/resolved point in execution logs and step outcomes.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: With default configuration, repeated executions of the same tap step produce device-level coordinates that vary from run to run, while always staying within 5 pixels of the authored target on each axis.
- **SC-002**: Operators can change the effective jitter radius (including disabling it by setting it to 0) by changing a single configuration value, with the new behavior taking effect without modifying any sequence, command, or step definition.
- **SC-003**: 100% of tap and swipe actions executed against a device have jitter applied automatically, regardless of how their target coordinates were produced (authored directly, computed from image detection, or replayed from a recording).
- **SC-004**: The jitter radius parameter is visible in the same configuration listing as existing parameters (e.g., ADB retry settings) without requiring any new navigation paths or screens.
- **SC-005**: Jittered tap/swipe coordinates sent to the device are never negative, even when the authored target is at or near coordinate (0, 0).
- **SC-006**: For every executed tap/swipe step, an operator can find both the originally targeted coordinates and the actual coordinates sent to the device in the execution log/step outcome.

## Clarifications

### Session 2026-06-10

- Q: When a tap/swipe executes with jitter applied, should the execution log/outcome report the original (pre-jitter) target, the actual jittered coordinates, or both? → A: Both — keep the existing target/resolved point field, and additionally record the actual jittered coordinates sent to the device.

## Assumptions

- "Radius" is interpreted as an independent per-axis bound: the X offset and Y offset are each randomly chosen within [-radius, +radius] pixels (a square jitter area), not a strict circular (Euclidean) radius. This matches the simplicity implied by "+/- 5 pixels."
- Only the lower bound (negative coordinates) is clamped. No attempt is made to clamp jittered coordinates to a maximum screen width/height, since screen dimensions are not consistently available at the point where jitter is applied and a ±5 px (default) offset is negligible relative to typical screen sizes.
- The configuration parameter is added to the existing global application configuration alongside related tap/ADB settings (e.g., near `TapRetryCount`, `AdbRetries`), following the existing naming and override conventions (default → saved config file → environment variable).
- Randomization does not need to be cryptographically secure — consistent with the existing sequence random-delay feature, which uses non-cryptographic randomness for non-security-sensitive timing/positioning.
