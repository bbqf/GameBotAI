# Contract: Editor icon buttons and Run next in the UI

## Icon buttons (desktop template editor)

- Each action button of the editor page is an `IconButton` (feature 173). A button has no visible text.
- Each button has an accessible name `<Action> <name>` and a tooltip `<Action>`. The names that exist
  today stay the same: `Remove <label>`, `Parameters for <label>`.
- A new icon is a 24 px path in `ICON_PATHS`. No icon library is added.
- The set: `runNext`, `parameters`, `remove`, `add`, `save`, `reload`, `load`. Existing icons stay.
- A button with a destructive effect keeps `tone="destructive"`.
- The form fields (schedule type, timer mode, time, offset) and the enable switch stay as they are.
- At 1024 px or wider, a sequence row with its controls uses 1 line. The parameter form opens below the
  row, only when the operator opens it.
- No function of the editor is removed.

## Run next button (desktop editor)

| Entry state | Button |
|-------------|--------|
| `idle` | On, name `Run next <label>`, tooltip `Run next` |
| `queued` | Off, name `Run next <label>`, tooltip `Queued to run next` |
| `running` | Off, tooltip `Running` |
| `scheduled` | Off, tooltip `Scheduled. Use the timer to change it.` |
| no state: new row | Off, tooltip "Save the template first" |
| no state: entry has no ID | Off, tooltip "Upgrade the template to use Run next" |
| no state: queue not running (editor of a stopped queue) | Off, tooltip "Start the queue to use Run next" |
| no state: disabled entry | Off, tooltip "The entry is disabled" |

- The row shows a small mark: "Queued to run next" or "Running".
- The row shows the reason of an off button as text, not only as a tooltip.
- A press sends `POST /api/queues/{id}/run-next` once. The button stays off until the call returns.
- On success the editor reads the monitor again. On error the editor shows the text of the error.
- The UI never sends the call again after a connection loss.

## Run next button (phone queue detail)

- The schedule list row shows the button on the right, next to the enable switch.
- Target: at least 44 x 44 px. No sideways scroll at 320 px. Long names are cut with an ellipsis.
- The row summary shows "Queued to run next" or "Running" when the state is `queued` or `running`.
- The row summary shows "Scheduled" when the state is `scheduled`. A touch screen has no hover, so the tooltip is never the only place for a reason.
- The button is off when the connection is lost, when the queue is not running, or when the state is not `idle`.
- The data refreshes with the existing 5 s poll. No new request is added for the state.
- Phone support level of Run next: `full`. The template editor stays `desktop-only`.
