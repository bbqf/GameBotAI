# Data Model: Visual Conditional Sequence Logic

## Entity: SequenceFlow

- Purpose: Directed execution graph for a sequence with conditional branching.
- Fields:
  - `sequenceId` (string, required, immutable)
  - `name` (string, required)
  - `version` (integer, required)
  - `entryStepId` (string, required)
  - `steps` (`FlowStep[]`, required)
  - `links` (`BranchLink[]`, required)
- Validation rules:
  - `entryStepId` references an existing step.
  - `stepId` values are unique within the sequence.
  - Graph has no dangling references.

## Entity: FlowStep

- Purpose: One node in the sequence graph.
- Fields:
  - `stepId` (string, required, immutable)
  - `label` (string, required)
  - `stepType` (enum: `action`, `command`, `condition`, `terminal`; required)
  - `payloadRef` (string, optional)
  - `condition` (`ConditionExpression`, required for `condition`)
  - `iterationLimit` (int, optional; required when step participates in a cycle)
- Validation rules:
  - Condition steps must define `condition`.
  - Non-condition steps must not define `condition`.
  - `iterationLimit` > 0 when provided.

## Entity: BranchLink

- Purpose: Directed edge between steps.
- Fields:
  - `linkId` (string, required, immutable)
  - `sourceStepId` (string, required)
  - `targetStepId` (string, required)
  - `branchType` (enum: `next`, `true`, `false`; required)
- Validation rules:
  - Source/target steps exist.
  - Condition steps have exactly one `true` and one `false` outgoing edge.
  - Non-condition executable steps use `next` only.

## Entity: ConditionExpression

- Purpose: Recursive boolean expression evaluated at condition steps.
- Fields:
  - `nodeType` (enum: `and`, `or`, `not`, `operand`; required)
  - `children` (`ConditionExpression[]`, required for logical nodes)
  - `operand` (`ConditionOperand`, required for `operand` node)
- Validation rules:
  - `and`/`or` nodes contain at least two children.
  - `not` node contains exactly one child.
  - Operand node has no child nodes.

## Entity: ConditionOperand

- Purpose: Runtime-evaluable predicate leaf.
- Fields:
  - `operandType` (enum: `command-outcome`, `image-detection`; required)
  - `targetRef` (string, required)
  - `expectedState` (string, required)
  - `threshold` (number, optional; image-detection only)
- Validation rules:
  - `targetRef` resolves to valid command/image target.
  - `expectedState` valid for operand type.
  - `threshold` bounded to configured match-range when present.

## Entity: SequenceSaveConflict

- Purpose: Payload returned for stale-version updates.
- Fields:
  - `sequenceId` (string, required)
  - `currentVersion` (integer, required)
  - `message` (string, required)
- Validation rules:
  - Returned only with HTTP `409` conflict responses.

## Entity: ConditionEvaluationTrace

- Purpose: Debug-level trace for branch-decision reconstruction.
- Fields:
  - `traceId` (string, required)
  - `sequenceId` (string, required)
  - `stepId` (string, required)
  - `evaluatedAtUtc` (datetime, required)
  - `operandResults` (`OperandResult[]`, required)
  - `operatorSteps` (`OperatorResult[]`, required)
  - `finalResult` (bool, required)
  - `selectedBranch` (enum: `true`, `false`, `none`; required)
  - `failureReason` (string, optional)

## Entity: ExecutionStepLogEntry

- Purpose: Step-level runtime log with authoring navigation metadata.
- Fields:
  - `executionId` (string, required)
  - `sequenceId` (string, required)
  - `sequenceLabel` (string, required)
  - `stepId` (string, required)
  - `stepLabel` (string, required)
  - `stepType` (string, required)
  - `status` (enum: `succeeded`, `failed`, `skipped`; required)
  - `deepLink` (`AuthoringDeepLink`, required)
  - `message` (string, required)

## Entity: AuthoringDeepLink

- Purpose: Deep-link metadata for opening authored context.
- Fields:
  - `sequenceId` (string, required)
  - `stepId` (string, required)
  - `sequenceLabel` (string, required)
  - `stepLabel` (string, required)
  - `resolutionStatus` (enum: `resolved`, `step_missing`, `sequence_missing`; required)
  - `fallbackRoute` (string, optional; required when status is missing)
- Validation rules:
  - `resolved` requires target sequence/step exists.
  - `step_missing` resolves to sequence overview route and user-facing missing-step message.

## Runtime State Model

- Step status transition: `pending -> running -> succeeded|failed|skipped`.
- Condition decision transition: `unevaluated -> evaluated(true|false)|evaluation_failed`.
- On `evaluation_failed`, sequence transitions to terminal `failed` immediately.
- For cycles, per-step iteration counters initialize to `0` at sequence-run start.
- Counter increments each revisit of cycle-participating step.
- When counter reaches `iterationLimit`, current step becomes `failed` and sequence stops.

## Relationships

- `SequenceFlow (1) -> (1..n) FlowStep`
- `SequenceFlow (1) -> (1..n) BranchLink`
- `FlowStep (condition) (1) -> (1) ConditionExpression`
- `ConditionExpression (operand) (1) -> (1) ConditionOperand`
- `SequenceFlow update request (1) -> (0..1) SequenceSaveConflict` (on stale save)
- `Condition step execution (1) -> (0..1) ConditionEvaluationTrace`
- `Sequence execution (1) -> (1..n) ExecutionStepLogEntry`
- `ExecutionStepLogEntry (1) -> (1) AuthoringDeepLink`
