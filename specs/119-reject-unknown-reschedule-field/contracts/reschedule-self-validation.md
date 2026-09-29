# Contract: reschedule-self step validation

Applies to the sequence create and update endpoints (with or without `dryRun`).

## Request

A sequence with a step whose action type is `reschedule-self` and whose payload has a top-level field that is not `option`, `timerTimeOfDay`, `timerRelativeOffset`, or `ocrOffset`.

```json
{ "option": "Timer", "timerTimeOfDay": "11:00", "nextDay": true }
```

## Response

- Status: 400.
- The error text contains each unknown field name and the list of known fields.
- Error text form: `Step '<label>' reschedule-self payload has unknown field(s): nextDay. Known fields: option, timerTimeOfDay, timerRelativeOffset, ocrOffset.`
- Without `dryRun`, the platform does not store the sequence.

## Unchanged behavior

- `option: "Bogus"` returns 400 with the message `... option 'Bogus' is not a known schedule option (expected one of AtQueueStart, OncePerRun, Timer, EveryStep)`.
- A payload with only known fields (any letter case) returns the same result as before.
- An unknown field inside `ocrOffset` is not checked.
- A stored sequence with an unknown field loads and runs as before.
