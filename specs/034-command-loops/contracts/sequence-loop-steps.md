# API Contracts: Command Loop Structures

**Feature**: 033-command-loops  
**Date**: 2026-03-31  
**Base URL**: `http://localhost:5081`

No new endpoints are introduced. Existing sequence endpoints accept and return the extended `SequenceStep` schema that includes `Loop` and `Break` step types.

---

## Extended `SequenceStep` Schema

The `stepType` discriminator gains two new values: `"loop"` and `"break"`.

```json
// Schema union — one of the following shapes per step:
{
  "stepId": "string (required)",
  "label": "string (optional)",
  "order": "integer (required)",
  "stepType": "\"action\" | \"command\" | \"conditional\" | \"loop\" | \"break\""
}
```

---

### Loop Step (`stepType: "loop"`)

```json
{
  "stepId": "tap-upgrade-loop",
  "label": "Tap upgrade 10 times",
  "order": 1,
  "stepType": "loop",
  "loop": {
    "loopType": "count",
    "count": 10,
    "maxIterations": null
  },
  "body": [
    {
      "stepId": "tap-upgrade",
      "label": "Tap upgrade button",
      "order": 1,
      "stepType": "action",
      "action": {
        "type": "primitiveTap",
        "parameters": { "x": 540, "y": 960 }
      }
    }
  ]
}
```

**Count loop** — `loop.loopType` = `"count"`:
| Field | Type | Required | Notes |
|---|---|---|---|
| `loopType` | `"count"` | yes | discriminator |
| `count` | integer ≥ 0 | yes | 0 = skip body |
| `maxIterations` | integer > 0 \| null | no | overrides global default (1000) |

---

**While loop** — `loop.loopType` = `"while"`:

```json
{
  "stepId": "wait-for-map",
  "order": 2,
  "stepType": "loop",
  "loop": {
    "loopType": "while",
    "condition": {
      "type": "imageVisible",
      "imageId": "loading-screen",
      "minSimilarity": 0.85
    },
    "maxIterations": 50
  },
  "body": [
    {
      "stepId": "wait-step",
      "order": 1,
      "stepType": "action",
      "action": { "type": "delay", "parameters": { "ms": 500 } }
    }
  ]
}
```

| Field | Type | Required | Notes |
|---|---|---|---|
| `loopType` | `"while"` | yes | discriminator |
| `condition` | `SequenceStepCondition` | yes | `imageVisible` or `commandOutcome` |
| `maxIterations` | integer > 0 \| null | no | default 1000 |

---

**RepeatUntil loop** — `loop.loopType` = `"repeatUntil"`:

```json
{
  "stepId": "tap-until-success",
  "order": 3,
  "stepType": "loop",
  "loop": {
    "loopType": "repeatUntil",
    "condition": {
      "type": "imageVisible",
      "imageId": "success-banner"
    }
  },
  "body": [
    {
      "stepId": "tap-confirm",
      "order": 1,
      "stepType": "action",
      "action": { "type": "primitiveTap", "parameters": { "x": 300, "y": 600 } }
    }
  ]
}
```

---

### Break Step (`stepType: "break"`)

```json
// Conditional break:
{
  "stepId": "break-on-error",
  "order": 2,
  "stepType": "break",
  "breakCondition": {
    "type": "imageVisible",
    "imageId": "error-screen"
  }
}

// Unconditional break:
{
  "stepId": "unconditional-break",
  "order": 3,
  "stepType": "break",
  "breakCondition": null
}
```

| Field | Type | Required | Notes |
|---|---|---|---|
| `breakCondition` | `SequenceStepCondition \| null` | no | null = unconditional break |

---

### `{{iteration}}` placeholder in action parameters

Inside a loop body, string-typed action parameter values may include `{{iteration}}`:

```json
{
  "stepId": "log-iteration",
  "order": 1,
  "stepType": "action",
  "action": {
    "type": "delay",
    "parameters": { "label": "iteration {{iteration}} of loop" }
  }
}
```

At execution time `{{iteration}}` is replaced with the current 1-based iteration number.  
`{{iteration}}` in step parameters outside a loop body is rejected at save/validate time.

---

## Existing Endpoints — Behaviour Changes

### `POST /api/sequences/{sequenceId}/validate`
Extended to validate:
- Loop step body does not contain loop steps (no nesting).
- Break steps only inside loop bodies.
- `{{iteration}}` placeholders only inside loop body step parameters.
- `CountLoopConfig.Count` ≥ 0.
- `LoopConfig.MaxIterations` > 0 when provided.
- `commandOutcome` condition `stepRef` in loop context does not forward-reference within the same body.

### `PUT /api/sequences/{sequenceId}` / `PATCH /api/sequences/{sequenceId}`
Accept the extended `SequenceStep` schema. Run the same validation as validate endpoint on save.

---

## Execution Log — Loop Step Outcome Shape

When a loop step executes, its `ExecutionStepOutcome` carries `loopIterations`:

```json
{
  "stepOrder": 1,
  "stepType": "loop",
  "outcome": "executed",
  "stepId": "tap-upgrade-loop",
  "loopIterations": [
    {
      "iterationIndex": 1,
      "breakTriggered": false,
      "stepOutcomes": [
        { "stepOrder": 1, "stepType": "action", "outcome": "executed" }
      ]
    },
    {
      "iterationIndex": 2,
      "breakTriggered": true,
      "stepOutcomes": [
        { "stepOrder": 1, "stepType": "action", "outcome": "executed" },
        { "stepOrder": 2, "stepType": "break", "outcome": "break" }
      ]
    }
  ]
}
```

**Loop step `outcome` values**:
| Value | Meaning |
|---|---|
| `"executed"` | Loop completed normally (count exhausted or while/repeatUntil condition met) |
| `"break"` | Loop exited via a break step |
| `"failed"` | Safety limit reached, condition eval error, or inner step failed |
| `"skipped"` | Count = 0 or while condition false on entry |
