# Contract: Duplicate Queue

## `POST /api/queues/{id}/duplicate`

Creates a new queue that is a 1:1 copy of the queue identified by `{id}`, except for its name and
identifier.

### Request

Path parameter:

- `id` (string, required) — the source queue's ID.

Body (`application/json`):

```json
{
  "name": "Daily Farming 2"
}
```

| Field | Type | Required | Notes |
|-------|------|----------|-------|
| `name` | string | yes | Trimmed. Must be non-empty and must differ from the source queue's current name. |

### Responses

**`201 Created`** — duplicate created successfully.

Headers: `Location: /api/queues/{newId}`

Body: same shape as the response of `POST /api/queues` (`QueueResponse`):

```json
{
  "id": "q-new-id",
  "name": "Daily Farming 2",
  "emulatorSerial": "emulator-5554",
  "cycleExecution": true,
  "pauseWhenIdle": true,
  "idleThresholdSeconds": 30,
  "emulatorInstanceName": null,
  "emulatorInstanceIndex": null,
  "status": "Stopped",
  "entryCount": 3,
  "linkedTemplateId": "tpl-123",
  "linkedGameId": "game-456"
}
```

**`404 not_found`** — `{id}` does not resolve to an existing queue.

```json
{ "error": { "code": "not_found", "message": "Queue not found", "hint": null } }
```

**`400 invalid_request`** — missing/blank `name`.

```json
{ "error": { "code": "invalid_request", "message": "name is required", "hint": null } }
```

**`400 invalid_request`** — `name` equals the source queue's current name (after trim).

```json
{ "error": { "code": "invalid_request", "message": "name must differ from the original queue's name", "hint": null } }
```

### Side effects

- A new `ExecutionQueue` is persisted via the existing queue repository, with every configuration
  field copied from the source (see [data-model.md](../data-model.md)).
- The new queue's runtime entries are set to match the source queue's current entries (sequence
  IDs), via the existing runtime store.
- The source queue is left completely unmodified (no write to it).
- The new queue's status is `Stopped`; no execution history exists for it.

### Non-goals

- Does not duplicate the linked `QueueTemplate` itself — the new queue links to the *same*
  template record as the source.
- Does not start the new queue.
- Does not enforce name uniqueness against any queue other than the immediate source.
