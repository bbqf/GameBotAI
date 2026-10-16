# Contract: Signature API

All routes use the existing API token rule for all other API routes. No new role. A contract test checks each route below against this rule.

Status names are snake_case: `valid`, `missing`, `invalid`, `untrusted`, `content_mismatch`, `not_checked`.

## POST /api/gamepacks/install (changed)

Query `dryRun=true` is as before.

Input modes:

- `application/json`: the gamepack file. No signature (status `missing` when the policy is not `off`).
- `multipart/form-data`: part `gamepack` (JSON, required) and part `signature` (JSON, optional).

Under policy `off`, the route does not load the `signature` part and does not pass it to the service.

Response (200 or 422): the existing members plus

```json
{ "signature": { "status": "valid", "policy": "required", "wouldReject": false, "fingerprint": "<hex>" } }
```

Rules:

| Policy | Status other than valid | Result |
|--------|-------------------------|--------|
| required | any | Real install: 422, `valid: false`, nothing changed. Dry run: 200, `wouldReject: true`, only the signature reason (no validator problems). |
| optional | any | As before the feature. The status is in `signature`. `wouldReject` is false. |
| off | `not_checked` | As before the feature. The signature part is not read. `wouldReject` is false. |

A gamepack that cannot be parsed gives the existing parse error under every policy.

A rejected install gives one problem: category `signature`, object type `manifest`, message
`The gamepack signature status is '<status>'. The policy 'required' needs 'valid'. Nothing changed.`
The message names the status and gives no other detail.

Rollback (`POST /api/gamepacks/{name}/rollback`) and list (`GET /api/gamepacks`) are not changed and run no signature check.

## GET /api/gamepacks/signature/policy

200: `{ "policy": "off" }`

## PUT /api/gamepacks/signature/policy

Body: `{ "policy": "required" }`. 200 with the new value. 400 for an unknown value. The next install uses the value, with no restart.

## GET /api/gamepacks/signature/keys

200: `[ { "fingerprint": "<hex>", "label": "builder", "addedAtUtc": "..." } ]`

## POST /api/gamepacks/signature/keys

Body: `{ "publicKey": "<base64 SPKI or PEM>", "label": "builder" }`.
201 with the key entry. 400 for a malformed key, a key that is not P-256, or a label over 100 characters.
409 when the fingerprint is already on the list.

## DELETE /api/gamepacks/signature/keys/{fingerprint}

204 when removed. 404 when no key has this fingerprint.
