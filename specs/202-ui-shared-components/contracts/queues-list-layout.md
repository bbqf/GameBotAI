# Contract: Queues list layout

The browser tests check these values. A tolerance of ±1 px applies.

## Desktop (window 1280 x 800)

| Column | Header | Width |
|--------|--------|-------|
| 1 | Name | flexible (min. 160 px) |
| 2 | Cycle | 96 px |
| 3 | Status | 132 px |
| 4 | Activity | 320 px |
| 5 | Actions | 184 px |

- Header row: 40 px. Each data row: 56 px.
- The table has `aria-label="Queues table"` and the class `queues-table`. Each data row has the class `queues-row`.
- The Status cell holds a `StatusChip` with `data-testid="queue-status"`. The Activity cell has `data-testid="queue-activity"`.
- The Actions cell holds, in this order: slot 1, slot 2 (Stop), slot 3 (Edit), and "⋮" (label "More actions for <name>").
  The accessible names are "<Action> <queue name>" (for example "Start PNS Daily", "Stop PNS Daily", "Edit PNS Daily").
- The menu items are "Duplicate" and "Delete" (Delete red and last).
- The widths and heights do not change when a queue goes from Stopped to Running.
- At a window of 1024 px, each data row is still 56 px.

## Delete dialog

- Title: "Delete queue <name>?"
- Message: "The queue and its settings are deleted. You cannot undo this."
- Buttons: **Cancel**, **Delete** (danger).
- On success: the message "Queue deleted successfully." and the list reads the data again.
- On 409: the error text of the response, or "Stop the queue before deleting."

## Phone (window 360 x 640)

- Each queue row is a `<li>` with `data-testid="queue-card-<id>"`, 64 px high.
- Line 1: the name. Line 2: the status chip (`data-testid="queue-status"`), the activity text, the "Old data" text, and
  the "Not live" mark.
- Right: the main icon button ("Start <name>" or "Stop <name>", 44 x 44 px) and "⋮" ("More actions for <name>",
  44 x 44 px).
- Menu: "Open"; for a running queue also "Pause", "Cancel pause", or "Resume".
- `document.documentElement.scrollWidth` ≤ `clientWidth` at 320, 360, and 390 px.
