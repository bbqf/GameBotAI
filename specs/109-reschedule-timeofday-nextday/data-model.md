# Data Model: A reschedule-self time of day that has passed books the next day

No stored data changes. No API field changes. Only the value of one field changes.

## SelfRescheduleEntry (existing, not stored)

| Field | Type | Change |
|-------|------|--------|
| `EntryId` | string | none |
| `SequenceId` | string | none |
| `Option` | `SelfRescheduleOption` | none |
| `FireAt` | `DateTimeOffset?` | For `Timer` with a time of day: the next occurrence of that local clock time (rule below). Before the fix, a time that had passed gave the current moment. |

## Rule: next occurrence of a time of day

Inputs: `now` (the current local instant from `TimeProvider.GetLocalNow()`), `tod` (a `TimeOnly`), `zone` (`TimeProvider.LocalTimeZone`).

1. `todayAt` = the local date of `now` at `tod`.
2. When `todayAt` is later than the local date and time of `now`, `local` = `todayAt`.
3. Else, `local` = `todayAt` plus one day.
4. `FireAt` = `new DateTimeOffset(local, zone.GetUtcOffset(local))`.

## Examples

| Now (local) | Zone | `tod` | `FireAt` |
|-------------|------|-------|----------|
| 2026-09-24 14:52:11 | UTC+02:00 | 14:55:00 | 2026-09-24T14:55:00+02:00 |
| 2026-09-24 14:55:41 | UTC+02:00 | 14:55:00 | 2026-09-25T14:55:00+02:00 |
| 2026-09-24 14:55:00 | UTC+02:00 | 14:55:00 | 2026-09-25T14:55:00+02:00 |
| 2026-10-24 12:00:00 | CET/CEST (clock goes back on 2026-10-25) | 11:00:00 | 2026-10-25T11:00:00+01:00 |
| 2026-03-28 12:00:00 | CET/CEST (clock goes forward on 2026-03-29 at 02:00) | 02:30:00 | 2026-03-29T02:30:00+01:00 (the clock time is in the gap; standard offset) |

## Result text (existing)

`SelfRescheduleResult.ResolvedTiming` keeps its format: `FireAt` in the `"u"` format. The step message `rescheduled this sequence (option Timer, <timing>); applies to the current run only` keeps its format.
