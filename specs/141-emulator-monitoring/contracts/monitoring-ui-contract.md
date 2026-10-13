# Contract: Monitoring UI

Phone support level: `full`. Area id `monitoring`, label "Monitoring", path `/monitoring`, order 2.

## Navigation

- M-01: `navigationAreas` has the entry `{ id: 'monitoring', label: 'Monitoring', path: '/monitoring', order: 2, phone: 'full' }`. Later areas move up by one order.
- M-02: The desktop navigation and the phone tab bar (or "More" list) show Monitoring. `?area=monitoring` and `/monitoring` open it.
- M-03: `docs/mobile-ui-principles.md` lists Monitoring as `full`.
- M-04: The existing tests `src/web-ui/src/components/__tests__/Navigation.test.tsx`, `src/web-ui/src/types/__tests__/navigation.test.ts`, and `src/web-ui/src/__tests__/navigation.spec.tsx` are updated for the new area and the new order.
- M-05: The session list shows ALL sessions the service holds, including `no_answer` and `stopping` (FR-002).

## Session list

- M-10: The page loads `GET /api/sessions/monitoring` at once and every 5 s while the page is visible (`usePolling`).
- M-11: Each item shows the name and a state chip (live, no answer, restarting). On a phone each item is a `PhoneCard`.
- M-12: With no sessions, the page shows "No running sessions."
- M-13: If the selected session leaves the list, the page shows "Session ended" and clears the selection.

## Screenshot

- M-20: Only the selected session is polled. The first request starts at once on selection. The interval is 2 s.
- M-21: The next request starts only after the last request ends (`useSequentialPolling`). The hook aborts the open request on a change of session or on unmount.
- M-22: The hook pauses when the page is hidden or the area is not mounted. It sends one request at once when it resumes.
- M-23: The page shows "Loading" until the first picture arrives. It then shows the picture and its age ("Updated 3 s ago"). The age counts up each second.
- M-24: After a failed request, the page keeps the last good picture, shows the marker "No live picture", and keeps the age. With no good picture, it shows the error text.
- M-25: On a phone the picture is at full width (`max-width: 100%`, height auto). No sideways scroll at 320 px and wider.

## Restart

- M-30: The detail has one primary control: "Restart emulator". At least 44 x 44 px. It is off when `restartAvailable` is false; the reason text shows near it.
- M-31: The control opens a dialog. The dialog names the emulator. When `ownerQueueId` is set, it shows the warning: "Queue <name> uses this emulator. The queue work will stop for a short time." Buttons: "Cancel", "Restart".
- M-32: "Cancel" or Escape closes the dialog and sends nothing.
- M-33: "Restart" sends one `POST /restart`. The UI never sends it again by itself, including after a connection loss.
- M-34: While `state` is `restarting`, the page polls `GET /restart` every 2 s and shows the step text. The restart control is off.
- M-35: On `success`, the page shows "Emulator restarted" and the screenshot polling goes on. On `failure`, it shows "Restart failed" with the reason text. On 409, it shows "A restart is already running for this emulator."
- M-36: When the connection is lost, the `ConnectionBanner` shows and the restart control is off.

## Layout change

- M-40: One `MonitoringPage` component serves both layouts. The selected session id is page state and the URL query `session`. A change between desktop and phone keeps it (FR-020) and does not remount the page.
- M-41: Desktop: two columns (list left, detail right). Phone: one column. List first. A tap on an item opens the detail with a new history entry; the back control calls `history.back()`. The `idx` rules of the queue detail apply.
- M-42: All `:hover` rules are inside `@media (hover: hover)`. Phone CSS is scoped to the phone query. Desktop tests stay green.

## STE text list

"Monitoring", "No running sessions.", "Session ended", "Loading", "Updated {n} s ago", "No live picture",
"Restart emulator", "Restarting", "Emulator restarted", "Restart failed", "Restart is not available for this session.",
"A restart is already running for this emulator."
