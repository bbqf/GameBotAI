# Feature Specification: Command Editor Rework

**Feature Branch**: `053-command-editor-rework`
**Created**: 2026-06-04
**Status**: Draft
**Input**: User description: "Let's rework and extend command creation/editing. I want to be able to specify all primitive actions. Different primitive actions have different attributes, which clutter the ui enormously, so let me select the action first, which will open a panel with corresponding attributes. Create one panel for each action. Remove all current controls: Add command (remove completely, it's not needed), primitive tap (rename to Tap, use attributes for the panel), wait for image (use attributes) and ensure game running."

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Select Action Then Fill Panel (Priority: P1)

A user editing a command wants to add a new step. Instead of all action fields cluttering the screen at once, they first choose which primitive action they want from a selector. Once chosen, a dedicated panel appears showing only the fields relevant to that action.

**Why this priority**: This is the core interaction redesign. All other stories depend on this flow working correctly.

**Independent Test**: Open command edit view, click add-step selector, pick any action, confirm only that action's panel appears with its attributes.

**Acceptance Scenarios**:

1. **Given** the command editor is open, **When** the user opens the add-step selector, **Then** they see the three primitive action options: Tap, Wait for Image, Ensure Game Running — with no Command option visible.
2. **Given** an action is selected from the selector, **When** the panel opens, **Then** only the attributes for that specific action are shown, with no fields from other action types visible.
3. **Given** a panel is open for one action, **When** the user picks a different action, **Then** the previous panel closes and the new action's panel opens.
4. **Given** a step was just confirmed and added, **When** the add-step area is inspected, **Then** the selector shows blank and no panel is visible.
5. **Given** an existing step is in the step list, **When** the user clicks on it, **Then** the step's attribute panel opens pre-filled with the step's current values.

---

### User Story 2 - Tap Panel (Priority: P2)

A user selects "Tap" and sees a focused panel with the four Tap-specific attributes: reference image, confidence, horizontal offset, and vertical offset.

**Why this priority**: Tap is the most commonly used primitive action; its panel must be clean and complete.

**Independent Test**: Select Tap, verify the panel shows reference image selector (required), confidence input, offsetX input, and offsetY input — and nothing else.

**Acceptance Scenarios**:

1. **Given** the Tap panel is open, **When** the user selects a reference image and submits, **Then** a Tap step is added to the command with the chosen image and default confidence/offsets.
2. **Given** the Tap panel is open, **When** the user leaves the reference image empty and submits, **Then** submission is blocked and a validation error indicates the image is required.
3. **Given** the Tap panel is open, **When** the user fills confidence, offsetX, and offsetY and submits, **Then** all four values are saved on the step.

---

### User Story 3 - Wait for Image Panel (Priority: P2)

A user selects "Wait for Image" and sees a focused panel with timeout (required) and an optional image with confidence.

**Why this priority**: WaitForImage is the second most used primitive; its panel must clearly show which fields are optional vs. required.

**Independent Test**: Select Wait for Image, verify the panel shows timeoutMs input (required), optional reference image selector, and optional confidence input.

**Acceptance Scenarios**:

1. **Given** the Wait for Image panel is open, **When** the user enters a timeout and no image and submits, **Then** a WaitForImage step is added that waits purely for the duration.
2. **Given** the Wait for Image panel is open, **When** the user leaves the timeout empty and submits, **Then** submission is blocked and a validation error indicates timeout is required.
3. **Given** the Wait for Image panel is open, **When** the user provides a timeout, an image, and a confidence and submits, **Then** all three values are saved on the step.

---

### User Story 4 - Ensure Game Running Panel (Priority: P3)

A user selects "Ensure Game Running" and sees a minimal panel (no configurable attributes) with just a confirm/add button.

**Why this priority**: No attributes to configure; the panel is trivially simple but must exist for consistency.

**Independent Test**: Select Ensure Game Running, verify the panel shows a description of the action and an add/confirm control — no input fields.

**Acceptance Scenarios**:

1. **Given** the Ensure Game Running panel is open, **When** the user confirms, **Then** an EnsureGameRunning step is added to the command.
2. **Given** the Ensure Game Running panel is open, **When** it is displayed, **Then** no input fields are shown (the action has no configurable attributes).

---

### Edge Cases

- What happens when a user opens a panel, partially fills it, then switches to a different action? Panel state should reset so stale values from the previous action are not carried over.
- What happens when an existing step of a type that no longer has a UI entry point (Command) is displayed in the step list? It should still render and be deletable, but cannot be added via the new UI.
- What happens when an image referenced by an existing Tap or Wait for Image step is deleted? The step list should indicate the reference is stale (existing staleness behaviour preserved).

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The step-addition UI MUST present a selector listing exactly three choices: Tap, Wait for Image, and Ensure Game Running. The "Add command" (Command step type) option MUST be removed entirely and not appear in any form.
- **FR-002**: Selecting an action from the selector MUST open a dedicated attribute panel for that action only. Panels for other actions MUST NOT be visible simultaneously.
- **FR-003**: The Tap panel MUST display: a required reference image selector, an optional confidence field (0–1), an optional offsetX field (integer pixels), and an optional offsetY field (integer pixels). No other fields may appear.
- **FR-004**: The Wait for Image panel MUST display: a required timeout field (non-negative milliseconds), an optional reference image selector, and an optional confidence field (0–1). No other fields may appear.
- **FR-005**: The Ensure Game Running panel MUST display a description of the action and a confirm/add control. It MUST NOT display any input fields.
- **FR-006**: Switching the action selector to a different action MUST clear/reset any partially entered data from the previously open panel before showing the new panel.
- **FR-007**: Submitting a Tap step MUST be blocked if the reference image is not selected; the panel MUST show a validation error indicating the image is required.
- **FR-008**: Submitting a Wait for Image step MUST be blocked if the timeout field is empty or invalid; the panel MUST show a validation error.
- **FR-009**: Successfully confirming an action in a panel MUST append the new step to the command's step list and reset the selector to blank (panel hides), ready for the user to pick the next action.
- **FR-010**: Existing steps of all types (including Command steps created before this change) MUST continue to render in the step list with their current display text and remain deletable.
- **FR-011**: Clicking an existing step in the step list MUST open its corresponding attribute panel pre-filled with that step's current values, allowing the user to edit and save changes in place.

### Key Entities

- **Command**: A named sequence of steps; unchanged except that new steps can only be Tap, Wait for Image, or Ensure Game Running.
- **Step**: A single action in a command sequence; discriminated by action type, each type has its own attribute set.
- **Attribute Panel**: A UI region tied to one specific action type; visible only when that action is selected; contains only that action's input fields.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A user can add any of the three supported primitive action steps (Tap, Wait for Image, Ensure Game Running) without seeing unrelated fields from the other two actions at any point during input.
- **SC-002**: The command editor shows zero fields from removed action types (Command step addition) in the add-step area.
- **SC-003**: Each action panel contains exactly the fields documented for that action in FR-003 through FR-005 — no more, no fewer.
- **SC-004**: Validation prevents saving incomplete steps (missing required fields) for all three action types; each error clearly identifies which field is missing.
- **SC-005**: Switching between action types while a panel is partially filled does not carry stale field values into the newly selected panel.
- **SC-006**: After a step is successfully added, the add-step selector returns to its blank state with no panel visible; the user must explicitly choose an action type to add another step.
- **SC-007**: Clicking any existing step in the step list opens that step's attribute panel pre-filled; the user can modify values and save without deleting and re-adding the step.

## Clarifications

### Session 2026-06-04

- Q: When a user wants to change an attribute on an existing step, what should happen? → A: Clicking the step opens its attribute panel pre-filled for in-place editing.
- Q: After adding a step, what is the state of the add-step area? → A: Selector resets to blank; panel closes.

## Assumptions

- The "Command" step type (referencing another command by ID) will no longer be addable via the UI but will remain in the data model and will still display and be deletable in the step list if previously created.
- The existing "Detection" section of the command form (used for game-detection, not step creation) is out of scope for this feature and remains unchanged.
- Display names follow the user's stated naming: "Tap" (not "Primitive Tap"), "Wait for Image", "Ensure Game Running".
- Existing validation rules (e.g., staleness checking for image references, confidence range 0–1) are preserved.
