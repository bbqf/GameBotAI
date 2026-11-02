# Data Model: Wait for Still Step

## WaitForStill step

A sequence step with `stepType` `Action` and an action of type `WaitForStill`. The step is stored in the
sequence JSON file. The fields are in `action.parameters` (stored) and `primitiveAction.payload` (API
request). No new property is added to `SequenceStep`.

| Field | Type | Required | Default | Rule |
|-------|------|----------|---------|------|
| `region` | object | no | whole screen | Has `x`, `y`, `width`, `height` (all four required). `x` and `y` are 0 or more. `width` and `height` are more than 0. |
| `stableMs` | integer | no | 300 | 1 or more. Must be at most `timeoutMs`. |
| `maxDiff` | number | no | 0.01 | From 0 to 1. |
| `timeoutMs` | integer | no | 5000 | 1 or more. |

A numeric field can hold a `{{name}}` parameter reference. The save check skips the field. The parameter
resolver replaces the reference before the run.

## Region

The existing `PixelRegion` class (feature 130). It holds `X`, `Y`, `Width`, `Height` in capture pixels.
The validation rule and the `ClipTo` rule stay in that class.

## Outcomes

| Outcome | When | Step status |
|---------|------|-------------|
| `still` | The region stays the same for `stableMs`. | Succeeded |
| `timeout_elapsed` | `timeoutMs` passes first, or no frame can be read. | Succeeded |
| `failed` | The region is fully outside the screen. | Failed (the sequence stops) |

`failed` is not an outcome name of the step in the sense of FR-010. It is the standard result for an
invalid run state and it uses the existing `ActionDispatchResult` failure path.

## New and changed types

- `WaitForStillPayload` (Domain): `Region` (PixelRegion?), `StableMs`, `MaxDiff`, `TimeoutMs`. Static
  `TryRead(SequenceActionPayload, out payload, out error)` and `Validate(SequenceActionPayload)`.
- `WaitForStillOutcomes` (Domain): constants `Still`, `TimeoutElapsed`, and `IsWaitForStillOutcome(string)`.
- `FrameDifference` (Domain.Vision): `Compute(Bitmap previous, Bitmap current, PixelRegion? region)` returns a
  double from 0 to 1.
- `WaitForStillTracker` (Domain): state `previousTime`, `stableStart`. Method `Observe(double difference,
  DateTimeOffset now)` returns true when the region is still. Method `Reset()` clears the state after a
  failed capture.
- `WaitForStillHandler` (Service): `ExecuteAsync(SequenceActionPayload action, string? sessionId,
  CancellationToken ct)` returns `ActionDispatchResult`.
- `ActionTypes.WaitForStill` (Domain): the constant `"WaitForStill"`.

## State transitions (tracker)

```text
no previous frame --first frame--> baseline set, stableStart = none
baseline/compare --diff <= maxDiff--> stableStart = stableStart ?? previous frame time
                                      if now - stableStart >= stableMs -> STILL
baseline/compare --diff >  maxDiff--> stableStart = none
any state --capture failed--> previous frame = none, stableStart = none
any state --now >= deadline--> TIMEOUT_ELAPSED
```
