# API Contract: Queue–Template Link with Auto-Load

Base: `/api/queues`. Error envelope (unchanged across the project):
`{ "error": { "code": string, "message": string, "hint": string | null } }`.

## NEW — Set / clear a queue's linked template

`PUT /api/queues/{id}/template`

Request body:

```json
{ "templateId": "a1b2c3d4e5f6..." }   // or { "templateId": null } to clear
```

Behavior:
- Sets `ExecutionQueue.LinkedTemplateId` to `templateId` (null clears). Persisted.
- **Not** blocked while the queue is `Running` (it mutates only the persisted link, never
  the runtime entries) — this allows associating a queue with a template saved while running.
- Does not itself load entries; auto-load happens on the next detail GET.

Responses:
| Status | When | Body |
|--------|------|------|
| 200 OK | Link set/cleared | `QueueDetailResponse` (with updated `linkedTemplateId`/`linkedTemplateName`) |
| 400 invalid_request | `templateId` non-null but no such template | error envelope, message "template not found" |
| 404 not_found | No queue with `{id}` | error envelope |

## CHANGED — Get a queue (auto-load trigger)

`GET /api/queues/{id}`

Side effect (before the response is built), applied in order; any failed guard skips
auto-load and the queue still returns 200:
1. No linked template → no auto-load.
2. Status `Running` → no auto-load (entries returned as-is).
3. Runtime state already exists for this queue (materialized or edited since startup) →
   no auto-load.
4. Linked template cannot be resolved → clear `LinkedTemplateId` (persist) → return empty.
5. Otherwise → replace runtime entries with the template's ordered sequence ids and return.

Response body `QueueDetailResponse` (additions in **bold**):

```json
{
  "id": "q1",
  "name": "Daily Farm Queue",
  "emulatorSerial": "emulator-5554",
  "cycleExecution": false,
  "status": "Stopped",
  "entryCount": 3,
  "linkedTemplateId": "tpl-abc",        // NEW (null when unlinked)
  "linkedTemplateName": "Daily Farm",   // NEW (null when unlinked/unresolved)
  "entries": [
    { "entryId": "e1", "sequenceId": "s1", "sequenceName": "Open app", "stale": false },
    { "entryId": "e2", "sequenceId": "s2", "sequenceName": "Collect",  "stale": false },
    { "entryId": "e3", "sequenceId": "sX", "sequenceName": null,       "stale": true }
  ]
}
```

Stale entries (referenced sequence deleted) are projected with `stale: true` as today,
covering auto-loaded template entries with dead references (FR-009).

## CHANGED — List / summary queue responses

`GET /api/queues`, and the `QueueResponse` returned by create/update/start/stop now include:

```json
{ "...": "...", "linkedTemplateId": "tpl-abc" }   // NEW (null when unlinked)
```

(`linkedTemplateName` is provided on the **detail** response only, where the template repo
is consulted.)

## UNCHANGED (reused)

- `PUT /api/queues/{id}/entries` — replace entries (still 409 `queue_running` while running).
  The load flow calls this, then calls `PUT {id}/template` to record the link.
- `GET /api/queue-templates`, `GET /api/queue-templates/{id}`,
  `POST /api/queue-templates`, `DELETE /api/queue-templates/{id}` — unchanged. The save
  response's `id` is used as the link target after a save.

## Contract test expectations

- `PUT /api/queues/{id}/template` exists; set→persist (survives a fresh repository instance),
  clear, 400 on unknown template, 404 on unknown queue, success while Running.
- `GET /api/queues/{id}` exposes `linkedTemplateId` and `linkedTemplateName`; auto-load
  matrix (Decisions 3–4, 6) holds.
- `openapi.json` updated to include the new path and the new response properties.
