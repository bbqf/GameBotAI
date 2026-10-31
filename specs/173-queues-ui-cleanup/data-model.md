# Data Model: Clean up the Queues page on the big screen (173)

The feature adds no stored data. It adds one field to a list response and some client types.

## Queue list item (service response, changed)

| Field | Type | Change |
|-------|------|--------|
| `id`, `name`, `emulatorSerial`, `cycleExecution`, `status`, and the others | as today | No change |
| `operatorPause` | `none`, `pausing`, `paused` | New. Optional. Present only when `status` is `Running`. Same values as the health block of feature 171. |

Rule: An old client that ignores the field keeps working.

## Queue activity (client, shared rule)

Input: `status`, `operatorPause`, monitor snapshot (or none).
Output: one text.

| State | Text |
|-------|------|
| Stopped | `Stopped` |
| Running, `operatorPause` = `paused` | `Paused` |
| Running, `operatorPause` = `pausing` | `Pausing` |
| Running, a sequence runs (not an idle pause) | `Running: <sequence name>` |
| Running, next time known | `Next run at <HH:mm>` |
| Running, nothing scheduled | `Nothing scheduled` |
| Running, none of the above | `Waiting` |

Order of checks: stopped, paused, pausing, running sequence, next time, nothing scheduled, waiting. The first match wins. The monitor failure case: use the status only (`Running`).

## Template changes notice (client)

| Field | Meaning |
|-------|---------|
| `count` | Number of differences from the diff call. 0 hides the notice. |
| `needsUpgrade` | True when the template or the run has entries with no entry ID. The notice shows "needs upgrade" and no count. |

The notice has no state of its own. It reads the diff of feature 171.

## Row actions (client)

| Queue state | Icons shown |
|-------------|-------------|
| Stopped | Start, Edit, Duplicate, Delete |
| Running (`none`) | Pause, Stop, Edit, Duplicate, Delete (disabled) |
| Pausing | Cancel pause, Stop, Edit, Duplicate, Delete (disabled) |
| Paused | Resume, Stop, Edit, Duplicate, Delete (disabled) |

## Running editor state (client)

| Field | Meaning |
|-------|---------|
| `entries` | Working list of template entries (with template entry IDs). Source: the linked template. |
| `dirty` | True when the list differs from the saved template. |

Rule: The running editor never sends the entries to the queue. It sends them to the template only.
