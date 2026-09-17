# Phase 1 Data Model: Condition-Model Ceiling Retest

**Feature**: 103-condition-model-retest-docs | **Date**: 2026-09-17

No new entity is introduced. Spec FR-013 forbids new condition variants and new
outcome states, so this document records the **existing** shapes, the validation
rules that apply to each, and the two places a rule or a field is being added.

Legend: **unchanged** · **rule added** · **field added**

---

## SequenceStepCondition (unchanged)

Abstract base, JSON-polymorphic on `type`.
`src/GameBot.Domain/Commands/SequenceStepCondition.cs`

| Field | Type | Notes |
|---|---|---|
| `type` | discriminator | `imageVisible` \| `commandOutcome` \| `all` \| `any` \| `none` |
| `negate` | bool | Applies to the leaf's or the composite's combined result |

### Leaf: `imageVisible` (unchanged)

| Field | Type | Rule |
|---|---|---|
| `imageId` | string | Required, non-empty |
| `minSimilarity` | double? | When present, within `0..1` |

### Leaf: `commandOutcome`

| Field | Type | Rule |
|---|---|---|
| `stepRef` | string | Required, non-empty. **Must resolve** to a step reachable from the sequence root, and **must be prior** in authored order. Enforced today for a directly-written condition; **rule added** for a condition reached through a composite (FR-008/FR-009). |
| `expectedState` | string | One of `success` \| `failed` \| `skipped` \| `break` \| `no_break`. **Unchanged** — FR-013 forbids additions. |

### Composite: `all` / `any` / `none` (unchanged shape)

| Field | Type | Rule |
|---|---|---|
| `children` | condition[] | 1..16 entries (`MaxChildren`); empty rejected at save time |
| — | — | Nesting at most 4 condition levels (`MaxDepth`), a leaf alone being depth 1 |

Truth rules: `all` true iff every child true; `any` true iff some child true;
`none` true iff no child true. Each short-circuits on the first settling child.

---

## Step reference resolution (rule added for composite children)

Two independent rules, both save-time:

| Rule | Meaning | Rejection |
|---|---|---|
| Resolution | `stepRef` names a `stepId` present somewhere in the sequence tree — root steps plus every `Loop.Body` and `If.Body`/`ElseBody`, recursively | "references unknown prior step" |
| Ordering | That step precedes the referencing condition in authored (document) order | "must reference a prior step" |

**Applied where:**

| Condition position | Resolution + ordering today | After this feature |
|---|---|---|
| Directly on a step guard | Yes | Yes (unchanged) |
| Directly on an `if` condition | Yes | Yes (unchanged) |
| Directly on a break condition | Yes | Yes (unchanged) |
| Directly in a `while`/`repeatUntil` condition | **No** — deliberate, decision D-006 of feature 088 | **No** (asserted as intended, FR-003a) |
| Inside a composite, any slot the composite is validated in | **No** | **Yes** (FR-008, FR-009) |

The asymmetry in the last two rows is inherited from D-006, not introduced here:
composites are validated in the loop-condition slots while leaves there are not, so a
composite-wrapped reference gains the checks and a bare one does not. Documented under
FR-016 rather than silently left for a reader to discover.

**Message shape** (FR-010) — reuses the composite validator's `$`-rooted path idiom,
so one format covers every composite rule:

```text
Step '<stepLabel>' condition at $.children[0]: commandOutcome references unknown prior step '<stepRef>'.
Step '<stepLabel>' condition at $.children[2].children[0]: commandOutcome stepRef '<stepRef>' must reference a prior step.
```

---

## LoopExitReason (unchanged shape, new place to read it)

`src/GameBot.Domain/Services/SequenceRunner.cs` — carried on a `Loop` step's
`StepResult.ExitReason`.

| Field | Type | Meaning |
|---|---|---|
| `brokeVia` | string? | `StepId` of the `Break` that fired — the `Break`'s own id, never an enclosing `If`'s. Null if none fired. |
| `exhaustedMaxIterations` | bool | The loop ran its full configured `MaxIterations` with no `Break` firing. Independent of `ExitOnMaxIterations`. |

**Three states, mutually exclusive** (pinned by tests per FR-005):

| Exit | `brokeVia` | `exhaustedMaxIterations` |
|---|---|---|
| A `Break` fired | the `Break`'s `stepId` | `false` |
| Ceiling reached, no break | `null` | `true` |
| Body/condition finished normally | `null` | `false` |

**Simultaneous case** (FR-012a): a `Break` firing in the iteration that also reaches
the ceiling reports the break — row 1, not row 2. Existing behaviour; pinned, not
changed.

**Readable from:**

| Surface | Today | After |
|---|---|---|
| `POST /api/sequences/{id}/execute` response, per loop step | Yes, as `exitReason: { brokeVia, exhaustedMaxIterations }` (the domain result is serialized directly) | Yes (unchanged — FR-011a) |
| Persisted run log, loop step entry | **No** — dropped | **Yes** (FR-011) |
| Run-tree node view | No | No (out of scope) |

---

## Loop step run-log entry (fields added)

`ExecutionDetailItem` attributes for a loop step, an open
`Dictionary<string, object?>`.
`src/GameBot.Service/Services/SequenceExecution/SequenceExecutionService.cs`

| Attribute | Status |
|---|---|
| `stepOrder`, `stepType`, `status`, `actionOutcome` | unchanged |
| `appliedDelayMs`, `stepDelayMs`, `interStepDelayMs` | unchanged |
| `iterations` | unchanged |
| `message`, `sequenceId`, `sequenceLabel`, `stepId`, `stepLabel` | unchanged |
| `brokeVia` | **field added** — string or null, mirroring `LoopExitReason.BrokeVia` |
| `exhaustedMaxIterations` | **field added** — bool, mirroring `LoopExitReason.ExhaustedMaxIterations` |

Purely additive keys, matching how `cancellationReason` / `timeLimitMs` were added by
an earlier feature. Absent on non-loop steps, as `iterations` already is.

---

## Web-UI condition model (rules corrected, types corrected)

`src/web-ui/src/types/sequenceFlow.ts`, `src/web-ui/src/lib/validation.ts`

| Element | Today | After |
|---|---|---|
| `CommandOutcomeStepCondition.expectedState` type | `'success' \| 'failed' \| 'skipped'` | plus `'break' \| 'no_break'` (FR-012) |
| Client-side `expectedState` allow-list | same three | all five (FR-012) |
| Client-side `stepRef` resolution | top-level `steps` array only | whole tree, descending `body` / `elseBody` (FR-012) |
| Client-side ordering check | index within the top-level array | authored order across the tree (FR-012) |
| Conditions on nested steps | not validated | validated on the same walk |
| `PerStepConditionType` | `'imageVisible' \| 'commandOutcome'` | **unchanged** — composites stay absent, recorded under FR-016a |

---

## Retest conclusion (documentation artifact, not code)

Recorded in `docs/architecture.md` per FR-016. Pairs each claim from the issue with
its status and the tests that establish it — the nine rows of the research
consolidation table, reduced to prose: both halves delivered in the API and domain;
three gaps found and closed here (composite-nested reference checks, run-log exit
reason, two stale web-UI rules); one absent capability recorded (composite authoring
in the web UI); one inherited boundary stated (D-006 loop-condition leaves).
