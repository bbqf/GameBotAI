# Data Model: Gamepack signature verify

## SignaturePolicy (enum)

| Value | JSON text | Meaning |
|-------|-----------|---------|
| Required | `required` | Reject a gamepack whose status is not `valid`. |
| Optional | `optional` | Install always. Report the status. |
| Off | `off` | No check. No signature read. Default. |

## SignatureStatus (enum)

| Value | JSON text | Meaning |
|-------|-----------|---------|
| Valid | `valid` | Signature verifies, hashes match, key is trusted. |
| Missing | `missing` | No signature was sent. |
| Invalid | `invalid` | Empty, bad format, bad encoding, or the crypto test fails. |
| ContentMismatch | `content_mismatch` | Recomputed hashes differ from the signed list (change, extra, or missing file). |
| Untrusted | `untrusted` | Signature is sound, but the fingerprint is not on the trusted list. |
| NotChecked | `not_checked` | Policy is `off`. |

Precedence when more than one fault applies: `missing`, `invalid`, `content_mismatch`, `untrusted`.

## TrustedKey

| Field | Type | Rule |
|-------|------|------|
| label | string | Required, 1 to 100 characters. Display only. No effect on trust. |
| fingerprint | string | SHA-256 of the SPKI bytes, lower case hex (64 characters). Unique. Set by the service. |
| publicKey | string | Base64 SPKI of an ECDSA P-256 key. Rejected on save if not valid. |

## GamepackSignatureSettings (file `gamepack-signature.json`)

| Field | Type | Rule |
|-------|------|------|
| policy | SignaturePolicy | Default `off` when the file is absent. |
| keys | list of TrustedKey | Zero or more. Fingerprints are unique. |

The file lives in the data root. A write goes through a temp file and an atomic replace. A read happens on each check.

## SignatureFile (the detached signature, parsed)

| Field | Type | Rule |
|-------|------|------|
| format | string | Must be `gamepack-signature-1`. |
| algorithm | string | Must be `ecdsa-p256-sha256`. |
| publicKey | string | Base64 SPKI of the signer key. Used for the crypto test only. |
| manifestSha256 | string | Lower case hex of the canonical manifest. |
| files | list | Each item: `type` (`sequence`, `command`, `image`), `name`, `sha256`. Unique by type and lower case name. |
| signature | string | Base64, 64 bytes (r then s). |

Signed bytes: the canonical JSON of `{ format, manifestSha256, files }` with `files` sorted by type then lower case name (ordinal). See [contracts/signature-file.md](contracts/signature-file.md).

## SignatureCheckResult (domain)

| Field | Type | Note |
|-------|------|------|
| status | SignatureStatus | |
| policy | SignaturePolicy | The policy at check time. |
| wouldReject | bool | True only for `required` and a status other than `valid`. |

## State changes

The check changes no state. Only the settings routes change `gamepack-signature.json`. A rejected install leaves the gamepack registry, sequences, commands, images, and the settings file as they were (SC-002).
