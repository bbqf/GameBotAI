# Data Model: Sleeker Queue Entries and Parameters on the Phone

This feature adds no stored data and no API field. This file lists the stored data that the feature reads and writes. It also lists the view state that the feature adds.

## Stored data (no change)

**Template entry** (`QueueTemplateEntryDto`, saved with `TemplateEntrySaveDto`)

| Field | Use in this feature |
|-------|---------------------|
| `entryId` | Identifies the entry on the phone page and in the address. It is absent in a template that needs an upgrade. |
| `sequenceId`, `sequenceName` | Title of the card and of the page. `sequenceId` gives the declarations. |
| `scheduleType`, `timerTimeOfDay`, `timerRelativeOffset`, `enabled` | Read for the summary. Edited on the big screen only. |
| `parameterValues` | Read and written. Each item is a `ParameterBinding` (`name`, `value`, optional `values`). |
| `effectiveParameters` | Read. Shows the resolved value and its origin for a "Use default" row. |

These rules stay as they are:
- A binding with a null value is not saved. The row uses its default.
- A save sends every entry of the template in order, with `overwrite: true`.
- The server checks the values. The page shows the error text of a refused save.

## View state (new, in memory only)

**OpenEntryIds** (big screen, in `QueueSchedulingAreas`)
- Type: a set of entry IDs (strings).
- Initial value: empty. All panels are closed.
- Changes: the settings button of a card adds or removes its ID. When an entry is removed, its ID is removed. A schedule change or a drag does not change the set.
- Lifetime: while the editor is mounted. It is not saved.

**EntryPageState** (phone, in `PhoneEntryPage`)

| Field | Meaning |
|-------|---------|
| `draft` | The working list of bindings of the entry. It starts as the saved `parameterValues`. |
| `dirty` | True when `draft` differs from the saved values. |
| `saving` | True while a save call runs. Save is off while it is true. |
| `saveResult` | `none`, `saved`, or an error text. An error keeps the draft. |
| `declarations` | The parameters of the sequence, from `getSequence`. |
| `declarationsState` | `loading`, `loaded`, or `failed`. In the state `failed` the page shows "The parameters did not load." and the stored values as plain text. Save is off. |
| `leavePrompt` | True while the keep-or-discard dialog shows. |

State changes:
- `draft` changes on each edit. `dirty` comes from the comparison with the saved values.
- A successful save sets the saved values to the draft, `dirty` to false, and `saveResult` to `saved`.
- A failed save keeps `draft` and `dirty`. It sets `saveResult` to the error text.
- A new template read (after a save or from the poll) replaces the saved values only when `dirty` is false. A poll then cannot overwrite typed text.

## Phone position (address)

`NavigationPosition` gets an optional `entryId` (a string or null).

| Address | Screen |
|---------|--------|
| `?area=queues` | Queue list |
| `?area=queues&queue=<id>` | Queue detail |
| `?area=queues&queue=<id>&entry=<entryId>` | Entry page |

Rules:
- The app reads `entry` only when `queue` is set and the area is `queues`. In other cases it drops `entry`.
- An unknown queue shows "Queue not found". An unknown entry shows "Entry not found". Both give a way back.
- A step to the page adds one history entry. Back and forward restore the page.

## Shared schedule text

`scheduleSummary(entry)` returns one of these texts: `Daily at HH:mm`, `Every <offset>`, `Once each run`, `Every step`, `At queue start`, `Before each run`, or `not scheduled`.

The result depends on the entry fields only. For a Timer entry the timer mode decides: in relative mode the helper reads `timerRelativeOffset`. In time-of-day mode it reads `timerTimeOfDay`. An old value in the other field does not change the text. A Timer entry with no value in the used field gives `not scheduled`. The card summary and the phone rows both call this helper.
