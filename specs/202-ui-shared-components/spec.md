# Feature Specification: Shared UI components and design tokens, with the Queues page as the first user

**Feature Branch**: `202-ui-shared-components`  
**Created**: 2026-11-27  
**Status**: Draft  
**Input**: User description: "Shared UI components and design tokens from docs/ui-guidelines.md, with the Queues page as the first page that uses them (PR 1 of the UI consistency work)." The full description is in the pipeline call. The design decisions come from a review with the user on 2026-11-27. The result is `docs/ui-guidelines.md` and the mockups in `docs/ui-guidelines/`.

## Background

The web UI is not consistent between pages and between the phone and the desktop:

- 7 of the 9 tables have no CSS. The browser sets the column widths from the content, so the columns change width when the data changes. The row height comes from the content, so the rows do not have the same height.
- About 40 buttons use the classes `btn`, `btn-primary`, `btn-secondary`, and `btn-danger`, but no CSS defines them. Thus a Delete button is not red on most pages.
- A global rule gives each `<button>` a gradient and a "lift" on hover. A name link in a table shows as a large gradient button on all pages except Commands.
- Only the Queues page uses SVG icons (`components/queues/IconButton.tsx`). Other pages use text buttons or text glyphs (⠿ ↑ ↓ ✕ ▾ ▸).
- The status chip of the desktop Queues table uses the classes `status-running` and `status-stopped`, but the CSS defines `status-chip--running`. So each queue status shows as a grey "unknown" chip.
- The token `--gb-color-text-muted` is used but not defined.

The user approved the guideline `docs/ui-guidelines.md` (sections 1 to 12) and three mockup images. This feature is PR 1 of the work. It adds the tokens and the shared components, and it moves the Queues list (desktop and phone) to them. Later PRs move the other areas, one area for each PR.

## Clarifications

### Session 2026-11-27

The pipeline runs with no manual review. Each answer is the most reasonable option, chosen from the guideline, the user decisions, and the code.

- Q: The phone support level of Queues is `reduced` (no create, edit, duplicate, or delete on the phone). What does the phone row show as its one main action and in its "⋮" menu? → A: The main action is **Start** (play icon) for a stopped queue and **Stop** (stop icon) for a running queue. This is the same action as the current text button. The "⋮" menu has **Open** for all queues and, for a running queue, the pause action that applies now (**Pause**, **Cancel pause**, or **Resume**). The menu has no Edit, Duplicate, or Delete. Rationale: this keeps the current phone behavior and the `reduced` level; the pause actions are already `full` on the phone (feature 171).
- Q: Which three icons does a desktop row show, and in which positions? → A: Position 1: the run or pause action (Start for a stopped queue; Pause, Cancel pause, or Resume for a running queue). Position 2: Stop (dimmed and off when the queue is stopped). Position 3: Edit. The "⋮" menu has Duplicate and Delete (Delete is red, last, and off while the queue runs). Rationale: rule L5 (max. 3 icons) and rule L6 (positions do not move).
- Q: Which text and color does the status chip of a queue show? → A: `Running` (success color) for a running queue with no operator pause or with the state "pausing"; `Paused` (warning color) for a running queue with the operator pause state "paused"; `Stopped` (idle color) for a stopped queue; `Unknown` (idle color) for other values. Rationale: rule S3 (one word); the pause state is important for the operator and is already known in the row.
- Q: Which desktop columns does the Queues table have, and how wide are they? → A: Name (flexible), Cycle 96 px, Status 132 px, Activity 320 px, Actions 184 px. Rationale: the current columns stay; the widths come from the mockup and the content (the Actions width is 4 icon buttons of 36 px, 3 gaps of 4 px, and 2 x 16 px padding).
- Q: Does the global font and button change apply to all pages, or only to the Queues page? → A: To all pages. The tokens are global. The font changes to the system font, and the global button style becomes flat (no gradient, no lift on hover). Rationale: the user chose "colors and font as in the mockups" for the full app; a token change for one page only would add a new inconsistency.
- Q: What does Delete in the desktop "⋮" menu do? → A: It opens the shared confirmation dialog with the title "Delete queue <name>?", one sentence ("The queue and its settings are deleted. You cannot undo this."), **Cancel**, and **Delete** (danger). **Delete** calls the current delete function of the page. It no longer opens the edit form first. Rationale: rule B5 and rule D1.
- Q: What happens to the old components `components/queues/IconButton.tsx` and `features/execution/StatusChip.tsx`? → A: The old `IconButton` keeps its props, classes, and look, but takes its icon paths from the shared icon set. The old `StatusChip` (Execution and Monitoring pages) does not change in this PR. The Queues page uses the new shared `StatusChip`. Rationale: the other pages move in later PRs; one icon set prevents copies of paths; a wrapper of the old chip would put two sets of chip CSS on one element. (Changed in analyze loop 1, finding I3.)
- Q: How does the "⋮" menu work with the keyboard and a screen reader? → A: The "⋮" button has `aria-haspopup="menu"` and `aria-expanded`. The list has the role `menu` and each action has the role `menuitem`. When the menu opens, the focus moves to the first action that is on. The Up and Down arrow keys move the focus. Enter selects. Escape and Tab close the menu, and Escape moves the focus back to the "⋮" button. Rationale: this is the standard menu-button pattern (WAI-ARIA); the current icon buttons are keyboard accessible, and the menu must not be worse.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Read the Queues list on the desktop with a stable layout (Priority: P1)

An operator opens the Queues page on the desktop. The table shows all queues. Each column has a fixed width, and each row has the same height. When a queue starts, stops, or changes its activity text, no column changes width and no row changes height. The status chip shows the correct color for each state.

**Why this priority**: The columns that "jump" and the rows of different height are the main problem that the user reported. The Queues page is the page that the operator uses most.

**Independent Test**: Open the Queues page with 5 queues in different states (one with a very long name). Measure the width of each column and the height of each row. Start one queue. Measure again.

**Acceptance Scenarios**:

1. **Given** 5 queues with names of different length, **When** the Queues page opens at a width of 1280 px, **Then** each data row is 56 px high (±1 px) and the Cycle, Status, Activity, and Actions columns have the widths of the clarification (±1 px).
2. **Given** a queue name that is longer than its column, **When** the row shows, **Then** the name is on one line, ends with "…", and the full name is in the tooltip.
3. **Given** a stopped queue, **When** the operator starts it, **Then** no column changes width and no row changes height.
4. **Given** a running queue, **When** the row shows, **Then** the status chip shows "Running" in the success color. A stopped queue shows "Stopped" in the idle color. A running queue with the pause state "paused" shows "Paused" in the warning color.
5. **Given** the list is loading, empty, or failed, **When** the page shows, **Then** it shows "Loading…", the empty text, or the error text with **Try again**, in the shared design. The default empty text is "No items.". A page can give its own empty text: the Queues table keeps "No queues found." and the phone list keeps "No queues yet. Use a larger screen to create one.".

---

### User Story 2 - Use the same row actions on the desktop (Priority: P1)

The operator uses the icons of a row to start, pause, stop, and edit a queue. Duplicate and Delete are in a "⋮" menu. An action that is not available stays in its position and is dimmed.

**Why this priority**: The actions are the main use of the list. The fixed positions stop accidental clicks and keep the column width fixed.

**Independent Test**: For a stopped queue and a running queue, read the buttons of the row and their positions. Open the "⋮" menu. Delete a stopped queue.

**Acceptance Scenarios**:

1. **Given** a stopped queue, **When** the row shows, **Then** it shows Start, Stop (dimmed and off), Edit, and "⋮", in this order.
2. **Given** a running queue with no pause, **When** the row shows, **Then** it shows Pause, Stop, Edit, and "⋮", in the same positions.
3. **Given** any queue, **When** the operator opens "⋮", **Then** the menu shows Duplicate and Delete. Delete is red and last. Delete is off while the queue runs.
4. **Given** a stopped queue, **When** the operator selects Delete, **Then** a confirmation dialog shows "Delete queue <name>?" with **Cancel** and a red **Delete**. **Cancel** closes the dialog and deletes nothing. **Delete** deletes the queue and the list reads the data again.
5. **Given** any row, **When** the operator clicks the row outside the actions, **Then** the edit form of the queue opens (the same as Edit).
6. **Given** any icon button, **When** the pointer stays on it, **Then** a tooltip shows the action name. Each icon button has the accessible name "<Action> <queue name>".

---

### User Story 3 - See the same list on the phone (Priority: P2)

On the phone, the operator sees the same queues as rows of the same height. Each row shows the name on line 1, and the status chip and the activity on line 2. On the right, it has one main icon (Start or Stop) and a "⋮" menu.

**Why this priority**: The phone is the base of the guideline. The phone list already works, so this story is about consistency, not about a new function.

**Independent Test**: Open the Queues page at 360 x 640 px with 5 queues. Measure the row heights. Open "⋮" of a running queue.

**Acceptance Scenarios**:

1. **Given** 5 queues, **When** the phone list shows, **Then** each row is 64 px high (±1 px), and no sideways scroll occurs at 320, 360, and 390 px.
2. **Given** a stopped queue, **When** the row shows, **Then** the main icon is Start. For a running queue, it is Stop. Each icon button is at least 44 x 44 px.
3. **Given** a running queue, **When** the operator opens "⋮", **Then** the menu shows Open and the pause action that applies now. A stopped queue shows only Open.
4. **Given** any row, **When** the operator taps the row outside the actions, **Then** the queue detail opens.
5. **Given** a queue with a not-live device, **When** the row shows, **Then** the "Not live" mark shows in line 2 and the row stays 64 px high.

---

### User Story 4 - Use the same tokens and components in later pages (Priority: P3)

A developer who moves another page to the guideline uses the shared components (icon, buttons, page header, status chip, list, row menu, footer bar, confirmation dialog) and the tokens. The developer writes no new color, size, or icon path.

**Why this priority**: This is the base for the later PRs. It has no direct value for the operator in this PR.

**Independent Test**: Read the component unit tests and the guideline. Each shared component has a test. The shared icon set has each icon of rule B4.

**Acceptance Scenarios**:

1. **Given** the shared icon set, **When** a developer lists the icons, **Then** it has play, pause, stop, edit, duplicate, delete, more, back, close, drag, expand, collapse, upload, download, search, filter, and add, and all icons that the old `IconButton` had.
2. **Given** the shared confirmation dialog, **When** it opens, **Then** the focus moves to **Cancel**, the Escape key closes it, and the action button is red when the dialog is marked as dangerous.
3. **Given** the theme, **When** any page shows, **Then** the text uses the system font, the buttons have no gradient, and a button does not move on hover.

### Edge Cases

- A queue name with 200 characters: the row keeps its height; the name ends with "…".
- An activity text that is longer than 320 px: it ends with "…"; the full text is in the tooltip.
- A queue with no activity text: the cell shows "—".
- A pause call that is refused: the error text shows in the page message area (as now); the icon stays in its position.
- A call is in progress (busy): the icon is off and dimmed, in its position.
- The "⋮" menu near the bottom of the screen: the menu opens upward, so that it stays on the screen.
- The "⋮" menu is open and the operator clicks outside it or presses Escape: the menu closes.
- The connection is lost on the phone: the main icon and the menu actions that send a call are off (as now).
- The desktop window is narrower than the sum of the fixed columns (for example 900 px): the Name column gets a minimum of 160 px and the table scrolls sideways inside its card. The rows keep their height.

## Requirements *(mandatory)*

### Functional Requirements

**Tokens (guideline sections 1 and 2)**

- **FR-001**: The theme MUST define the colors, spacing, radius, and size values of guideline sections 1 and 2, in `tokens.ts` and in the CSS variables, with the same values in both.
- **FR-002**: The theme MUST define `--gb-color-text-muted`.
- **FR-003**: All text MUST use the system font (`Segoe UI`, `system-ui`, sans-serif). The app MUST NOT load a web font.
- **FR-004**: The global button style MUST be flat: no gradient background and no movement on hover. Hover rules MUST be inside `@media (hover: hover)`.

**Shared components**

- **FR-005**: The app MUST have one shared icon set with the icons of rule B4 and all icons of the old `IconButton`. Each icon is an inline SVG with a 24 px view box.
- **FR-006**: The app MUST have a shared `Button` component with the variants primary, secondary, danger-outline, and danger, 40 px high.
- **FR-007**: The app MUST have a shared `IconButton` component: 36 x 36 px on the desktop, 44 x 44 px on the phone, an icon of 20 px, no border, a tooltip, a required accessible name, a danger variant (red icon), and a dimmed off state.
- **FR-008**: The app MUST have a shared `PageHeader` component: an optional back-arrow icon button, the title, an optional status chip, and an area for actions on the right.
- **FR-009**: The app MUST have one shared `StatusChip` component with the states running, paused, stopped, failed, done, and unknown. Each state has one color and a dot. On the desktop the chip is 96 px wide and 24 px high.
- **FR-010**: The app MUST have a shared `ListView` component. On the desktop it shows a table with fixed column widths (only one column is flexible), a 40 px header row, 56 px data rows, one-line cells with "…" and a tooltip, "—" for an empty value, and a row click. On the phone it shows 64 px rows with two lines (title; status chip and one meta value), one optional main action, and an optional "⋮" menu. It MUST show the shared loading, empty, and error states.
- **FR-011**: The app MUST have a shared `RowActionMenu` component: an icon button "⋮" that opens a list of actions. A danger action is red and is last. An action can be off. The menu closes on a selection, on Escape, on Tab, and on a click outside. It stays on the screen. It follows the keyboard and screen-reader rules of the clarification (roles `menu` and `menuitem`, arrow keys, Enter, focus back to "⋮" on Escape).
- **FR-012**: The app MUST have a shared `FooterBar` component: a bar of 68 px at the bottom, with a left area and a right area.
- **FR-013**: The app MUST have a shared `ConfirmDialog` component: a title (a question), one sentence, **Cancel**, and the action button. The action button is red when the dialog is dangerous. The focus starts on **Cancel**. Escape closes the dialog.
- **FR-014**: The old `components/queues/IconButton.tsx` MUST keep its current props, classes, and behavior for the components that are not moved in this PR, and it MUST take its icon paths from the shared icon set. The old `features/execution/StatusChip.tsx` MUST NOT change in this PR.

**Queues page, desktop**

- **FR-015**: The Queues page MUST use `PageHeader` with the title "Queues" and the primary button **Create queue**.
- **FR-016**: The Queues table MUST use `ListView` with the columns and widths of the clarification.
- **FR-017**: Each row MUST show three icon buttons in fixed positions (run or pause action, Stop, Edit) and a "⋮" menu (Duplicate, Delete), as in the clarification. An action that does not apply MUST stay in its position, dimmed and off.
- **FR-018**: Delete MUST open the shared confirmation dialog of the clarification. It MUST NOT open the edit form first.
- **FR-019**: A click on a row outside its actions MUST open the edit form of the queue.
- **FR-020**: The status chip MUST show the state and color of the clarification.
- **FR-021**: All current behavior of the Queues list MUST stay: polling, the start and stop messages, the pause calls and their busy state, the error messages, Duplicate, and the form panel.

**Queues page, phone**

- **FR-022**: The phone Queues list MUST use the phone mode of `ListView`: 64 px rows, the name on line 1, the status chip and the activity (with the "Old data" text and the "Not live" mark) on line 2.
- **FR-023**: Each phone row MUST show the main icon Start or Stop, and a "⋮" menu with the actions of the clarification. Each control MUST be at least 44 x 44 px.
- **FR-024**: A tap on a phone row outside its actions MUST open the queue detail.
- **FR-025**: The phone list MUST keep the `reduced` phone support level and all rules of `docs/mobile-ui-principles.md`: no sideways scroll at 320 px and wider, actions off when the connection is lost, and no action sent again after a connection loss.

**Documents and tests**

- **FR-026**: The feature MUST add `docs/ui-guidelines.md`, `docs/ui-guidelines/` (3 images and the HTML sources), and the link in `docs/mobile-ui-principles.md`.
- **FR-027**: Browser tests MUST check that the desktop Queues columns have fixed widths, that all desktop rows are 56 px high, that all phone rows are 64 px high, and that the widths and heights do not change when a queue starts.
- **FR-028**: The feature states its phone support level: Queues stays `reduced`. The shared components support the phone and the desktop. The feature follows `docs/mobile-ui-principles.md`.

### Key Entities

- **Design token**: a named value (color, size, spacing, radius, font) that all components use.
- **Row action**: an action of one list row, with an icon, a name, a state (on or off), and a tone (normal or danger). It is either a visible icon button or an item of the "⋮" menu.
- **List column**: a column of the desktop list, with a header text, a fixed width or "flexible", and an alignment.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: On the desktop Queues page, 100% of the data rows have the same height (56 px ±1), in a test with names of 5 to 200 characters.
- **SC-002**: On the desktop Queues page, no column changes its width by more than 1 px when a queue starts, stops, or changes its activity.
- **SC-003**: On the phone Queues list, 100% of the rows are 64 px high (±1), and no sideways scroll occurs at 320, 360, and 390 px.
- **SC-004**: The status chip shows the correct color for 100% of the queue states (Running, Paused, Stopped).
- **SC-005**: Each row action of the Queues list (the desktop table and the phone list) is an icon button or a menu item from the shared set. The Queues list has 0 text glyphs used as icons, and the new CSS has 0 direct color values. The form panel and the phone queue detail are out of scope and keep their back buttons.
- **SC-006**: All current unit tests and browser tests of the Queues list pass, changed only where the guideline changes the layout (button names, positions, the "⋮" menu).

## Assumptions

- The user approved the guideline and the mockups on 2026-11-27. The rule numbers in this spec refer to `docs/ui-guidelines.md`.
- The token change of the font and the button style applies to all pages. Other pages can look a little different (the font and the flat buttons), but their layout and behavior do not change in this PR.
- The desktop table has the same columns as now. The column "Runs today" of the mockup is not added, because the data is not in the queue list.
- The edit form, the create form, the Duplicate dialog, and the phone queue detail page do not change in this PR. Forms are the subject of a later PR.
- No backend or API change is needed.
- The guideline gives buttons a height of 40 px. The shared `Button` has 40 px. The global `<button>` rule keeps 44 px in this PR, so that the pages that are not moved keep their layout. Each page gets 40 px buttons when it moves to the shared `Button`.
