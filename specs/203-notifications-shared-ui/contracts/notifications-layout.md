# Contract: Notifications layout

The browser tests check these values. A tolerance of ±1 px applies.

## Page (desktop, 1280 x 800)

- `PageHeader`: title "Notifications" (`<h2>`), primary button "Add target".
- Under the header: the message area, the intro text, the Telegram setup guide, the section "Targets" (`SectionHeader`),
  the target list, the section "Queue notification levels" (`SectionHeader`), the level list.

## Target list

- `ListView` with `ariaLabel="Notification targets"` and `tableClassName="notification-target-list"`.
- Columns: Name (flex), Type 120 px, Enabled 132 px, Chat ID 200 px, Bot token 160 px, Actions 148 px.
- Header row 40 px. Data rows 56 px.
- Actions cell, in order: "Send test message <name>", "Edit <name>", "More actions for <name>".
- Menu: "Delete" (danger).
- Phone: `rowTestId` = `target-row-<id>`; rows 64 px; main action "Send test message <name>" (44 x 44 px); menu "Edit",
  "Delete".

## Delete dialog

- Title "Delete target <name>?"; message "The target gets no more messages. You cannot undo this."; buttons **Cancel**
  and **Delete** (danger).
- On success: the row goes away; if the target is open in the editor, the editor closes.
- On error: the page message area shows the error text (or "The target could not be deleted.").

## Target editor

- Desktop: `SidePanel` with `testId="target-editor"`, 480 px wide, on the right edge of the window. Title "Add target" or
  "Edit target".
- Phone: full page with "Back" and the title; the page header, the guide, and the lists do not show.
- Body: an alert for an API error (if any), the card "Target" (Name, Bot token, Chat ID), the card "Status" (Toggle
  "Enabled"), the result message (`role="status"`).
- Footer: left **Delete** (danger-outline, edit only); right **Cancel**, **Save and test**, **Save** (primary, submit).

## Level list

- `SectionHeader` "Queue notification levels".
- `ListView` with `ariaLabel="Queue notification levels"` and `tableClassName="queue-level-table"`.
- Columns: Queue (flex), Level 240 px. Rows 56 px. The select has the accessible name "Notification level for <queue>"
  and is 40 px high.
- Phone: `rowTestId` = `level-row-<id>`; rows 64 px; the name on the left; the select 160 px on the right.
- States: "Loading…"; "No queue exists."; error text with **Try again**.

## Phone (360 x 640)

- `document.documentElement.scrollWidth` ≤ `clientWidth` at 320, 360, and 390 px, with the lists and with the editor
  open.
