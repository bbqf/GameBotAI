# Contract: Queue settings export and import

Base path: `/api/queue-settings`. A new constant `ApiRoutes.QueueSettings` is added. The existing gamepack routes do not change.

## GET /api/queue-settings/export

Writes one settings document (see data-model.md).

- 200: `application/json`, the `SettingsDocument`. Header `Content-Disposition: attachment; filename="gamebot-settings.json"`.
- The document always has `formatVersion: 1`. With no data the lists are empty and the response is still 200.
- No instance id and no content of a gamepack-owned object is in the body. Each use of a gamepack-owned object is in `references`.

## POST /api/queue-settings/import

Body: a `SettingsDocument` (`application/json`).

- 200: `ImportReport`. The import ran. Some items can be `skipped` and `unresolvedReferences` can be non-empty. This is still 200 (FR-008).
- 400: problem response. The document is not valid JSON, the shape is wrong, `formatVersion` is not supported, or a name is duplicated. The detail names the cause (and the duplicate). Nothing changed (FR-009).
- 413: the body is larger than the route limit (20 MB).
- 500: a repository failed during the import (FR-018). The body is a problem response with a partial `ImportReport`: `items` (applied before the failure), `failed` (the item that threw, with a reason), and `notApplied` (items not tried). Items already written stay written. No object is deleted. A repeat of the import is safe (FR-011). To recover, the user fixes the cause and sends the same document again.

Exclusion (FR-017): the import waits while the gamepack gate lock is held, and holds the lock while it applies items. It makes no write before it has the lock. A gamepack install that starts during an import waits.

Query: none. The import has no dry-run option in the first release.

### Example report

```json
{
  "items": [
    { "type": "sequence", "name": "My Sequence", "outcome": "created", "reason": null, "restartRequired": false },
    { "type": "sequence", "name": "Daily Help", "outcome": "skipped", "reason": "owned_by_gamepack", "restartRequired": false },
    { "type": "queue", "name": "PNS Daily", "outcome": "updated", "reason": null, "restartRequired": true }
  ],
  "unresolvedReferences": [
    { "type": "sequence", "name": "Pit Mining", "usedBy": "queueTemplate:PNS Daily", "gamepack": "pns", "version": "1.4.0" }
  ],
  "summary": { "created": 1, "updated": 1, "unchanged": 0, "skipped": 1 }
}
```

## Rules

- The import never calls a delete method and never writes to an object with a gamepack owner (FR-006, FR-007).
- The import never stops or starts a queue (FR-016).
- OpenAPI: a schema filter adds descriptions for the new types, as feature 143 did for its types. Each route has `WithName`, `WithSummary`, and `WithDescription` in STE.
