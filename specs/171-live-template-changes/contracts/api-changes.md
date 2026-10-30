# API Changes: Apply template changes to a running queue

Base path: `/api`. All new members are optional for old clients. Errors use the existing error body
(`code`, `message`, `hint`).

## Pause

### `POST /api/queues/{id}/pause`

Request a pause of the run. No body.

| Case | Status | Body |
|------|--------|------|
| Queue unknown | 404 | error `not_found` |
| Queue not running | 409 | error `not_running`, hint "Start the queue first." |
| Run active | 200 | `{ "id", "operatorPause": "pausing" \| "paused" }` |

A repeat call is safe and returns the current state. If no firing group runs, the state is `paused` at once.
The call also works while a failure-policy pause is in force (FR-035).

### `POST /api/queues/{id}/resume` (existing, extended)

Releases the operator pause first. If there is none, it releases the failure-policy pause as today.
Resume of a state `pausing` cancels the request (FR-004).

New response members: `kind` (`operator`, `failurePolicy`, or null when nothing changed). `resumed` keeps
its meaning. The old members do not change.

### `GET /api/queues/{id}/health` (existing, extended)

| Member | Change |
|--------|--------|
| `operatorPause` | New. `none`, `pausing`, or `paused`. |
| `paused`, `pausedAt`, `pauseReason`, `pauseKind` | `pauseKind` gets the value `operator` when the operator pause is the one reported. `pauseReason` is `operator pause: waiting for resume`. |

When a failure-policy pause and an operator pause are both in force, `pauseKind` reports `failurePolicy`.

## Review and apply

### `GET /api/queues/{id}/template-diff`

Available while the queue runs, in any pause state. Needs a linked template.

```json
{
  "queueId": "q1",
  "templateId": "t1",
  "needsUpgrade": false,
  "baseToken": "sha256:…",
  "differences": [
    { "key": "parameters:3f2a…", "type": "parameters", "entryId": "3f2a…",
      "sequenceName": "Collect rewards", "summary": "Parameter values changed: count 3 to 5.",
      "selectedByDefault": true, "affectsSchedule": false }
  ]
}
```

| Case | Status |
|------|--------|
| Queue unknown, or no linked template | 404 |
| Queue not running | 409 `not_running` |
| Template needs upgrade | 200 with `needsUpgrade: true` and an empty `differences` list |

### `POST /api/queues/{id}/apply-template`

```json
{ "baseToken": "sha256:…", "selectedKeys": ["parameters:3f2a…", "added:9c1b…"] }
```

| Case | Status | Error code |
|------|--------|------------|
| Success | 200 | `{ "applied": [keys], "planVersion": 7 }` |
| Queue not paused | 409 | `not_paused`, hint "Pause the queue first." |
| Template needs upgrade | 409 | `needs_upgrade`, hint "Upgrade the template first." |
| `baseToken` differs | 409 | `template_changed`, hint "Review the changes again." |
| Another apply runs | 409 | `apply_in_progress` |
| Unknown key | 400 | `invalid_request` |
| New plan not valid | 400 | `invalid_request` (same body shape as the template save) |
| Run ended | 409 | `not_running` |

On any error the queue is as it was (FR-015).

## Templates

### `POST /api/queue-templates` (existing, extended)

Request entry: new optional `entryId`. Detail and summary responses: each entry gets `entryId` (omitted when
empty). New member `needsUpgrade` in the summary and the detail. The ID rules are in research R-003.

### `POST /api/queue-templates/{id}/upgrade?dryRun=true|false&confirm=true|false`

| Case | Status | Body |
|------|--------|------|
| Template unknown | 404 | |
| `dryRun=true` | 200 | `{ "entriesToChange": n, "runningMatch": [ { "queueId", "position", "sequenceName", "entryId" } ], "unmatched": [ … ] }` |
| Real call, running queue uses the template, `confirm` missing | 409 `confirm_required` | |
| Real call, `unmatched` not empty | 409 `unmatched_entries` | lists them; hint "Stop the queue or reload the template." |
| Real call, done | 200 | `{ "entriesChanged": n }` |
| Template already upgraded | 200 | `{ "entriesChanged": 0 }` |

The call changes no other field of the template. It changes no schedule and no next due time.

## Settings document (feature 145)

Each template entry may have `entryId`. The exporter writes it. The importer keeps it. The importer rejects a
duplicate ID inside one template with the existing validation error format.

## OpenAPI

All new members and endpoints get STE descriptions through the existing schema filters (see
`QueueHealthSchemaFilter`, `QueueSettingsSchemaFilter`).
