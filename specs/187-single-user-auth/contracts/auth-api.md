# Contract: Auth API

Base path: `/api/auth`. All bodies are JSON. Binary values are base64url text.

## Fixed refusals

| Case | Status | Body |
|------|--------|------|
| A-1. No access, for each cause (no session, expired session, wrong token, token from the source `Internet`, wrong or expired code, passkey not registered, blocked source, setting not valid, wrong `Origin`) | `401` | `{"error":{"code":"unauthorized","message":"Sign-in is necessary.","hint":null}}` |
| A-2. Sign-in function off and the source is `Internet` | `403` | `{"error":{"code":"remote_access_refused","message":"The service refuses this request.","hint":null}}` |

The body of A-1 MUST be the same bytes for each cause (FR-018). `/health` stays `200` in all cases (FR-002).

## Access rules for all other endpoints

| Sign-in function | Source | Access token configured | Result |
|------------------|--------|-------------------------|--------|
| off | `LocalMachine`, `HomeNetwork` | no | pass (as before this feature) |
| off | `LocalMachine`, `HomeNetwork` | yes | token necessary (as before this feature) |
| off | `Internet` | any | A-2 |
| on | `LocalMachine`, `HomeNetwork` | yes, and the request has the correct token | pass |
| on | `Internet` | yes, and the request has the correct token and no session | A-1 |
| on | any | any, with a valid session | pass |
| on | any | all other cases | A-1 |

A request from an address in `TrustedProxies` always has the source `Internet`, with and without forward headers, and with each `Host` value (FR-028). So a proxy on a different host gets the `Internet` rows of this table.

Open with the sign-in function on: `/health`, web UI static files and the `index.html` fallback, `OPTIONS`, and the endpoints C-1 to C-5 below. `/swagger` is not open.

## Endpoints

### C-1. `GET /api/auth/status` (open)

Response `200`:

```json
{ "signInEnabled": true, "signedIn": false, "passkeysSupportedHere": true, "source": "internet", "publicOrigin": null, "tokenAccessHere": false }
```

- `signedIn` is true when the request has access by a session or by a valid access token, or when the sign-in function is off and the source passes.
- `passkeysSupportedHere` is true when the request came from a trusted proxy, or when its `Host` header is the host of the public origin. In other cases (for example `http://localhost:8080` or `http://192.168.1.20:8080`) it is false.
- `source` is `localMachine`, `homeNetwork`, or `internet`.
- `publicOrigin` has the configured public origin only when `source` is `localMachine` or `homeNetwork`. For `internet` it is null. The UI uses it to show the address to use (FR-031).
- `tokenAccessHere` is true when an access token is configured and `source` is `localMachine` or `homeNetwork`. The UI shows the token prompt only when it is true. For `internet` it is always false.
- With the sign-in function on, a request with a wrong bearer token counts for the limiter (FR-016).
- The response MUST NOT show if a passkey or a code exists.

### C-2. `POST /api/auth/sign-in/options` (open)

Request: empty body. Response `200`:

```json
{ "ceremonyId": "…", "publicKey": { "challenge": "…", "rpId": "bot.example.com", "timeout": 60000, "userVerification": "required", "allowCredentials": [] } }
```

The response is the same when no passkey is registered. One source address has at most 5 pending ceremonies (C-2 and C-4 together). One more removes the oldest ceremony of that source. This gives no refusal.

### C-3. `POST /api/auth/sign-in/complete` (open)

Request:

```json
{ "ceremonyId": "…", "credential": { "id": "…", "rawId": "…", "type": "public-key", "response": { "clientDataJSON": "…", "authenticatorData": "…", "signature": "…", "userHandle": "…" } } }
```

- Success: `204` and `Set-Cookie: __Host-gamebot-session=…; Path=/; Secure; HttpOnly; SameSite=Strict; Max-Age=2592000`.
- Failure: A-1. The failure counts for the limiter (5 in 5 minutes, then 15 minutes block).

### C-4. `POST /api/auth/register/options` (open, needs a code)

Request: `{ "code": "ABCD-EFGH" }`. The service ignores case, spaces, and the hyphen.

- Correct code: `200` with `{ "ceremonyId": "…", "publicKey": { … creation options: rp, user, challenge, pubKeyCredParams, excludeCredentials, authenticatorSelection { residentKey: "required", userVerification: "required" }, attestation: "none" } }`.
- Wrong, used, or expired code, or no active code: A-1. The failure counts for the limiter and for the 5-attempt limit of the code.

### C-5. `POST /api/auth/register/complete` (open, needs the ceremony of C-4)

Request: `{ "ceremonyId": "…", "name": "My phone", "credential": { "id": "…", "rawId": "…", "type": "public-key", "response": { "clientDataJSON": "…", "attestationObject": "…" } } }`

- Success: `204`, the session cookie as in C-3. The code is used and cannot be used again.
- Failure: A-1. The code stays active until its expiry or its attempt limit.
- `name`: optional, 1 to 40 characters after trim. Not valid → default name.

### C-6. `POST /api/auth/sign-out` (session)

`204`. Ends this session and clears the cookie. With no session: `204` also.

### C-7. `POST /api/auth/sessions/end-all` (session)

`204`. Ends all sessions, this one included, and clears the cookie (FR-014).

### C-8. `GET /api/auth/passkeys` (session)

Response `200`:

```json
{ "passkeys": [ { "id": "3fa9c1d2", "name": "My phone", "createdUtc": "2026-11-10T18:00:00Z", "lastUsedUtc": "2026-11-11T07:30:00Z", "current": true } ] }
```

`current` is true for the passkey of this session. The response has no key material.

### C-9. `PATCH /api/auth/passkeys/{id}` (session)

Request: `{ "name": "Work laptop" }`. `200` with the changed item. Name not valid: `400` with code `invalid_name`. Unknown id: `404`.

### C-10. `DELETE /api/auth/passkeys/{id}` (session)

`204`. Removes the passkey and ends its sessions. If it is the passkey of this session, the response clears the cookie. Unknown id: `404`. The removal of the last passkey is permitted; the UI gives a warning first.

A request with a valid access token and no session can also use C-6 to C-10. In that case, `current` is false for each passkey.

## Cross-site rule

For a request with a session cookie and a method other than `GET`, `HEAD`, or `OPTIONS`: an `Origin` header that is present and is not the public origin gives A-1 (FR-020).
