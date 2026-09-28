# Data Model: A condition on a Loop step is a guard that the runtime obeys

No stored format changes. The changes are in the use of existing fields and in two run-result fields.

## SequenceStep (domain, `GameBot.Domain.Commands.SequenceStep`)

| Field | Change |
|-------|--------|
| `Condition` (`SequenceStepCondition?`) | Now kept and used on a `Loop` step. Before, the save mapping dropped it for a `Loop` step. Same types as on an `Action` step: `imageVisible`, `commandOutcome`, `lastRun`, composite (`all`, `any`, `none`). |

Validation rules for `Condition` on a `Loop` step (same as on an `Action` step):

- `commandOutcome`: `stepRef` is not empty, names a step of the sequence, and that step is before the `Loop` step in document order. A body step of the same loop is not before it. `expectedState` is one of `success`, `failed`, `skipped`, `break`, `no_break`.
- `imageVisible`: `imageId` is not empty, the image exists, and `minSimilarity` is in 0..1 when set.
- composite: the rules of `CompositeConditionValidator` (children count, depth, the leaf rules above for each child).

## StepResult (run result, `SequenceExecutionResult.Steps`)

| Field | Change |
|-------|--------|
| `ConditionType` | Now also set on a `Loop` entry when the `Loop` step has a guard. |
| `ConditionResult` | Now also set on a `Loop` entry: `true`, `false` or `error`. |
| `Status` | A `Loop` entry with a false guard has `Skipped`. |
| `LoopIterations` | An empty list for a skipped `Loop` entry (not null, so the entry stays a loop entry). |
| `Message` | For a skipped `Loop` entry: `Loop '<stepKey>' skipped: its condition is false.`, and, when a composite decides, the text ` condition <path> (<description>) settled the guard`. |

## Step outcome state (`stepOutcomes`, read by `commandOutcome`)

| Case | State |
|------|-------|
| `Loop` guard false | `skipped` |
| `Loop` guard error | `failed` |
| `Loop` guard true | as before (`success` or `failed`) |

## Execution log entry for a Loop step (`details[].attributes`, `stepType: "loop"`)

| Attribute | Change |
|-----------|--------|
| `conditionType` | New. The guard type, or `null` when the `Loop` step has no guard. |
| `conditionResult` | New. `true`, `false`, `error`, or `null` when the `Loop` step has no guard. |
| `status` | `Skipped` for a false guard. |
| `iterations` | `0` for a false guard. |
