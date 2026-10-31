# Contract: Queues API and UI changes (173)

## Service API

### GET /api/queues (changed)

Each item may carry `operatorPause`: `none`, `pausing`, or `paused`. The field is present only for a running queue. The change is additive.

### PUT /api/queues/{id} (changed)

- Before: 409 `queue_running` ("Stop the queue before editing.") for a running queue.
- After: The call works for a running queue. The service keeps the stored emulator serial. A changed `emulatorSerial` is not part of the request, so no new error is needed. The saved settings take effect at the next start.
- All other validation stays the same.

### POST /api/queues/{id}/entries and DELETE /api/queues/{id}/entries/{entryId} (changed)

- Before: no check of the run state.
- After: 409 `queue_running` ("Stop the queue before changing its entries. Use the template to change a running queue.") for a running queue.
- Reason: The running editor changes the template only (FR-011).

### Unchanged

`PUT {id}/entries` (already 409 for a running queue), `PUT {id}/template`, `GET {id}/template-diff`, `POST {id}/apply-template`, `POST {id}/pause`, `POST {id}/resume`, `POST {id}/live-schedule` (the service keeps it; the web UI no longer calls it).

## UI contract (big screen)

| Item | Rule |
|------|------|
| Columns | Name, Cycle, Status, Activity, Actions. No Emulator column. |
| Action buttons | Icon buttons. Each has `aria-label` "<Action> <queue name>" and a tooltip "<Action>". |
| Schedule button | None. |
| Edit | Enabled for all states. The name link opens the same page. |
| Edit page, running queue | Pause controls, template changes notice, live monitor, queue settings (emulator read-only), template editor in template-only mode. |
| Edit page, running, no template | Read-only entries and the message "Link a template to edit this queue while it runs." |
| Settings note | Near Save: "Settings apply at the next start." |

## UI contract (phone)

| Item | Rule |
|------|------|
| Card summary | Same text as the big screen Activity column (shared rule). |
| Detail, running queue | Pause controls, template changes notice with Review (only when count > 0), the entry switches. |
| Detail text | For a running queue the note "Changes apply when the queue starts again" is replaced by the notice. |
| Tap size | 44 px (Rule 3 of `docs/mobile-ui-principles.md`). |
| Phone support level | `reduced`. |

## Test contract

- A shared test feeds the six states of FR-024 to the shared rule. It checks the big screen cell and the phone card for the same text.
- A service test checks that `PUT {id}` saves settings for a running queue and does not change the run.
- A service test checks that add-entry and remove-entry return 409 for a running queue.
