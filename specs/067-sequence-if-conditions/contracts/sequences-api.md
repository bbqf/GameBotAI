# API Contract: If Steps in Sequences (067)

Applies to the existing sequence endpoints in `GameBot.Service` (`POST/PUT /api/sequences`, `GET /api/sequences/{id}`, `PATCH /api/sequences/{id}`). No new endpoints; the step schema is extended.

## Upsert request — if step

```jsonc
{
  "name": "farm-with-popup-guard",
  "version": 3,
  "steps": [
    { "stepId": "s1", "stepType": "Action", "primitiveAction": { "type": "command", "schemaVersion": "v1", "payload": { "commandId": "open-map" } } },
    {
      "stepId": "if-popup",
      "stepType": "If",
      "if": {
        "condition": { "type": "imageVisible", "imageId": "img-popup", "minSimilarity": 0.85, "negate": false }
      },
      "body": [
        { "stepId": "dismiss", "stepType": "Action", "primitiveAction": { "type": "command", "schemaVersion": "v1", "payload": { "commandId": "tap-close" } } }
      ],
      "elseBody": [
        { "stepId": "proceed", "stepType": "Action", "primitiveAction": { "type": "command", "schemaVersion": "v1", "payload": { "commandId": "tap-continue" } } }
      ]
    }
  ]
}
```

Rules:

| Element | Requirement |
|---------|-------------|
| `stepType` | `"If"` (case-insensitive on parse) |
| `if.condition` | Required. Same polymorphic schema as `loop.condition` for `while`/`repeatUntil` (`imageVisible` \| `commandOutcome`, optional `negate`) — any condition accepted/rejected for a while loop is accepted/rejected here (SC-003) |
| `body` | Optional then-branch step array; same child schema as loop `body`. Empty/absent = no-op then |
| `elseBody` | Optional else-branch step array; `null`/absent = no else (editor shows no else area); `[]` = else present but empty |
| `primitiveAction` | Not required on if steps (like loop/break steps) |
| Branch children | Same constraints as loop-body children; additionally: no `Loop`, no `If`; `Break` only valid when the if step itself is inside a loop `body` |
| Placement | If steps valid at top level and inside loop `body`; invalid inside `body`/`elseBody` of another if step |

Validation failures return the existing 400 error-list shape with messages following current phrasing, e.g.:

- `If step 'if-popup' requires an if configuration with a condition.`
- `Branch step 'x' inside if 'if-popup' must not itself be a loop step.`
- `Branch step 'x' inside if 'if-popup' must not itself be an if step.`
- `Branch step 'brk' inside if 'if-popup' has stepType 'Break' which is only valid inside a loop body.`
- `Step 'if-popup' imageVisible condition requires imageId.`

## Read (GET) — if step DTO

Mirrors the request shape (existing `MapStepToDto` pattern):

```jsonc
{
  "stepId": "if-popup",
  "label": null,
  "stepType": "If",
  "commandReference": null,
  "primitiveAction": null,
  "condition": null,
  "if": { "condition": { "type": "imageVisible", "imageId": "img-popup", "minSimilarity": 0.85, "negate": false } },
  "loop": null,
  "body": [ /* then steps */ ],
  "elseBody": [ /* else steps */ ],
  "breakCondition": null
}
```

Round-trip guarantee: upsert → GET returns the same if/branch structure (FR-010); sequences saved before 067 return `"if": null` and no `elseBody`, and continue to accept writes unchanged (SC-004).

## Execution log subtree — if node

`GET /api/execution-logs/{id}/subtree` tree nodes gain kind `"if"`:

```jsonc
{
  "nodeKind": "if",
  "order": 2,
  "label": "if-popup",
  "status": "success",            // success (branch ran) | skipped (no-op) | failure
  "message": "If 'if-popup': condition true → then branch",
  "children": []                   // branch steps appear as sibling step nodes, as loop-body steps do today
}
```

The if node's `message` + `conditionResult`/`actionOutcome` detail metadata (`then` | `else` | `none`) let a reader determine which branch ran without the sequence definition (SC-005).
