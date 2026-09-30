# Data Model: reschedule-self payload

No stored data changes. This file lists the payload fields that the check uses.

## reschedule-self payload (top level)

| Field | Constant in `SelfReschedulePayload` | Known | Notes |
|-------|-------------------------------------|-------|-------|
| `option` | `OptionKey` | Yes | Required. Value check is not changed. |
| `timerTimeOfDay` | `TimerTimeOfDayKey` | Yes | Value check is not changed. |
| `timerRelativeOffset` | `TimerRelativeOffsetKey` | Yes | Value check is not changed. |
| `ocrOffset` | `OcrOffsetKey` | Yes | Nested keys are not checked. |
| any other key | - | No | New rule: reject with an error that names the key. |

## Rules

- Key match is without regard to letter case.
- The check reads only the top-level keys of the step payload.
- The check does not run in the load path or in `SelfReschedulePayload.TryRead`.
- A wrong `option` value is reported by `TryRead` as before, and the method returns at that point.
