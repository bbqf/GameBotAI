# Data Model: A Break outcome when its If branch did not run

**Feature**: 117-break-outcome-untaken-branch | **Date**: 2026-09-29

This feature adds no persisted field, no API field, and no new `expectedState` value (FR-006). It changes one in-memory structure of a run and adds one domain helper.

## Run outcome map (in memory, one for each run)

`linearStepOutcomes` in `SequenceRunner.ExecuteAsync`: `Dictionary<string, string>` with a case-insensitive key.

| Field | Type | Description |
|-------|------|-------------|
| key | string | The `StepId` of a step (or the `CommandId`, or a generated key such as `loop@<order>`, when `StepId` is empty). |
| value | string | The outcome: `success`, `failed`, `skipped`, `not_executed`, `skipped_dry_run`, `break`, or `no_break`. |

### State transitions for a Break key (new)

| Moment | Value | Source |
|--------|-------|--------|
| Run start, before step 1 | `no_break` | New: default from the definition (`BreakStepIndex`). Only for an id that names Break steps only. |
| The Break runs and fires | `break` | Current: `ExecuteLoopBodyAsync`. |
| The Break runs and does not fire, or its condition cannot be evaluated | `no_break` | Current: `ExecuteLoopBodyAsync` (feature 066 FR-002a). |
| A later iteration in which the Break does not run | no change | The last recorded value stays. |

A key for a step that is not a Break has no default. It gets a value only when the step runs, as before (FR-005).

## BreakStepIndex (new static helper, `GameBot.Domain.Services`)

| Member | Signature | Description |
|--------|-----------|-------------|
| `CollectBreakStepIds` | `IReadOnlySet<string> CollectBreakStepIds(IReadOnlyList<SequenceStep> steps)` | Walks the step tree in preorder: each root step, each `Loop.Body`, each `If.Body` and `If.ElseBody`, recursively. Returns the `StepId` of each Break step (case-insensitive set). Skips a Break with an empty `StepId`. Skips an id that also names a step of a different type. |
| `SeedNoBreakOutcomes` | `void SeedNoBreakOutcomes(IDictionary<string, string> outcomes, IReadOnlyList<SequenceStep> steps)` | For each id from `CollectBreakStepIds`, adds `BreakOutcomes.NoBreak` with `TryAdd`. It does not replace a value that is already in the map. |

### Validation rules

- A null `steps` argument or a null `outcomes` argument throws `ArgumentNullException`.
- A null `Body` or `ElseBody` is an empty list.
- The walk has no depth limit. The save allows a depth of 3 at most (Loop, If, Break), and the runner does not validate.

## commandOutcome condition (no change to the shape)

| Field | Type | Rule |
|-------|------|------|
| `stepRef` | string | Save rules do not change (FR-007). Run rule: a Break with no recorded outcome reads as `no_break` (FR-001). An empty or unknown `stepRef` still fails with `condition-evaluation-error` (FR-004). |
| `expectedState` | string | `success`, `failed`, `skipped`, `break`, `no_break` (FR-006, no change). |
| `negate` | bool | No change. |
