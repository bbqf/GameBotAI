# Contract: commandStepOutcome condition

The condition is a new value of the `type` discriminator of the step condition. It is valid in each place where `commandOutcome` is valid: a step `condition`, a child of `all`/`any`/`none`, an `if` condition, a loop condition, and a break condition.

## Request and response shape

```json
{
  "type": "commandStepOutcome",
  "stepRef": "send-1",
  "commandStepOrder": 12,
  "expectedState": "executed",
  "negate": false
}
```

OpenAPI: the schema lists `commandStepOutcome` in the condition `type` enum. `expectedState` has the enum of the four states. `commandStepOrder` has `minimum: 1`.

## Validation errors (HTTP 400; the order row applies at save and save dry run only)

| Case | Message (same style as `commandOutcome`) |
|------|-----------------------------------------|
| `stepRef` empty | `Step '<label>' commandStepOutcome condition requires stepRef.` |
| `stepRef` unknown | `Step '<label>' commandStepOutcome references unknown prior step '<stepRef>'.` |
| `stepRef` not before the condition | `Step '<label>' commandStepOutcome stepRef '<stepRef>' must reference a prior step.` |
| `stepRef` is not a command step | `Step '<label>' commandStepOutcome stepRef '<stepRef>' must reference a step that runs a command.` |
| `commandStepOrder` missing or below 1 | `Step '<label>' commandStepOutcome commandStepOrder must be 1 or more.` |
| Order not in the command | `Step '<label>' commandStepOutcome: command '<commandId>' has no step with order <n>.` |
| `expectedState` empty or not allowed | `Step '<label>' commandStepOutcome expectedState must be one of executed\|skipped_detection_failed\|completed_timeout\|image_detected.` |

The order check against the command runs only at save and in the save dry run. It is skipped when the command cannot be loaded. The run dry run and the run check only the `stepRef` rules. A missing order at run time gives "no outcome recorded".

A new command run of a sequence step replaces the older recorded outcome. A step that is skipped, fails, or does not run in a dry run keeps the older entry.

## Run behavior

| Situation | Result |
|-----------|--------|
| Step of the command ended in the expected state | true |
| Step ended in another state | false |
| No outcome recorded (step not reached, sequence step skipped, dry run, other status) | false before `negate`; the run goes on |

Run log: one line for each evaluation, with `stepRef`, order, expected state, and the state read or `none`.

## Unchanged

`commandOutcome` condition, command execution, the force-execute result, and all saved sequences stay as they are.
