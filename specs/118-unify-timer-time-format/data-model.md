# Data Model: One format for timerTimeOfDay

**Feature**: 118-unify-timer-time-format

No stored field changes. No migration.

## timerTimeOfDay (value)

| Place | Type | Change |
|-------|------|--------|
| Template save request `TemplateEntrySaveRequest.TimerTimeOfDay` | `string?` | Accepts `HH:mm` and `HH:mm:ss`. Was `HH:mm` only. |
| Template response `QueueTemplateDetailResponse.TimerTimeOfDay` | `string?` | `HH:mm` when seconds are zero, else `HH:mm:ss`. |
| Stored `QueueTemplateEntry.TimerTimeOfDay` | `TimeOnly?` | None. |
| Sequence step payload key `timerTimeOfDay` (`reschedule-self`) | string in `parameters` | Accepts only `HH:mm` and `HH:mm:ss`. Was any string that `TimeOnly.TryParse` reads. |
| `SelfReschedulePayload.TimerTimeOfDay` | `TimeOnly?` | None. |

## Grammar

```text
timeOfDay = hh ":" mm [ ":" ss ]
hh        = two digits, 00 to 23
mm        = two digits, 00 to 59
ss        = two digits, 00 to 59
```

No space, no sign, no fraction, no 12-hour marker. `24:00` is not valid.

## New type

`TimerTimeOfDayFormat` (static, `GameBot.Domain.Services`)

| Member | Rule |
|--------|------|
| `AcceptedFormatText` | Text for error messages: `HH:mm or HH:mm:ss (24-hour)`. |
| `TryParse(string?, out TimeOnly)` | True only for the grammar above. Uses `InvariantCulture`. |
| `Format(TimeOnly)` | `HH:mm` if second is 0, else `HH:mm:ss`. |
