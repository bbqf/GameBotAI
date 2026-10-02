# Contract: reschedule-self `keep`

Applies to the action payload of a sequence step with type `reschedule-self`, on every save path (create, update, import).

## Request payload

```json
{
  "type": "reschedule-self",
  "schemaVersion": "1",
  "payload": { "option": "Timer", "timerRelativeOffset": "00:15:00", "keep": "earliest" }
}
```

## Rules

| Input | Response |
|-------|----------|
| No `keep` key (or JSON null) | Accepted. Last booking of a run wins (unchanged). |
| `keep: "earliest"` (any case) with option `Timer` | Accepted. Value returned unchanged. |
| `keep` with any other value, an empty string, or a non-string value | 400. Message names `earliest`. |
| `keep` with option other than `Timer` | 400. Message: `reschedule-self keep is only valid when option is Timer.` |
| `keep` inside `ocrOffset` | Not read. `keep` is a top-level field only. |

## Run-time behavior (Timer bookings)

- Each Timer booking records the id of its run, with or without `keep`.
- A booking with `keep: earliest` replaces the pending booking of the same run only if its fire time is strictly earlier. The compare uses absolute fire times. For an `ocrOffset` booking, it uses the fire time after `min`, `max`, and the fallback.
- The run is the root execution of the sequence. A booking in a child sequence uses the run of the root execution.
- A pending booking from another run, from the engine, from the step-0 retry, or from the re-arm path (`RearmTimerFiring` stores `RunId = null`) is replaced by the first booking of the run.
- A booking without `keep` always replaces the pending booking (unchanged).
- A losing booking: the step succeeds, the outcome is `scheduled`, and the Information log message names both fire times.
- The queue keeps at most one pending booking for each sequence.

## OpenAPI

The `reschedule-self` payload description (from `PrimitiveActionSchemaFilter`) names `keep`, the value `earliest`, the Timer-only rule, and the default behavior.

## Documentation

`docs/architecture.md` (self-reschedule section), `specs/STATUS.md`, and `CHANGELOG.md` describe the same rules (FR-013).
