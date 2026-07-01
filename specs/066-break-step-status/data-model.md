# Phase 1 Data Model: Break Step Success/Failure Execution Statuses

This feature adds **no persisted schema**. It defines a small outcome vocabulary and a
status-mapping that flow through existing in-memory result types and the execution-log
projection. Below are the conceptual entities, the values that change, and the mapping.

## Entities

### Break step (existing — `SequenceStep`, `StepType = Break`)

A step inside a loop body that optionally carries a `BreakCondition`.

- Fires when unconditional (`BreakCondition == null`) or when its condition evaluates true.
- Does not fire when the condition evaluates false or cannot be evaluated.
- No structural change to the entity; only its recorded **outcome** changes.

### Loop-level break condition (existing — while-block `breakOn`)

A condition attached to a while-style block, evaluated at `breakOn-start` and `breakOn-mid`.

- Fires (ends the block) when it evaluates true → block `Status = "true"`.
- Does not fire when false; an evaluation error is now treated as false (guarded).
- No structural change; only its error handling and the success/no-break interpretation change.

### Break step outcome (new vocabulary over existing fields)

Recorded per break evaluation on the existing `StepResult` fields (Domain) and carried through to
the execution-log projection. **No new field is introduced.**

| Concept | `StepResult.Status` | `StepResult.ActionOutcome` | `StepResult.ConditionResult` | `StepResult.Message` |
|---------|---------------------|----------------------------|------------------------------|----------------------|
| Fired (unconditional) | `Succeeded` | `break` | `true` | "Unconditional break triggered" |
| Fired (condition true) | `Succeeded` | `break` | `true` | "Break triggered: {detail} evaluated to true" |
| No break (condition false) | `Succeeded` | `no_break` | `false` | "No break: {detail} evaluated to false" |
| No break (eval error) | `Succeeded` | `no_break` | `error` | "No break: {detail} could not be evaluated ({error})" |

Notes:
- `Status` is `Succeeded` in every case (a break is never a real failure), which keeps the
  enclosing loop/sequence/run out of `Failed` (FR-004/FR-005).
- The distinct neutral appearance is produced entirely by the `no_break` **outcome token**, not
  by `Status`.
- `ConditionResult` / `Message` retain the evaluation detail for both outcomes (FR-007).

## Outcome token → node status mapping

`ExecutionLogService.MapStepStatus(step.Outcome)` gains two cases:

| Outcome token | Node status (tree/grid) | Rendering |
|---------------|-------------------------|-----------|
| `break` | `success` | existing success styling (fixes today's fall-through to `failure`) |
| `no_break` | `no_break` (NEW) | distinct **neutral** "No break" badge — not red `failure`, not `skipped` |

web-ui type change: `ExecutionTreeNodeStatus = ExecutionStatus | 'skipped' | 'not_executed' | 'no_break'`.

## State transitions (per break evaluation, one loop iteration)

```text
                     ┌─ unconditional ──────────────► FIRED  (break, success) ─► end iteration/loop
break step reached ──┤
                     └─ conditional ─┬─ eval true ──► FIRED  (break, success) ─► end iteration/loop
                                     ├─ eval false ─► NO BREAK (no_break) ─────► continue (next body step / iteration)
                                     └─ eval error ─► NO BREAK (no_break) ─────► continue (next body step / iteration)   ← reversed from today (was Fail + stop)

loop-level breakOn ──┬─ eval true ──► FIRED  (block Status "true", success) ─► end block
                     ├─ eval false ─► NO BREAK ─────────────────────────────► continue (no discrete record)
                     └─ eval error ─► NO BREAK (guarded, treated as false) ──► continue   ← previously threw / failed run
```

## Non-influence invariants (validation rules the tests assert)

- **INV-1 (FR-004/FR-005)**: A `no_break` outcome never sets `SequenceExecutionResult.Status` to
  `Failed` and never marks the enclosing loop or any ancestor as failed. Run `FinalStatus` stays
  `Succeeded` when the only non-success break outcomes are `no_break`.
- **INV-2 (FR-006)**: A `no_break` outcome does not early-stop, skip subsequent body steps, or
  skip remaining iterations — execution flow is identical to today's condition-false path.
- **INV-3 (FR-008)**: A `no_break` outcome maps to node status `no_break` (never `failure`) and is
  excluded from failure counts/alerts.
- **INV-4 (FR-003)**: A non-firing conditional break is never recorded as `Skipped`.
- **INV-5 (FR-002a / FR-010)**: A break-condition evaluation error (loop-body break step or
  loop-level `breakOn`) is treated as `no_break` and never throws out to fail the run.
