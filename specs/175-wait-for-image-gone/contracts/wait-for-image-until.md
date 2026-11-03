# Contract: WaitForImage `until` option

## Request (PUT or POST of a sequence)

```json
{
  "stepId": "wait-toast-gone",
  "stepType": "Action",
  "primitiveAction": {
    "type": "WaitForImage",
    "payload": {
      "detectionTarget": { "referenceImageId": "toast-auto-enabled", "confidence": 0.8 },
      "timeoutMs": 6000,
      "until": "absent",
      "absentCaptures": 2
    }
  }
}
```

`until` and `absentCaptures` are optional. A payload without them behaves as before this feature.

## Validation (HTTP 400 on save, and on the load check)

| Case | Error text |
|------|------------|
| `until` is not `present` or `absent` | `Step '<label>' waitForImage until must be 'present' or 'absent'.` |
| `absentCaptures` is less than 1 or is not an integer | `Step '<label>' waitForImage absentCaptures must be an integer of 1 or more.` |

`absentCaptures` with `until` `present` or no `until` is accepted and has no effect. The 400 response
lists all errors, as it does for other action types.

## Run result

| Outcome | Condition | Step status |
|---------|-----------|-------------|
| `image_absent` | `absentCaptures` consecutive captures lack the image. | Succeeded |
| `timeout_elapsed` | `timeoutMs` passes first. | Succeeded |
| `image_unavailable` | No capture source, or an evaluation error. The step waits to the deadline. | Succeeded |

A capture where the image is seen, and a failed capture, reset the count. A stop signal cancels the wait
and passes up as `OperationCanceledException`. The step records no outcome. `"absent"` is also true when
the screen changed to something else. A sequence needs its own arrival check after the wait.

## Use of the outcome

As for present mode. The `stepOutcomes` value is `success`. The execution log `actionOutcome` and
`exitCondition` are `image_absent`.

## OpenAPI

- The `WaitForImage` line of the `payload` description names `until`, `absentCaptures`, the outcome
  `image_absent`, and the arrival-check note.
- The execution log `exitCondition` text names `image_absent`.

## Execution log entry

`stepType` `WaitForImage`, `actionOutcome` and `exitCondition` set to the outcome.
