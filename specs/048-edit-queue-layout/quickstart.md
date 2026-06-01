# Quickstart: Edit Queue Page Layout

Manual verification of the refined edit-queue page (frontend-only). Assumes the GameBot
service is running and the web-ui dev server is up (or the built UI is served), with at
least one queue and a couple of saved templates available.

## A. Row order and Name/Emulator (FR-001 … FR-005)

1. Open the **Queues** tab and click a queue's name (or **Edit**) to open the edit page.
2. Confirm the controls appear top to bottom in this order:
   1. **Name** (editable) + **Emulator** (read-only)
   2. **Template controls** (template-name button, Save Template, Reload Template)
   3. **Cycle execution**
   4. **Sequences** (one row per entry)
   5. **Save** / **Cancel**
3. Confirm there is **no** text saying the bound emulator cannot be changed/exchanged.
4. Edit the name and toggle cycle execution, click **Save**, reopen — values persisted.
   Click **Cancel** on a fresh edit — field edits discarded, page closes.

## B. Inline template sections (FR-006 … FR-013)

1. On open, both the Save and Load panels are **closed**.
2. Click the **template-name button** → the **Load** panel opens inline between rows 2
   and 3, listing templates (or an empty state).
3. Click **Save Template** → the **Load** panel closes and the **Save** panel opens in
   the same place (only one open at a time).
4. In Save, leave the name blank → validation message; enter an existing name → overwrite
   confirmation; cancel → nothing saved.
5. In Load, delete a template (confirm) → it disappears from the list. Close the panel →
   row 2 returns to default (both closed).

## C. Reload Template (FR-014 … FR-018)

1. Load a template into the queue → the template-name button now shows that name and
   **Reload Template** becomes enabled.
2. Add or remove a sequence so the queue diverges from the template.
3. Click **Reload Template** → a **confirmation** appears; **Cancel** leaves entries
   unchanged; **Reload** restores the template's entries in order.
4. Click **Reload Template** again immediately (entries now match the template) → it
   applies **without** a confirmation prompt (nothing to discard).
5. With an **empty** queue associated with a template, click Reload → applies without a
   prompt.
6. Start the queue (status **Running**) → **Reload Template** (and Load) are disabled.
7. Delete or rename the associated template elsewhere, return and click Reload → message
   "Template '<name>' is no longer available"; entries unchanged.

## D. Independence from Save/Cancel (FR-004a)

1. Load/reload/add/remove entries, then click **Cancel** (row 5) → the field edits are
   discarded but the queue's sequence entries reflect the actions already applied
   (entry actions are immediate and independent of Save/Cancel).

## E. Automated tests

Run the frontend suite:

```powershell
npm --prefix src/web-ui test
```

Expect green: `QueueForm`, `SaveTemplateDialog`/section, `TemplatePickerDialog`/section,
`QueueTemplateControls`, and `QueuesPage.templates` specs covering layout order, inline
section toggling, and the reload diff/confirm/disabled/missing behaviors.
