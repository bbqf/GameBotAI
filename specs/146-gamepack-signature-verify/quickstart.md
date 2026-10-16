# Quickstart: Gamepack signature verify

## Set up

1. Make a P-256 key pair (test helper or `ECDsa.Create(ECCurve.NamedCurves.nistP256)`).
2. Add the public key: `POST /api/gamepacks/signature/keys` with `{ "label": "Builder A", "publicKey": "<base64 SPKI>" }`.
3. Set the policy: `PUT /api/gamepacks/signature/policy` with `{ "policy": "required" }`.

## Install a signed gamepack

Send `POST /api/gamepacks/install` as `multipart/form-data` with the parts `gamepack` and `signature`. The result has `signature.status = "valid"`.

## Try the bad cases (policy `required`)

| Change | Expected status | HTTP |
|--------|-----------------|------|
| Send no `signature` part | `missing` | 422 |
| Change one byte of the signature | `invalid` | 422 |
| Change one sequence in the gamepack | `content_mismatch` | 422 |
| Sign with a key that is not on the list | `untrusted` | 422 |

After each 422 the registry, sequences, commands, images, and settings file are as before.

## Try `optional` and `off`

- `optional`: each case above installs (200) and the result shows its status.
- `off`: the install ignores the `signature` part and the result shows `not_checked`.

## Dry run

`POST /api/gamepacks/install?dryRun=true` with a bad signature under `required` gives 200 with `signature.wouldReject = true`. No change.

## Test map

| Requirement | Test owner |
|-------------|-----------|
| FR-001, FR-003 | Unit: hasher and verifier (extra file, missing file, changed byte) |
| FR-002, FR-007, SC-002 | Contract and integration: state compare after each bad case |
| FR-004, FR-013 | Unit: settings store (add, list, remove, restart reload); contract: key routes |
| FR-005, FR-012, SC-006 | Unit and contract: untrusted, two keys, label change |
| FR-006, SC-005 | Contract: policy change and key add with no restart |
| FR-008, SC-003 | Contract: `optional` with the four bad cases |
| FR-009, SC-004 (User Story 3 only) | Contract: `off` with a read counter and a verifier spy |
| FR-010 | Unit: status order with many faults at once |
| FR-011 | Contract: rejection text has no hash, key, or offset |
| FR-014 | Contract: dry run `wouldReject` for all three policies |
| FR-015 | Existing gamepack tests unchanged; rollback and list not checked |
| FR-016 | Log capture test: status and policy present, no key text |
| FR-017 | Doc review task for `docs/gamepacks.md` |
| SC-001 | Contract: a valid gamepack under all three policies |
| SC-007 | Timing test with a 50 MB gamepack |
| Clarification A2 | Contract: token rule on each new route |
| Clarification A4 | Contract: unparsable gamepack under `required` gives the parse error |
| Clarification A11 | Unit: canonical JSON strings and numbers |
