# Feature Specification: Move the Monitoring area to the shared UI components

**Feature Branch**: `204-monitoring-shared-ui`  
**Created**: 2026-11-27  
**Status**: Draft  
**Input**: User description: "Move the Monitoring area (PR 3 of the UI consistency work, after features 202 and 203) to the shared UI components and to docs/ui-guidelines.md." The full description is in the pipeline call. The user approved mockup B ("Detail page — Monitoring session") of `docs/ui-guidelines/2-form-detail-editor-logs.png` and the plan of one PR for each area on 2026-11-27.

## Background

The Monitoring area (feature 141) shows all running sessions, a live picture of the selected session, and the restart of the whole emulator. Now:

- On the desktop, the page has two columns: a list of buttons on the left and the detail on the right. On the phone, the list uses `PhoneCard` and the detail is a separate screen with a "← Back" text button.
- The state chip uses the old classes `status-chip status-chip--…` of feature 141, not the shared `StatusChip`.
- The detail shows the data as loose lines ("Sequence: …", a hint). It has no key-value card.
- The restart dialog is its own component (`RestartEmulatorDialog`) with the classes `btn`, `btn-secondary`, and `btn-danger`, which no CSS defines.
- The desktop shows the title "Monitoring" two times: the `<h1>` of `App.tsx` and no page header (the list has no header).

The guideline (rules L1 to L14, P1 to P5, D1, N6) and mockup B give one layout for both screens: a list page and a detail page with a back arrow, a status chip, the actions in the header (desktop) or in a footer bar (phone), the picture on the left, and a key-value card on the right.

## Clarifications

### Session 2026-11-27

The pipeline runs with no manual review. Each answer is the most reasonable option, chosen from the guideline, the approved mockup, and the code.

- Q: Does the desktop keep the split view (list and detail at the same time)? → A: No. The desktop shows the list page or the detail page, as the phone does. A click on a row opens the detail page; the back arrow shows the list page. Rationale: the user approved mockup B, which shows a detail page with a back arrow on the desktop; rules L7 and P1 say the same; one layout for both screens is the aim of the work.
- Q: How does the desktop keep the selection in the address? → A: As now: `replaceState` with the query `session`. The browser back control does not go from the detail page to the list page on the desktop. The phone keeps `pushState` and the current back behavior (`isFirstEntry`). Rationale: `docs/mobile-ui-principles.md` ("The desktop layout keeps `replaceState` only"); a change of the desktop history is navigation work for a later PR.
- Q: What does the page show when the open session ends? → A: The list page with the notice "Session ended" in the message area, as the current code does when it clears the selection. Rationale: the detail of an ended session has no data; the current behavior (contract M-13) stays.
- Q: How does the card show "Started"? → A: The local date and time of `startedAtUtc` in the short format of the browser (`Intl.DateTimeFormat` with `dateStyle: 'short'` and `timeStyle: 'short'`). Rationale: the operator needs the start time; a duration that changes each second would make the card move.
- Q: What does the Device row show? → A: "Live" when `ownerQueueDeviceLiveness` is `live`, the "Not live" mark (`NotLiveBadge`) when it is `not_live`, and "—" when it is missing. Rationale: rule L8 ("—" for an empty value); the "Not live" mark is the one of feature 199.
- Q: When does the Reason row show? → A: Only when the state is `no_answer` and `reason` is not empty. Rationale: the current detail shows the reason only in that case.
- Q: What does the page show when a restart is not available? → A: The text "Restart is not available for this session." and, when `restartUnavailableReason` is set, that text after it in the same paragraph. The Restart button is off. Rationale: the API gives the reason; the operator needs it.
- Q: What is line 2 of a phone row? → A: The state chip, then the queue name (or "No queue"), then the "Not live" mark when the device is not live. Rationale: rule L4 (status chip and one meta value) with the mark of feature 199, as on the Queues list.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - See all sessions in the shared list (Priority: P1)

The operator opens Monitoring. The page header shows "Monitoring". The sessions show in the shared list: on the desktop a table with fixed columns (Name, State, Device, Queue, Sequence) and 56 px rows; on the phone rows of 64 px with the name, the state chip, and the queue.

**Why this priority**: The list is the entry to the area and the first part of the guideline for this area.

**Independent Test**: Open Monitoring with 3 sessions (one live with a queue, one `no_answer`, one with a not-live device). Measure the columns and rows.

**Acceptance Scenarios**:

1. **Given** 3 sessions, **When** the page opens at 1280 px, **Then** the State (132), Device (120), Queue (220), and Sequence (260) columns have these widths (±1 px) and each row is 56 px high.
2. **Given** a session in each state, **When** the list shows, **Then** the chips show "Live" (success color), "No answer" (danger color), "Restarting" (warning color), and "Stopping" (idle color).
3. **Given** a session with a not-live device, **When** the list shows, **Then** the Device cell shows the "Not live" mark. A session with no queue shows "—" in Device and Queue.
4. **Given** no session, **When** the page opens, **Then** the list shows "No running sessions.".
5. **Given** a failed list call, **When** the page shows, **Then** the list shows the error text and **Try again**, which loads the list again.
6. **Given** the phone layout, **When** the list shows, **Then** each row is 64 px high and shows the name, the chip, and the queue name (or "No queue").

---

### User Story 2 - Open a session on a detail page (Priority: P1)

The operator selects a row. The detail page opens: a header with a back arrow, the session name, and the state chip; the live picture; and a card with State, Queue, Device, Sequence, Reason (only for no answer), and Started. On the desktop the picture is on the left and the card (320 px) is on the right. On the phone they are in one column.

**Why this priority**: The picture and the data of one session are the main use of the area.

**Independent Test**: Open the detail of a session at 1280 px and at 360 px. Read the card. Go back.

**Acceptance Scenarios**:

1. **Given** the list, **When** the operator clicks a row, **Then** the detail page of that session opens, and the list does not show.
2. **Given** the desktop detail page, **When** it shows, **Then** the card column is 320 px wide (±1) and is on the right of the picture.
3. **Given** a session with a queue and a sequence, **When** the card shows, **Then** it shows the queue name and the sequence name (the value has `data-testid="monitoring-sequence"`).
4. **Given** the detail page, **When** the operator selects the back arrow, **Then** the list page shows.
5. **Given** the open session ends (it leaves the list), **When** the list refreshes, **Then** the list page shows with "Session ended".
6. **Given** the phone, **When** the operator uses the browser back control on the detail page, **Then** the list shows (as now).

---

### User Story 3 - Restart the emulator with the shared dialog (Priority: P2)

The operator selects **Restart emulator** (desktop: in the header; phone: in the footer bar). The shared confirmation dialog asks "Restart emulator <name>?" and shows the queue warning when a queue uses the emulator.

**Why this priority**: The restart works now; this story moves it to the shared parts.

**Independent Test**: Open a session that a queue uses, select Restart emulator, cancel, then restart.

**Acceptance Scenarios**:

1. **Given** a session with a queue, **When** the operator selects **Restart emulator**, **Then** a dialog shows "Restart emulator <name>?", "The whole emulator stops and starts again.", the warning "Queue <queue> uses this emulator. The queue work will stop for a short time.", **Cancel**, and a red **Restart**.
2. **Given** a session with no queue, **When** the dialog opens, **Then** it shows no queue warning.
3. **Given** the dialog, **When** the operator presses Escape or selects **Cancel**, **Then** the dialog closes and no call is sent. The focus starts on **Cancel**.
4. **Given** the dialog, **When** the operator selects **Restart**, **Then** one restart call is sent and the restart status shows ("Restarting: <step>", then "Emulator restarted" or "Restart failed. <reason>").
5. **Given** a restart is not available, **When** the detail shows, **Then** the button is off and the page shows "Restart is not available for this session." (and the reason, if the API gives one).
6. **Given** the phone, **When** the detail shows, **Then** **Restart emulator** is in the footer bar and is at least 44 px high.

### Edge Cases

- A session name of 100 characters: the row keeps its height; the name ends with "…"; the header title ends with "…".
- The connection is lost: the Restart button is off; the list and the picture stop their calls (as now).
- A restart call returns 409: the page shows "A restart is already running for this emulator." and reads the restart state (as now).
- A failed restart read: nothing changes, and the page sends no new restart (contract M-33).
- The picture fails: `ScreenshotView` shows its current marker; the card stays.
- The phone in landscape (640 x 360): the phone layout; no sideways scroll.
- A list call fails while the detail page shows: the detail page stays with the last data (the call failed, not the session).

## Requirements *(mandatory)*

### Functional Requirements

**List page**

- **FR-001**: The list page MUST use `PageHeader` with the title "Monitoring" and no actions.
- **FR-002**: The list error and the "Session ended" notice MUST show in one message area under the header.
- **FR-003**: The session list MUST use `ListView` with the desktop columns Name (flexible), State 132 px, Device 120 px, Queue 220 px, and Sequence 260 px, and no actions column.
- **FR-004**: The State column MUST use the shared `StatusChip`: `live` → `done` "Live"; `no_answer` → `failed` "No answer"; `restarting` → `paused` "Restarting"; `stopping` → `stopped` "Stopping"; another value → `unknown` with the value as text. The chip has `data-testid="session-state"`.
- **FR-005**: Device MUST show the "Not live" mark for `not_live`, "Live" for `live`, else "—". Queue and Sequence MUST show "—" when empty.
- **FR-006**: Phone rows MUST show the name (line 1), and the chip, the queue name or "No queue", and the "Not live" mark (line 2). Each row MUST have `data-testid="session-row-<id>"` on both layouts.
- **FR-007**: The list MUST show "Loading…" while it loads, "No running sessions." when it is empty, and the error text with **Try again** when the call fails and the list has no data. When a call fails and the list has data from an earlier call, the list MUST stay and the error MUST show in the message area (as now).
- **FR-008**: A click on a row MUST open the detail page of the session.

**Detail page**

- **FR-009**: The detail page MUST use `PageHeader` with a back arrow, the session name, and the state chip. The back arrow MUST show the list page (desktop: clear the selection with `replaceState`; phone: the current `closeDetail` behavior).
- **FR-010**: On the desktop, the header MUST have the secondary button **Restart emulator** (icon `reload`). On the phone, the same button MUST be in a `FooterBar` at full width, and the header MUST have no actions.
- **FR-011**: The button MUST be off when the restart is not available, a restart runs, a start call runs, or the connection is lost.
- **FR-012**: The desktop body MUST be a grid: the picture (`ScreenshotView`) in a flexible column of at least 360 px on the left, and a column of 320 px on the right with the key-value card and the status texts. The phone body MUST be one column in the order: picture, card, status texts.
- **FR-013**: The key-value card MUST have rows of 40 px with the muted label on the left and the value on the right: State (chip), Queue, Device, Sequence (`data-testid="monitoring-sequence"` on the value), Reason (only for `no_answer` with a reason), Started (short local date and time).
- **FR-014**: Under the card, the page MUST show the restart status (same texts and roles as now, `data-testid="restart-status"`), the restart message (`data-testid="restart-message"`), and, when a restart is not available, "Restart is not available for this session." with the reason if set (`data-testid="restart-unavailable"`).
- **FR-015**: The detail section MUST keep `data-testid="monitoring-detail"`.

**Restart dialog**

- **FR-016**: The restart MUST use the shared `ConfirmDialog` with the title "Restart emulator <name>?", the message "The whole emulator stops and starts again.", **Cancel**, and the danger button **Restart**.
- **FR-017**: `ConfirmDialog` MUST get an optional `details` prop. The dialog renders it between the message and the buttons.
- **FR-018**: For a session with a queue, the dialog MUST show "Queue <name> uses this emulator. The queue work will stop for a short time." (`role="note"`, `data-testid="restart-queue-warning"`). A queue id with no name MUST show "(no name)" as the name.
- **FR-019**: `RestartEmulatorDialog` and its test MUST go away. Its behavior checks (Escape, Cancel sends nothing, focus on Cancel, the warning only with a queue) MUST move to other tests.

**Behavior that stays**

- **FR-020**: All current behavior MUST stay: list polling every 5 s, restart polling every 2 s while a restart runs, the list refresh at the end of a restart, the 409 message, no automatic new restart, the session-ended handling, the reload signal, and the connection-lost rules.

**Cleanup, documents, tests**

- **FR-021**: The desktop `<h1>Monitoring</h1>` in `App.tsx` MUST go away (the page header has the title).
- **FR-022**: CSS that no code uses after the change (the split view, the old list buttons, the old detail rows) MUST go away. The `ScreenshotView` styles MUST stay.
- **FR-023**: `docs/ui-guidelines.md` section 13 MUST describe the `details` prop of `ConfirmDialog`.
- **FR-024**: The Monitoring unit tests MUST change only where the layout and the names change. No behavior check may be removed.
- **FR-025**: Browser tests MUST check: on the desktop, the column widths, the 56 px rows, a row click that opens the detail with a back arrow, the 320 px card column, the dialog with the queue warning, and the back arrow; on the phone, the 64 px rows, no sideways scroll at 320, 360, and 390 px on the list and the detail, the footer button of at least 44 px, and the back arrow.
- **FR-026**: The phone support level of Monitoring stays `full`. The feature follows `docs/mobile-ui-principles.md` and `docs/ui-guidelines.md`.

### Key Entities

- **Monitored session**: name, state, reason, owner queue (id, name, device liveness), current sequence, restart availability and reason, start time. No change of the data.
- **Restart record**: state (restarting, success, failure), step, reason. No change.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: On the desktop, 100% of the session rows are 56 px high (±1) and the fixed columns keep their widths (±1) when the states change.
- **SC-002**: On the phone, 100% of the session rows are 64 px high (±1), and no sideways scroll occurs at 320, 360, and 390 px on the list and on the detail page.
- **SC-003**: The Monitoring area has 0 uses of the classes `btn`, `btn-secondary`, `btn-danger`, and `status-chip`, and 0 text glyphs used as icons ("← Back").
- **SC-004**: The operator reaches the picture of a session with 1 click from the list on both layouts.
- **SC-005**: All behavior checks of the current Monitoring tests pass (changed only for names and layout).

## Assumptions

- No backend or API change is needed.
- `ScreenshotView` keeps its internals (polling, age text, error marker). Only its place in the page changes.
- The navigation (tabs, More) does not change in this PR.
