# Data Model: Make a Queue's Live Roster Discoverable

No persisted or runtime model changes. The entities below are the **published** shapes whose documentation changes; their serialised form is unchanged.

## QueueDetailResponse (response of `GET /api/queues/{id}`, `PUT /api/queues/{id}/entries`)

| Field | Type | Change |
|-------|------|--------|
| `entries` | array of `QueueEntryResponse` | Gains a description (FR-001). Published `nullable` removed (FR-002); `readOnly` kept. Serialised value unchanged: always an array, in run order, `[]` when empty. |
| every other field | — | Unchanged. |

Source of values: `IQueueRuntimeStore.GetEntries(queueId)` — the queue's own runtime roster, not the linked template's entries.

## QueueEntryResponse (item of `entries`; also the 201 body of `POST /api/queues/{id}/entries`)

| Field | Type | Published description (meaning) |
|-------|------|------|
| `entryId` | string | Identifies this entry within the queue; the id `DELETE /api/queues/{id}/entries/{entryId}` removes. |
| `sequenceId` | string | The sequence this entry runs. |
| `sequenceName` | string, nullable | The referenced sequence's current name, resolved at read time; null when that sequence no longer exists. |
| `stale` | boolean | True when the referenced sequence no longer exists (then `sequenceName` is null). |

Nullability of `entryId`, `sequenceId`, `sequenceName` is left as currently published (out of scope).

## Relationships

- Queue 1 — 0..n QueueEntry (ordered). Queue 0..1 — 1 QueueTemplate (link). The template's entries are a separate list, read through `GET /api/queue-templates/{id}`; they are copied into the queue's roster when loaded, and a running queue keeps the roster it started with.
