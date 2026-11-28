# Feature Specification: Move the Commands and Games pages to the shared UI components

**Feature Branch**: `206-commands-games-shared-ui`  
**Created**: 2026-11-28  
**Status**: Draft  
**Input**: User description: "Move the Authoring pages Commands and Games to the shared UI components and to docs/ui-guidelines.md." The full description is in the pipeline call. This is PR 5 of the UI consistency work and part 1 of 3 of Authoring. The user approved the split (1: Commands and Games; 2: Sequences; 3: Images and Backup & Restore) on 2026-11-28.

## Background

Commands and Games are Authoring pages. The phone shows Authoring as desktop-only. Now:

- Each page has an `<h2>` title, a plain "Create …" button, and an old table. The name filter is inside the table header. The name is a link button. The Commands table has a Game column that always shows "—" and a Game filter that is always off.
- The form pages have a "← Back to list" text button, an `<h3>` "Edit Game" / "Edit Command", a full-width form, and `FormActions` (Save, Cancel) at the end of the form. Delete is a separate button with the classes `btn btn-danger`, which no CSS defines.
- Delete uses the old `ConfirmDeleteModal` ("Confirm Delete", "Are you sure …").
- `CommandForm` and the visual step picker use the classes `btn`, `btn-secondary`, and `btn-primary`.

Rules H1 to H3, B1 to B5, L1 to L8, L14, F1 to F5, D1, and N6 of the guideline and mockup A ("Form page") give one design: a page header, a filter bar, the shared list, a form page with a back arrow, cards, and a footer bar, and the shared confirmation dialog.

## Clarifications

### Session 2026-11-28

The pipeline runs with no manual review. Each answer is the most reasonable option, chosen from the guideline, the mockups, and the code.

- Q: Which title does the edit page show while the operator changes the name? → A: The name as it was loaded (it does not change while the operator types). Rationale: the title identifies the item that the page edits; a title that changes with each key is noise.
- Q: Where does the command form get its footer bar? → A: `CommandForm` renders the `FooterBar` inside its `<form>` element, so that Save stays a submit button. It gets the new optional props `onDelete` (shows Delete on the left) and `deleting`. Rationale: the form element is in `CommandForm`; a submit button outside the form would need extra code.
- Q: Where does "Loading…" show on a form page? → A: In the left area of the footer bar, when there is no Delete button (create mode). In edit mode it shows after the Delete button. Rationale: the current hint is in the actions area; the footer bar is the new actions area.
- Q: How does the dialog turn off its Delete button after a 409 answer? → A: `ConfirmDialog` gets a new optional prop `confirmDisabled` (only the confirm button is off; Cancel stays on). Rationale: `busy` turns off both buttons, but the operator must be able to close the dialog.
- Q: What does a successful delete from the edit page do? → A: The page goes back to the list, the list reloads, and the message area shows "Game deleted." / "Command deleted.". Rationale: the current behavior, with the new texts.
- Q: Does the edit IconButton in a row do the same as a row click? → A: Yes. Both open the edit page with the unsaved-changes check. Rationale: rule L7; the icon makes the action visible.
- Q: What does the Package name column show for a long package name? → A: One line with "…" and the full value in the tooltip (rule L3).

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Find a game or a command in the shared list (Priority: P1)

The author opens Authoring > Games or Authoring > Commands. The page header shows the title and the primary button "Create game" / "Create command". A filter bar is under the header. The list has fixed columns and 56 px rows. A click on a row opens the item.

**Why this priority**: The list is the entry to each page.

**Independent Test**: Open both pages at 1280 px with 3 items (one with a long name, one with no package name). Measure the columns and the rows. Type in the filter.

**Acceptance Scenarios**:

1. **Given** 3 games, **When** Games opens, **Then** the list shows the columns Name and Package name (320 px ±1), each row is 56 px high (±1), the rows are sorted by name, and a game with no package name shows "—" in the package column (monospace font).
2. **Given** 3 commands, **When** Commands opens, **Then** the list shows the columns Name and Steps (96 px ±1, numbers on the right), each row is 56 px high, and no Game column and no Game filter show.
3. **Given** a list, **When** the author types "gold" in "Filter by name", **Then** only items with "gold" in the name (any case) show. The filter is above the list, not inside the table header.
4. **Given** no items, **When** the page opens, **Then** the list shows "No games." / "No commands.". **Given** a failed list call, **Then** the list shows the error text and **Try again**, which loads the list again.
5. **Given** a row, **When** the author clicks the row or its Edit icon, **Then** the edit page of that item opens.

---

### User Story 2 - Create and edit on a form page (Priority: P1)

The author selects **Create game** or a row. The form page opens with a back arrow and the title ("New game", "New command", or the item name). The form is 720 px wide and centered, in cards. A footer bar at the bottom of the screen has Cancel and Save (and Delete in edit mode).

**Why this priority**: Create and edit are the main work of these pages.

**Independent Test**: Create a game, edit it, cancel an edit with changes, and use the back arrow.

**Acceptance Scenarios**:

1. **Given** the Games list, **When** the author selects **Create game**, **Then** the page header shows a back arrow and "New game", the form is 720 px wide (±1) and centered, the fields are in a card with the heading "Basics", and the footer bar shows Cancel and Save (Save last, primary) and no Delete.
2. **Given** an edit page of "Farm gold", **When** it opens, **Then** the title is "Farm gold", and the footer bar shows Delete (danger outline) on the left and Cancel and Save on the right. The title stays "Farm gold" while the author changes the name field.
3. **Given** an empty name, **When** the author selects Save, **Then** the name field has a red border and "Name is required" under it, and no call is sent.
4. **Given** a valid form, **When** the author selects Save, **Then** the same create or update call as now is sent, the list shows, and the message area shows "Game created." / "Game updated." / "Command created." / "Command updated.".
5. **Given** unsaved changes, **When** the author selects the back arrow, Cancel, or the active Authoring sub-tab, **Then** the unsaved-changes prompt shows (as now).
6. **Given** a long command form, **When** the author scrolls, **Then** the footer bar stays visible at the bottom of the screen.
7. **Given** a parameter error from the API on a command save, **When** the save fails, **Then** the same field and step errors show as now (feature 078).

---

### User Story 3 - Delete with the shared dialog (Priority: P2)

The author selects Delete in the row menu or in the footer bar. The shared dialog asks "Delete game <name>?". If other items use the item, the dialog shows them and Delete is off.

**Why this priority**: Delete works now; this story moves it to the shared parts.

**Independent Test**: Delete a game from the row menu; try to delete a command that a sequence uses.

**Acceptance Scenarios**:

1. **Given** a row, **When** the author opens "⋮" and selects Delete (red, last item), **Then** a dialog shows "Delete game <name>?" (or "Delete command <name>?"), "You cannot undo this.", Cancel, and a red Delete. The focus starts on Cancel.
2. **Given** the dialog, **When** the author selects Delete and the call succeeds, **Then** the dialog closes, the list reloads, and the message area shows "Game deleted." / "Command deleted.".
3. **Given** the item is in use, **When** the delete call returns 409, **Then** the dialog stays open and shows the API message, "Referenced by:", and each group with its count and the names. Delete is off; Cancel closes the dialog.
4. **Given** another error, **When** the delete call fails, **Then** the dialog closes and the error shows in the message area (list) or in the form error (edit page).
5. **Given** the edit page, **When** the author selects Delete in the footer bar and confirms, **Then** the item is deleted and the list shows with the message.

### Edge Cases

- A name of 200 characters: the row keeps 56 px and the name ends with "…"; the page title ends with "…".
- The filter matches no item: the list shows "No games." / "No commands.".
- The address opens an edit page (`initialEditId`) of an item that does not load: the form error shows the load error (as now).
- The author selects Delete two times fast: one call only (the dialog buttons are off while the call runs).
- A 409 with no references: the dialog shows the API message (or "Cannot delete: the game is in use. Remove the links first." when the message is empty) and Delete is off.
- The window is 1024 px wide: no sideways scroll on the lists and the form pages.

## Requirements *(mandatory)*

### Functional Requirements

**Lists**

- **FR-001**: Games and Commands MUST use `PageHeader` with the title "Games" / "Commands" and one primary `Button` "Create game" / "Create command" (icon `add`).
- **FR-002**: A filter bar under the header MUST hold the input "Filter by name" (with a visible or accessible label "Filter by name"). The filter matches the name, with no difference between upper and lower case.
- **FR-003**: The Games list MUST use `ListView` with the columns Name (flexible) and Package name (320 px, monospace, "—" when empty), sorted by name.
- **FR-004**: The Commands list MUST use `ListView` with the columns Name (flexible) and Steps (96 px, right aligned, tabular digits), sorted by name. The Game column and the Game filter MUST go away.
- **FR-005**: Each row MUST have the actions Edit (`IconButton`, icon `edit`) and the "⋮" menu with Delete (red, last). A click on the row or on Edit MUST open the edit page with the unsaved-changes check. The name MUST NOT be a separate link.
- **FR-006**: The lists MUST show "Loading…", "No games." / "No commands.", and the list error with **Try again** (rule L14).
- **FR-007**: One message area under the header MUST show the success messages and the list errors.

**Form pages**

- **FR-008**: The create and edit pages MUST use `PageHeader` with a back arrow and the title "New game" / "New command" (create) or the item name as loaded (edit). The back arrow MUST do the current "back to list" action with the unsaved-changes check.
- **FR-009**: The "← Back to list" button, the `<h3>` "Edit Game" / "Edit Command", and the page `<h2>` MUST go away.
- **FR-010**: The form MUST be max. 720 px wide and centered. Each form section MUST show as a card with its heading. Labels MUST be above the fields.
- **FR-011**: A field with an error MUST have a red border and the error text under it (rule F5).
- **FR-012**: A `FooterBar` MUST stay visible at the bottom of the screen: Delete (`danger-outline`, edit mode only) on the left; Cancel (secondary) and Save (primary, last) on the right. "Loading…" shows in the left area while the data loads.
- **FR-013**: `FormActions` and the separate Delete button MUST go away from these two pages and from `CommandForm`. `CommandForm` MUST render the footer bar inside its form, with the new optional props `onDelete` and `deleting`.
- **FR-014**: The classes `btn`, `btn-secondary`, `btn-primary`, and `btn-danger` in `CommandForm`, its step panels, and the visual step picker MUST be replaced by the shared `Button` or `IconButton` with the same texts and behavior. The layout of the Steps section MUST NOT change otherwise.

**Delete**

- **FR-015**: Delete (row menu or footer bar) MUST open the shared `ConfirmDialog`: title "Delete game <name>?" / "Delete command <name>?", message "You cannot undo this.", Cancel, and the danger button Delete. Both buttons MUST be off while the call runs.
- **FR-016**: A 409 answer MUST keep the dialog open, show the API message and the references ("Referenced by:", each group with its name and count, and the item names) in the `details` area, and turn off Delete. Cancel MUST stay on.
- **FR-017**: `ConfirmDialog` MUST get an optional prop `confirmDisabled` that turns off only the confirm button.
- **FR-018**: Another error MUST close the dialog and show the error in the message area (list) or in the form error (edit page).
- **FR-019**: A successful delete MUST show the list, reload it, and show "Game deleted." / "Command deleted.".
- **FR-020**: These two pages MUST NOT use `ConfirmDeleteModal`. The component stays for the other pages that use it.

**Behavior that stays**

- **FR-021**: All current behavior MUST stay: the create, update, and delete calls and their payloads, the validation texts, the parameter error mapping of commands (feature 078), the detection checks, the unsaved-changes prompt, the reset signal of the Authoring sub-tab, and `initialCreate` / `initialEditId` from the address.
- **FR-022**: The success texts MUST be "Game created.", "Game updated.", "Game deleted.", "Command created.", "Command updated.", "Command deleted.".

**Cleanup, documents, tests**

- **FR-023**: CSS that no code uses after the change (the old table rules, `link-button` where unused) MUST go away. New CSS MUST use tokens only, with hover rules inside `@media (hover: hover)`.
- **FR-024**: `docs/ui-guidelines.md` section 13 MUST describe the shared changes: the `confirmDisabled` prop of `ConfirmDialog`, the form card style for form pages, the filter bar and the message area. The "Last reviewed" line MUST name feature 206. The `CommandForm` footer is page code and is not in the guideline.
- **FR-025**: The unit tests of the two pages, `CommandForm`, and its panels MUST change only for the layout and the names. No behavior check may be removed. New unit tests MUST cover the row Delete flow, the 409 references in the dialog, and `confirmDisabled`.
- **FR-026**: Browser tests (desktop 1280 px) MUST check: fixed column widths and 56 px rows on both lists; the filter bar above the list; a row click opens the edit page with a back arrow and the item name; the form is 720 px wide and centered; the footer bar is visible at the bottom with Delete on the left and Save last; Delete from the row menu opens the dialog; the 409 references show in the dialog; the back arrow shows the list.
- **FR-027**: The phone support level of Authoring stays `desktop-only`.

### Key Entities

- **Game**: name, package name. No change.
- **Command**: name, steps, detection, parameters. No change.
- **Delete conflict (409)**: message and references (groups of id and name). No change.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: 100% of the rows of both lists are 56 px high (±1), and the fixed columns keep their widths (±1) when the data changes.
- **SC-002**: Both pages have 0 uses of `btn`, `btn-secondary`, `btn-primary`, `btn-danger`, `link-button`, `ConfirmDeleteModal`, and "← Back to list".
- **SC-003**: The author opens an item with 1 click from the list and saves it with 1 click on Save, which is visible without scroll on each form page.
- **SC-004**: All behavior checks of the current Commands and Games tests pass (changed only for names and layout).

## Assumptions

- No backend or API change is needed.
- The step list of `CommandForm` keeps its design until the Sequences PR.
- The lists have no paging (rule L12 comes later); the number of games and commands is small.
