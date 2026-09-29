# Contract: commandOutcome reference to a Break that did not run

**Feature**: 117-break-outcome-untaken-branch | **Issue**: #250

## Scope

- Save: `POST /api/sequences` (with and without `dryRun: true`), `PUT` and `PATCH /api/sequences/{id}`. No change.
- Run: `POST /api/sequences/{sequenceId}/execute`, and each queue run of a sequence. Changed as below.
- OpenAPI: the description of `CommandOutcomeCondition` and of its property `stepRef`. Changed as below.

## Run rule

A `commandOutcome` condition reads the outcome of the step that `stepRef` names.

| Case | Before | After |
|------|--------|-------|
| `stepRef` names a Break that ran and fired | `break` | `break` (no change) |
| `stepRef` names a Break that ran and did not fire | `no_break` | `no_break` (no change) |
| `stepRef` names a Break in an If branch that did not run | fails: `condition-evaluation-error`, "Step '<step>' commandOutcome reference '<ref>' is unavailable" | `no_break` |
| `stepRef` names a Break in a Loop body that ran zero iterations | fails, as above | `no_break` |
| A bare leaf `while`/`repeatUntil` condition names a Break in the body of its own Loop (D-006), before the Break runs for the first time | fails, as above | `no_break` |
| `stepRef` names a Break in a Loop body; the branch ran in one iteration and not in a later iteration | outcome of the last run of the Break | same (no change) |
| `stepRef` names a step that is not a Break and that did not run | fails, as above | fails, as above (no change, FR-005) |
| `stepRef` is empty or names no step in the sequence | fails, as above | fails, as above (no change, FR-004) |

The rule applies in each condition position: a step guard, a Loop guard, an If condition, a Break condition, a `while`/`repeatUntil` condition, and in each child of an `all`/`any`/`none` composite at each depth. `negate` applies after the read, as before.

### Example (issue #250 reproduction)

```json
{
  "stepId": "fail-no-booking",
  "stepType": "Action",
  "requireDispatch": true,
  "condition": {
    "type": "all",
    "children": [
      { "type": "commandOutcome", "stepRef": "brk-empty", "expectedState": "break", "negate": true },
      { "type": "commandOutcome", "stepRef": "brk-wait", "expectedState": "break", "negate": true }
    ]
  }
}
```

When no If branch of the Loop `book` runs, both Break steps read `no_break`. Each child is `true`, the `all` is `true`, and the step runs.

## Save rule

No change (FR-007). The save accepts a `stepRef` to a Break in an If branch in a Loop body when the Break is prior in authored order. The save rejects a `stepRef` that names no step with 400 "references unknown prior step", with `dryRun: true` and with no `dryRun`.

## Trace and log

No new field. A guard that reads the default reports `conditionResult` `true` or `false` in the execute response, as for a recorded outcome (FR-008). In the execution log detail (`GET /api/execution-logs/{id}`), the step entry has `conditionTrace.finalResult` with the same value and no `conditionTrace.failureReason` (before the fix, it was `condition-evaluation-error`). The Break that did not run has no entry in the run result, as before.

## OpenAPI description

`ConditionReferenceScopeSchemaFilter` gets a new constant `BreakNotRunRule`. The text must contain the words "Break", "did not execute", and "no_break", and the phrase "evaluates as no_break". The contract test (tasks T015) checks these words in the `stepRef` description and in the schema-level description of `CommandOutcomeCondition`:

> A stepRef can name a Break step that did not execute in a run: its If branch did not run, or its loop body ran zero iterations. This reference evaluates as no_break. A Break that ran keeps the outcome that it recorded last.

- `StepRefDescription` = the current rules + `BreakNotRunRule`.
- `ConditionDescription` = the current text, with `RuntimeUnavailableRule` changed and `BreakNotRunRule` added.
- New `RuntimeUnavailableRule` (STE, because the text changes): "A reference that is valid at save time can name a step that is not a Break and that did not execute in a run: an If branch that did not run, or a loop body that ran zero iterations. Then the referencing step and the run fail with a 'reference is not available' error. The service does not change this error into a skip, because a guard that the service cannot evaluate is not the same as a guard that is false."