# Contract: Queue Templates API

**Feature**: 047-queue-templates | **Base route**: `/api/queue-templates`
**Error envelope** (all errors): `{ "error": { "code": string, "message": string, "hint": string | null } }`

This contract adds a new template resource and **one** new method on the existing
queue resource (`PUT /api/queues/{id}/entries`) used to load a template into a queue.

---

## Template resource

### GET `/api/queue-templates`
List all templates (summaries for the picker).

**200 OK**
```json
[
  { "id": "8f3c…", "name": "Daily Farm", "entryCount": 3,
    "createdAt": "2026-05-31T10:00:00+00:00", "updatedAt": "2026-05-31T10:00:00+00:00" }
]
```
Empty list `[]` when none exist (UI renders an empty state — FR-024).

---

### GET `/api/queue-templates/{id}`
Template detail with ordered, resolved entries (used when loading).

**200 OK**
```json
{
  "id": "8f3c…",
  "name": "Daily Farm",
  "entryCount": 3,
  "createdAt": "2026-05-31T10:00:00+00:00",
  "updatedAt": "2026-05-31T10:00:00+00:00",
  "entries": [
    { "sequenceId": "seq-collect", "sequenceName": "Collect", "stale": false },
    { "sequenceId": "seq-gone",    "sequenceName": null,      "stale": true  }
  ]
}
```
`sequenceName`/`stale` resolved against the sequence store at read time (FR-017).

**404 not_found** — unknown id.

---

### POST `/api/queue-templates`
Save (create or overwrite-by-name) a template. Single save entry point — there is no
separate template editor (FR-025).

**Request**
```json
{ "name": "Daily Farm", "sequenceIds": ["seq-collect", "seq-upgrade"], "overwrite": false }
```
| Field | Type | Notes |
|-------|------|-------|
| `name` | string | Required. Trimmed; `[A-Za-z0-9 _-]`; ≤100 chars. |
| `sequenceIds` | string[] | Ordered; may be empty (empty template, FR-011); duplicates allowed (FR-006). |
| `overwrite` | bool | Optional, default `false`. Must be `true` to replace an existing same-name template. |

**201 Created** — new template (body = detail shape). `Location: /api/queue-templates/{id}`.

**200 OK** — existing template overwritten (`overwrite: true`); body = detail shape with new `updatedAt`.

**400 invalid_request** — name blank/missing, illegal characters, or >100 chars.
`message` names the violated rule (FR-008/008a/008b).

**409 template_exists** — a template with the same name (case-insensitive) exists and
`overwrite` was not `true`. The client prompts for overwrite confirmation, then
re-sends with `overwrite: true` (FR-009). `hint`: "Resend with overwrite=true to replace."

---

### DELETE `/api/queue-templates/{id}`
Delete a template.

**204 No Content** — deleted. Does not affect any queue's entries (FR-020).

**404 not_found** — unknown id.

---

## Queue resource — added method

### PUT `/api/queues/{id}/entries`
Replace **all** runtime entries of a queue with the given ordered sequences. Used by
the client to load a template (after `GET /api/queue-templates/{id}`) and is otherwise
a generic bulk-set. Order preserved; each entry gets a fresh `entryId` (FR-013, FR-015).

**Request**
```json
{ "sequenceIds": ["seq-collect", "seq-upgrade", "seq-collect"] }
```
Empty array clears the queue's entries.

**200 OK** — queue detail (same shape as `GET /api/queues/{id}`) reflecting the new entries.

**404 not_found** — unknown queue id.

**409 queue_running** — the queue is Running; loading/replacing is blocked. `message`:
"Stop the queue before loading a template." (FR-014a). Saving a template is unaffected
(it does not call this endpoint).

---

## Notes

- All write paths reuse the shared `{ error: { code, message, hint } }` envelope and
  the project's existing JSON casing (camelCase).
- No endpoint emits log entries (no template-action logging this iteration).
- The replace endpoint is the only queue-module change; the template module has no
  dependency on the queue module.
