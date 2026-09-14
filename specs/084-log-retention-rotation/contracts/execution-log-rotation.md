# Contract: Execution Log Retention Default & Rotation Links

No new endpoints. Two existing endpoints in `src/GameBot.Service/Endpoints/ExecutionLogsEndpoints.cs` gain additive, backward-compatible response fields; one config default changes.

## `GET /api/execution-logs/retention`

Response body (`ExecutionLogRetentionPolicyDto`) shape unchanged. Behavior change only: on a deployment with no previously saved policy, `RetentionDays` in the response is now `7` instead of `60`.

## `PUT /api/execution-logs/retention`

Unchanged — request/response shape and validation (`Math.Max(1, ...)` clamp) are identical.

## `GET /api/execution-logs` (list)

`ExecutionLogEntryDto` gains two new optional fields (present, possibly `null`, on every item — additive, non-breaking for existing consumers that ignore unknown fields):

```jsonc
{
  "id": "...",
  // ...existing fields unchanged...
  "rotatedToExecutionId": null,   // set only on a closed-out queue-root segment
  "rotatedFromExecutionId": null  // set only on a queue-root segment opened by rotation
}
```

## `GET /api/execution-logs/{id}` (detail)

`ExecutionLogDetailDto.relatedObjects` gains one additional entry when the entry is a rotation boundary:

```jsonc
// closing segment (rotated away from)
{
  "label": "Continues in newer run segment",
  "targetType": "execution",
  "targetId": "<new segment id>",
  "isAvailable": true,
  "unavailableReason": null
}
```

```jsonc
// opening segment (continuation)
{
  "label": "Continued from earlier run segment",
  "targetType": "execution",
  "targetId": "<previous segment id>",
  "isAvailable": true,
  "unavailableReason": null
}
```

`targetType`/`isAvailable` follow the existing "Parent execution" related-object link in the same projection: `isAvailable` reflects whether the id is present, not whether the target still exists. `BuildDetailProjection` is a pure static function over a single entry with no repository access, so it cannot probe the target — a link whose target has since been removed by retention behaves exactly like a link to a deleted parent does today (the follow-up request 404s).

## `GET /api/execution-logs/{id}/subtree`

Unchanged. Rotation links are between two separate queue-root entries (two separate subtrees), not within one subtree, so this endpoint's shape and behavior are untouched.
