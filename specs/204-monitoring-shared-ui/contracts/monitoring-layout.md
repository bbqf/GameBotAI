# Contract: Monitoring layout

The browser tests check these values. A tolerance of ±1 px applies.

## List page (desktop 1280 x 800)

- `PageHeader`: `<h2>` "Monitoring", no actions.
- `ListView` with `ariaLabel="Sessions"` and `tableClassName="session-list"`.
- Columns: Name (flex), State 132, Device 120, Queue 220, Sequence 260. No Actions column.
- Header row 40 px, data rows 56 px. Each row: `data-testid="session-row-<id>"`.
- The first cell holds the open button "Open <name>".

## Detail page (desktop)

- `<section data-testid="monitoring-detail">`.
- `PageHeader`: "Back" icon button, `<h2>` = name, the chip, and the button "Restart emulator".
- Body: `.ui-detail-grid` with the picture column (min 360 px) and `.ui-detail-side` (320 px) on the right.
- `.ui-kv` card with rows of at least 40 px: State, Queue, Device, Sequence, [Reason], Started.

## Restart dialog

- `role="dialog"`, name "Restart emulator <name>?"; message "The whole emulator stops and starts again."; when a
  queue owns the session, `role="note"` `data-testid="restart-queue-warning"` with "Queue <name> uses this emulator.
  The queue work will stop for a short time."; buttons **Cancel** (focus at open) and **Restart** (danger).

## Phone (360 x 640)

- List rows 64 px with `data-testid="session-row-<id>"`.
- Detail: header with "Back" and the title; the picture; the card; the status texts; `FooterBar` with "Restart
  emulator" (full width, at least 44 px high).
- `scrollWidth <= clientWidth` at 320, 360, and 390 px on the list and on the detail page.
