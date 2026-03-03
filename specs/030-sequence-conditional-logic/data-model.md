# Data Model: Visual Conditional Sequence Logic

## Entity: SequenceFlow

- Purpose: Graph representation of a sequence that can branch conditionally.
- Fields:
  - `sequenceId` (string, required, immutable)
  - `name` (string, required)
  - `version` (integer, required)
  - `entryStepId` (string, required)
  - `steps` (collection of `FlowStep`, required)
  - `links` (collection of `BranchLink`, required)
- Validation rules:
  - `entryStepId` must reference an existing step.
  - Step IDs must be unique within a sequence.
  - Every non-terminal step must have at least one outgoing link.

## Entity: FlowStep

- Purpose: One node in the sequence graph.
- Fields:
  - `stepId` (string, required, immutable)
  - `label` (string, required)
  - `stepType` (enum: `action`, `command`, `condition`, `terminal`; required)
  - `payloadRef` (string, optional; action/command reference)
  - `condition` (`ConditionExpression`, required for `condition` type)
  - `iterationLimit` (integer, optional; required when step participates in cycle)
- Validation rules:
  - `condition` must exist only for `condition` steps.
  - `iterationLimit` must be positive when present.

## Entity: BranchLink

- Purpose: Directed connection between source and target steps.
- Fields:
  - `linkId` (string, required, immutable)
  - `sourceStepId` (string, required)
  - `targetStepId` (string, required)
  - `branchType` (enum: `next`, `true`, `false`; required)
- Validation rules:
  - `sourceStepId` and `targetStepId` must exist in `steps`.
  - `condition` steps must have exactly one `true` and one `false` outgoing link.
  - Non-condition executable steps must use `next` link type.

## Entity: ConditionExpression

- Purpose: Recursive boolean expression used by condition steps.
- Fields:
  - `nodeType` (enum: `and`, `or`, `not`, `operand`; required)
  - `children` (collection of `ConditionExpression`, required for `and`/`or`, single child for `not`)
  - `operand` (`ConditionOperand`, required for `operand` nodes)
- Validation rules:
  - `and`/`or` must contain at least two children.
  - `not` must contain exactly one child.
  - `operand` node must not include child expressions.

## Entity: ConditionOperand

- Purpose: Leaf predicate evaluated at runtime.
- Fields:
  - `operandType` (enum: `command-outcome`, `image-detection`; required)
  - `targetRef` (string, required)
  - `expectedState` (string, required; type-specific)
  - `threshold` (number, optional; image-detection only)
- Validation rules:
  - `targetRef` must resolve to existing command or reference image.
  - For image detection, threshold must be within configured valid range.
  - Command outcome supports `success` and `failure` expected states.

## Entity: ConditionEvaluationTrace

- Purpose: Debug-level reconstruction of condition evaluation.
- Fields:
  - `traceId` (string, required)
  - `sequenceId` (string, required)
  - `stepId` (string, required)
  - `evaluatedAtUtc` (datetime, required)
  - `operandResults` (collection of `OperandResult`, required)
  - `operatorSteps` (collection of `OperatorResult`, required)
  - `finalResult` (boolean, required)
  - `selectedBranch` (enum: `true`, `false`, `none`; required)
  - `failureReason` (string, optional)

## Entity: ExecutionStepLogEntry

- Purpose: User/operator-visible execution record for each executed step.
- Fields:
  - `executionId` (string, required)
  - `sequenceId` (string, required, immutable)
  - `sequenceLabel` (string, required)
  - `stepId` (string, required, immutable)
  - `stepLabel` (string, required)
  - `stepType` (string, required)
  - `status` (enum: `succeeded`, `failed`, `skipped`; required)
  - `deepLink` (`AuthoringDeepLink`, required)
  - `message` (string, required)

## Entity: AuthoringDeepLink

- Purpose: Navigation metadata to open the authored sequence step directly.
- Fields:
  - `sequenceId` (string, required)
  - `stepId` (string, required)
  - `sequenceLabel` (string, required)
  - `stepLabel` (string, required)

## Relationships

- `SequenceFlow (1) -> (1..n) FlowStep`
- `SequenceFlow (1) -> (1..n) BranchLink`
- `FlowStep (condition) (1) -> (1) ConditionExpression`
- `ConditionExpression (operand) (1) -> (1) ConditionOperand`
- `FlowStep (1) -> (0..n) ConditionEvaluationTrace` (for condition steps)
- `SequenceFlow execution (1) -> (1..n) ExecutionStepLogEntry`
- `ExecutionStepLogEntry (1) -> (1) AuthoringDeepLink`

## Lifecycle / State Transitions

- Step execution status transitions: `pending -> running -> succeeded|failed|skipped`.
- Condition step branch decision: `unevaluated -> evaluated(true|false)|evaluation_failed`.
- If `evaluation_failed`, sequence execution transitions to terminal failed state immediately.
- For cyclic flows, per-step iteration counters increment on each revisit; when a cycle limit is reached, the current step transitions to `failed` and sequence execution transitions to terminal failed state.
