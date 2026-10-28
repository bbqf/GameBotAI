# Data Model: commandStepOutcome condition

## CommandStepOutcomeStepCondition (domain class, JSON type `commandStepOutcome`)

| Field | Type | Rule |
|-------|------|------|
| `type` | string | Discriminator, value `commandStepOutcome`. |
| `stepRef` | string | Required. The id of a prior sequence step that runs a command. |
| `commandStepOrder` | int | Required. At least 1 (the API schema shows `minimum: 1`). The `order` of a top-level step in that command. |
| `expectedState` | string | Required. One of `executed`, `skipped_detection_failed`, `completed_timeout`, `image_detected`. Match is not case-sensitive. |
| `negate` | bool | Optional, default false. Inverts the result. |

Saved JSON example:

```json
{ "type": "commandStepOutcome", "stepRef": "send-1", "commandStepOrder": 12, "expectedState": "executed", "negate": false }
```

## CommandStepOutcomeStates (static class, Domain)

- Constants: `Executed`, `SkippedDetectionFailed`, `CompletedTimeout`, `ImageDetected`.
- `All`, `AllText` (`executed|skipped_detection_failed|completed_timeout|image_detected`), `IsAllowed(string?)`.
- The reason code `detection_failed_after_3_retries` is not a state and is not allowed.

## CommandDispatchOutcome (change)

New optional field `StepStates`: `IReadOnlyDictionary<int, string>?`. Key: step order of the top-level command. Value: one of the four states. Default null. It holds only readable states. Any other status (for example `failed`, `not_accepted`, `skipped_invalid_config`) is left out, so the condition reads "no outcome recorded".

## PrimitiveTapStepOutcome (change)

New optional field `Depth` (int, default 0). `CommandExecutor` sets it to the nesting depth of the step: 0 for a step of the command that the sequence step runs, and 1 or more for a step of a nested command. No other field and no behavior changes. `SequenceExecutionService` keeps only entries with depth 0 (FR-009a).

## Run-scoped record (SequenceRunner)

`commandStepStates`: `Dictionary<string, IReadOnlyDictionary<int, string>>` (key: sequence step key, case-insensitive).

State transitions:

- A sequence step runs its command: entry is set to `StepStates`. This replaces any older entry, even if the new run has no readable states (the entry is then removed).
- A sequence step is skipped, fails, or does not run in a dry run: no change. The older entry stays until the same step runs a command again (same rule as `commandOutcome`).
- The map lives for one run only. It is not saved.

## Evaluation result

| Case | Result before `negate` | Log text |
|------|-----------------------|----------|
| State found, equals expected | true | `actual=<state>` |
| State found, other state | false | `actual=<state>` |
| No entry for `stepRef`, or no state for the order | false | `actual=none (no outcome recorded)` |

The run never fails because of a missing outcome.

## Contract class (Service)

`CommandStepOutcomeConditionContract` with the same four fields. It maps to and from the domain class in `SequencesEndpoints`.
