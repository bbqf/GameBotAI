# Phase 1 Data Model: Loop Exit Reason & Nested Step-Outcome References

No new persisted entities and no change to any stored sequence-definition schema.
This feature touches two in-memory shapes: the execution-result type returned per
run, and the internal outcome map the execution engine consults while running.

## `LoopExitReason` (new, `GameBot.Domain.Services.SequenceRunner` / `StepResult`)

| Field | Type | Meaning |
|-------|------|---------|
| `BrokeVia` | `string?` | The `StepId` of the `Break` step that fired during this loop's execution, or `null` if none fired. |
| `ExhaustedMaxIterations` | `bool` | `true` iff the loop ran its full configured `MaxIterations` without any `Break` firing; independent of the `ExitOnMaxIterations` flag and of the loop's overall status. |

Invariants:
- `BrokeVia` and `ExhaustedMaxIterations: true` are mutually exclusive — a loop
  either broke, exhausted, or did neither (finished its body/condition normally);
  it cannot do more than one.
- When the firing `Break` is nested inside an `If` body within the loop body,
  `BrokeVia` is that `Break` step's own `StepId`, never the enclosing `If` step's.
- Present only on a `StepResult` for a `Loop` step (i.e., wherever
  `LoopIterations` is non-null today); absent/irrelevant for non-loop steps.

## `StepResult` (existing, extended)

Adds one new nullable property, `ExitReason: LoopExitReason?`, alongside the
existing `LoopIterations: IReadOnlyList<LoopIterResult>?`. No existing field
changes meaning or is removed. Serializes as-is through
`/api/sequences/{id}/execute`'s existing response — no separate DTO/contract
layer to update (see [research.md](research.md) R-006).

## Runtime outcome map (`stepOutcomes: Dictionary<string, string>`, existing)

No shape change — still a flat `stepId → outcome-token` map built up as steps
execute. Extended in **coverage**, not shape: a `Break` step now writes its own
`brokeVia`/outcome token (`"break"` or `"no_break"`) into this dictionary, which
it previously never did (see [research.md](research.md) R-005). This is what
makes a widened `stepRef` actually resolve to a meaningful value at runtime for a
`Break` step, not just pass validation.

## `CommandOutcomeStepCondition` (existing, validation behavior changed)

No field changes. Two validation-time behavior changes in
`SequenceStepValidationService.ValidateStepCondition`:

1. `StepRef` resolution scope widens from "the condition's immediate sibling
   list" to "every `StepId` reachable from the sequence root, in authored
   (document) order" — see [research.md](research.md) R-003.
2. `ExpectedState`'s allowed-values set gains `"break"` and `"no_break"` alongside
   the existing `"success"`/`"failed"`/`"skipped"`.

## Flattened step index (new, internal validation helper — not a persisted or
returned entity)

A single ordered list, built once per top-level `Validate(...)` call, of every
step reachable from the sequence root: root `steps` in authored order, with each
`Loop`'s `Body` and each `If`'s `Body`/`ElseBody` recursively expanded in place at
its authored position. Used only to answer "is stepId X reachable, and does it
appear before stepId Y in authored order" during validation. Never serialized,
never exposed via any endpoint — a pure internal computation aid for R-003's
resolution rule.
