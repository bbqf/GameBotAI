# Contract: Image alternates (097)

## GET /api/images/{id}/alternates

200:
```json
{ "id": "pns-resource-cluster-anchor",
  "alternates": [
    { "id": "pns-resource-cluster-anchor-night1", "exists": true },
    { "id": "pns-resource-cluster-anchor-night2", "exists": false } ] }
```
404 `{ "error": { "code": "not_found", "message": "Image not found" } }` — primary not stored.
400 `{ "error": { "code": "invalid_id", ... } }` — id fails validation.

## PUT /api/images/{id}/alternates

Request: `{ "alternates": ["night1", "night2"] }` (`[]` clears).

200: same body as GET (all `exists: true`).
404 `not_found` — primary not stored.
400 `{ "error": { "code": "invalid_request", "message": "alternates is required", "hint": null } }` — body missing / `alternates` null.
400 `{ "error": { "code": "invalid_alternates", "message": "<reason>", "hint": "<remediation>", "ids": ["..."] } }` for:
- more than 8 entries (message names the limit 8);
- an entry equal to the primary (`ids` = [primary]);
- duplicate entries (`ids` = duplicated ids);
- entries with an invalid id or not stored (`ids` = offending ids).
On any 400/404 the stored list is unchanged.

## GET /api/images/{id}/metadata (additive)

Adds `"alternates": ["night1", "night2"]` (ids, registered order; `[]` when none).

## DELETE /api/images/{id} (behaviour)

On success also removes the image's own alternates list. Lists of other primaries naming it are kept
(the id then reports `exists: false`).

## POST /api/images/detect (additive)

Each `matches[]` item adds `"matchedReferenceId": "<id>"`. `templateId` is still the requested
`referenceImageId`. With alternates, matches are the union across references, ordered by score
(ties: primary, then alternates in order), de-duplicated by `overlap`, capped at `maxResults`.

## Unchanged

`POST /api/images/detect-all`; all sequence/command/trigger execution result shapes.
