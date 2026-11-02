# Contract: WaitForStill step

## Request (PUT or POST of a sequence)

A step with `stepType` `Action`. The `primitiveAction.type` is matched without case, as for the other
types. The canonical spelling is `WaitForStill`.

```json
{
  "stepId": "wait-pan",
  "stepType": "Action",
  "primitiveAction": {
    "type": "WaitForStill",
    "payload": {
      "region": { "x": 0, "y": 120, "width": 720, "height": 900 },
      "stableMs": 300,
      "maxDiff": 0.01,
      "timeoutMs": 5000
    }
  }
}
```

All payload fields are optional. An empty payload uses the defaults: whole screen, `stableMs` 300,
`maxDiff` 0.01, `timeoutMs` 5000.

## Validation (HTTP 400 on save, and on the load check)

| Case | Error text (starts with `Step '<label>' WaitForStill`) |
|------|----------------------------------------------------------|
| `stableMs` less than 1 or not an integer | `stableMs must be an integer of 1 or more` |
| `timeoutMs` less than 1 or not an integer | `timeoutMs must be an integer of 1 or more` |
| `maxDiff` below 0, above 1, or not a number | `maxDiff must be a number from 0 to 1` |
| `stableMs` greater than `timeoutMs` | `stableMs must not be greater than timeoutMs, so that the step can end with still` |
| Region field missing, `x` or `y` below 0, `width` or `height` 0 or less | The messages of `PixelRegion.Validate`, with the prefix `region` |
| Unknown payload field | `payload field '<name>' is not supported` |

The 400 response lists all errors, as it does for the other action types.

## Run result

| Outcome | Condition | Step status | Message |
|---------|-----------|-------------|---------|
| `still` | The region stays the same for `stableMs`. | Succeeded | `still after <ms> ms; last difference <d>` |
| `timeout_elapsed` | `timeoutMs` passes, or no frame can be read. | Succeeded | `not still after <ms> ms; last difference <d>` or `the screen could not be read` |
| `failed` | The region is fully outside the screen. | Failed | `region is outside the screen (<w>x<h>)` |

The difference has four decimals. It is `none` when the step compared no frame pair. A step with no readable frame at all gives `the screen could not be read`. A dry run records `skipped_dry_run` and reads no frame. A step-through shows the preview text
`would wait until the region is still` and reads no frame. A failed capture is skipped: the step clears the previous frame and the stable time, waits one capture interval, and tries again until the deadline, as `WaitForImage` does. A stop signal cancels the wait and passes up as `OperationCanceledException`. The step records no outcome, as `WaitForImage` does.

## Use of the outcome

A `commandOutcome` condition may use `expectedState` `still` or `timeout_elapsed` for a `WaitForStill`
step. A condition with `expectedState` `success` matches `still`.

## OpenAPI

- `PrimitiveAction.type` enum gets the value `WaitForStill` (from `SequenceActionTypes.All`).
- The `payload` description gets a `- WaitForStill:` line with the table above in short form.
- The `commandOutcome` `expectedState` text names `still` and `timeout_elapsed`.

## Execution log entry

`stepType` `WaitForStill`, `actionOutcome` and `reasonCode` set to the outcome, `message` as in the run
result table.
