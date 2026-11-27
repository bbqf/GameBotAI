# Feature Specification: Move the Notifications area to the shared UI components

**Feature Branch**: `203-notifications-shared-ui`  
**Created**: 2026-11-27  
**Status**: Draft  
**Input**: User description: "Move the Notifications area (PR 2 of the UI consistency work, after feature 202) to the shared UI components and to docs/ui-guidelines.md." The full description is in the pipeline call. The user approved the plan of one PR for each area on 2026-11-27.

## Background

Feature 202 added the design tokens and the shared components (`src/web-ui/src/components/ui/`) and moved the Queues list to them. The Notifications area does not use them yet:

- The target list is a table with no CSS on the desktop. Its columns change width with the data. Its actions are the text buttons "Edit", "Send test message", and "Delete".
- On the phone, a target card shows five lines of text and three text buttons, so the cards do not have the same height.
- The target form opens under the list. It has the old `FormField` rows (label at the side), a checkbox for "Enabled", and the text buttons Save, Save and test, and Cancel. It has no Delete and no field errors.
- The queue level table is a table with no CSS. On the phone, it shows cards of different height.
- Delete uses the old `ConfirmDeleteModal` with the classes `btn` and `btn-danger`, which no CSS defines.

## Clarifications

### Session 2026-11-27

The pipeline runs with no manual review. Each answer is the most reasonable option, chosen from the guideline, the user decisions, and the code.

- Q: What does the Type column show? → A: "Telegram" for the type `telegram`. Other types show the type value as it is. Rationale: the list shows names for people; `telegram` is the only type now.
- Q: Which chip states does the Enabled column use? → A: An enabled target shows the state `done` with the text "On" (success color). A disabled target shows the state `stopped` with the text "Off" (idle color). Rationale: rule S3 (one word) and the colors of the guideline.
- Q: The side panel is open and the operator clicks another row. What happens? → A: The panel shows the other target. The form loads the values of that target, as the current form does when the target changes. There is no question about unsaved values. Rationale: this is the current behavior; a guard for unsaved values is not in the scope of this PR.
- Q: What happens after **Save**? → A: The panel or the phone page stays open with the saved target and shows "The target is saved." (as now). After Delete, the panel or the phone page closes. Rationale: the current behavior; the operator can use **Save and test** next.
- Q: Does the phone form page get its own browser history entry? → A: No. The on-screen back arrow closes the page and shows the list. Rationale: only the Queues area has history entries now (`docs/mobile-ui-principles.md`); a history change for Notifications is navigation work for a later PR.
- Q: What does the row action **Send test message** do while the call runs? → A: The icon of that row is off (dimmed, in its position) until the call ends. The result shows in the page message area: "The test message was sent." or "The test message failed: <reason>". Rationale: rule L6 and the current messages.
- Q: Does the form check the fields before it sends the call? → A: Yes. An empty Name shows "Enter a name." and an empty Chat ID shows "Enter a chat ID." under the field, with a red border. The form sends no call while a required field is empty. The check runs on **Save** and on **Save and test**. A field error goes away when the operator types in the field. Rationale: rule F5; the API also refuses an empty name, so the check only makes the error clear earlier.
- Q: The current text "Wait. The targets load." and "Wait. The queues load." — do they stay? → A: No. Both lists use the shared loading state "Loading…". Rationale: rule L14 (one text for all lists).

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Read and act on the targets with the shared list (Priority: P1)

The operator opens Notifications. The targets show in the shared list: on the desktop a table with fixed column widths and 56 px rows, on the phone rows of 64 px. Each row has the icons **Send test message** and **Edit**, and **Delete** in the "⋮" menu (on the phone, **Send test message** is the main icon, and **Edit** and **Delete** are in the menu).

**Why this priority**: The target list is the main content of the area, and its layout is the problem that the user reported.

**Independent Test**: Open Notifications with 3 targets (one with a long name, one disabled). Measure the columns and rows. Send a test message. Delete one target.

**Acceptance Scenarios**:

1. **Given** 3 targets, **When** the page opens at 1280 px, **Then** the Type (120), Enabled (132), Chat ID (200), Bot token (160), and Actions (148) columns have these widths (±1 px), and each row is 56 px high.
2. **Given** a disabled target, **When** the row shows, **Then** the Enabled chip shows "Off" in the idle color. An enabled target shows "On" in the success color.
3. **Given** a target, **When** the operator selects **Send test message**, **Then** the icon is off until the call ends, and the page message area shows the result.
4. **Given** a target, **When** the operator selects **Delete** in "⋮", **Then** a dialog shows "Delete target <name>?" with **Cancel** and a red **Delete**. **Delete** deletes the target and removes its row.
5. **Given** no target, **When** the page opens, **Then** the list shows "No target is saved. Follow the guide and add one.".
6. **Given** the phone layout, **When** the list shows, **Then** each row is 64 px high, shows the name, the On/Off chip, and "Telegram · <chat ID>", and each control is at least 44 x 44 px.

---

### User Story 2 - Add and edit a target in the side panel or the phone page (Priority: P1)

The operator selects **Add target**, a row, or **Edit**. On the desktop, the form opens in a 480 px side panel on the right, and the list stays visible. On the phone, the form opens as a full page with a back arrow. The form has the cards "Target" (Name, Bot token, Chat ID) and "Status" (Enabled as a switch), and a footer bar with Delete (edit only), Cancel, Save and test, and Save.

**Why this priority**: The form is the second main task of the area, and it shows the form rules of the guideline for the first time.

**Independent Test**: Add a target with an empty name. Fix the name and save. Edit the target and save with an empty token. Use Save and test.

**Acceptance Scenarios**:

1. **Given** the desktop, **When** the operator selects **Add target**, **Then** a side panel of 480 px (±1) opens on the right with the title "Add target", and the list stays visible.
2. **Given** the phone, **When** the operator selects **Add target**, **Then** a full page opens with a back arrow and the title "Add target", and the list does not show.
3. **Given** an empty Name, **When** the operator selects **Save**, **Then** "Enter a name." shows under Name, the field has a red border, and no call is sent.
4. **Given** a saved target with a token, **When** the operator saves with an empty Bot token field, **Then** the call has no token and the stored token stays.
5. **Given** valid fields, **When** the operator selects **Save and test**, **Then** the form saves first, then sends the test message, and shows "The target is saved. The test message was sent." (or the failure text). A save error stops the test.
6. **Given** an open edit form, **When** the operator selects **Delete** in the footer and confirms, **Then** the target is deleted and the form closes.
7. **Given** the side panel is open, **When** the operator presses Escape or selects the close icon, **Then** the panel closes and the focus goes back to the control that opened it.
8. **Given** the side panel is open, **When** the operator selects another row, **Then** the panel shows that target.

---

### User Story 3 - Set the notification level of each queue (Priority: P2)

Under the section header "Queue notification levels", the queues show in the shared list. Each row has the queue name and a select with None, Failure, and Success + Failure.

**Why this priority**: The level table is a small part of the area and works now. This story is about the same layout.

**Independent Test**: Open Notifications with 3 queues. Measure the rows. Change one level.

**Acceptance Scenarios**:

1. **Given** 3 queues, **When** the page opens at 1280 px, **Then** the Level column is 240 px (±1) and each row is 56 px high.
2. **Given** the phone layout, **When** the list shows, **Then** each row is 64 px high, with the name on the left (one line, "…") and the select of 160 px on the right.
3. **Given** a queue, **When** the operator changes the level, **Then** one call is sent and the select shows the saved level. A failed call shows its text in the section.

---

### User Story 4 - Use the new shared parts in later pages (Priority: P3)

A developer who moves another page uses the new shared components SidePanel, Toggle, and SectionHeader, and the icon "send".

**Why this priority**: This is the base for later PRs (Configuration, Authoring).

**Independent Test**: Read the unit tests of the new components and section 13 of the guideline.

**Acceptance Scenarios**:

1. **Given** the SidePanel, **When** it opens, **Then** the focus moves into the panel, Escape closes it, and the focus goes back to the opener.
2. **Given** the Toggle, **When** the operator clicks it or presses Space, **Then** it changes its state, and it has `role="switch"` and `aria-checked`.
3. **Given** the guideline, **When** a developer reads section 13 and rule F6, **Then** SidePanel, Toggle, SectionHeader, and the select exception of rule F6 are described.

### Edge Cases

- A target name of 100 characters: the row keeps its height and the name ends with "…".
- A target with no chat ID (old data): the Chat ID cell shows "—"; the phone meta shows "Telegram".
- A test call that fails with a network error: the message area shows "The test message failed: <error text>"; the icon is on again.
- The operator deletes the target that is open in the side panel from the row menu: the panel closes.
- The load of the targets fails: the list shows the error text and **Try again**, which loads the list again.
- The load of the queues fails: the level list shows the error text and **Try again**.
- The phone in landscape (640 x 360): the layout is the phone layout; no control is cut, no sideways scroll.
- Busy state: while a save or a test runs in the form, all footer buttons are off.

## Requirements *(mandatory)*

### Functional Requirements

**Page**

- **FR-001**: The Notifications page MUST use `PageHeader` with the title "Notifications" and the primary button **Add target**. The target list MUST have the shared `SectionHeader` "Targets" above it (it replaces the current `<h3>Targets</h3>`).
- **FR-002**: The intro text and the Telegram setup guide MUST stay, with no change of their content.
- **FR-003**: The page MUST show its messages (load error, delete error, test result) in one message area under the header.

**Target list**

- **FR-004**: The target list MUST use `ListView` with the columns of User Story 1 (Name flexible; Type 120 px; Enabled 132 px with a `StatusChip`; Chat ID 200 px in monospace; Bot token 160 px with "Saved (<hint>)", "Saved", or "Not set"; Actions 148 px).
- **FR-005**: Each desktop row MUST show the icon buttons **Send test message** (icon `send`) and **Edit**, and a "⋮" menu with **Delete** (danger). The accessible names are "<Action> <target name>".
- **FR-006**: Each phone row MUST show the name, the On/Off chip, the meta "Telegram · <chat ID>", the main icon **Send test message**, and a "⋮" menu with **Edit** and **Delete**.
- **FR-007**: A click on a row (outside the actions) MUST open the edit form of the target.
- **FR-008**: **Delete** MUST open the shared `ConfirmDialog` with the title "Delete target <name>?", the message "The target gets no more messages. You cannot undo this.", and a danger **Delete** button. The list MUST NOT use `ConfirmDeleteModal`.
- **FR-009**: While a test call of a row runs, the **Send test message** icon of that row MUST be off.
- **FR-010**: The empty text MUST be "No target is saved. Follow the guide and add one.". The loading state MUST be the shared "Loading…". A load error MUST show the error state with **Try again**.

**Target form**

- **FR-011**: On the desktop, the form MUST open in the shared `SidePanel` (480 px, right side) with the title "Add target" or "Edit target", a close icon button, a body that scrolls, and a `FooterBar`.
- **FR-012**: On the phone, the form MUST open as a full page with a back-arrow header and the same title, in place of the list.
- **FR-013**: The form MUST have the cards "Target" (Name, Bot token, Chat ID) and "Status" (Enabled as the shared `Toggle`). Labels MUST be above the fields. Inputs MUST be 40 px high, with a 16 px font on the phone.
- **FR-014**: The footer MUST have **Delete** (danger-outline, only in edit mode) on the left, and **Cancel**, **Save and test**, and **Save** (primary, last) on the right. While a call runs, all footer buttons MUST be off.
- **FR-015**: An empty Name MUST show "Enter a name." and an empty Chat ID MUST show "Enter a chat ID." under the field with a red border, and the form MUST NOT send a call. The error MUST go away when the operator types in the field.
- **FR-016**: All current form behavior MUST stay: an empty Bot token keeps the stored token; the token placeholder shows "Saved (<hint>). Leave empty to keep it."; **Save and test** saves first and a save error stops the test; the messages "The target is saved.", "The target is saved. The test message was sent.", and "The target is saved. The test message failed: <reason>".
- **FR-017**: An API error MUST show in an alert at the top of the form body.
- **FR-018**: **Delete** in the footer MUST open the same `ConfirmDialog` as the row. After a delete, the form MUST close.

**Queue levels**

- **FR-019**: The level section MUST use the shared `SectionHeader` with the title "Queue notification levels".
- **FR-020**: The level list MUST use `ListView`. Desktop: Queue (flexible) and Level (240 px, the select). Phone: rows of 64 px with the name on the left and the select of 160 px on the right.
- **FR-021**: A level change MUST send one call and keep the current behavior and error text. The loading state MUST be "Loading…", the empty text "No queue exists.", and a load error MUST show **Try again**.

**Shared components and documents**

- **FR-022**: The app MUST have the shared `SidePanel`: `role="dialog"`, `aria-modal="false"`, `aria-labelledby` = the title; the focus moves into the panel on open; Escape and the close button call `onClose`; the focus goes back to the opener; on the phone it renders as a full page with a back arrow.
- **FR-023**: The app MUST have the shared `Toggle`: `role="switch"`, `aria-checked`, a label, a 44 px tap area on the phone, and the primary color when on.
- **FR-024**: The app MUST have the shared `SectionHeader`: an `<h3>` of 18 px and an optional actions area on the right.
- **FR-025**: The shared icon set MUST have the icon `send`.
- **FR-026**: `docs/ui-guidelines.md` MUST describe SidePanel, Toggle, and SectionHeader in section 13, and rule F6 MUST have the exception: a choice inside a fixed list row uses a select, because a segmented control does not fit the row on the phone.
- **FR-027**: The new CSS MUST use only theme tokens, and each hover rule MUST be inside `@media (hover: hover)`.

**Tests**

- **FR-028**: The notification unit tests MUST change only where the layout and the names change. No behavior check may be removed.
- **FR-029**: Browser tests MUST check the column widths and the 56 px rows of both lists on the desktop, the 480 px side panel, and the Delete dialog; and on the phone the 64 px rows, the 44 px tap targets, no sideways scroll at 320, 360, and 390 px, and the full-page form with the back arrow.
- **FR-030**: The phone support level of Notifications stays `full`. The feature follows `docs/mobile-ui-principles.md` and `docs/ui-guidelines.md`.

### Key Entities

- **Notification target**: a Telegram target with a name, a chat ID, a stored bot token (shown only as a hint), and an enabled state. No change of the data.
- **Queue notification level**: None, Failure, or Success + Failure for each queue. No change of the data.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: On the desktop, 100% of the rows of both lists are 56 px high (±1), and the fixed columns have their widths (±1), with names of 5 to 100 characters.
- **SC-002**: On the phone, 100% of the rows of both lists are 64 px high (±1), each control is at least 44 x 44 px, and no sideways scroll occurs at 320, 360, and 390 px.
- **SC-003**: The Notifications area has 0 text buttons in list rows and 0 uses of the classes `btn`, `btn-secondary`, and `btn-danger`.
- **SC-004**: An operator can add a target in one panel or page with no scroll on a 1280 x 800 desktop window.
- **SC-005**: All behavior checks of the current notification tests pass (changed only for names and layout).

## Assumptions

- No backend or API change is needed.
- The area name stays "Notifications". The rename to "Alerts" and the move under the new navigation come in the navigation PR.
- The Telegram setup guide keeps its content and its layout in this PR.
- `ConfirmDeleteModal` stays in the code for other pages that use it.
