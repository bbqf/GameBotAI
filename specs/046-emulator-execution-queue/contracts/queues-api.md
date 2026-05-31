# API Contract: Emulator Execution Queues

Base path: `/api/queues` (constant `ApiRoutes.Queues`). Tag: `Queues`.
Auth: same token scheme as the rest of the API. Error envelope (all 4xx):
```json
{ "error": { "code": "string", "message": "string", "hint": "string|null" } }
```

---

## POST /api/queues — Create queue

**Request**
```json
{ "name": "Daily Farm", "emulatorSerial": "emu-1", "cycleExecution": true }
```
**Validation**
- `name` required, non-empty → else `400 invalid_request` "name is required".
- `emulatorSerial` required, non-empty → else `400 invalid_request` "emulatorSerial is required".

**Responses**
- `201 Created`, `Location: /api/queues/{id}`, body = `QueueResponse` (status `Stopped`, `entryCount` 0).

---

## GET /api/queues — List queues

**Response** `200 OK`
```json
[
  { "id": "ab12", "name": "Daily Farm", "emulatorSerial": "emu-1",
    "cycleExecution": true, "status": "Running", "entryCount": 3 }
]
```
Status and entryCount come from the in-memory runtime store (reset on restart).

---

## GET /api/queues/{id} — Get queue detail

**Response** `200 OK` = `QueueDetailResponse`
```json
{
  "id": "ab12", "name": "Daily Farm", "emulatorSerial": "emu-1",
  "cycleExecution": true, "status": "Stopped", "entryCount": 2,
  "entries": [
    { "entryId": "e1", "sequenceId": "seq-100", "sequenceName": "Login", "stale": false },
    { "entryId": "e2", "sequenceId": "seq-999", "sequenceName": null,    "stale": true  }
  ]
}
```
- `404 not_found` if queue id unknown.
- `stale: true` when the referenced sequence no longer exists (entry retained).

---

## PUT /api/queues/{id} — Update name / cycle flag

**Request**
```json
{ "name": "Daily Farm v2", "cycleExecution": false }
```
- `emulatorSerial` is **not** accepted/changed (immutable, FR-004).

**Responses**
- `200 OK` = `QueueResponse`.
- `404 not_found` if unknown.
- `400 invalid_request` if `name` empty.
- `409 conflict` (`queue_running`) if the queue is `Running` — message: "Stop the queue before editing." (FR-005a)

---

## DELETE /api/queues/{id} — Delete queue

**Responses**
- `204 No Content` on success (config + runtime state removed).
- `404 not_found` if unknown.
- `409 conflict` (`queue_running`) if `Running` — "Stop the queue before deleting." (FR-005a)

---

## POST /api/queues/{id}/entries — Add sequence entry (append)

**Request**
```json
{ "sequenceId": "seq-100" }
```
**Responses**
- `201 Created` = the new entry `{ entryId, sequenceId, sequenceName, stale }` (appended at end, FR-010).
- `404 not_found` if queue unknown.
- `400 invalid_request` if `sequenceId` missing.
- Allowed while `Running` (FR-013a). Duplicate `sequenceId` permitted (FR-013).

---

## DELETE /api/queues/{id}/entries/{entryId} — Remove entry

**Responses**
- `204 No Content` on success (remaining order preserved).
- `404 not_found` if queue or entry unknown.
- Allowed while `Running`; stale entries are removable.

---

## POST /api/queues/{id}/start — Start (placeholder)

Sets status to `Running` and logs the transition (FR-019b). Idempotent (FR-017). Allowed even if `emulatorSerial` is not currently connected (FR-019a). No sequences are executed (FR-019).

**Responses**
- `200 OK` = `QueueResponse` (status `Running`).
- `404 not_found` if unknown.

---

## POST /api/queues/{id}/stop — Stop (placeholder)

Sets status to `Stopped` and logs the transition. Idempotent.

**Responses**
- `200 OK` = `QueueResponse` (status `Stopped`).
- `404 not_found` if unknown.

---

## Restart semantics (not an endpoint)

After a service restart: all queue **config** is reloaded from disk; all **entries** are empty and all **statuses** are `Stopped` (FR-021, FR-022). No migration or recovery of runtime state occurs.
