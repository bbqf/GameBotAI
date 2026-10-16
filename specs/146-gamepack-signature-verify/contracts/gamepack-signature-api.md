# Contract: Gamepack signature API

Base path: `/api/gamepacks`. New constant `ApiRoutes.GamepackSignature` = `ApiRoutes.Gamepacks + "/signature"`. All routes use the existing API token rule. No new role.

## Install and dry run (changed)

`POST /api/gamepacks/install[?dryRun=true]`

Request forms:

- `application/json`: the gamepack file. No signature. The status is `missing` (or `not_checked` under `off`).
- `multipart/form-data`: part `gamepack` (the gamepack file, JSON) and optional part `signature` (the signature file, JSON). Under policy `off` the route does not read the `signature` part.

Response additions (all responses that have the install response body):

```json
{
  "valid": true,
  "dryRun": false,
  "signature": { "status": "valid", "policy": "required", "wouldReject": false }
}
```

| Case | HTTP | Body |
|------|------|------|
| Installed | 200 | `signature.status` as found. Under `off`: `not_checked`. |
| Dry run | 200 | `signature.wouldReject` true only for `required` and a status other than `valid`. No change. |
| `required` and status not `valid` (install) | 422 | `valid: false`, `result: "signature_rejected"`, problem code `signature_<status>`. Nothing changed. |
| Parse error | 400 | The existing parse error. The signature check starts after parse. |
| Too large | 413 | As before (100 MB). |

The rejection text names the status only. It has no hash, key, or offset.

Rollback and list do not change and run no signature check.

## GET /api/gamepacks/signature/policy

- 200: `{ "policy": "off" }`

## PUT /api/gamepacks/signature/policy

Body: `{ "policy": "required" | "optional" | "off" }`

- 200: `{ "policy": "<new value>" }`. The next install uses it. No restart.
- 400: unknown value.

## GET /api/gamepacks/signature/keys

- 200: list of `{ "label", "fingerprint" }`. The response has no public key text.

## POST /api/gamepacks/signature/keys

Body: `{ "label": "Builder A", "publicKey": "<base64 SPKI>" }`

- 201: `{ "label", "fingerprint" }`.
- 400: label empty or too long, key not valid base64 SPKI, or key not P-256. Nothing is saved.
- 409: a key with this fingerprint is already on the list.

## DELETE /api/gamepacks/signature/keys/{fingerprint}

- 204: removed. The next install does not trust the key.
- 404: no key has this fingerprint.

## OpenAPI

All routes have a summary and a description in STE. The schema filter adds descriptions to the new types. The status names in the schema are the snake_case names.
