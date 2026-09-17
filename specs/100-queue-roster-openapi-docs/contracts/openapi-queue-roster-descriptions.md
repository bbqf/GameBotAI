# Contract: Queue Roster Descriptions in the OpenAPI Document

Published at `/swagger/v1/swagger.json`. Only documentation metadata changes; request/response bodies, routes and status codes are unchanged.

## Schema: `components.schemas.QueueDetailResponse.properties.entries`

- `description` MUST contain, in substance:
  - it is the queue's current roster, in run order;
  - it is always an array — `[]` when the queue has no entries, never null;
  - it is the queue's own entries, which can differ from its linked template's entries (a running queue keeps the entries it started with); the template's entries are read from `GET /api/queue-templates/{id}`;
  - reading this field from `GET /api/queues/{id}` is how a queue's roster is read — there is no `GET /api/queues/{id}/entries`.
- `nullable` MUST NOT be `true`.
- `readOnly` stays `true`.
- `type: array`, `items.$ref: #/components/schemas/QueueEntryResponse` unchanged.

Test key phrases: `roster`, `never null`, `template`, `GET /api/queues/{id}`.

## Schema: `components.schemas.QueueEntryResponse.properties.*`

| Property | `description` must convey | Test key phrase |
|----------|---------------------------|-----------------|
| `entryId` | identifies the entry; used by `DELETE /api/queues/{id}/entries/{entryId}` | `DELETE /api/queues/{id}/entries/{entryId}` |
| `sequenceId` | the sequence this entry runs | `sequence this entry runs` |
| `sequenceName` | current name of the referenced sequence; null when it no longer exists | `null`, `no longer exists` |
| `stale` | true when the referenced sequence no longer exists | `no longer exists` |

## Operation: `paths./api/queues/{id}.get`

- `description` MUST state that the response's `entries` is the queue's roster (key phrase `entries`, `roster`).
- Existing health text MUST remain (key phrases `health`, `pauseKind` — already pinned by `QueueHealthOpenApiTests`).
- `responses.200.content.application/json.example.entries` MUST be an array (already true; now pinned).

## Operations: `paths./api/queues/{id}/entries.post` and `.put`

- `description` MUST point readers to `GET /api/queues/{id}` and its `entries` field to read the roster (key phrases `GET /api/queues/{id}`, `entries`).
- Summaries and examples unchanged.
