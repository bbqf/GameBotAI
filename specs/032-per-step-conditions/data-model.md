# Data Model: Per-Step Optional Conditions

## Entity: Sequence

- Purpose: Ordered container of executable sequence steps.
- Fields:
  - `sequenceId` (string, required, immutable)
  - `name` (string, required)
  - `version` (integer, required)
  - `steps` (`SequenceStep[]`, required, ordered)
  - `createdAtUtc` (datetime, required)
  - `updatedAtUtc` (datetime, required)
- Validation rules:
  - At least one step is required.
  - Step order is explicit and preserved.
  - Entry-step and branch-link graph fields are not part of this feature model.

## Entity: SequenceStep

- Purpose: One linear execution unit in a sequence.
- Fields:
  - `stepId` (string, required, immutable within sequence)
  - `label` (string, optional)
  - `action` (`ActionPayload`, required)
  - `condition` (`StepCondition`, optional)
- Validation rules:
  - `action` is always required.
  - `condition` is optional, but when present must be valid by type-specific rules.
  - A step contains at most one condition.

## Entity: StepCondition (Discriminated Union)

- Purpose: Predicate that determines whether a step executes or is skipped.
- Variants:

### Variant: ImageVisibleCondition (`type=imageVisible`)
- Fields:
  - `type` (enum, required)
  - `imageId` (string, required)
  - `minSimilarity` (number, optional)
- Validation rules:
  - `imageId` must resolve to existing image metadata at save/validate time.
  - `minSimilarity` must be within configured threshold bounds when provided.

### Variant: CommandOutcomeCondition (`type=commandOutcome`)
- Fields:
  - `type` (enum, required)
  - `stepRef` (string, required) - referenced prior step id
  - `expectedState` (enum: `success`, `failed`, `skipped`, required)
- Validation rules:
  - `stepRef` must reference a step that appears earlier in sequence order.
  - Referencing current or later steps is invalid.
  - `expectedState` outside allowed enum is invalid.

## Entity: ActionPayload

- Purpose: Generic action contract executed when step runs.
- Fields:
  - `type` (string, required)
  - `parameters` (object, required)
- Validation rules:
  - `type` must be one of currently supported action types.
  - `parameters` must satisfy action-type validation rules.

## Entity: StepExecutionOutcome

- Purpose: Runtime result for one step execution attempt.
- Fields:
  - `sequenceId` (string, required)
  - `stepId` (string, required)
  - `conditionType` (string, nullable)
  - `conditionResult` (enum: `true`, `false`, `error`, nullable)
  - `actionOutcome` (enum: `executed`, `skipped`, `failed`, required)
  - `message` (string, nullable)
  - `timestampUtc` (datetime, required)
- Validation rules:
  - `conditionResult` is populated when step has condition.
  - `actionOutcome=skipped` is valid when `conditionResult=false`.
  - `actionOutcome=failed` required for evaluation or execution failures.

## Runtime State Transitions

- Step lifecycle:
  - `pending -> evaluating-condition` (if condition exists)
  - `evaluating-condition -> skipped` when condition false
  - `evaluating-condition -> running-action` when condition true
  - `evaluating-condition -> failed` when evaluation errors
  - `running-action -> executed|failed`
- Sequence lifecycle:
  - `running -> completed` when all steps finish without failure
  - `running -> failed` when any conditioned evaluation errors or action execution fails

## Relationships

- `Sequence (1) -> (1..n) SequenceStep`
- `SequenceStep (0..1) -> (1) StepCondition`
- `Sequence execution (1) -> (1..n) StepExecutionOutcome`
