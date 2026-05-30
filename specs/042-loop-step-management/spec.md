# Feature Specification: Sequence Loop Step Management

**Feature Branch**: `042-loop-step-management`  
**Created**: 2026-05-30  
**Status**: Draft  
**Input**: User description: "I want to be able to create new steps in the sequence within the loop as well as outside. Now it works like this: once I add a loop to the sequence, I can only create new steps within this loop. This is OK for starters, and it must be kept, however I cannot add a step after the loop. Once The step is added outside the loop, I also need to be able to move the step before and after the loop, i.e. reordering the steps should work only between the nesting level where the steps are, not moving the steps in and out of the loop just by reordering."

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Add Step Outside a Loop (Priority: P1)

A user is authoring a sequence that contains one or more loops. They want to add a step that executes at the top level of the sequence — either before or after a loop — but currently the only "add step" control available targets the inside of the loop. This story covers making it possible to add steps at the top-level sequence scope regardless of whether loops are present.

**Why this priority**: Without this, any sequence that uses a loop is structurally incomplete — the user cannot add post-loop cleanup steps, conditional follow-ups, or any actions that should run after the loop finishes. This is a blocking gap.

**Independent Test**: Can be fully tested by opening a sequence that contains a loop and verifying that an "add step" control exists at the top-level scope (outside the loop), then adding a step and confirming it appears at the top level.

**Acceptance Scenarios**:

1. **Given** a sequence with a loop and no top-level steps after the loop, **When** the user triggers "add step" at the top-level scope, **Then** a new step is inserted at the top level of the sequence, not inside the loop.
2. **Given** a sequence where the last element is a loop, **When** the user adds a step using the top-level add control, **Then** the new step appears after the loop in the sequence.
3. **Given** a sequence with top-level steps both before and after a loop, **When** the user clicks the persistent "Add step" button, **Then** the new step is appended at the end of the top-level scope.
4. **Given** a sequence containing only a loop (no other top-level steps), **When** the user opens the sequence editor, **Then** a top-level "add step" control is visible and functional.

---

### User Story 2 - Reorder Steps Within the Same Nesting Level (Priority: P2)

A user has a sequence with a mix of top-level steps and at least one loop. They want to reorder the top-level steps — including changing whether a step runs before or after the loop — using the existing reordering controls. They expect that reordering operates only within the same scope: top-level steps can be shuffled among top-level items, and loop steps can be shuffled among loop items, but steps cannot accidentally cross the loop boundary.

**Why this priority**: Once users can add steps at both levels, reordering is the next essential authoring operation. Scope-constrained reordering prevents accidental structural changes and makes the authoring model predictable.

**Independent Test**: Can be fully tested by creating a sequence with two top-level steps and one loop (with its own internal steps), then reordering the top-level steps and verifying that loop contents are unaffected, and that reordering inside the loop does not affect top-level step order.

**Acceptance Scenarios**:

1. **Given** a sequence with steps [A, Loop, B] at the top level, **When** the user drags step B before the Loop, **Then** the sequence becomes [A, B, Loop] and Loop contents are unchanged.
2. **Given** a sequence with steps [A, Loop, B] at the top level, **When** the user drags step A after the Loop, **Then** the sequence becomes [Loop, B, A] and Loop contents are unchanged.
3. **Given** a loop with internal steps [X, Y, Z], **When** the user drags them into the order [Z, X, Y], **Then** the top-level sequence order is unchanged.
4. **Given** a top-level step being dragged, **When** the user hovers it over the interior of a loop, **Then** the loop interior displays a "not allowed" visual indicator and the step snaps back to its original position on release.
5. **Given** a loop-interior step being dragged, **When** the user hovers it over a top-level drop target outside the loop, **Then** the top-level drop target displays a "not allowed" visual indicator and the step snaps back to its original position on release.

---

### User Story 3 - Add Steps Inside a Loop (Priority: P3)

A user adds a loop to a sequence and then adds steps inside that loop. This is the currently working behavior and must continue to work without regression.

**Why this priority**: Existing functionality that must be preserved; lower priority because it already works.

**Independent Test**: Can be fully tested by adding a loop to a sequence, then adding multiple steps inside the loop and verifying they are scoped to the loop.

**Acceptance Scenarios**:

1. **Given** a sequence with a loop, **When** the user uses the in-loop "add step" control, **Then** a new step is added inside the loop, not at the top level.
2. **Given** a loop with existing internal steps, **When** the user adds another step inside the loop, **Then** the new step is added at the correct position within the loop.

---

### Edge Cases

- What happens when a sequence has only a loop and the user tries to add a top-level step? The add control must still be accessible and functional.
- What happens when a loop is the first element and the user wants a step before it? The user appends a step using the persistent "Add step" button (which places it at the end of the top-level scope) and then drags it to the desired position before the loop. Direct positional insertion is not supported; the "Add step" button always appends.
- What happens when a loop is nested inside another loop? **Out of scope for this feature** — nested loops (loop within a loop) are a known limitation and will be addressed in a follow-up feature.
- How does the system handle reordering when only one step exists at a nesting level? Controls may be disabled or hidden since there is nothing to reorder.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: Users MUST be able to add new steps at the top-level sequence scope even when one or more loops exist within the sequence. A persistent "Add step" button MUST appear at the bottom of the sequence editor, always appending a new step at the end of the top-level scope; the existing in-loop "add step" control continues to scope additions to the loop interior.
- **FR-002**: Users MUST be able to add new steps inside a loop (existing behavior MUST be preserved without regression).
- **FR-003**: Users MUST be able to reorder steps within the top-level sequence scope, including changing a step's position relative to loops at that level.
- **FR-004**: Users MUST be able to reorder steps within a loop's scope.
- **FR-005**: Step reordering MUST be constrained to the nesting level of the step being moved — a step cannot be moved into or out of a loop via reordering controls.
- **FR-006**: Step reordering uses drag-and-drop. The drag-and-drop interaction MUST prevent a step from being dropped into a drop target that belongs to a different nesting level (i.e., dragging a top-level step cannot drop it inside a loop, and dragging a loop-interior step cannot drop it at the top level).
- **FR-007**: During a drag operation, invalid drop targets MUST display a visual "not allowed" indicator (such as a cursor change, highlight, or dimming). When a step is released over an invalid drop target, it MUST snap back to its original position.

### Key Entities

- **Sequence**: The top-level ordered list of steps and loop blocks that is executed as a unit.
- **Loop Block**: A container element within a sequence that holds its own ordered list of steps and repeats them according to loop configuration.
- **Step**: An individual action item that belongs to exactly one scope — either the top-level sequence or a specific loop block.
- **Nesting Level**: The scope to which a step belongs (top-level = 0, inside a loop = 1, inside a nested loop = 2, etc.).

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Users can add a step at the top-level scope in a sequence containing at least one loop, without any additional navigation or workarounds.
- **SC-002**: 100% of drag-and-drop reorder operations produce a result where the dropped step remains at the same nesting level it started at; cross-level drop targets are rejected.
- **SC-003**: Users can successfully add and reorder steps both inside and outside loops within the same sequence editing session without errors.
- **SC-004**: The add-step and reorder controls are visually distinct and available at each nesting level, reducing authoring errors related to wrong-level insertion.

## Clarifications

### Session 2026-05-30

- Q: Does scope-constrained step management apply recursively to nested loops (loop-within-a-loop), or only to the first level of nesting? → A: First level only. Nested loops (loop within a loop) are out of scope; this feature covers only a loop directly inside a top-level sequence.
- Q: Where does the top-level "add step" control appear? → A: A single persistent "Add step" button at the very bottom of the sequence; it always appends a new step at the end of the top-level scope.
- Q: What mechanism is used for step reordering? → A: Drag-and-drop only. Move-up/move-down buttons are not part of this feature.
- Q: How does the UI communicate a rejected cross-level drop during drag-and-drop? → A: Visual "not allowed" indicator on invalid drop targets during drag (cursor change, highlight, or dimming); item snaps back to its original position on an invalid drop.

## Assumptions

- The existing loop authoring UI already shows steps inside the loop and provides an "add step" control scoped to the loop interior.
- "Reordering" means drag-and-drop. Move-up/move-down buttons are not part of this feature.
- Nested loops (loop within a loop) are **out of scope**. This feature covers exactly one level of nesting: a loop directly inside a top-level sequence. Nested loop support is a known limitation for a follow-up feature.
- The Loop Block itself is treated as a single item at the top-level scope and can be reordered among other top-level items (moving the whole loop, not its contents).
