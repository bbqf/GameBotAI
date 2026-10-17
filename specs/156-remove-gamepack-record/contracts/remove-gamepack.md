# Contract: Remove an installed gamepack record

## Route

`DELETE /api/gamepacks/{name}`

- Tag: `Gamepacks`
- Operation id: `RemoveGamepack`
- Summary: Remove the record of an installed gamepack
- Description (STE): Removes the installed record of the gamepack, its kept previous version, and all its owner entries. It does not delete or change any sequence, command, or image. An item that stays belongs to the user after the removal. It does not delete take-over backups. A restore of such a backup then gives 409 with owner_changed. The route takes the same gate as install and rollback. A name that is not installed gives 404. A failed write gives 500 and the record stays as before.

## Request

- Path parameter `name`: the gamepack name. Case is ignored.
- No query. No body.

## Responses

| Status | Body | When |
|--------|------|------|
| 204 | none | The record was removed. |
| 404 | problem, `result` = `not_found`, title "No gamepack has this name." | The name is not installed, is not valid, or the record is already removed. |
| 500 | problem, `result` = `failed`, title "A write failed. All objects stay as before." | The registry write failed. Nothing changed. |

## Behaviour

1. After `204`, `GET /api/gamepacks` does not list the pack.
2. After `204`, `POST /api/gamepacks/{name}/rollback` gives `404 not_found`.
3. After `204`, an install of the same name is a first install. Objects of the same type and name that still exist belong to the user, so the conflict rule for user objects applies (conflict, or take-over with `takeOver=true`).
4. A second `DELETE` of the same name gives `404`.
5. Other installed packs do not change.
6. Install, dry run, signature, catalog, and rollback behave as before. A rollback of a pack with no kept previous version still gives `409 no_previous_version`.
