# Feature Specification: Tap Wait-and-Retry Before Execution

**Feature Branch**: `036-tap-wait-retry`  
**Created**: 2025-04-15  
**Status**: Draft  
**Input**: User description: "Extend primitive tap actions with a wait-and-retry mechanism before execution. Before a primitive tap action is executed, the system waits for the configured screenshot capture interval (WAIT_TIME). Two new configuration parameters are introduced: COUNT (max retry cycles, default 3) and PROGRESSION (wait time multiplier, default 1). On each retry cycle, if the expected image is not found and the retry count has not been exceeded, the system waits WAIT_TIME and then sets WAIT_TIME = WAIT_TIME × PROGRESSION. Once the image is found, the tap executes. If COUNT is exceeded, the action fails. All three variables must be documented and set in the configuration file."

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Tap action waits for image before executing (Priority: P1)

As a user running an automated game sequence, I want the system to automatically wait for an expected image to appear on screen before performing a tap, so that taps are only executed when the game is in the correct state and timing-sensitive UI transitions do not cause missed taps.

**Why this priority**: This is the core value of the feature — making tap actions resilient to variable game loading times and UI transitions. Without this, taps fire immediately and often miss their target.

**Independent Test**: Can be fully tested by configuring a primitive tap action with a reference image, starting the game in a state where the image appears after a short delay, and verifying the tap waits and then executes once the image is detected.

**Acceptance Scenarios**:

1. **Given** a primitive tap step with a detection target configured, and the expected image is already visible on screen, **When** the step executes, **Then** the system detects the image on the first check (after an initial WAIT_TIME pause) and performs the tap immediately.
2. **Given** a primitive tap step with a detection target configured, and the expected image appears after 2 screenshot cycles, **When** the step executes, **Then** the system waits through 2 retry cycles (pausing WAIT_TIME between each), detects the image on the 3rd check, and performs the tap.
3. **Given** a primitive tap step with a detection target configured, and the expected image never appears, **When** the step executes and all retry cycles are exhausted (COUNT exceeded), **Then** the action fails with a clear indication that the expected image was not found within the allowed retry window.

---

### User Story 2 - Progressive wait time increases between retries (Priority: P2)

As a user automating a game with variable load times, I want the wait time between retries to increase progressively (via a multiplier), so that the system can start with short waits for fast transitions and gradually extend waits for slower ones, reducing unnecessary delays while still catching slow loads.

**Why this priority**: The progression multiplier adds flexibility beyond fixed-interval retries. It enables smarter waiting strategies, but the feature is still useful with the default PROGRESSION of 1 (no increase).

**Independent Test**: Can be tested by setting PROGRESSION to a value greater than 1 (e.g., 2), configuring a tap action where the image appears late, and verifying via logs that each successive wait period is longer than the previous one according to the multiplier.

**Acceptance Scenarios**:

1. **Given** WAIT_TIME is 500ms, PROGRESSION is 2, and COUNT is 3, **When** the image is not found across all cycles, **Then** the system waits 500ms before the first recheck, 1000ms before the second recheck, and 2000ms before the third recheck (each wait doubles).
2. **Given** WAIT_TIME is 500ms and PROGRESSION is 1, **When** the image is not found across retries, **Then** each wait interval remains 500ms (no progression).

---

### User Story 3 - Configuration parameters are persisted and documented (Priority: P2)

As a system administrator or advanced user, I want the three parameters (screenshot capture interval, retry count, and wait progression) to be configurable in the application configuration file with sensible defaults, so that I can tune the retry behaviour without modifying code.

**Why this priority**: Configuration is essential to make the feature usable in different game environments with varying response times. It is ranked P2 because it ships alongside P1 — but the defaults must be correct for zero-config use.

**Independent Test**: Can be tested by verifying that default configuration values are applied when no explicit configuration is provided, and that overriding each parameter in the configuration file changes the observed retry behaviour.

**Acceptance Scenarios**:

1. **Given** no explicit configuration for retry count and progression, **When** a primitive tap step executes, **Then** the defaults of COUNT = 3 and PROGRESSION = 1 are used.
2. **Given** the screenshot capture interval is not explicitly configured, **When** the system starts, **Then** the default capture interval (currently 500ms) is used as the base WAIT_TIME.
3. **Given** a user sets COUNT = 5 and PROGRESSION = 1.5 in the configuration, **When** a primitive tap step executes and the image is not found, **Then** the system retries up to 5 times with progressively longer waits (500ms, 750ms, 1125ms, 1687ms, 2531ms).

---

### Edge Cases

- What happens when COUNT is set to 0? The system should perform no retries — it checks for the image once, and if not found, fails immediately.
- What happens when PROGRESSION is set to 0? The system should treat this as an invalid configuration and fall back to the default value of 1 (no progression).
- What happens when PROGRESSION is less than 0? The system should treat negative values as invalid and fall back to the default value of 1.
- What happens when the screenshot service is unavailable during a retry cycle? The system should treat a missing screenshot as "image not found" and continue the retry loop.
- What happens when the tap action has no detection target? The existing behaviour is preserved — no wait-and-retry occurs; the step is skipped with an appropriate log message as it does today.
- What happens when the reference image is not registered in the image store? The existing behaviour is preserved — the step fails with a "template_not_found" indication.
- What happens when cancellation is requested during a retry wait? The retry loop is immediately cancellable — the current wait is aborted and the step is reported as "cancelled" (distinct from "failed").

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: System MUST wait for at least the configured screenshot capture interval before performing the first image detection check in a primitive tap step.
- **FR-002**: System MUST re-check for the expected image after each wait period, up to the configured maximum retry count (COUNT).
- **FR-003**: System MUST multiply the current wait time by the configured PROGRESSION factor after each unsuccessful retry cycle (not after the initial detection check), so that `next_wait = current_wait × PROGRESSION`.
- **FR-004**: System MUST execute the tap action immediately once the expected image is detected during any retry cycle.
- **FR-005**: System MUST fail the primitive tap step when the maximum retry count is exceeded without detecting the expected image, preserving the current failure behaviour and error reporting.
- **FR-006**: System MUST expose a configuration parameter for the maximum number of retry cycles (COUNT) with a default value of 3.
- **FR-007**: System MUST expose a configuration parameter for the wait time progression multiplier (PROGRESSION) with a default value of 1.
- **FR-008**: System MUST use the background screenshot service capture interval as the base wait time (WAIT_TIME), which already defaults to 500ms via the existing `GAMEBOT_CAPTURE_INTERVAL_MS` environment variable — but MUST also be settable via the configuration file for discoverability.
- **FR-009**: System MUST validate configuration values: COUNT must be a non-negative integer; PROGRESSION must be a positive number (> 0). Invalid values must fall back to defaults.
- **FR-010**: System MUST log each retry cycle (cycle number, current wait time, detection result) via runtime logging (console/Application Insights) at an appropriate log level. Individual retry cycles MUST NOT be written to the persisted execution log; only the final step outcome (succeeded/failed/cancelled with total retry count) MUST be recorded in the execution log.
- **FR-011**: System MUST document all three configuration parameters (capture interval, retry count, progression) in the configuration file with descriptions and default values.
- **FR-012**: System MUST preserve existing behaviour for primitive tap steps that lack a detection target (no change to non-detection tap flows).
- **FR-013**: System MUST support immediate cancellation of the retry loop when a cancellation is requested (e.g., sequence stop). The current wait MUST be aborted and the step outcome reported as "cancelled", distinct from a retry-exhaustion failure.

### Key Entities

- **Tap Retry Configuration**: The set of parameters governing wait-and-retry behaviour for primitive tap steps. Key attributes: base wait time (derived from capture interval), maximum retry count (COUNT), wait progression multiplier (PROGRESSION).
- **Retry Cycle**: A single iteration of the wait-then-detect loop. Attributes: cycle number, wait duration applied, detection result (found/not found), elapsed time.
- **Primitive Tap Step**: An existing command step type that locates a reference image on screen and taps its coordinates. Extended with retry logic before execution.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Primitive tap actions that depend on image detection succeed at least 95% of the time in scenarios where the target image appears within the configured retry window, compared to the current behaviour where they often fail due to timing. *(Aspirational outcome metric — verified indirectly via unit/integration test coverage, not a directly testable gate.)*
- **SC-002**: Users can fully configure the retry behaviour (wait time, count, progression) without modifying any code, using only the configuration file.
- **SC-003**: Each retry cycle and its outcome is visible in the application logs, enabling users to diagnose timing issues within seconds.
- **SC-004**: The default configuration (COUNT = 3, PROGRESSION = 1, base wait from capture interval) works correctly out of the box with no explicit user configuration required.
- **SC-005**: The total additional latency introduced by the retry mechanism does not exceed `WAIT_TIME × (COUNT + 1)` when PROGRESSION = 1, or `WAIT_TIME × (1 + (PROGRESSION^COUNT − 1) / (PROGRESSION − 1))` when PROGRESSION > 1, ensuring predictable worst-case execution time (includes the initial wait before the first detection check).
- **SC-006**: Existing tap actions without detection targets continue to work identically to the current behaviour — no regressions. *(Verified by dedicated regression test for FR-012.)*

## Clarifications

### Session 2025-04-15

- Q: How should the retry loop respond to cancellation requests during a wait cycle? → A: Immediately cancellable — abort the current wait, report step as "cancelled" (not "failed").
- Q: Should there be an upper bound on the per-cycle wait time when PROGRESSION > 1? → A: No cap — let the formula run unconstrained.
- Q: Should individual retry cycles be persisted to the execution log, or is runtime logging sufficient? → A: Runtime logging only — execution log records final step outcome with retry count.
- Q: Should individual primitive tap steps be able to override global COUNT and PROGRESSION values? → A: Global configuration only — all primitive tap steps share the same settings.
- Q: Should the system verify screenshot freshness before each retry detection check? → A: Use latest cached frame — trust that the capture interval naturally produces fresh frames.

## Assumptions

- The background screenshot capture service is running and providing updated screenshots at the configured interval. The retry mechanism uses whatever the latest cached frame is at detection time, without explicit freshness checks. Since the base WAIT_TIME equals the capture interval, a new frame will naturally be available after each wait cycle.
- The existing `GAMEBOT_CAPTURE_INTERVAL_MS` environment variable (default 500ms) is the authoritative source for base wait time. The new configuration file entry provides the same value for discoverability but the environment variable takes precedence if both are set.
- The existing template matching / image detection pipeline (OpenCvSharp TemplateMatcher) is used for each retry attempt — no changes to the detection algorithm itself.
- PROGRESSION = 1 (default) means constant wait intervals (no exponential backoff). Values > 1 create exponential backoff. Fractional values between 0 and 1 would create decreasing waits, which is valid but unusual.
- There is no upper bound on individual per-cycle wait times. Users who set high PROGRESSION values with high COUNT accept the resulting long waits; the cancellation mechanism (FR-013) provides an escape hatch.
- Retry configuration (COUNT, PROGRESSION) is global only. Per-step overrides are explicitly out of scope for this feature and may be introduced in a future iteration if needed.
