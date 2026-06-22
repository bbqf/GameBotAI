# Feature Specification: Execution Log Grid Cleanup

**Feature Branch**: `050-execution-log-grid`  
**Created**: 2026-06-02  
**Status**: Implemented
**Input**: User description: "let's cleanup the ui in the execution log area. first let's remove the execution detail area alltogether as it is superflous. Use the whole available width for the main area. Command execution log detail should be expandable via a button, like it is for the sequences. Sequences, that have commands as steps, should then get second level of expanding - reuse the expandable commands within the sequences. second: remove \"Open in sequence\" buttons, they just clutter the UI without significant benefit. And third - probably the largest change, the main area should be a grid on all levels, so make like: <Expand Button> | Timestamp | Name | Type | Status | Additional information (you can reuse the information from the current Execution Detail). For the sequence it will be for example: <Expand Button> | 6/2/2026, 7:09:01 AM | Donate | Sequence | success | Sequence 'Donate' success with 7 steps executed. <Expand Button> |  | Open Alliance Tech | Command | success |  Step 'step-3' ran command 'Open Alliance Tech' with outcome 'executed'. (delay 197 ms). |  | primitiveTap | Tap | success |"

## Clarifications

### Session 2026-06-02

- Q: When the grid is a single full-width tree, how should expansion state behave across rows? → A: Multiple independent — each row's expand toggle is independent; expanding one row does not collapse others.
- Q: On phone/narrow widths, how should the full-width grid handle the six columns without a separate detail screen? → A: Horizontal scroll — keep all six columns at every level and let the grid scroll horizontally when too narrow.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Single full-width grid replaces the split list/detail layout (Priority: P1)

A user opens the Execution Logs page and sees one full-width grid that uses the entire available width. There is no separate "Execution Detail" panel beside or below the list. Every row — whether a top-level execution or a nested sub-element — is presented as a grid row with the same set of columns: an expand control, timestamp, name, type, status, and an additional-information column that carries the descriptive text previously shown in the detail panel.

**Why this priority**: This is the core restructuring the user asked for. Collapsing the two-panel layout into a single grid is what delivers the "use the whole available width" outcome and is the foundation every other change builds on.

**Independent Test**: Open the Execution Logs page and confirm there is a single grid spanning the full content width, with columns Expand / Timestamp / Name / Type / Status / Additional information, and no separate detail panel.

**Acceptance Scenarios**:

1. **Given** at least one execution log exists, **When** the user opens the Execution Logs page, **Then** a single grid is shown that uses the full available width and there is no separate Execution Detail area.
2. **Given** the grid is displayed, **When** the user inspects a top-level row, **Then** it shows the expand control, timestamp, name, type (e.g. "Sequence" or "Command"), status, and an additional-information summary.
3. **Given** a top-level sequence row such as "Donate", **When** the user reads its additional-information column, **Then** it shows the summary text (e.g. "Sequence 'Donate' success with 7 steps executed.") that was previously surfaced in the detail panel.

---

### User Story 2 - Stand-alone commands are expandable, and sequence steps that are commands expand to a second level (Priority: P1)

A user expands a stand-alone command row and sees its sub-elements (primitive actions, taps, wait outcomes, etc.) as nested grid rows. When a sequence step is itself a command, that step row is also expandable, revealing the command's own sub-elements — a second level of expansion reusing the same expandable-command behavior.

**Why this priority**: The user explicitly wants command detail reachable by expansion (mirroring sequences) and wants commands invoked inside sequences to expand to their own detail. Without this, removing the detail panel would hide information.

**Independent Test**: Execute a stand-alone command and a sequence whose steps invoke commands. In the grid, expand the stand-alone command and confirm its sub-elements appear as nested rows; expand the sequence, then expand a command step within it, and confirm that command's sub-elements appear nested beneath the step.

**Acceptance Scenarios**:

1. **Given** a stand-alone command execution, **When** the user clicks its expand control, **Then** its sub-elements appear as nested grid rows with the same columns.
2. **Given** an expanded sequence, **When** a step within it invoked a command, **Then** that step row exposes an expand control.
3. **Given** an expandable command step inside a sequence, **When** the user expands it, **Then** the command's own sub-elements (e.g. primitive taps) appear nested one level deeper, reusing the same expandable-command rendering.
4. **Given** a sub-element that has no children (e.g. a primitive tap), **When** the user views its row, **Then** no expand control is shown for it.

---

### User Story 3 - "Open in sequence" buttons are removed (Priority: P2)

A user browsing the execution logs no longer sees "Open in sequence" deep-link buttons on rows or sub-elements. The grid is cleaner and focused on the execution information itself.

**Why this priority**: This is a discrete decluttering request that depends on the grid existing but is independent of the expansion behavior; it can land separately.

**Independent Test**: Open the Execution Logs page, expand sequences and commands, and confirm that no "Open in sequence" button appears anywhere in the grid.

**Acceptance Scenarios**:

1. **Given** any top-level row or nested sub-element, **When** the user views it, **Then** no "Open in sequence" button is present.
2. **Given** the "Open in sequence" buttons are removed, **When** the user uses the page, **Then** all remaining information (name, type, status, additional information, applied delays) is still visible without those buttons.

---

### Edge Cases

- **No logs**: When no execution logs match the current filters, the grid shows an empty-state message instead of column rows.
- **Running executions**: An in-progress execution still appears as a grid row with a "running" status and updates live (existing behavior is preserved); expanding it shows sub-elements as they become available.
- **Nested sequences**: A sequence invoked by another sequence remains expandable at its own level, continuing the multi-level expansion downward.
- **Sub-element with extra detail (conditions, waits, loops)**: The additional-information column carries the descriptive text for these (condition result, wait timeout/exit, applied delay) that the detail panel used to show, without a separate panel.
- **Narrow / phone widths**: The grid must remain usable on small screens (the previous layout switched to a phone view). All six columns are kept at every level and the grid scrolls horizontally when too narrow; there is no longer a separate detail screen to navigate to.
- **Timestamp only on top-level rows**: Nested sub-element rows may have a blank timestamp column (as in the user's example), since their timing is conveyed via the additional-information column (e.g. applied delay).
- **Deep links previously available**: Removing "Open in sequence" removes the only navigation to the authored step/sequence from this page; users navigate via the authoring area instead.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The Execution Logs page MUST present a single grid that occupies the full available content width, with no separate "Execution Detail" panel.
- **FR-002**: The grid MUST use a consistent set of columns at every level: an expand control, Timestamp, Name, Type, Status, and Additional information.
- **FR-003**: The "Type" column MUST identify what each row represents (e.g., "Sequence", "Command", "Tap", or the corresponding sub-element kind).
- **FR-004**: The "Additional information" column MUST carry the descriptive/summary text that was previously shown in the Execution Detail area (e.g., a sequence summary like "Sequence 'Donate' success with 7 steps executed.", a step summary like "Step 'step-3' ran command 'Open Alliance Tech' with outcome 'executed'. (delay 197 ms)", and condition/wait/delay details for the relevant sub-elements).
- **FR-005**: Top-level command executions (stand-alone commands) MUST be expandable via an expand control, in the same way sequences are, revealing their sub-elements as nested grid rows.
- **FR-006**: A sequence step that invoked a command MUST be expandable to a further level, revealing that command's own sub-elements; this nested command expansion MUST reuse the same expandable-command behavior used for stand-alone commands.
- **FR-007**: Expansion MUST support multiple levels (sequence → command step → command sub-elements, and deeper for nested sequences), with each container-type row exposing its own expand control and non-container rows (e.g., primitive taps) exposing none.
- **FR-007a**: Each row's expand/collapse state MUST be independent: expanding or collapsing one row (at any level) MUST NOT collapse or alter the expansion state of any other row, so multiple executions and branches can be open simultaneously.
- **FR-008**: All "Open in sequence" buttons (deep links to the authored sequence/step) MUST be removed from the Execution Logs page, on both top-level rows and nested sub-elements.
- **FR-009**: Removing the detail panel and the "Open in sequence" buttons MUST NOT lose any execution information that is still relevant; the information formerly shown in the detail panel MUST be reachable within the grid (primarily via the Additional information column and expansion), except for the deep-link navigation that is intentionally dropped.
- **FR-010**: Each row's Status MUST be displayed and MUST reflect that row's outcome (e.g., success, failure, skipped, running), consistent with current status reporting.
- **FR-011**: Existing list capabilities — filtering (by timestamp, object name, status), sorting, timestamp display mode (exact/relative), and live updates for running executions — MUST continue to function over the new grid.
- **FR-012**: The grid MUST remain usable on small/phone-width screens. When the viewport is too narrow to fit all columns, the grid MUST scroll horizontally while keeping all six columns at every level (no column hiding, no separate detail screen, no per-row card stacking).
- **FR-013**: Nested sub-element rows MAY display a blank Timestamp column where a per-element timestamp is not meaningful, conveying timing via the Additional information column instead (e.g., applied delay).

### Key Entities *(include if data involved)*

- **Grid Row**: A single line in the unified grid. Can be a top-level execution (sequence or stand-alone command) or a nested sub-element. Attributes shown: expandability, timestamp (possibly blank for sub-elements), name, type, status, additional-information text.
- **Top-level Execution**: A stand-alone command run or a sequence run; the root rows of the grid. Expandable when it has sub-elements.
- **Sub-element (nested)**: Something that happened within an execution — a sequence step, an invoked command, a primitive action/tap, a condition evaluation, a loop iteration, or a wait outcome. Rendered as a nested grid row; expandable when it is itself a container (e.g., a command step or nested sequence).
- **Additional information**: Per-row descriptive text reused from the former Execution Detail area — sequence/step summaries, condition results, wait timeout/exit conditions, and applied delays.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: The Execution Logs page renders as a single grid using 100% of the available content width, with zero separate detail panels present.
- **SC-002**: A user can read a top-level execution's summary information (name, type, status, descriptive text) from its grid row alone, without opening any separate panel.
- **SC-003**: A stand-alone command can be expanded to reveal its sub-elements, and a command invoked as a sequence step can be expanded to a second level — verified by expanding both and seeing nested rows in each case.
- **SC-004**: Zero "Open in sequence" buttons appear anywhere on the Execution Logs page.
- **SC-005**: 100% of the execution information previously available in the detail panel (summaries, step outcomes, applied delays, condition traces, wait details) remains reachable in the grid, with the sole intentional exception of the removed deep-link navigation.
- **SC-006**: Filtering, sorting, timestamp display toggling, and live updates for running executions all continue to work against the new grid with no regression.
- **SC-007**: On a phone-width viewport, the grid remains readable and operable: all six columns are reachable via horizontal scroll, rows are expandable, and no content is hidden or clipped.
- **SC-008**: A user can expand two or more separate executions (or branches within them) at the same time, and collapsing or expanding one leaves the others' expansion state unchanged.

## Assumptions

- The information needed for the "Additional information" column already exists in the data currently powering both the list rows and the Execution Detail panel; this feature reorganizes its presentation rather than introducing new captured data.
- "Type" values correspond to the existing execution/sub-element kinds already tracked (sequence, command, tap, condition, loop, wait, etc.); their display labels can be derived from those kinds.
- The deep-link "Open in sequence" navigation is intentionally dropped per the user's request; users who need to reach the authored object will do so from the authoring area, and no replacement navigation is required on this page.
- Live updates, filtering, sorting, and timestamp-mode behavior from the current page are retained as-is and simply operate on the unified grid.
- Nested-row timestamps may be left blank where a meaningful per-element time is not recorded (matching the user's example), with timing conveyed through the additional-information text.
- This feature builds on the existing execution-logs hierarchy (feature 049): the parent/child/root structure and expandable subtree already exist and are reused; this work changes the presentation (single grid, multi-level inline expansion, removed detail panel and deep-link buttons).
