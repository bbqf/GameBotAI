# Feature Specification: Key Input and Swipe Primitive Actions

**Feature Branch**: `054-key-swipe-actions`  
**Created**: 2026-06-04  
**Status**: Implemented
**Input**: User description: "i want to be able to add key input and swipe as primitive actions as steps of a command via UI. it should be done in the same style and reusing all possible components that are available for the tap, wait for image and connect to game."

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Add Key Input Step (Priority: P1)

A user building a command wants to press a keyboard key as part of an automation sequence. They select "Key Input" from the action type selector, enter the key identifier (e.g., "Enter", "Escape", "F5"), and add it as a step. The step appears in the step list labeled with the key name, and the user can reorder, edit, or delete it like any other step.

**Why this priority**: Key presses are fundamental automation actions used to dismiss dialogs, confirm prompts, or trigger shortcuts in games. This is immediately useful on its own.

**Independent Test**: Can be fully tested by opening a command, selecting "Key Input", entering a key name, adding the step, and verifying it appears correctly in the step list with the key name displayed.

**Acceptance Scenarios**:

1. **Given** a command editor is open with no pending step, **When** the user selects "Key Input" from the action type selector, **Then** a Key Input panel appears with a key identifier input field and Add/Cancel buttons.
2. **Given** the Key Input panel is shown, **When** the user enters a key identifier and clicks Add, **Then** the step is added to the step list showing the key name, and the panel resets.
3. **Given** the Key Input panel is shown, **When** the user clicks Add without entering a key identifier, **Then** a validation error is shown and the step is not added.
4. **Given** a Key Input step is in the step list, **When** the user clicks Edit, **Then** the Key Input panel repopulates with the current key identifier.
5. **Given** the Key Input panel is shown, **When** the user clicks Cancel, **Then** the panel closes without adding a step and any entered text is discarded.

---

### User Story 2 - Add Swipe Step (Priority: P2)

A user building a command wants to perform a swipe gesture as part of an automation sequence (e.g., scrolling a list, swiping between screens). They select "Swipe" from the action type selector, specify the start position, end position, and optionally the swipe duration, then add it as a step. The step appears in the step list with a summary of the swipe parameters.

**Why this priority**: Swipe is a common game interaction. It depends on the same infrastructure as Key Input so both are natural to deliver together.

**Independent Test**: Can be fully tested by opening a command, selecting "Swipe", filling in start and end coordinates, adding the step, and verifying the step list shows the swipe summary.

**Acceptance Scenarios**:

1. **Given** a command editor is open with no pending step, **When** the user selects "Swipe" from the action type selector, **Then** a Swipe panel appears with start position fields, end position fields, an optional duration field, and Add/Cancel buttons.
2. **Given** the Swipe panel is shown with start and end positions filled in, **When** the user clicks Add, **Then** the step is added to the step list with a readable summary (e.g., "(0,0) → (100,200)"), and the panel resets.
3. **Given** the Swipe panel is shown, **When** the user clicks Add with any required position field left empty, **Then** a validation error is shown on the empty field and the step is not added.
4. **Given** the Swipe panel is shown, **When** the user enters an optional duration and clicks Add, **Then** the step list description includes the duration.
5. **Given** a Swipe step is in the step list, **When** the user clicks Edit, **Then** the Swipe panel repopulates with all previously entered values.
6. **Given** the Swipe panel is shown, **When** the user clicks Cancel, **Then** the panel closes without adding a step.

---

### User Story 3 - Both actions consistent with existing action style (Priority: P3)

The Key Input and Swipe panels look, behave, and validate consistently with the existing Tap, Wait for Image, and Ensure Game Running panels. The same shared controls (field layout, buttons, error display) are used so the overall command editor feels cohesive.

**Why this priority**: Visual and behavioral consistency makes the product feel polished and reduces the user's cognitive load when working across action types.

**Independent Test**: Can be verified by inspecting both new panels side-by-side with existing panels to confirm matching field layout, button placement, error styling, and form behavior.

**Acceptance Scenarios**:

1. **Given** a user switches between Key Input, Swipe, Tap, Wait for Image, and Ensure Game Running, **Then** all panels use the same layout structure, button styles, and error formatting.
2. **Given** a user has partially filled a Key Input or Swipe panel and switches to a different action type, **Then** switching back resets the panel to its empty state (consistent with existing action switching behavior).

---

### Edge Cases

- A swipe duration of zero is valid; the runtime determines the behavior of a zero-duration swipe.
- Long key identifiers in the step list label are truncated with an ellipsis to fit the list item width.
- What happens if start and end positions for a swipe are identical (zero-distance swipe)?
- What happens when the user edits a Key Input step and clears the key identifier before saving?

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The action type selector MUST include "Key Input" and "Swipe" as selectable action types alongside the existing options.
- **FR-002**: Selecting "Key Input" MUST display a Key Input configuration panel with a required key identifier field and Add/Cancel buttons.
- **FR-003**: The key identifier field MUST accept free-text input representing a key name or code (e.g., "Enter", "Escape", "F5", "a").
- **FR-004**: The key identifier field MUST be required; attempting to add a step without a value MUST show a validation error.
- **FR-005**: Selecting "Swipe" MUST display a Swipe configuration panel with start position (X, Y), end position (X, Y), optional duration, and Add/Cancel buttons.
- **FR-006**: Start X, Start Y, End X, and End Y MUST be required integer fields representing absolute screen pixel coordinates; leaving any empty MUST show a validation error.
- **FR-007**: Duration MUST be an optional non-negative integer field representing the swipe duration in milliseconds.
- **FR-008**: A confirmed Key Input step MUST appear in the step list with the key identifier displayed as the step label; long identifiers MUST be truncated with an ellipsis to fit the list item width.
- **FR-009**: A confirmed Swipe step MUST appear in the step list with a readable summary of start position, end position, and duration (if set).
- **FR-010**: Clicking Edit on a Key Input step MUST repopulate the Key Input panel with the existing key identifier.
- **FR-011**: Clicking Edit on a Swipe step MUST repopulate the Swipe panel with all existing values.
- **FR-012**: Both panels MUST use the same shared layout components (field containers, button bar, error display) used by existing action panels.
- **FR-013**: Both panels MUST apply the same validation timing behavior as existing panels (deferred until Add/Save is clicked).
- **FR-014**: Switching the action type selector away from Key Input or Swipe and back MUST reset the panel to its empty state.
- **FR-015**: Key Input and Swipe steps MUST support deletion and drag-to-reorder in the step list, consistent with other step types.

### Key Entities

- **Key Input Step**: A command step that performs a single key press (not a hold); has exactly one attribute — the key identifier. No hold duration or repeat count.
- **Swipe Step**: A command step that performs a swipe gesture; has start position (X, Y), end position (X, Y), and optional duration (ms).

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A user can add a Key Input step with a valid key identifier in under 30 seconds from opening the action selector.
- **SC-002**: A user can add a Swipe step with start/end positions in under 45 seconds from opening the action selector.
- **SC-003**: All validation errors for Key Input and Swipe panels are visible and clearly describe the issue within the panel bounds, without page navigation.
- **SC-004**: Switching among all five action types (Tap, Wait for Image, Ensure Game Running, Key Input, Swipe) produces no visible layout shifts or style inconsistencies.
- **SC-005**: Editing and re-saving an existing Key Input or Swipe step preserves all previously entered values and updates the step list correctly.

## Clarifications

### Session 2026-06-04

- Q: Are swipe coordinates absolute screen pixels or normalized (0–1 relative to screen size)? → A: Absolute pixel coordinates (integers, e.g., x=540, y=960)
- Q: Is the key identifier a free-text field or a predefined dropdown of supported keys? → A: Free-text input; runtime is responsible for validating the key name
- Q: Should Key Input support an optional hold duration (how long to hold the key down)? → A: No — key press only; single key identifier field, no hold duration
- Q: How should the step list label render when a key identifier is very long? → A: Truncate with ellipsis to fit the list item width

## Assumptions

- Swipe positions are defined by absolute screen pixel coordinates (integers), consistent with how offsets work in existing actions. No image-based start/end point targeting is required for this feature.
- The key identifier is a free-text string; validation of whether it corresponds to a real key is left to the runtime, not the UI.
- A swipe duration of zero is treated as valid input (instantaneous swipe); the runtime determines behavior.
- No modifier key support (Ctrl, Alt, Shift combinations) is required in this iteration.
- Key Input is a single instantaneous key press with no configurable hold duration or repeat count.
