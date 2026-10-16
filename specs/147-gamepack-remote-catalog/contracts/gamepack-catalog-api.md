# Contract: Gamepack catalog API

All routes follow the existing API token rule (FR-018). Error body: the existing problem format with `code` and `message`.

## GET /api/gamepacks/catalog/settings

200: `{ "location": string|null, "allowInsecure": bool, "tokenSet": bool }`. The token is never in the body.

## PUT /api/gamepacks/catalog/settings

Body: `{ "location": string|null, "allowInsecure": bool, "token": string|null }`.
Token: null keeps the saved token. Empty string clears it. Other text sets it.
200: same as GET. 400 `invalid_location` when the location is not valid.

## GET /api/gamepacks/catalog

200:

```json
{ "gamepacks": [ { "name": "...", "installedVersion": "1.0.0",
  "versions": [ { "version": "1.2.0", "changelog": "...", "installed": false, "updateAvailable": true } ] } ] }
```

Errors (the instance never changes): 409 `not_configured`; 400 `invalid_location`; 502 `unreachable`, `index_invalid`, `index_too_large`, `redirect_refused`; 401/403 passed as 502 with `unauthorized` or `forbidden` (the API token of the caller is not the catalog token); 504 `timeout`.

A missing `index.json` (folder: file not found; web address: the catalog answers "not found" for the index) gives the reason `index_invalid` with HTTP 502, the same status as the other `index_invalid` cases.

## POST /api/gamepacks/catalog/install

Body: `{ "name": "...", "version": "...", "dryRun": bool }`.
200: same body as the manual install route (feature 143 and 146 shape: status, counts, signature block, dryRun flag).
Errors: 404 `entry_not_listed` (before any download); 409 `not_configured`; 502 `file_missing`, `file_too_large`, `hash_mismatch`, `unreachable`, `unauthorized`, `forbidden`; 504 `timeout`; 422 for a signature rejection and the other install rejections, with the same body as the manual install route.

The install route downloads the optional signature file of the entry with the same rules as the gamepack file (same host, token rule, request time limit, 1 MB size limit). A failed signature download uses the same error codes as a failed file download. An entry with no signature file gives the signature status "missing" in the feature 146 check.

`not_found` applies only to a listed file that the catalog answers with "not found". An unlisted name or version always gives `entry_not_listed`.

## Not in this contract

No route installs without a request. No route starts a poll.
