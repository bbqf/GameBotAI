# Contract: Run next API

## POST /api/queues/{id}/run-next

Queues one run of one template entry. The entry runs after the sequence that runs now.

### Request

```json
{ "entryId": "9f1c2b7e4d5a4c0e8f31a6b2c7d8e9f0" }
```

| Field | Type | Required | Note |
|-------|------|----------|------|
| entryId | string | yes | Stable ID of an enabled entry of the running plan. |

### Responses

| Status | Code | When |
|--------|------|------|
| 200 | none | The request is queued, or it was already queued. |
| 400 | `invalid_request` | `entryId` is missing or empty. |
| 404 | `not_found` | The queue does not exist. |
| 409 | `not_running` | The queue has no active run. |
| 409 | `entry_not_available` | The entry is not in the plan of the run, or it is disabled. |
| 409 | `entry_running` | The entry runs now. |
| 409 | `entry_scheduled` | The run still has a normal firing for the entry. |

Error bodies use the same shape as the other queue calls: `{ "error": { "code": "...", "message": "..." } }`.
Each message says what the operator can do. Example: "The entry has a pending run. Wait for it, or edit its timer."

### 200 body

```json
{ "entryId": "9f1c...", "sequenceId": "seq-daily", "alreadyQueued": false, "state": "queued" }
```

### Behavior rules

- The call is safe to repeat. A second call for a queued entry returns `alreadyQueued: true`.
- The call works while the queue is paused. The entry runs after the resume.
- The call does not change the template, the plan, or the schedule of any other entry.
- The entry runs with the same parameter values as a normal run of the entry.
- The request ends when the run ends.

## GET /api/queues/{id}/monitor (change)

New field `entryStates`:

```json
{ "entryStates": { "9f1c...": "idle", "a2b3...": "scheduled", "c4d5...": "queued" } }
```

- Values: `running`, `queued`, `scheduled`, `idle`.
- The map is empty when the queue does not run.
- New `scheduleKind` value `RunNext` in `upcoming` items.

## Swagger

Add the call and the new fields to `SwaggerConfig.cs` with STE descriptions.
