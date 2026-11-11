# Data Model: Single-Owner Sign-In for Internet Access

All files are in `data/auth/` (below the storage root). All times are UTC in ISO 8601 format. The backup and restore endpoints MUST NOT include this directory.

## Passkey store: `passkeys.json`

| Field | Type | Notes |
|-------|------|-------|
| `userHandle` | base64url text | 16 random bytes. Made at the first registration. The same for all passkeys. |
| `passkeys` | list of Passkey | Empty list means that no device can sign in (FR-008). |

### Passkey

| Field | Type | Notes |
|-------|------|-------|
| `id` | text | Short id for the UI and the command (first 8 hex characters of the SHA-256 of `credentialId`; longer if not unique). |
| `credentialId` | base64url text | From the authenticator. Unique. |
| `publicKey` | base64url text | COSE public key. Not a secret. |
| `signCount` | number | Last counter value. 0 for a synced passkey. |
| `name` | text | 1 to 40 characters. Default: "Device" plus the registration date. The owner can change it (FR-011). |
| `createdUtc` | time | Registration time. |
| `lastUsedUtc` | time or null | Last sign-in with this passkey. |

Rules:
- A registration with a `credentialId` that is in the list gets the usual refusal.
- A removal also removes all sessions with the same `passkeyId` (FR-011).
- Maximum 20 passkeys. One more registration gets the usual refusal, and the sign-in record gives the cause.

## Session store: `sessions.json`

| Field | Type | Notes |
|-------|------|-------|
| `tokenHash` | hex text | SHA-256 of the session token. The token itself is only in the cookie. |
| `passkeyId` | text | The passkey that made the session. |
| `createdUtc` | time | Sign-in time. |
| `lastUsedUtc` | time | Written at most one time in each hour. |
| `expiresUtc` | time | `lastUsedUtc` plus 30 days (FR-013). |

State changes:
- **Made**: a correct sign-in or a correct registration.
- **Extended**: each use moves `expiresUtc` to 30 days after that use.
- **Ended**: sign-out (this session), end-all (all sessions), passkey removal (sessions of that passkey), expiry, or the `end-sessions` command.
- The service removes expired sessions when it writes the file.

## Registration code: `registration-code.json`

The file is present only while a code is active.

| Field | Type | Notes |
|-------|------|-------|
| `codeHash` | hex text | SHA-256 of the code without the hyphen, in upper case. |
| `expiresUtc` | time | Creation time plus 10 minutes (FR-010). |
| `failedAttempts` | number | The 5th failed attempt deletes the file. |

State changes: made by `auth new-code` (replaces an older code) → used (file deleted) or expired (ignored, then deleted) or cancelled after 5 failed attempts (file deleted).

## Sign-in record: `sign-in-records.jsonl`

One JSON object in each line. The file rolls to `sign-in-records.1.jsonl` at 1 MB.

| Field | Type | Notes |
|-------|------|-------|
| `timeUtc` | time | |
| `event` | text | `sign_in`, `sign_in_failed`, `sign_out`, `sessions_ended`, `passkey_registered`, `registration_failed`, `passkey_removed`, `source_blocked`, `remote_access_refused`, `proxy_no_client_address` |
| `source` | text | The source address (research R8). `local-command` for the command. |
| `passkeyId` | text or null | When the event has a passkey. |
| `detail` | text or null | Short cause for the owner, for example `code_expired`. No secret (FR-017). For `remote_access_refused`, the text is always: "The sign-in function is off. Set GAMEBOT_AUTH_ENABLED=true to permit access from the internet." (FR-024). |

## In-memory only

- **Pending ceremony**: `ceremonyId`, kind (sign-in or registration), the challenge options, the source address, expiry (5 minutes). Maximum 100 in total and 5 for each source address (research R8).
- **Failed-attempt counts**: for each source, the times of failed attempts and the block end time.

## Settings (not in `data/auth/`)

| Setting | Environment | Default | Notes |
|---------|-------------|---------|-------|
| `Service:Auth:Enabled` | `GAMEBOT_AUTH_ENABLED` | `false` | FR-022 |
| `Service:Auth:PublicOrigin` | `GAMEBOT_AUTH_PUBLIC_ORIGIN` | none | `https` origin. Necessary when the sign-in function is on. |
| `Service:Auth:TrustedProxies` | `GAMEBOT_AUTH_TRUSTED_PROXIES` | empty | Comma list of IP addresses and CIDR ranges. Each request from these is an internet request (FR-027, FR-028). Loopback addresses are permitted. |
| `Service:Auth:Token` | `GAMEBOT_AUTH_TOKEN` | none | Existing. Local machine only when the sign-in function is on (FR-015). |
