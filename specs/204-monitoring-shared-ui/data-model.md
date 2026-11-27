# Data Model: Monitoring UI

No stored data changes. These are the UI values from `MonitoredSessionDto`.

## Session chip

| `state` | Chip state | Text |
|---------|------------|------|
| `live` | `done` | Live |
| `no_answer` | `failed` | No answer |
| `restarting` | `paused` | Restarting |
| `stopping` | `stopped` | Stopping |
| other | `unknown` | the value |

## List row

| Column | Width | Value |
|--------|-------|-------|
| Name | flex | `name` (one line, "…", tooltip) |
| State | 132 | chip (`data-testid="session-state"`) |
| Device | 120 | `not_live` → "Not live" mark; `live` → "Live"; missing → "—" |
| Queue | 220 | `ownerQueueName`, or "—" |
| Sequence | 260 | `currentSequenceName`, or "—" |

Phone: line 1 `name`; line 2 chip, `ownerQueueName` or "No queue", and the "Not live" mark for `not_live`.

## Key-value card (detail)

| Label | Value | Shows |
|-------|-------|-------|
| State | chip | always |
| Queue | `ownerQueueName` or "—" | always |
| Device | "Live", "Not live" mark, or "—" | always |
| Sequence | `currentSequenceName` (`data-testid="monitoring-sequence"`) or "—" (no test id) | always |
| Reason | `reason` | only for `no_answer` with a reason |
| Started | short local date and time of `startedAtUtc`, or "—" | always |

## Restart control

Off when: `!restartAvailable`, or `state === 'restarting'`, or the restart record is `restarting`, or a start call
runs, or the connection is lost. Texts stay as in the current code.
