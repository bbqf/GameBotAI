# Contract: Navigation layout (feature 205)

The browser tests and the unit tests check these points.

## Desktop (1280 px)

| ID | Check |
|----|-------|
| D-01 | `header.ui-appbar` is 56 px high (±1). It holds "GameBot", the navigation "Primary Navigation", and the connection state. |
| D-02 | "Primary Navigation" has 5 `role="tab"` items in this order: Queues, Monitor, Alerts, Authoring, Settings. Each holds one `svg` icon. |
| D-03 | The active top tab has `aria-selected="true"`. |
| D-04 | The connection state (`role="status"`) shows "Online" (or "Connection lost" when the health call fails). |
| D-05 | A click on Settings opens Execution logs: "Settings Navigation" (44 px high, ±1) shows 5 tabs in this order: Execution logs, Execution, Configuration, Gamepacks, Update; Execution logs is selected; the page header title is "Execution logs". |
| D-06 | Authoring shows "Authoring Navigation" with Commands, Games, Sequences, Images, Backup & Restore. No `h1` "Authoring" shows. |
| D-07 | `?area=configuration`, `?area=update`, `?area=gamepacks`, `?area=execution`, `?area=execution-logs` open that page; Settings and the matching sub-tab are selected. `?area=more` opens Execution logs. |
| D-08 | `?area=monitoring` opens Monitor (title "Monitor"); `?area=notifications` opens Alerts (title "Alerts"). |
| D-09 | The text "GameBot Web UI" does not show. |
| D-10 | The address names no area: Authoring > Commands shows. |
| D-11 | Each page of D-05 to D-08 shows exactly one page title heading (`.ui-page-header__title`, or the page's own title where the page has not moved yet). |
| D-12 | At 1024 x 768 the app bar is 56 px high (±1) and each top tab and the connection state are on one line (same `top` ±1). |
| D-13 | The document title is "GameBot". |
| D-14 | Each area of D-02 and D-05 opens with at most 2 clicks from Queues (top tab, then sub-tab). |

## Phone (360 x 640)

| ID | Check |
|----|-------|
| P-01 | "Primary Navigation" (bottom bar) has 4 buttons in this order: Queues, Monitor, Alerts, More. Each holds one `svg` icon and is at least 44 px high. The bar is 64 px high (±1, without the safe area). |
| P-02 | A tap on More opens the More page (`?area=more`). The More tab has `aria-current="page"`. The app bar shows "More" and no back arrow. |
| P-03 | The More page has the buttons Configuration, Gamepacks, Update (each 56 px high, ±1), the text "Desktop only", and the dimmed rows Execution logs and Authoring with "Use a computer" and `aria-disabled="true"`. |
| P-04 | A tap on a dimmed row does not change the page. |
| P-05 | A tap on Configuration opens Configuration; the app bar shows "Configuration" and a "Back" button; the More tab stays current. A tap on Back opens the More page. |
| P-06 | No sideways scroll at 320, 360, and 390 px on Queues and on the More page (`scrollWidth <= clientWidth`). |
| P-07 | `?area=authoring` shows the desktop-only notice with the button "Back to More", which opens the More page. |
| P-08 | The browser back control after Queues → More → Configuration shows More, then Queues. |
| P-09 | `?area=execution` shows Queues (hidden area, as now). |
