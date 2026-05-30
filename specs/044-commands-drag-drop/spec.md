# Feature Specification: Drag and Drop for Command Steps

**Feature Branch**: `044-commands-drag-drop`  
**Created**: 2026-05-30  
**Status**: Draft  
**Input**: User description: "I want to be able to use drag and drop not only in sequences, but also in commands. Reuse the components you've already created"

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Reorder Command Steps via Drag and Drop (Priority: P1)

A user editing a command wants to reorder its steps by dragging and dropping them into a new position, instead of clicking up/down arrow buttons repeatedly.

**Why this priority**: This is the core feature request and delivers the most immediate usability improvement. Commands with many steps currently require repeated arrow-button clicks to reorder, which is tedious.

**Independent Test**: Open a command with at least two steps in the command editor. Grab a step by its drag handle, drag it to a new position, and release. The step should appear in the new position and the order should persist on save.

**Acceptance Scenarios**:

1. **Given** a command with multiple steps is open in the editor, **When** the user drags a step from position 2 to position 4, **Then** the step appears at position 4 and all other steps shift accordingly.
2. **Given** the user begins dragging a step, **When** they hover over valid drop positions, **Then** a drop indicator line appears between steps showing where the item will land.
3. **Given** the user begins dragging a step, **When** they release the step at any valid position, **Then** the reordering is applied immediately without requiring a separate save action.
4. **Given** the user drags a step and then presses Escape (or moves back to origin), **When** the drag is cancelled, **Then** the steps return to their original order without any change.

---

### User Story 2 - Consistent Interaction Pattern Across Commands and Sequences (Priority: P2)

A user who already knows how to reorder steps in the Sequences editor finds that the same drag handle and visual feedback work identically in the Commands editor.

**Why this priority**: Consistency reduces learning overhead. Users already familiar with sequence DnD should not need to relearn a different interaction for commands.

**Independent Test**: Compare the drag handle appearance and drag behaviour in both the Sequences editor and the Command step editor — they should be visually and functionally equivalent.

**Acceptance Scenarios**:

1. **Given** a user familiar with sequence step reordering, **When** they open a command and look for the drag handle, **Then** the same drag handle icon (⠿) appears in the same position on each step row.
2. **Given** the user drags a command step, **When** a drop target is hovered, **Then** the drop indicator line matches the visual style used in the Sequences editor.

---

### Edge Cases

- What happens when a command has only one step? The drag handle should be present but dragging has no effect (only one position exists).
- What happens when the command editor is in a read-only or disabled state? Drag handles should be hidden or inert.
- What happens if the user drops a step on its original position? No reordering occurs and the list remains unchanged.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The command step list in the command editor MUST support drag-and-drop reordering using the same drag handle component (`SortableStepItem`) already used in the sequence step editor.
- **FR-002**: The command step list MUST display a visual drop indicator (`DropIndicator`) between steps during a drag operation, consistent with the sequence editor.
- **FR-003**: The command step list MUST use the existing `@dnd-kit/sortable` vertical list sorting strategy, matching the sequence editor implementation.
- **FR-004**: The existing `ReorderableList` up/down arrow button controls in the command editor MUST be replaced by the drag-and-drop interaction.
- **FR-005**: Reordering a step via drag-and-drop MUST update the in-memory step order immediately; the change MUST be persisted when the user saves the command form.
- **FR-006**: The drag-and-drop interaction MUST be disabled when the command form is in a disabled/read-only state.
- **FR-007**: Cancelling a drag (e.g., pressing Escape or dragging off-screen) MUST restore the original step order with no side effects.
- **FR-008**: The `PointerSensor` with the same 5px activation constraint used in the sequence editor MUST be used, to prevent accidental drag activation on clicks.
- **FR-009**: Keyboard-only reordering is out of scope; no keyboard sensor or keyboard fallback is required. Users must use a pointer device (mouse or touch) to reorder steps.

### Key Entities

- **Command**: A named automation unit composed of an ordered list of steps; reordering applies to its step list.
- **Command Step**: One item in a command's step list (Command reference, PrimitiveTap, or WaitForImage); the unit being dragged.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A user can reorder any command step via drag-and-drop in a single drag gesture, with no additional clicks required.
- **SC-002**: The visual feedback during drag (drag handle, drop indicator, item lift) matches the sequence editor — verified by confirming the same `SortableStepItem` and `DropIndicator` components are used in both editors (no duplicated markup).
- **SC-003**: 100% of existing command editor functionality (add step, delete step, edit step fields, save) remains intact after the change.
- **SC-004**: The code change reuses at least the `SortableStepItem` and `DropIndicator` components without duplication.

## Clarifications

### Session 2026-05-30

- Q: Should keyboard users be able to reorder command steps after the arrow buttons are removed? → A: No — DnD only (mouse/touch); no keyboard sensor or fallback required.

## Assumptions

- The command step list is always a flat (non-nested) list; no loop blocks or scoped DnD is required for commands.
- The existing `ReorderableList` component is not used anywhere else in the UI in a way that would be broken by its removal from `CommandForm`; if it is, only the command-form usage is changed.
- Step type diversity (Command reference, PrimitiveTap, WaitForImage) does not affect drag-and-drop behaviour — all step types are draggable equally.
