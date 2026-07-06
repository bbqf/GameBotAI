# Data Model: If-Then-Else Conditions in Sequences (067)

## Domain (GameBot.Domain)

### SequenceStepType (extended enum)

```
Command | Action | Conditional | Loop | Break | If   ← new member
```

### IfConfig (new, `src/GameBot.Domain/Commands/IfConfig.cs`)

| Field | Type | Rules |
|-------|------|-------|
| Condition | `SequenceStepCondition` (required) | Same polymorphic set as `WhileLoopConfig.Condition`: `imageVisible` (imageId required, minSimilarity 0..1 optional, negate) or `commandOutcome` (stepRef required, expectedState ∈ success/failed/skipped, negate) |

No `MaxIterations` — an if block executes its branch at most once.

### SequenceStep (extended)

| Field | Type | Used when | Rules |
|-------|------|-----------|-------|
| If | `IfConfig?` | `StepType == If` | Required for if steps (validation error when null) |
| Body | `IReadOnlyList<SequenceStep>` (existing) | `StepType == If` → **then branch**; `StepType == Loop` → loop body | Empty list valid (no-op branch) |
| ElseBody | `IReadOnlyList<SequenceStep>?` (new) | `StepType == If` → **else branch** | `null` = else absent; empty list = else present but empty; both execute as no-op. Must be null/ignored for non-if steps |

### Branch content rules (validation)

Within `Body` (then) and `ElseBody` (else) of an if step — identical to loop-body rules except where noted:

- Step ids: non-empty, unique within their branch.
- `Loop` steps: **rejected** (flat rule, mirrors loop-in-loop rejection).
- `If` steps: **rejected** (no if-in-if).
- `Break` steps: allowed **only when the if block itself sits inside a loop body**; break condition `imageVisible` requires imageId.
- Action steps: action payload required; `commandOutcome` conditions may only reference prior siblings in the same branch.
- Placement of if steps: sequence top level and loop bodies only (loop body scan accepts `If` children; if-branch scan rejects them).

### State transitions (runtime, `stepOutcomes[stepKey]`)

| Event | If-step outcome | Sequence effect |
|-------|-----------------|-----------------|
| Condition true → then branch completes (incl. empty then) with steps executed | `success` | continue |
| Condition false → else branch completes with steps executed | `success` | continue |
| No branch steps run (condition true + then empty/absent; condition false + else empty/absent) | `skipped` | continue |
| Condition evaluation throws (missing image ref, unavailable stepRef, no evaluator) | `failed` | sequence fails (same as while-loop condition error) |
| Branch step fails (earlyStop) | `failed` | sequence fails (propagates exactly like a loop-body step failure) |
| Break step inside branch fires (if inside a loop) | `success` | enclosing loop exits (break propagates through the if) |

### Execution log records (`StepResult`)

If-step record is appended **before** its branch-step records:

| Field | Value |
|-------|-------|
| CommandId | stepKey (`StepId` or `if@{Order}`) |
| Status | `Succeeded` (branch ran or no-op) / `Failed` |
| ConditionType | `imageVisible` \| `commandOutcome` |
| ConditionResult | `true` \| `false` \| `error` |
| ActionOutcome | `then` \| `else` \| `none` |
| Message | e.g. `If 'check-popup': condition true → then branch` / `... condition false → no else branch (no-op)` |

Branch steps then record themselves exactly as loop-body steps do today. Detail items emitted by `SequenceExecutionService` carry `stepType: "if"`; `ExecutionLogService.MapStepKind` maps it to tree node kind `if`.

## API contract (GameBot.Service)

### SequenceStepContract (extended)

```jsonc
{
  "stepId": "check-popup",
  "label": "Dismiss popup when present",
  "stepType": "If",                     // parsed case-insensitively; new value
  "if": {                                // NEW: IfConfigContract
    "condition": {                       // SequenceStepConditionContract (existing polymorphic)
      "type": "imageVisible",
      "imageId": "img-123",
      "minSimilarity": 0.9,
      "negate": false
    }
  },
  "body": [ /* then-branch steps, same shape as loop body steps */ ],
  "elseBody": [ /* else-branch steps */ ]   // NEW; null/absent = no else
}
```

- `IfConfigContract { SequenceStepConditionContract Condition }` — plain record, no discriminator.
- `SequenceStepContract` gains `If` and `ElseBody` properties.
- `ParseStepType` gains `"if" → SequenceStepType.If`.
- `TryReadPerStepRequest` step-shape guard treats `"if"` like `"loop"`/`"break"` (no `primitiveAction` required).
- `MapToLinearSteps` / `MapBodySteps` map if steps (condition, then via `MapBodySteps(step.Body)`, else via `MapBodySteps(step.ElseBody)` preserving null); `MapBodySteps` gains an `If` child case (loop bodies may contain ifs).
- `MapStepToDto` emits `@if` (serialized `"if"`) and `elseBody` alongside existing `body`.
- `FlattenSequenceSteps` traverses `ElseBody` in addition to `Body` (command-reference enrichment + lookups).

## Web UI types

### stepEntry.ts

```ts
export type IfStepEntry = {
  type: 'If';
  id: string;
  stepId: string;
  condition?: SequenceStepCondition;   // same editor state as while loops
  body: StepEntry[];                   // then branch
  elseBody?: StepEntry[];              // undefined = no else; [] = empty else area shown
};
// StepEntry union gains IfStepEntry; loop bodies may contain IfStepEntry (flat: if branches may not)
```

### sequenceFlow.ts

```ts
export type IfConfigDto = { condition: SequenceStepCondition };
// SequenceLinearStep gains: stepType 'If'; if?: IfConfigDto | null; elseBody?: SequenceLinearStep[] | null
```

### executionLogsApi.ts

`ExecutionTreeNodeKind` gains `'if'`.

## Entity relationships

```
SequenceStep (StepType=If)
├── If: IfConfig ──── Condition: SequenceStepCondition (shared with WhileLoopConfig)
├── Body: SequenceStep[]        (then branch — same rules/behaviour as loop Body)
└── ElseBody: SequenceStep[]?   (else branch — same rules/behaviour as loop Body)

Allowed containment:
  Sequence ── If ── {Action, Wait}                      (top level)
  Sequence ── Loop ── If ── {Action, Wait, Break}       (if inside loop body)
  If ── Loop ✗   If ── If ✗   (flat branches)
```
