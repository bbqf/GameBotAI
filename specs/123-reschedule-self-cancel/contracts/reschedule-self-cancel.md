# Contract: reschedule-self option Cancel

The contract changes the payload of the existing `reschedule-self` primitive action. No new endpoint exists.

## Request payload

```json
{
  "type": "reschedule-self",
  "schemaVersion": "1",
  "payload": { "option": "Cancel" }
}
```

Use this payload in `POST /api/sequences`, `PUT /api/sequences/{id}` and `PATCH /api/sequences/{id}`.
`POST /api/sequences/{id}/validate` validates the saved steps of the sequence and cannot receive a new payload.
Its test saves a sequence with a valid Cancel step, then calls validate and expects `valid: true`.

## Validation (400 responses)

| Input | Response | Message must contain |
|-------|----------|----------------------|
| option "Cancel" with `timerTimeOfDay` | 400 | "timerTimeOfDay" and "only valid when option is Timer" |
| option "Cancel" with `timerRelativeOffset` | 400 | "timerRelativeOffset" and "only valid when option is Timer" |
| option "Cancel" with `ocrOffset` | 400 | "ocrOffset" and "only valid when option is Timer" |
| option "Cancel" with an unknown key | 400 | the key name and the known fields |
| unknown option text | 400 | "AtQueueStart, OncePerRun, Timer, EveryStep, Cancel" |
| missing option | 400 | "option is required" |

An invalid payload never gives 500 on create, update, patch or the repository save path.

## Run-time behavior

| Case | Step status | Outcome | removed |
|------|-------------|---------|---------|
| At least one one-time booking of this sequence removed | success | cancelled | true |
| No booking of this sequence | success | noop | false |
| Sequence not started from a queue | success | noop | false |
| Queue run no longer active | success | noop | false |

- The one-time bookings are the Timer, OncePerRun and AtQueueStart bookings that a reschedule-self step made in
  the current queue run (one start-to-stop run of the queue).
- After a Cancel step, no booking of the sequence fires. This includes a OncePerRun booking that the queue
  already copied for firing in the same cycle: the queue skips it.
- A Cancel step in a nested or called sequence uses the id of the sequence that owns the step, the same id that a
  Timer step uses.
- The step never fails a run. It never changes a booking of another sequence.
- The step keeps an EveryStep injection, a live schedule and every queue template entry.

## Step result and execution log

The step result and the execution log item of the step carry the outcome name (`cancelled` or `noop`), the
boolean `removed` and a short message.

## OpenAPI

The payload description of `PrimitiveAction` for `reschedule-self` lists Cancel. It says that Cancel takes no
other field, that it removes only the one-time bookings of this sequence in the current queue run, and that it
succeeds with no change when nothing is pending or when no queue started the run.
