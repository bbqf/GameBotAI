# Feature Specification: Commands Based on Detected Image

**Feature Branch**: `005-image-detect-command`  
**Created**: 2025-12-05  
**Status**: Draft  
**Input**: User description: "commands based on detected image. I need a new command trigger and parameterized definition combined: if exactly one image is detected on the current screen, I need the possibility to use the coordinates of the detected image to be used as coordinates for the actions. For example is a 'Home' image is detected, I need to be able to tap at the center of the detected image, or if another image is detected on the screen, I need to be able to tap the location of the detected image plus offset x,y. Make sure this only works if only one image is detected and an appropriate error message is logged if more than one images are detected. A good default for the confidence should be .8, but it should be configurable within the command itself. These detected coordinates and offsets should be available for all the input actions requiring coordinates."

## User Scenarios & Testing (mandatory)

### User Story 1 - Tap detected image center (Priority: P1)

As a command author, I can define a command that, when executed, detects a specific reference image on the current screen and taps the center of the uniquely detected image.

**Why this priority**: This is the core user value: map a known UI element (reference image) to a tap without hard-coded coordinates.

**Independent Test**: Seed a screen with a single occurrence of the template. Execute the command. The tap occurs at the detected center; logs confirm unique detection and action.

**Acceptance Scenarios**:

1. Given a screen containing exactly one instance of the reference image, When the image is detected with default confidence, Then the system taps the center of the detected image and logs success.
2. Given a screen with no instance of the reference image, When the command runs, Then no tap occurs and an informative message is logged (no detection).

---

### User Story 2 - Tap with offset from detection (Priority: P2)

As a command author, I can specify an (x,y) offset so that the tap happens at a position relative to the uniquely detected image (e.g., top-right or a fixed n-pixel offset).

**Why this priority**: Offsets are commonly required to interact with controls adjacent to the detected element or to avoid occlusion.

**Independent Test**: Seed a single detection and specify offsets. Execute the command and verify the tap point equals detected center plus offset and lies within screen bounds.

**Acceptance Scenarios**:

1. Given one detected image and an offset (dx, dy), When the command runs, Then the tap occurs at (centerX+dx, centerY+dy).
2. Given an offset that would move the tap outside the screen, When the command runs, Then the position is clamped to the nearest in-bounds coordinate and a debug log mentions clamping.

---

### User Story 3 - Enforce unique detection (Priority: P1)

As a command author, I want the command to run only if exactly one detection is present; otherwise it should not perform actions and should log a clear error when multiple detections are found.

**Why this priority**: Prevents ambiguous or unintended actions when multiple candidates exist.

**Independent Test**: Place two instances of the template on screen. Execute the command. Verify no tap occurs and an error log states multiple detections found with the count.

**Acceptance Scenarios**:

1. Given multiple detections above the confidence threshold, When the command runs, Then no input action is performed and an error is logged mentioning the detection count and threshold.
2. Given exactly one detection below the configured threshold, When the command runs, Then no action occurs and an info/warn log indicates insufficient confidence.

---

### Edge Cases

- No screenshot available or screen capture fails: command exits gracefully with info log.
- Reference image missing or unreadable: command exits with error log.
- Confidence configured outside [0,1]: command validation fails and logs configuration error.
- Offset values very large: resulting coordinates are clamped to screen bounds; logged at info level.
- Template larger than screen: detection is skipped with info log.

## Requirements (mandatory)

### Functional Requirements

- **FR-001**: Command MUST support a new detection-based target mode that resolves coordinates from a uniquely detected reference image at execution time.
- **FR-002**: Command MUST accept a reference image identifier and an optional confidence threshold (default 0.8) override local to the command.
- **FR-003**: Command MUST support choosing the base point for coordinates as the detection center; optionally allow top-left as a derived mode via offset.
- **FR-004**: Command MUST support optional (dx, dy) offsets applied to the base detection point; final coordinates MUST be clamped to screen bounds.
- **FR-005**: Command MUST only proceed with input actions if exactly one detection is found above threshold; otherwise, it MUST skip action and log an appropriate message.
- **FR-006**: System MUST log at information level on success (unique detection, coordinates used), and at error level when multiple detections are found.
- **FR-007**: The resolved coordinates (including offset) MUST be available to all input actions that require coordinates (e.g., tap, swipe start/end, drag).
- **FR-008**: Validation MUST reject invalid configurations (missing reference image id; threshold not in [0,1]; offsets not numeric), producing clear messages.
- **FR-009**: When zero detections are found, command MUST not perform actions and SHOULD log an info message indicating no match.
- **FR-010**: Detection MUST honor detection timeouts and max-results consistent with existing detection pipeline defaults.

### Key Entities

- **DetectionTarget**: A parameter group on a command containing `referenceImageId`, `confidence` (default 0.8), optional `offsetX`, `offsetY`, and `basePoint` (center by default).
- **ResolvedCoordinate**: Structure representing the derived screen coordinate (x,y) after applying base point and offsets, plus metadata (confidence, bbox).
- **Command**: The user-defined action container that can reference a DetectionTarget for coordinate resolution.

## Success Criteria (mandatory)

### Measurable Outcomes

- **SC-001**: With one on-screen match above threshold, the command resolves and applies coordinates within 100 ms in 95% of runs.
- **SC-002**: When 2+ matches exist, 100% of runs skip actions and emit an error log mentioning the count.
- **SC-003**: 100% of out-of-bounds computed coordinates are clamped, and clamping is observable via debug logs.
- **SC-004**: Authors can set confidence per-command; 100% of commands respect configured thresholds between 0.0 and 1.0.
- **SC-005**: All coordinate-requiring actions (tap, swipe, drag) can consume resolved coordinates without additional user input.

## Clarifications

### Session 2025-12-05

- Q: Which base point modes are supported for coordinate resolution? → A: center only

Applied updates:
- FR-003 clarified: Base point is center-only; other points are modeled via offsets (no explicit mode flags).
