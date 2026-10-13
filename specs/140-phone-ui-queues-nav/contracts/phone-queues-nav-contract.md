# Contract: Phone Queue Activity and Navigation

This contract states the UI behavior rules that tests check. The service API does not change.

## Service endpoints used (no change)

| Endpoint | Use |
|----------|-----|
| `GET /api/queues` | Queue list, state |
| `GET /api/queues/{id}` | Queue detail |
| `GET /api/queues/{id}/monitor` | Now, up next, last outcome. Safe to poll. |
| `GET /api/queue-templates/{id}` | Entries, enabled state, timer settings |
| `POST /api/queues/{id}/start`, `.../stop` | Start and stop (as today) |
| `POST /api/queue-templates` | Entry enable switch (as today) |

## Address rules

- A-01: The address shows the area as `area=<id>`. When a queue detail is open, it also shows `queue=<queueId>`.
- A-02: A link with these values opens that screen directly (SC-004).
- A-03: A phone link with `area=execution`, or a path that starts with `/execution` (not `/execution-logs`), shows the Queues area. The current history entry is replaced.
- A-04: A link with `queue=<unknown id>` shows "Queue not found" and a button to the queue list.

## Desktop `tab` address rules (unchanged, read from `App.tsx`)

- T-01: On load, the `tab` value is read from the `tab` query parameter and normalized. If it is missing or not valid, the legacy path (for example `/commands`, `/games/new`, `/sequences/<id>/edit`) gives the tab. If there is no legacy path, the tab is `Commands`.
- T-02: After each change of tab or area, the address is rewritten with `replaceState`. It keeps the path, sets `tab=<current tab>` and `area=<current area>`, and removes the `create` and `id` parameters. All other parameters stay.
- T-03: When the user selects the tab that is already active, no tab change happens. The page gets a reset signal and returns to its list.
- T-04: The phone address work (A-01 to A-04, H-01 to H-07) adds `queue` and the History API in phone mode only. It does not change T-01 to T-03 in desktop mode.

## History rules (phone mode)

- H-01: A move to a new screen adds one entry (`pushState`). A move to the screen that is already shown adds none.
- H-02: Back and forward restore the screen of the entry. They add no entry.
- H-03: The first screen of the session has `state.idx = 0`. Back from it leaves the application.
- H-04: The on-screen back control of the queue detail calls `history.back()` when `state.idx > 0`. Otherwise it replaces the entry with the queue list address.
- H-05: Redirects use `replaceState`.
- H-06: A page reload keeps `history.state`. The rules H-01 to H-05 stay true after a reload.
- H-07: Desktop mode keeps the current behavior (`replaceState` only).

## Navigation list rules

- N-01: The tab bar and the "More" sheet do not list an area whose phone level is `hidden`.
- N-02: `execution` has the phone level `hidden`. `execution-logs` stays `desktop-only`.

## Queue activity rules

- Q-01: The detail shows the state chip, the "Now" text, and the last outcome.
- Q-02: The detail shows one row per template entry. Order: enabled with time (by time), enabled without time, disabled (marked "disabled").
- Q-03: A stopped queue shows no next time for any entry. It shows the schedule text of the entry or "not scheduled".
- Q-04: The queue card shows one line: the running sequence name, else the next scheduled time, else "Stopped".
- Q-05: The data refreshes every 5 s while the page is visible and the connection is not lost. The refresh stops when the detail closes. On resume, one refresh runs at once.
- Q-06: When the connection is lost, the banner shows, start/stop and the enable switch are disabled, and the last data stays visible and is marked old.
- Q-07: No control for schedule creation, reorder, or edit is shown.
- Q-08: At 320 to 768 CSS px wide there is no sideways page scroll. Controls are at least 44 x 44 CSS px. Long names are shortened and have a `title` with the full text.
