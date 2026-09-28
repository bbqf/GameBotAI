# Contract: `POST /api/sequences` (changes of feature 113)

## Malformed step

Request (the command id is the id of a command that exists):

```json
{"name":"ZZZ.DropProbe","steps":[
  {"stepId":"a","commandReference":{"commandId":"<valid id>"}},
  {"stepId":"b","primitiveAction":{"type":"reschedule-self","schemaVersion":"v1","payload":{"option":"AtQueueStart"}}}]}
```

Response: `400 Bad Request`

```json
{
  "message": "Invalid sequence payload",
  "errors": ["steps[0] (stepId 'a'): each action step must include primitiveAction object."]
}
```

The service stores nothing. The same body with `"dryRun": true` gets the same `400`, and the service stores nothing. The same body sent to `PUT /api/sequences/{id}` or `PATCH /api/sequences/{id}` gets the same `400`, and the stored sequence does not change.

## `dryRun` on each body shape

Request (old body):

```json
{ "name": "old-shape", "steps": ["<command id>"], "dryRun": true }
```

Response: `200 OK`

```json
{ "valid": true, "dryRun": true, "errors": [] }
```

The service stores nothing. A `blocks` body with `dryRun: true` gets the same result when it passes the `blocks` validation, and the current `400` when it does not.

## Old body with data that it cannot keep

| Request | Response |
|---------|----------|
| `{ "name": "x", "steps": ["c1", 5] }` | `400`, error `steps[1]: each step must be a string command id or a step object.` |
| `{ "name": "x", "steps": ["c1"], "parameters": [] }` | `400`, error `parameters requires the per-step body shape (steps as step objects).` |
| `{ "name": "x", "steps": ["c1"], "parameters": null }` | `201` (no change) |

## No change

- A valid per-step body: `201` with all steps and all parameters, or `200` with the dry-run body.
- An old body with string ids and without `parameters`: `201`.
- A body with `entryStepId` or `links`: `400` (no change).
