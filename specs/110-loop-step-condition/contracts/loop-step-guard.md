# Contract: Guard on a Loop step

## Request: `POST /api/sequences`, `PUT /api/sequences/{id}` (and `dryRun: true`)

No new field. The existing step field `condition` is now kept on a step with `stepType: "Loop"`.

```json
{
  "name": "loop-guard-example",
  "steps": [
    {
      "stepId": "wait-dialog",
      "primitiveAction": { "type": "tap", "schemaVersion": "v1", "payload": { "x": 10, "y": 10 } }
    },
    {
      "stepId": "leave-if-stuck",
      "stepType": "Loop",
      "condition": {
        "type": "none",
        "children": [ { "type": "lastRun", "sequence": "self", "status": "success", "within": "01:00:00" } ]
      },
      "loop": { "loopType": "count", "count": 3 },
      "body": [
        { "stepId": "press-back", "primitiveAction": { "type": "key", "schemaVersion": "v1", "payload": { "key": "BACK" } } }
      ]
    }
  ]
}
```

Responses:

- `201` (create) or `200` (update, dry run): the stored step has the same `condition`.
- `400`: the guard is not correct. The error names the step, for example `Step 'leave-if-stuck' commandOutcome references unknown prior step 'nope'.`

## Response: `GET /api/sequences/{id}`

The `Loop` step has `condition` with the stored value. When the step has no guard, `condition` is absent or `null`, as before.

## Run result: `POST /api/sequences/{id}/execute`

The `Loop` entry in `steps`:

| Guard | `status` | `loopIterations` | `conditionType` | `conditionResult` |
|-------|----------|------------------|-----------------|-------------------|
| none | as before | as before | `null` | `null` |
| true | as before | as before | guard type | `true` |
| false | `Skipped` | `[]` | guard type | `false` |
| error | `Failed` | `[]` | guard type | `error` |

## Execution log: `GET /api/execution-logs/{id}`

The detail item with `attributes.stepType` `loop` has two new attributes, `conditionType` and `conditionResult`, with the values of the table above.

## OpenAPI

The `condition` property of the `SequenceStepContract` schema has a description that tells: the guard on an `Action` step skips the step when it is false; the guard on a `Loop` step is evaluated one time before the first iteration and skips the full loop when it is false; `If` and `Break` steps use `if.condition` and `breakCondition`.
