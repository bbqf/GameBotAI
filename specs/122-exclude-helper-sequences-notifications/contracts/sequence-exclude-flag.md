# Contract: Sequence Exclude Flag

Base path: `/api/sequences`. All bodies are JSON. The member name is `excludeFromSuccessNotifications`.

## Member

| Property | Type | Required | Meaning |
|----------|------|----------|---------|
| excludeFromSuccessNotifications | boolean | No | If true, queues send no "success" message for this sequence. Queues still send "failure", "cancelled" and "recovered" messages. |

## Requests

### POST /api/sequences

- Member absent: the stored value is false.
- Member `true` or `false`: stored as given.
- Works in the per-step body, the authoring body and the domain body.
- A dry run (`dryRun: true`) checks the member and does not store it.
- A client clone is a GET, then a POST with the same data. The POST keeps the value that the GET returned.

### PUT /api/sequences/{sequenceId}

- Member absent: the stored value does not change.
- Member `true` or `false`: stored as given.
- A body that has only this member (and a name or version) is valid.

### PATCH /api/sequences/{sequenceId}

- Member absent: the stored value does not change.
- Member `true` or `false`: stored as given.
- A body that has only this member is valid.

### Version and update time

A change of the member value (PUT or PATCH) increments `version` and sets `updatedAt`, the same as any
other change (FR-012). A request that sends the saved value is not a change.

### Errors

| Case | Status | Body |
|------|--------|------|
| Member is not a boolean (for example `null`, `"yes"`, `1`) | 400 | `{ "message": "Invalid sequence payload", "errors": ["excludeFromSuccessNotifications must be true or false."] }` |
| Sequence not found | 404 | As before |
| Version conflict | 409 | As before |

No error path returns 500. The check runs before any change is saved.

## Responses

GET `/api/sequences`, GET `/api/sequences/{sequenceId}`, and the POST, PUT and PATCH responses
include the member in every response shape:

```json
{ "id": "...", "name": "PNS.GuardPopups", "version": 3, "excludeFromSuccessNotifications": true }
```

The member is always present. The value is `false` for a sequence with no stored value.

## Backup archive

The sequence JSON file in a backup archive has the member when the value is true. A restore of an
archive with no member sets the value to false.

## OpenAPI

The schemas for the sequence request and response show the member as `boolean`. The description is:
"If true, queues send no success message for this sequence. Failure, cancelled and recovered messages
are still sent. If absent on PUT or PATCH, the stored value does not change."

## Notification behavior (reference, no API change)

The worker reads the flag when it handles a run job. No restart and no queue change are necessary.
