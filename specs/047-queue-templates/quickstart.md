# Quickstart: Queue Templates

**Feature**: 047-queue-templates

This walks the primary flows and the manual checks that back the success criteria.
Prereqs: the service is running, at least one queue exists, and a few sequences are
authored.

## A. Save the current queue's entries as a template (US1)

1. Open the **Queues** tab, click a stopped queue's **Edit** to open its entry editor.
2. Add two or more sequences (existing add control).
3. Click **Save as template**, enter `Daily Farm`, confirm.
   - ✅ A template `Daily Farm` is created (POST `/api/queue-templates` → 201).
4. Click **Save as template** again, enter `Daily Farm` once more.
   - ✅ An overwrite confirmation appears ("A template named 'Daily Farm' exists. Overwrite?").
   - Confirm → template replaced (server: 409 → retry with `overwrite:true` → 200).
   - Cancel → nothing changes.
5. Try to save with a blank name, a name containing `/`, or a 120-char name.
   - ✅ Each is rejected with a message naming the rule (400).

## B. Restart persistence (US1 / SC-002)

1. Restart the service.
2. Open any queue's editor → its entry list is **empty** (queue entries are not
   persisted, by 046's design).
3. Click **Load template**.
   - ✅ `Daily Farm` is still listed with its 3 entries (templates persist).

## C. Load a template into a queue (US2)

1. In a queue editor (stopped), click **Load template**, pick `Daily Farm`, **Load**.
   - If the queue already has entries → ✅ a **Replace** confirmation appears; confirm to proceed.
   - ✅ The queue's entries become exactly the template's, in order (PUT `/api/queues/{id}/entries`).
2. Open a **different** queue, load `Daily Farm`.
   - ✅ Both queues independently contain the template's entries.
3. Edit one queue's entries (add/remove). Reopen `Daily Farm` in the picker.
   - ✅ The template is unchanged (load is a copy, no live link).

## D. Edit-via-load-and-save (the only way to "edit" a template, US1/US2)

1. Load `Daily Farm` into a queue, change its entries, click **Save as template**.
   - ✅ The name field is **pre-filled** with `Daily Farm` (origin remembered).
2. Confirm the overwrite.
   - ✅ `Daily Farm` now reflects the edited entries.

## E. Delete a template (US3)

1. Click **Load template** to open the picker.
2. Click **Delete** on a template, confirm.
   - ✅ It disappears from the list (DELETE → 204) and stays gone after restart.
   - ✅ Any queue that previously loaded it keeps its current entries.
3. With no templates saved, open the picker.
   - ✅ An empty state is shown; nothing to load.

## F. Load blocked while running (FR-014a)

1. Start a queue (status → Running).
2. Attempt to load a template via the API `PUT /api/queues/{id}/entries`.
   - ✅ `409 queue_running` ("Stop the queue before loading a template.").
   - In the UI the **Load** button is disabled while running; **Save** is not blocked.

## API smoke (optional)

```bash
# Save
curl -X POST localhost:5000/api/queue-templates \
  -H 'Content-Type: application/json' \
  -d '{"name":"Daily Farm","sequenceIds":["seq-a","seq-b"],"overwrite":false}'

# List / detail
curl localhost:5000/api/queue-templates
curl localhost:5000/api/queue-templates/{id}

# Load into a queue (replace)
curl -X PUT localhost:5000/api/queues/{queueId}/entries \
  -H 'Content-Type: application/json' -d '{"sequenceIds":["seq-a","seq-b"]}'

# Delete
curl -X DELETE localhost:5000/api/queue-templates/{id}
```

## Success-criteria mapping

| Check | Criterion |
|-------|-----------|
| A + C end-to-end < 30s, single tab | SC-001, SC-006 |
| B (restart) | SC-002 |
| C order preserved | SC-003 |
| A overwrite confirm + C replace confirm cancelable | SC-004 |
| E delete persists, queues unaffected | SC-005 |
| C independence | SC-006 |
| picker responsive @ 50×100 | SC-007 |
