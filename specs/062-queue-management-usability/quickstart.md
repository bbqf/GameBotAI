# Quickstart: Queue Management Usability

Manual verification for the three improvements. Backend unchanged; all changes are in `src/web-ui`.

## Build & test gate

```powershell
# from repo root
cd src/web-ui
npm install      # if not already installed
npm run build    # Vite build must succeed
npm test         # Jest suite must be green
```

(Lint and `tsc --noEmit` have pre-existing failures and are NOT the gate — `vite build` + `jest` are.)

## Scenario A — Sequences column removed (FR-001/FR-002)

1. Open the Queues page.
2. **Expect**: the overview table shows **Name, Emulator, Cycle, Status, Actions** and **no
   Sequences column**.
3. Start a queue, then click Schedule on a running queue.
4. **Expect**: the inline live-schedule row still spans the full table width correctly (no broken
   layout from the changed column count).

## Scenario B — One-click save and rename (FR-003/004/005/009/010/011)

1. Edit a queue already associated with a template (e.g. "Daily Farm"). Change an entry.
2. Click **Save Template** (the bottom button).
3. **Expect**: saved back to "Daily Farm" immediately — **no** "already exists — overwrite?" prompt.
4. Click the template-name button to open the template area; there's no separate "save as template"
   popup. Type a brand-new name not used by any template and click **Rename** (next to the field).
5. **Expect**: saved as a new template in one action, no overwrite prompt; the queue is now
   associated with it.
6. Open the template area again, type the name of a *different* existing template, click **Rename**.
7. **Expect**: an overwrite confirmation appears **next to the Rename button**; only on confirm is
   that other template replaced.
8. Open the template area, edit the name but do **not** click Rename; instead click **Save Template**
   (bottom).
9. **Expect**: the template is saved under the **old** name — the unconfirmed edit is ignored.
10. Open the template area, clear the name, click **Rename**.
11. **Expect**: a validation message appears by the field; no save attempted.
12. Open a brand-new queue with no template. **Expect**: the bottom **Save Template** button is
    disabled; you create the first template via the name field + **Rename**.

## Scenario C — Confirmations at the click location (FR-006/007/008)

1. In the Edit Queue form, change the name and click **Save** (the queue Save button).
2. **Expect**: a success confirmation appears **at the queue form's Save action row** — not only (or
   not at all) at the top of the page.
3. In the same edit session, save a template (click **Save Template** for an associated queue, or
   type a name in the template area and click **Rename**) and let it succeed.
4. **Expect**: a success confirmation appears **at the template controls (Save Template / Rename
   row)**.
5. Force a failure (e.g. stop the dev server / simulate an API error) and save.
6. **Expect**: an error indication appears at the same Save control, telling you the save did not
   complete.

## Done

All three scenarios pass and the Jest suite is green.
