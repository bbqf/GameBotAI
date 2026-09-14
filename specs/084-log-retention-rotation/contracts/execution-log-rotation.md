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
  "targetType": "execution-log",
  "targetId": "<new segment id>",
  "isAvailable": true,
  "unavailableReason": null
}
```

```jsonc
// opening segment (continuation)
{
  "label": "Continued from earlier run segment",
  "targetType": "execution-log",
  "targetId": "<previous segment id>",
  "isAvailable": false,
  "unavailableReason": "Deleted by retention"
}
```

`isAvailable`/`unavailableReason` follow the existing `RelatedObjectLinkDto` semantics already used for other related-object links (e.g. an unavailable linked sequence) — resolved by attempting to look up the target execution id and falling back to `isAvailable: false` when retention has already removed it.

## `GET /api/execution-logs/{id}/subtree`

Unchanged. Rotation links are between two separate queue-root entries (two separate subtrees), not within one subtree, so this endpoint's shape and behavior are untouched.
