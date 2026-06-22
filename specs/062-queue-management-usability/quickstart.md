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

## Scenario B — One-click template save (FR-003/004/005)

1. Edit a queue already associated with a template (e.g. "Daily Farm"). Change an entry.
2. Click **Save Template** without changing the pre-filled name.
3. **Expect**: the template is saved immediately — **no** "already exists — overwrite?" prompt.
4. Click **Save Template** again, type a brand-new name not used by any template, save.
5. **Expect**: saved in one action, no overwrite prompt.
6. Click **Save Template** again, type the name of a *different* existing template.
7. **Expect**: an overwrite confirmation appears; only on confirm is that other template replaced.
8. Try saving with an empty/invalid name.
9. **Expect**: validation message; no save attempted.

## Scenario C — Confirmations at the click location (FR-006/007/008)

1. In the Edit Queue form, change the name and click **Save** (the queue Save button).
2. **Expect**: a success confirmation appears **at the queue form's Save action row** — not only (or
   not at all) at the top of the page.
3. In the same edit session, click **Save Template** and let it succeed.
4. **Expect**: a success confirmation appears **at the template controls (Save Template row)**.
5. Force a failure (e.g. stop the dev server / simulate an API error) and save.
6. **Expect**: an error indication appears at the same Save control, telling you the save did not
   complete.

## Done

All three scenarios pass and the Jest suite is green.
