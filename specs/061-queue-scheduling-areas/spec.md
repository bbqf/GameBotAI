# Feature Specification: Drag-and-Drop Scheduling Areas in the Queue Template Editor

**Feature Branch**: `061-queue-scheduling-areas`  
**Created**: 2026-06-18  
**Status**: Implemented
**Input**: User description: "In order to provide a better overview of the queue execution scheduling, I want to see distinct areas when defining a queue template: Start of execution (full page/area on the page width at the top), \"Once per run\" and \"scheduled\" under each other on the left side, \"After every step\" to the right of the \"once per run\" and \"scheduled\". I should be able to move the sequences from one area to the other as well as change the order of the sequences by drag and drop. If a sequence moves from one area to the other, its scheduling type has to be adjusted automatically. By default new sequences should be added to the \"once per run\" area."

## Clarifications

### Session 2026-06-18

- Q: When a Timer sequence is dragged out of the "Scheduled" area, what happens to its configured timer details (time-of-day / relative offset)? → A: Retain the details on the entry but keep them inactive; restore them if the sequence is dragged back into "Scheduled".
- Q: Is a keyboard-accessible alternative to drag-and-drop (for reassigning/reordering sequences) in scope for this feature? → A: No — drag-and-drop only; the keyboard-parity requirement is dropped for this feature.

## User Scenarios & Testing *(mandatory)*

A queue template is an ordered list of sequence references, each tagged with a **schedule option** that controls when, during a run, that sequence executes. The available schedule options today are: **At Queue Start**, **Once Per Run**, **Timer** (time-of-day or relative offset), and **After Every Step**. Today the template editor presents these as a single flat list of rows, where each row carries a drop-down to pick its schedule option.

This feature replaces that flat list with **four visually distinct areas**, one per schedule option, laid out so the operator can see at a glance how the run is organized:

- **Start of execution** — a full-width area across the top of the editor (maps to the "At Queue Start" schedule option).
- **Once per run** — a panel on the upper-left (maps to the "Once Per Run" schedule option).
- **Scheduled** — a panel on the lower-left, directly under "Once per run" (maps to the "Timer" schedule option, both time-of-day and relative-offset modes).
- **After every step** — a panel on the right, spanning beside both "Once per run" and "Scheduled" (maps to the "After Every Step" schedule option).

An operator arranges the template by **dragging sequence cards** between areas and **reordering** them within an area. Moving a card from one area to another automatically changes that sequence's schedule option to the destination area's type. Newly added sequences land in the **Once per run** area by default. The change is purely an editor/presentation-and-interaction reorganization: it does not change run-time execution semantics, the stored data model's meaning, or the API.

### User Story 1 - See the template organized by schedule type (Priority: P1)

As an operator, I want the template editor to show my sequences grouped into four labeled areas by schedule type — "Start of execution" across the top, "Once per run" and "Scheduled" stacked on the left, and "After every step" on the right — so I can understand at a glance what runs first, what runs each cycle, what runs on a timer, and what runs after every step, without reading a per-row drop-down for every sequence.

**Why this priority**: The core value the user asked for is "a better overview of the queue execution scheduling." The grouped, labeled layout delivers that overview on its own — even before drag-and-drop is added — and is the foundation every other story builds on.

**Independent Test**: Open an existing template that has at least one sequence of each schedule type; confirm each sequence appears in exactly the area matching its current schedule option, that the four areas are laid out as described (full-width top, stacked left pair, right column), and that each area is clearly labeled.

**Acceptance Scenarios**:

1. **Given** a template with an "At Queue Start" sequence, a "Once Per Run" sequence, a "Timer" sequence, and an "After Every Step" sequence, **When** the operator opens the editor, **Then** each sequence appears in its corresponding area ("Start of execution", "Once per run", "Scheduled", "After every step" respectively).
2. **Given** the editor is open, **When** the operator views the layout, **Then** "Start of execution" spans the full width at the top, "Once per run" and "Scheduled" are stacked vertically on the left, and "After every step" occupies the right column beside them.
3. **Given** an area that currently contains no sequences, **When** the operator views it, **Then** the area is still visible and clearly labeled, indicating it can receive sequences (e.g. an empty drop zone with a hint).

---

### User Story 2 - Reassign a sequence's schedule by dragging it to another area (Priority: P1)

As an operator, I want to drag a sequence card from one area into another so its schedule option is automatically changed to the destination area's type, so I can reschedule a sequence without hunting for a drop-down.

**Why this priority**: This is the primary interaction the user requested ("move the sequences from one area to the other … its scheduling type has to be adjusted automatically"). Together with Story 1 it forms the MVP: a grouped layout you can actually rearrange.

**Independent Test**: Drag a sequence from "Once per run" to "After every step", save, reload, and confirm the sequence now has the "After Every Step" schedule option; repeat for each pair of areas.

**Acceptance Scenarios**:

1. **Given** a sequence in the "Once per run" area, **When** the operator drags it into the "After every step" area and drops it, **Then** the sequence moves to that area and its schedule option becomes "After Every Step".
2. **Given** a sequence in the "After every step" area, **When** the operator drags it into the "Start of execution" area, **Then** the sequence moves there and its schedule option becomes "At Queue Start".
3. **Given** a sequence dragged into the "Scheduled" area, **When** it is dropped, **Then** its schedule option becomes "Timer" and the operator can then set the timer details (time-of-day or relative offset) for it within that area.
4. **Given** a sequence dragged into the "Scheduled" area and given the "Timer" type, **When** the operator later drags it out into another area, **Then** its schedule option changes to the destination type and the timer-specific details are no longer applied while it is outside the "Scheduled" area.
5. **Given** any schedule reassignment via drag, **When** the operator saves and reloads the template, **Then** the new schedule option persists and the sequence appears in the destination area.

---

### User Story 3 - Reorder sequences within an area by dragging (Priority: P2)

As an operator, I want to drag sequence cards up and down within an area to change their order, so I control the order in which same-type sequences execute (e.g. the order of "At Queue Start" or "After Every Step" sequences).

**Why this priority**: Ordering within a type is already meaningful to run execution (e.g. "At Queue Start" and "After Every Step" run in template order). Surfacing it as in-area drag reordering is valuable but secondary to having the grouped layout and cross-area reassignment.

**Independent Test**: In an area with three sequences, drag the third above the first, save, reload, and confirm the new within-area order is preserved and reflected in the order those sequences execute for that schedule type.

**Acceptance Scenarios**:

1. **Given** three sequences in the "Start of execution" area in order A, B, C, **When** the operator drags C above A, **Then** the area shows C, A, B and that becomes the execution order for those "At Queue Start" sequences.
2. **Given** sequences reordered within an area, **When** the operator saves and reloads, **Then** the within-area order is preserved.
3. **Given** a sequence dragged within its own area, **When** it is dropped, **Then** only the order changes — its schedule option is unchanged.

---

### User Story 4 - New sequences default to "Once per run" (Priority: P2)

As an operator, I want a newly added sequence to appear in the "Once per run" area by default, so adding a sequence has a predictable, sensible starting schedule that I can then drag elsewhere if needed.

**Why this priority**: Establishes a clear default for the add flow that complements the drag interactions; without it the add behavior is ambiguous, but it is a small, well-bounded rule rather than the headline interaction.

**Independent Test**: Add a sequence to the template; confirm it appears at the end of the "Once per run" area with the "Once Per Run" schedule option, regardless of which area (if any) was focused.

**Acceptance Scenarios**:

1. **Given** the template editor with any current contents, **When** the operator adds a sequence, **Then** the sequence appears in the "Once per run" area with the "Once Per Run" schedule option.
2. **Given** a sequence was just added to "Once per run", **When** the operator drags it to another area, **Then** it behaves like any other sequence (schedule option updates to the destination type).

---

### Edge Cases

- **Empty areas**: Each of the four areas is always shown even when empty, with a label and a drop target, so the operator can drop a sequence into it.
- **Dropping into the same area at the same position**: A no-op drag (drop where it started) leaves both order and schedule option unchanged.
- **Timer details on move into/out of "Scheduled"**: Moving into "Scheduled" assigns the "Timer" type and lets the operator configure timer details; moving a "Timer" sequence out of "Scheduled" changes its type to the destination's and stops applying the timer details at run time, while retaining them (inactive) on the entry so they are restored if the sequence is dragged back into "Scheduled".
- **Stale / unresolved sequence reference**: A sequence whose reference is stale still appears as a card in the area matching its current schedule option, retains its stale indicator, and remains draggable.
- **Many sequences in one area**: An area with many cards scrolls or grows within its panel without breaking the overall four-area layout.
- **Cross-area order vs within-area order**: Each schedule type executes its own sequences in their within-area order; rearranging the relative position of the areas themselves on screen has no execution effect (only within-area order matters per type).
- **Disabled / read-only state**: When the editor is in a disabled/read-only state, areas still render grouped but cards cannot be dragged or reassigned.
- **Drag cancelled** (e.g. dropped outside any area, or via the Escape key during a drag): the sequence returns to its origin area and position with no change.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The template editor MUST present queue sequences grouped into four distinct, labeled areas, one per schedule option: "Start of execution" (At Queue Start), "Once per run" (Once Per Run), "Scheduled" (Timer), and "After every step" (After Every Step).
- **FR-002**: The four areas MUST be laid out as: "Start of execution" full-width across the top; "Once per run" and "Scheduled" stacked vertically (Once per run above Scheduled) on the left; "After every step" on the right, beside the stacked left pair.
- **FR-003**: On opening a template, each sequence MUST appear in exactly the area whose schedule option matches that sequence's current schedule option.
- **FR-004**: The operator MUST be able to drag a sequence card from one area to another, which MUST move the card into the destination area and automatically set that sequence's schedule option to the destination area's type.
- **FR-005**: The operator MUST be able to reorder sequence cards within an area by dragging, changing only their order within that area and not their schedule option.
- **FR-006**: The within-area order of sequences MUST define the execution order for that schedule type (consistent with existing "template order" semantics for At Queue Start, Once Per Run, and After Every Step, and ordering among Timer entries).
- **FR-007**: A newly added sequence MUST be placed in the "Once per run" area with the "Once Per Run" schedule option by default.
- **FR-008**: When a sequence is moved into the "Scheduled" area, its schedule option MUST become "Timer", and the operator MUST be able to configure the timer details (time-of-day or relative offset) for it within that area.
- **FR-009**: When a sequence is moved out of the "Scheduled" area into another area, its schedule option MUST change to the destination area's type and the timer details MUST no longer be applied at run time while the sequence is not a Timer entry. The previously configured timer details MUST be retained (inactive) on the entry and restored if the sequence is later dragged back into "Scheduled".
- **FR-010**: Each area MUST remain visible and act as a valid drop target even when it contains no sequences, with a clear label and an empty-state hint.
- **FR-011**: All schedule reassignments and reorderings performed via drag MUST persist when the template is saved and MUST be reflected correctly when the template is reloaded.
- **FR-012**: This reorganization MUST NOT change run-time execution semantics, the stored schedule-option values/identifiers, or the API contract; it is an editor presentation-and-interaction change only, fully backward compatible with existing templates.
- **FR-013**: A drag that is cancelled or dropped outside any valid area MUST leave the template unchanged (sequence returns to its origin area and position).
- **FR-014**: When the editor is disabled/read-only, the four-area grouping MUST still render but sequences MUST NOT be draggable or reassignable.
- **FR-015**: Each card MUST clearly indicate the area/schedule it currently belongs to.
- **FR-016**: Stale or unresolved sequence references MUST still render as cards in the area matching their current schedule option, retain their stale indicator, and remain movable.

### Key Entities *(include if feature involves data)*

- **Queue Template Entry**: a positional reference to a sequence within a template, carrying a schedule option (and, for Timer entries, timer details). This feature changes how entries are *presented and manipulated* in the editor (grouped into areas, drag to move/reorder) but does not change the entry's stored shape or meaning.
- **Scheduling Area**: an editor-only grouping that corresponds one-to-one with a schedule option — "Start of execution" ↔ At Queue Start, "Once per run" ↔ Once Per Run, "Scheduled" ↔ Timer, "After every step" ↔ After Every Step. An area has a label, a position in the layout, an ordered set of sequence cards, and an empty state.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Opening any existing template shows 100% of its sequences in the area matching their schedule option, with no sequence lost, duplicated, or mis-placed, across a sample template containing all four schedule types.
- **SC-002**: An operator can change a sequence's schedule option by dragging it to another area and have that change persist across save/reload in 100% of attempts, for every ordered pair of areas.
- **SC-003**: An operator can reorder sequences within an area by dragging, and the new within-area order persists across save/reload and matches the resulting execution order for that schedule type in 100% of attempts.
- **SC-004**: A newly added sequence appears in the "Once per run" area with the "Once Per Run" option in 100% of adds.
- **SC-005**: Run-time execution outcomes for any template are identical before and after this change (no scheduling regression), verified by the existing scheduling/queue test suite continuing to pass.
- **SC-006**: A first-time operator can identify which sequences run first, each run, on a timer, and after every step within 10 seconds of opening the editor, without consulting documentation.

## Assumptions

- **Schedule options are unchanged**: The four schedule options and their stored/wire identifiers (At Queue Start, Once Per Run / `OncePerRun`, Timer, After Every Step / `EveryStep`) are exactly those established in feature 060; this feature only reorganizes their presentation. No new schedule option is introduced.
- **One area per schedule option**: "Scheduled" represents the single "Timer" schedule option; its two sub-modes (time-of-day and relative offset) are configured per-card within that area rather than as separate areas.
- **Timer details when leaving "Scheduled"**: When a Timer sequence is dragged out of "Scheduled", its previously entered timer details are not applied at run time (the sequence is no longer a Timer entry), but they are retained (inactive) on the entry and restored if the sequence is dragged back into "Scheduled".
- **Default timer configuration on entering "Scheduled"**: A sequence newly assigned "Timer" by dropping it into "Scheduled" starts with an unset/empty timer value that the operator must fill in, consistent with how a freshly chosen Timer is configured today.
- **Cross-area ordering**: Because each schedule type is executed in its own pass, only the order of cards *within* an area affects execution. There is no need to define a single global order spanning areas; the editor persists each area's internal order.
- **Add control**: The existing "Add sequence" control continues to add a single selected sequence; the only change is that the new sequence is routed to the "Once per run" area by default (rather than appended to a single flat list).
- **No backend/API changes**: Persistence uses the existing template-entry storage and API; the editor maps area membership to the existing schedule-option field on save and back to area membership on load.
- **Scope**: This feature covers the web UI template editor only. The API surface, run-time scheduler, and stored data model are unchanged.
- **Keyboard accessibility out of scope**: A keyboard-operable alternative to drag-and-drop is not required for this feature (clarified 2026-06-18). Reassign/reorder are delivered via drag-and-drop; a keyboard-accessible path may be added in a later feature.
