# Feature Specification: Wait for Image Primitive Action

**Feature Branch**: `[001-wait-for-image]`  
**Created**: 2026-05-27  
**Status**: Draft  
**Input**: User description: "Next feature: new primitive action 'Wait for Image'. I want to be able to specify a step in which command execution will pause and wait for an image to appear, so I have to select an image to be detected, as well as certainity. There waiting time should be limited by a optional timeout in milliseconds, default 1000ms. In the authoring UI as well as in the execution log I want to see all the parameters of this step. The image itself should also be optional, so when there's no image specified or the specified image cannot be found, the step should just wait for the timeout to occur and return. Timeout should not trigger error condition, the execution should just continue. In the execution log I want to see the actual exit condition though, i.e. if an image has been detected, timeout occured as well as if the image could not have been loaded."

## Clarifications

### Session 2026-05-27

- Q: When an image is selected, how should certainty behave if the author leaves it unset? → A: Make certainty optional; use the existing default detection certainty when omitted.
- Q: If the configured image cannot be loaded, should the step still wait out the timeout or stop immediately? → A: Still wait the timeout and record image unavailable as the final exit condition.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Author a Wait Step (Priority: P1)

As an automation author, I can add a "Wait for Image" primitive action to a command step or sequence step and configure its image, certainty, and timeout values, so I can pause execution until a screen state is likely ready without creating a failure when that state never appears.

**Why this priority**: The feature has no user value unless authors can define the wait behavior and save it as part of normal command and sequence authoring.

**Independent Test**: Can be fully tested by creating both a command step and a sequence step that use the new primitive action, saving them, reloading them, and verifying the configured values remain visible and unchanged in the authoring UI.

**Acceptance Scenarios**:

1. **Given** an author is editing a step, **When** they choose the "Wait for Image" primitive action, **Then** they can configure an optional image, an optional image certainty value, and an optional timeout in milliseconds.
2. **Given** an author leaves the timeout blank, **When** they save the step, **Then** the step uses the default timeout of 1000 milliseconds.
3. **Given** an author saves a "Wait for Image" step in a command or sequence with configured values, **When** the definition is reopened later, **Then** the same parameter values are shown in the authoring UI.

---

### User Story 2 - Wait Without Failing Execution (Priority: P1)

As an operator running automation, I want execution to pause until a target image appears or a timeout is reached, so timing-sensitive command and sequence steps can wait for the game state to stabilize without turning normal waiting into a failure.

**Why this priority**: Runtime waiting behavior is the core purpose of the feature. Without this, the new primitive action does not change execution outcomes in a useful way.

**Independent Test**: Can be fully tested by running both a command and a sequence that use the new primitive action in three scenarios: the image appears before timeout, no image is configured, and the configured image cannot be loaded.

**Acceptance Scenarios**:

1. **Given** a "Wait for Image" step has a valid image configured and that image appears before the timeout expires, **When** the step executes, **Then** execution resumes as soon as the image is detected and the step completes normally.
2. **Given** a "Wait for Image" step has no image configured, **When** the step executes, **Then** the step waits for the timeout period and completes normally without raising an error.
3. **Given** a "Wait for Image" step references an image that cannot be loaded at runtime, **When** the step executes, **Then** the step waits for the timeout period and completes normally without raising an error.
4. **Given** a "Wait for Image" step has a valid image configured but the image never appears before the timeout expires, **When** the step executes, **Then** the step completes normally at timeout and execution continues to the next step.

---

### User Story 3 - Inspect Wait Outcomes in Logs (Priority: P2)

As an operator reviewing automation history, I want the execution log to show both the configured wait parameters and the actual reason the wait ended, so I can tell whether the step resumed because an image was detected, because time ran out, or because the image asset was unavailable.

**Why this priority**: Logging is essential for diagnosing whether a wait behaved as intended, but it is secondary to the authoring and runtime behavior itself.

**Independent Test**: Can be tested by executing the step in each supported exit condition and confirming the execution log records the same configured parameters plus the final exit condition for each run.

**Acceptance Scenarios**:

1. **Given** a "Wait for Image" step executes, **When** its execution record is opened, **Then** the log shows the configured image reference, certainty value, and timeout value used for that run.
2. **Given** the step ends because the image was detected, **When** the execution record is viewed, **Then** the log states that the exit condition was image detected.
3. **Given** the step ends because the timeout elapsed without detection, **When** the execution record is viewed, **Then** the log states that the exit condition was timeout elapsed.
4. **Given** the step references an image that could not be loaded, **When** the execution record is viewed, **Then** the log states that the exit condition was image unavailable.

### Edge Cases

- Timeout is omitted during authoring.
- Timeout is set to 0 milliseconds.
- The step has no image configured, so certainty should not block saving or execution.
- A previously selected image is deleted or otherwise becomes unavailable after the step was saved.
- The image appears just before the timeout expires.
- The image appears after the timeout has already elapsed.
- Command and sequence stop or termination semantics are out of scope for this feature and must not redefine the wait step's terminal exit conditions.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: System MUST provide a new primitive action named "Wait for Image" that can be selected in executable command-step authoring and executable sequence-step authoring.
- **FR-002**: System MUST allow a "Wait for Image" step to store an optional image reference.
- **FR-003**: System MUST allow a "Wait for Image" step to store an optional certainty value that is used only when an image reference is present.
- **FR-004**: System MUST allow a "Wait for Image" step to store an optional timeout in milliseconds.
- **FR-005**: System MUST apply a default timeout of 1000 milliseconds when no timeout value is provided.
- **FR-005A**: System MUST apply the existing default image-detection certainty when an image reference is present and no certainty value is provided.
- **FR-006**: System MUST pause step execution until either the configured image is detected or the effective timeout elapses.
- **FR-007**: System MUST resume execution immediately when the configured image is detected before timeout.
- **FR-008**: System MUST treat timeout expiration as a normal completion outcome, not as an execution error.
- **FR-009**: System MUST continue to the next command step or sequence step after a timeout-based completion unless a broader command or sequence stop condition completes after the wait finishes.
- **FR-010**: System MUST wait for the timeout period and complete normally when no image reference is configured.
- **FR-011**: System MUST wait for the timeout period and complete normally when the configured image cannot be loaded or resolved at runtime.
- **FR-012**: System MUST expose the step's configurable parameters in the command and sequence authoring UI, including the selected image reference when present, the certainty value when relevant, and the effective timeout.
- **FR-013**: System MUST persist and reload all "Wait for Image" step parameters without loss or silent substitution beyond the documented default timeout behavior in both command and sequence definitions.
- **FR-014**: System MUST include the step's configured parameters in the execution log for each "Wait for Image" step run.
- **FR-015**: System MUST record exactly one terminal exit condition for each "Wait for Image" step execution: image detected, timeout elapsed, or image unavailable.
- **FR-016**: System MUST record the image unavailable exit condition when the step references an image that cannot be loaded or resolved at runtime, even though the step still waits until timeout before completing.
- **FR-017**: System MUST NOT record timeout expiration as an error outcome for this step type.
- **FR-018**: System MUST allow the step to be saved and executed without an image reference.
- **FR-019**: System MUST validate timeout values as non-negative durations in milliseconds.
- **FR-020**: System MUST NOT introduce any terminal wait exit condition beyond image detected, timeout elapsed, and image unavailable.

### Key Entities *(include if feature involves data)*

- **Wait for Image Step**: A primitive action step in a command or sequence that pauses execution until a target image is detected or a timeout elapses. Key attributes: optional image reference, optional certainty value, timeout in milliseconds.
- **Wait Exit Condition**: The single final reason a wait step completed. Allowed values: image detected, timeout elapsed, image unavailable.
- **Execution Log Entry**: The recorded history item for a wait step run, including the configured parameters used for the run and the final exit condition.

### Assumptions

- The "Wait for Image" primitive action uses the same image-selection experience and certainty scale already familiar from other image-based authoring flows.
- When no image is configured, the certainty value is ignored and does not prevent save or execution.
- When an image is configured and certainty is omitted, the existing default image-detection certainty is used.
- A timeout value of 0 milliseconds is valid and causes the step to complete immediately unless the image is already detectable at execution start.
- Image unavailable means the referenced image asset cannot be loaded or resolved for the run; it is distinct from a valid image that simply never appears on screen before timeout.
- When the image is unavailable, the step still consumes its configured timeout window rather than ending early, and the final recorded exit condition remains image unavailable.
- Command and sequence stop or termination behavior is outside this feature's scope and must not add a fourth wait exit condition.

### Dependencies

- Existing command and sequence primitive action authoring surfaces must be extended to present and persist the new step type and its parameters.
- Existing execution logging must support parameter visibility plus the new wait exit-condition values.
- Existing image-selection assets and definitions must remain available to authors who choose to configure an image-based wait.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Authors can create, save, and reload a "Wait for Image" step with its configured parameters in both command and sequence authoring flows in 100% of acceptance-test scenarios without manual file edits.
- **SC-002**: In 100% of tested runs where the target image appears before timeout, the step resumes before the timeout limit and execution continues normally.
- **SC-003**: In 100% of tested runs where no image is configured, the image never appears, or the image is unavailable, the step completes without a failure state and execution continues normally after the timeout.
- **SC-004**: In 100% of tested runs where timeout is omitted, the effective wait limit is 1000 milliseconds.
- **SC-005**: In 100% of tested "Wait for Image" executions, the execution log shows the configured parameters and exactly one final exit condition.
