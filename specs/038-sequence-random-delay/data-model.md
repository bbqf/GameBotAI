# Data Model: Randomized Sequence Step Delays

## Entity: CommandSequence

- Purpose: Persisted sequence aggregate executed by SequenceRunner.
- New/changed fields:
  - `id` (string, required)
  - `name` (string, required)
  - `version` (integer, required)
  - `steps` (`SequenceStep[]`, existing)
  - `flowSteps` (`FlowStep[]`, existing)
  - `flowLinks` (`BranchLink[]`, existing)
  - `interStepDelayRangeMs` (`SequenceInterStepDelayRangeMs`, optional)
- Validation rules:
  - If `interStepDelayRangeMs` is omitted, runtime default range `100..300` ms applies.
  - If provided, `min` and `max` must be integer milliseconds with `min >= 0` and `min <= max`.

## Entity: SequenceInterStepDelayRangeMs

- Purpose: Sequence-level random delay configuration used between executed steps.
- Fields:
  - `min` (integer, required when object present)
  - `max` (integer, required when object present)
- Validation rules:
  - `min` must be `>= 0`.
  - `max` must be integer.
  - Inclusive sampling boundaries apply: `min <= sampled <= max`.

## Entity: SequenceStep

- Purpose: Existing executable unit in linear, loop, and flow-supported sequence logic.
- Existing timing fields retained:
  - `delayMs` (optional)
  - `delayRangeMs` (optional)
- Relationship to new behavior:
  - Step-level timing remains valid and is not removed.
  - Sequence-level inter-step delay is applied between consecutive executed steps in addition to existing step behavior.

## Entity: SequenceExecutionRun

- Purpose: Runtime execution instance used for status and step results.
- Relevant derived runtime fields:
  - `sequenceId` (string)
  - `steps[]` (ordered execution results)
  - `status` (succeeded/failed)
  - `appliedInterStepDelayMs` (derived per transition; may be represented in logs/step metadata if exposed)
- State transitions:
  - `pending -> running -> succeeded|failed|cancelled`
  - For each transition from executed step `n` to executed step `n+1`, compute and apply one sampled inter-step delay.
  - No inter-step delay after terminal/final executed step.

## Relationships

- `CommandSequence (1) -> (0..1) SequenceInterStepDelayRangeMs`
- `CommandSequence (1) -> (0..n) SequenceStep`
- `CommandSequence (1) -> (0..n) FlowStep`
- `SequenceExecutionRun (1) -> (0..n) executed step transitions`
- `Each executed step transition (1) -> (1) sampled inter-step delay value`

## Backward Compatibility

- Existing persisted sequence JSON without `interStepDelayRangeMs` remains valid.
- Read path defaults missing range to `100..300` ms at execution time.
- Write path may emit explicit range once configured per sequence.
