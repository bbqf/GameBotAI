# Contract: Loop Exit Reason & Widened `stepRef` Scope

This feature adds **no new HTTP endpoint**. It extends an existing response field
(`/api/sequences/{id}/execute`'s per-step result) and widens acceptance on an
existing request field: a sequence step's own `condition` (the per-step gate
deciding whether that step executes at all — `SequenceStepContract.Condition`,
distinct from an `If` step's branch-selector `if.condition`), when it is a
`commandOutcome` condition, accepted by `POST /api/sequences` and
`PUT /api/sequences/{id}`. Both changes are additive: every request/response shape
that is valid today remains valid and unchanged in meaning.

Note: an `If` step's own branch-selector condition (`if.condition`) is a separate,
pre-existing code path (`SequenceStepValidationService.ValidateIfCondition`) that
does not perform stepRef existence/ordering checks today and is unchanged by this
feature — only the per-step `condition` gate's resolution (`ValidateStepCondition`)
is widened here.

## Response addition: `Loop` step result `exitReason`

A `Loop` step's entry in the `steps` array of a sequence-execution result gains one
new, optional field:

```json
{
  "commandId": "collect-loop",
  "status": "Succeeded",
  "loopIterations": [ /* unchanged */ ],
  "exitReason": {
    "brokeVia": "cluster-not-found-break",
    "exhaustedMaxIterations": false
  }
}
```

| Field | Type | Meaning |
|-------|------|---------|
| `exitReason.brokeVia` | `string \| null` | `stepId` of the `Break` step that fired, or `null` if none fired. |
| `exitReason.exhaustedMaxIterations` | `boolean` | `true` iff the loop ran its full configured `maxIterations` without any `Break` firing. |

Present only on `Loop` step entries (wherever `loopIterations` is present today).
A consumer that does not read `exitReason` sees no change to any field it already
parses.

## Request acceptance: `commandOutcome.stepRef` scope

**Before**: `stepRef` must name a step that is an immediate sibling of the
condition's own step, within the same top-level `steps` array, `Loop.body`, or
`If.body`/`elseBody` list the condition itself lives in. Any other reference —
including a step nested in a *different* `Loop`/`If` body — is rejected at
`POST /api/sequences` / `PUT /api/sequences/{id}` time with:

```json
{ "message": "...", "errors": ["Step 'gate' commandOutcome references unknown prior step 'nested-break'."] }
```

**After**: `stepRef` may name any `stepId` reachable from the sequence root —
including one nested inside a `Loop`/`If` body other than the condition's own —
provided it is structurally prior in the sequence's authored order. A reference to
a step that is reachable but not prior (e.g. it appears later in the sequence)
continues to be rejected, now with the same message but evaluated against the
whole sequence rather than only the immediate list:

```json
{ "message": "...", "errors": ["Step 'gate' commandOutcome stepRef 'later-step' must reference a prior step."] }
```

## Request acceptance: `commandOutcome.expectedState` vocabulary

**Before**: `"success" | "failed" | "skipped"` (case-insensitive); anything else,
including `"break"`/`"no_break"`, is rejected:

```json
{ "message": "...", "errors": ["Step 'gate' commandOutcome expectedState must be one of success|failed|skipped."] }
```

**After**: `"success" | "failed" | "skipped" | "break" | "no_break"`
(case-insensitive). `"break"`/`"no_break"` are meaningful only when `stepRef`
names a `Break` step; naming a non-`Break` step whose recorded outcome is always
`success`/`failed`/`skipped` and expecting `"break"`/`"no_break"` is accepted by
validation (the vocabulary is not tied to the referenced step's type) but will
never match at runtime — this mirrors today's existing behavior for any
`expectedState` that can never match a given step's actual outcome tokens (not a
new class of authoring foot-gun introduced by this feature).

## Unchanged execution-time failure mode

Referencing a step that is now validation-legal (reachable + prior) but did not
actually execute during a specific run (e.g. it lives in an `If` branch not taken
that run, or a loop body that ran zero iterations) continues to fail the
referencing step and the sequence run at execution time with the existing message
shape:

```json
{ "status": "Failed", "message": "Step 'gate' commandOutcome reference 'other-branch-step' is unavailable" }
```

This is unchanged behavior (see [research.md](../research.md) R-004) — only newly
*reachable* through the API because it was previously blocked at validation for an
unrelated reason (any cross-scope reference was rejected outright).

## Invariants (MUST hold — asserted by tests)

1. Every sequence and condition accepted before this feature is still accepted,
   with unchanged validation errors for genuinely invalid input.
2. Every `Loop` step result's existing fields (`status`, `loopIterations`,
   `message`, etc.) are unchanged; `exitReason` is purely additive.
3. `exitReason.brokeVia` and `exitReason.exhaustedMaxIterations: true` never both
   hold for the same result.
4. A `stepRef` that is reachable but not prior (by authored order) is rejected at
   creation/replace time — widening scope never widens past "prior."
5. A `Break` step's fired/not-fired outcome resolves via `commandOutcome` at any
   validation-legal scope, including its own loop body (previously legal but
   always broken at runtime).

## Out of scope (explicitly unchanged)

- Persisted sequence-definition JSON schema — no field added or removed.
- `Break` step authoring, firing semantics, or execution-log entry shape (feature
  066's outcome vocabulary is reused, not altered).
- Non-`Break` step outcome recording/resolution semantics for
  `success`/`failed`/`skipped` — unchanged, only now resolvable from a wider set of
  referencing scopes.
