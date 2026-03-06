# Data Model: Conditional Sequence Steps (Minimal)

## Entity: Sequence

- Purpose: Ordered container of executable steps for command flow.
- Fields:
  - `sequenceId` (string, required, immutable)
  - `name` (string, required)
  - `version` (integer, required)
  - `steps` (`SequenceStep[]`, required)
  - `createdAtUtc` (datetime, required)
  - `updatedAtUtc` (datetime, required)
- Validation rules:
  - `steps` must have at least one item.
  - Step order is persisted explicitly and executed in array order.
  - All persisted steps must use explicit `stepType` shape.

## Entity: SequenceStep (Discriminated Union)

- Purpose: One ordered executable unit in a sequence.
- Shared fields:
  - `stepId` (string, required, immutable)
  - `stepType` (enum: `action`, `conditional`; required)
  - `label` (string, optional)

### Variant: ActionStep (`stepType=action`)

- Fields:
  - `action` (`ActionPayload`, required)
- Validation rules:
  - `action.type` must be a supported action type from the existing action execution infrastructure.
  - `action.parameters` must satisfy type-specific validation rules.

### Variant: ConditionalStep (`stepType=conditional`)

- Fields:
  - `condition` (`ImageVisibleCondition`, required)
  - `action` (`ActionPayload`, required)
- Validation rules:
  - Exactly one condition and one action are required.
  - Unknown condition types rejected.
  - `action` follows the same `ActionPayload` validation as `ActionStep`.

## Entity: ActionPayload

- Purpose: Generic action payload used by both unconditional and conditional steps.
- Fields:
  - `type` (string, required)
  - `parameters` (object, required)
- Validation rules:
  - `type` must be in the supported action type set defined by existing action execution infrastructure.
  - `parameters` must match type-specific schema/validation rules.

## Entity: ImageVisibleCondition

- Purpose: Predicate evaluating whether an image target is currently visible.
- Fields:
  - `type` (enum: `imageVisible`, required)
  - `imageId` (string, required)
  - `minSimilarity` (number, optional)
- Validation rules:
  - `imageId` must resolve to an existing image at save/validation time.
  - `minSimilarity`, when present, must be within configured detection threshold bounds.
  - If `minSimilarity` absent, evaluator uses configured default threshold.

## Entity: StepExecutionRecord

- Purpose: Runtime log/result details for each executed step.
- Fields:
  - `sequenceId` (string, required)
  - `stepId` (string, required)
  - `stepType` (string, required)
  - `conditionSummary` (string, optional)
  - `conditionResult` (enum: `true`, `false`, `error`, optional)
  - `actionOutcome` (enum: `executed`, `skipped`, `failed`, required)
  - `message` (string, optional)
  - `timestampUtc` (datetime, required)
- Validation rules:
  - `conditionResult` required for `conditional` steps.
  - `actionOutcome=skipped` valid when `conditionResult=false`.
  - Deterministic outcome comparisons use ordered pairs of (`conditionResult`, `actionOutcome`) per step and ignore run metadata fields (timestamps/IDs).

## Runtime State Transitions

- Conditional step evaluation:
  - `pending -> evaluating -> evaluated-true|evaluated-false|evaluation-error`
- Step execution:
  - `pending -> running -> executed|skipped|failed`
- Sequence execution:
  - `running -> completed` when all steps finish without failure.
  - `running -> failed` when a conditional evaluation errors or action fails.

## Relationships

- `Sequence (1) -> (1..n) SequenceStep`
- `ConditionalStep (1) -> (1) ImageVisibleCondition`
- `Sequence execution (1) -> (1..n) StepExecutionRecord`
