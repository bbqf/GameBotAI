# Contract: Condition Reference Scope and Loop Exit Reason

**Feature**: 103-condition-model-retest-docs | **Date**: 2026-09-17
**Issue**: [#193](https://github.com/bbqf/GameBotAI/issues/193)

Two behaviour changes and one documentation obligation. Everything else in the
condition model is unchanged and is restated here only where a reader needs it to
tell changed from unchanged.

---

## C-1 — A `commandOutcome` nested inside a composite is reference-checked

**Endpoints**: `POST /api/sequences`, `PUT /api/sequences/{id}`
(and the `dryRun: true` variant, which runs the same validation)

### Before

A reference written inside an `all`/`any`/`none` composite was checked only for
non-emptiness. Both of these were accepted with `201 Created` and then failed at run
time with `commandOutcome reference '...' is not available`:

```json
{
  "stepId": "gate",
  "stepType": "Action",
  "condition": {
    "type": "all",
    "children": [
      { "type": "imageVisible", "imageId": "confirm-btn" },
      { "type": "commandOutcome", "stepRef": "no-such-step", "expectedState": "success" }
    ]
  },
  "primitiveAction": { "type": "Command", "payload": { "commandId": "c1" } }
}
```

### After

The same two rules that already govern a directly-written reference apply to a
reference reached through a composite, at any depth:

- **Resolution** — `stepRef` must name a `stepId` present somewhere in the sequence
  tree (root steps plus every `Loop.Body`, `If.Body`, `If.ElseBody`, recursively).
- **Ordering** — that step must precede the referencing condition in authored order.

`400 Bad Request`:

```json
{
  "message": "...",
  "errors": [
    "Step 'gate' condition at $.children[1]: commandOutcome references unknown prior step 'no-such-step'."
  ]
}
```

and for a forward reference:

```json
{
  "message": "...",
  "errors": [
    "Step 'gate' condition at $.children[1]: commandOutcome stepRef 'later-step' must reference a prior step."
  ]
}
```

The `$`-rooted path is the one the composite validator's existing messages already
use, so nested paths read the same way: `$.children[2].children[0]`.

### Unchanged by C-1

- A reference **naming a nested step** stays accepted — in a composite exactly as it
  already is directly. C-1 widens *who is checked*, not *what passes*.
- `expectedState` keeps exactly its five values: `success`, `failed`, `skipped`,
  `break`, `no_break`. Nothing added.
- A reference that is legal but names a step which did not execute in a given run (an
  untaken `If` branch, a zero-iteration loop body) still fails that run with the
  existing "not available" error. Deliberately not softened into a silent skip.
- A **leaf** reference written directly in a `while`/`repeatUntil` loop condition
  remains unvalidated, per feature 088 decision D-006. A composite in that slot *is*
  validated, so a composite-wrapped reference there gains the checks while a bare one
  does not. Inherited asymmetry, asserted by test and documented.

### Compatibility

A stored sequence whose composite-nested reference is dangling or forward will now be
rejected on its next save. Such a sequence already fails at run time when the
condition is reached, so this converts a run-time failure into a save-time error — the
posture the composite validator's own contract states. Called out as a deliberate,
stated correction (spec A-005).

---

## C-1a — A reference written directly in an `If` condition is reference-checked

**Endpoints**: `POST /api/sequences`, `PUT /api/sequences/{id}`

### Before

An `If` step's condition validated its shape — a missing `imageId`, an unknown
`expectedState` — but never resolved or ordered a `commandOutcome` reference. Both of
these were accepted with `201 Created`:

```json
{
  "stepId": "branch",
  "stepType": "If",
  "if": { "condition": { "type": "commandOutcome", "stepRef": "no-such-step", "expectedState": "success" } },
  "body": [ { "stepId": "then1", "stepType": "Action",
              "primitiveAction": { "type": "Command", "payload": { "commandId": "c1" } } } ]
}
```

### After

The same two rules that govern a step guard's reference apply here:

```json
{
  "message": "...",
  "errors": ["Step 'branch' commandOutcome references unknown prior step 'no-such-step'."]
}
```

and for a forward reference, `Step 'branch' commandOutcome stepRef 'later' must
reference a prior step.` — the per-step validator's existing wording, not a new one,
since the condition is written directly rather than nested and so has no `$` path.

### Also corrected

The same slot's unknown-`expectedState` message named only three of the five accepted
values:

```text
Before: Step 'branch' commandOutcome expectedState must be one of success|failed|skipped.
After:  Step 'branch' commandOutcome expectedState must be one of success|failed|skipped|break|no_break.
```

The validator has accepted all five since feature 081; only the message lagged. An
author was being told `break` was invalid by the code that accepts it.

### Compatibility

As with C-1: a stored sequence whose `If` condition carries a dangling or forward
reference is rejected on its next save, and already fails at run time when that
condition is reached.

---

## C-2 — A loop's exit reason is recorded in the persisted run log

### Before

A loop step's run-log entry carried `iterations`, `status`, `message` and the delay
and identity attributes, but nothing structural about *why* the loop stopped. Since a
queue-driven run leaves only its log behind, the exit reason was unreadable after the
fact for exactly the runs that matter; it was inferable only from the prose in
`message`.

### After

The loop step's run-log entry gains two attributes, mirroring the loop's
`exitReason`:

| Attribute | Type | Meaning |
|---|---|---|
| `brokeVia` | string \| null | `stepId` of the `Break` that fired — its own id, never an enclosing `If`'s |
| `exhaustedMaxIterations` | bool | The loop ran its full configured `maxIterations` with no `Break` firing |

The three exits are therefore distinguishable from the log alone:

| Exit | `brokeVia` | `exhaustedMaxIterations` |
|---|---|---|
| A `Break` fired | `"cluster-not-found-break"` | `false` |
| Ceiling reached, no break | `null` | `true` |
| Finished normally | `null` | `false` |

A `Break` firing in the same iteration that reaches the ceiling reports the break
(row 1). Attributes are absent on non-loop step entries, as `iterations` already is.

### Unchanged by C-2

`POST /api/sequences/{id}/execute` already returns
`exitReason: { brokeVia, exhaustedMaxIterations }` per loop step, because the domain
run result is serialized directly. That response keeps its shape and field names
exactly; C-2 adds a second place to read the same value, and moves nothing.

---

## C-3 — The web authoring UI stops rejecting what the service accepts

Not a wire contract; a client-side validation contract. Two rules are corrected so
the UI's answer matches the service's:

| Authored in the UI | Before | After |
|---|---|---|
| `stepRef` naming a step nested in a loop body or `if` branch | rejected: "references unknown prior step" | accepted |
| `expectedState: "break"` / `"no_break"` | rejected: "must be one of success\|failed\|skipped" | accepted |
| `stepRef` naming a nonexistent step | rejected | rejected (unchanged) |
| `stepRef` naming a later step | rejected | rejected, now by authored order across the tree |

**Not covered**: authoring a composite condition in the UI. The UI's condition model
has no `all`/`any`/`none` form, so composites cannot be written there at all. That is
absent capability rather than a rule contradicting the service; it is recorded as a
remaining limitation (spec FR-016a), not built here.

---

## C-4 — The published interface description states the rules

The OpenAPI document must state, without a reader having to try a request:

1. For a step condition's `stepRef`: the resolution scope (any step reachable from the
   sequence root, nested bodies included) and the ordering constraint (authored order),
   plus the inherited exception for a leaf in a `while`/`repeatUntil` condition.
2. Every accepted `expectedState` value, including `break` and `no_break` and what
   each means.
3. A loop step's `exitReason` shape, the three exits it distinguishes, the mutual
   exclusivity of `brokeVia` and `exhaustedMaxIterations`, and the simultaneous-case
   rule.

Descriptions only — no schema field is renamed, removed, or made required by this
feature.
