# Contract: timerTimeOfDay format

**Feature**: 118-unify-timer-time-format | **Issue**: #226

## Scope

- Template save: `POST /api/queue-templates`. Changed.
- Template read: the entry `timerTimeOfDay` in the save response and in the GET response. Changed (format rule).
- Sequence validation: `POST /api/sequences` with `dryRun: true`. This applies to a `reschedule-self` step with `option` `Timer`. Changed.
- The other save paths (`PUT` and `PATCH`) use the same shared parser, so they follow the same rule.
- OpenAPI: description of `timerTimeOfDay` for the template entry and for the `reschedule-self` payload. Changed.

## Accept and reject matrix

Both endpoints give the same result for each string.

| Value | Result |
|-------|--------|
| `00:00`, `09:05`, `15:30`, `23:59` | accept |
| `00:00:00`, `15:30:45`, `23:59:59` | accept |
| `24:00`, `24:00:00`, `23:60`, `12:00:60` | reject |
| `9:30`, `9:30:00` (one-digit hour) | reject |
| `11:00 PM`, `5pm` | reject |
| ` 15:30`, `15:30 ` (spaces) | reject |
| `15:30:`, `15.30`, `1530`, `15:30:45.123`, `abc` | reject |
| empty, or missing | not checked by this rule. The "exactly one of timerTimeOfDay or timerRelativeOffset" rule keeps its behavior and its error text. |

## Error messages

Both messages contain the phrase `HH:mm or HH:mm:ss`.

- Template (HTTP 400, code `invalid_request`): `entries[<i>].timerTimeOfDay '<value>' is not a valid time of day; accepted format: HH:mm or HH:mm:ss (24-hour), for example '15:30' or '15:30:00'`
- Sequence (validation error, with the step label prefix as now): `timerTimeOfDay '<value>' is not a valid time of day; accepted format: HH:mm or HH:mm:ss (24-hour), for example '15:30' or '15:30:00'`

## Template response format

| Stored time | Response text |
|-------------|---------------|
| `15:30:00` | `15:30` |
| `15:30:45` | `15:30:45` |

The response drops sub-second ticks, as it does today.

## OpenAPI text

- `reschedule-self` payload description (constant in `PrimitiveActionSchemaFilter`): `exactly one of timerTimeOfDay (HH:mm or HH:mm:ss, 24-hour, service-local time of day, no other form) or timerRelativeOffset (...)`. The rest of the text does not change.
- Template entry `timerTimeOfDay`: `Time of day for a Timer entry, as HH:mm or HH:mm:ss (24-hour, service-local time). The response writes HH:mm when the seconds are zero. Exclusive with timerRelativeOffset.`

## Not changed

`timerRelativeOffset`, the other schedule fields, the run behavior, and the `nextDay` field behavior (issue #228).
