# Data Model: Log each command step under its own step ID (B-020)

## SequenceExecutionResult.StepResult (changed, internal)

| Field | Type | Change | Note |
|-------|------|--------|------|
| `StepId` | `string?` | New | The `stepId` of the sequence step that ran. Set for command-path results of `ExecuteSingleStepAsync`. Null for other results. Not in JSON (`[JsonIgnore]`). |

All other fields do not change.

## SequenceExecutionResult.AddStep (changed, internal)

A new optional last parameter, `string? stepId = null`. It sets `StepResult.StepId`. All current calls stay valid.

## Execution-log command node (no shape change)

The attributes `stepId`, `stepLabel`, the `message`, and the `deepLink.stepId` now come from the step that ran. The field set does not change.

Lookup order for the sequence step of a result:

1. If `StepResult.StepId` is set and a step with this ID exists in the flattened step tree (`Body` and `ElseBody`, all levels), use that step.
2. Else use the current lookup: flow step by command reference, then the first sequence step with the same command ID.
