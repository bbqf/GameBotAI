# Research: Verify the signature of a gamepack before install

No item is marked NEEDS CLARIFICATION. Each decision follows.

## R1. Signature algorithm

- Decision: ECDSA with curve P-256 and SHA-256. The signature is in IEEE P1363 form (r then s, 64 bytes), as base64 text. The public key is the SubjectPublicKeyInfo form, as base64 text.
- Rationale: `System.Security.Cryptography.ECDsa` has this in the platform runtime on Windows. It needs no new package (Assumptions). Tools such as OpenSSL and .NET can sign with it.
- Alternatives: Ed25519 (the .NET runtime has no support, so it needs a package); RSA (larger keys, slower, no gain); a certificate chain (excluded by FR-012).

## R2. What is signed

- Decision: The signed bytes are the canonical JSON of the object `{ "format", "manifestSha256", "files": [ { "type", "name", "sha256" } ] }`. `files` is sorted by type then name (ordinal, lower case). A file is one sequence, one command, or one image. The name of an image is its id.
- Rationale: The signature then covers the manifest and the hash of each content file (FR-001). The list shows an extra file or a missing file (FR-003).
- Alternatives: Sign the whole file bytes (the line breaks and key order would break the signature, and no "content mismatch" detail); sign each file alone (many signatures, no manifest tie).

## R3. Canonical JSON (clarification A11)

- Decision: One writer, `CanonicalJson`. It sorts object keys by ordinal compare. It writes no white space. A string uses `\"`, `\\`, `\b`, `\f`, `\n`, `\r`, `\t`, and `\u00XX` (lower case hex) for other control characters; all other characters are written as UTF-8 and are not escaped. A number that is an integer is written with no fraction or exponent. Another number is written with the shortest round-trip form of `double`, invariant culture, with a lower case `e` and no `+`. `true`, `false`, and `null` are literal. Hashing reads the raw JSON text with `JsonDocument` so unknown fields are part of the hash.
- Rationale: Equal content must give equal hashes. A fixed form does not depend on the serializer settings of the instance.
- Alternatives: Hash the model after deserialization (drops unknown fields, so a forger can add data); RFC 8785 in full (not needed; this subset is enough and a test covers it).

## R4. Delivery of the signature (detached)

- Decision: The install route accepts `multipart/form-data` with the part `gamepack` (the file) and the optional part `signature` (the signature file). The existing raw `application/json` request stays valid and has no signature (status `missing`). Under policy `off`, the route reads the `gamepack` part only and skips the `signature` part without any load or parse (clarification A1). The 100 MB limit stays for the whole request.
- Rationale: The hash list can have hundreds of lines, which does not fit in a header (Kestrel header limit). A second part keeps the file detached and the old request unchanged (FR-015).
- Alternatives: A request header (size limit); a second route to upload the signature first (state between calls, harder to keep atomic); a query parameter (size limit).

## R5. Where the check runs

- Decision: In `GamepackService.InstallAsync`, after `GamepackFileSerializer.Parse` and before the gate wait and the validator. The parse error stays as before (clarification A4). A dry run that the gate rejects returns only the signature reason (clarification A4). Rollback and list do not call the checker (clarification A12).
- Rationale: FR-002: the check ends before any state change. The service already has the parsed text, so the hasher uses the raw text of the same request.
- Alternatives: A check in the endpoint (the service would be open to callers with no check); a check inside the validator (mixes version and signature checks; FR-015).

## R6. Status order and trust

- Decision: The verifier runs these steps and returns the first fault.
  1. No signature text: `missing`.
  2. The text is empty, not JSON, has an unknown `format` or `algorithm`, has a bad key or signature encoding, or the signature does not verify with the key in the file: `invalid`.
  3. The recomputed hashes differ from the signed list (change, extra, or missing file): `content_mismatch`.
  4. The fingerprint of the key in the file is not on the trusted list: `untrusted`.
  5. Otherwise `valid`.
  Under `off` the status is `not_checked`.
- Rationale: This is the order from the Clarifications (missing, invalid, content mismatch, untrusted). The key in the file is only a test input for the crypto. Trust is only "the fingerprint is on the list" (FR-012, FR-005). With no trusted key the status is `untrusted`.
- Alternatives: Verify against each trusted key only (cannot tell `invalid` from `untrusted`); trust the label or a name (excluded by FR-012).

## R7. Policy and key storage

- Decision: One file `gamepack-signature.json` in the data root: `{ "policy": "off", "keys": [ { "label", "fingerprint", "publicKey" } ] }`. The store reads the file on each check (a cache on the file time stamp is allowed) and writes through a temp file and an atomic replace. A lock guards writes. The default is `off` when the file is absent. The fingerprint is the SHA-256 of the SPKI bytes, in lower case hex. Add rejects a key that is not valid SPKI for P-256, an empty label, and a duplicate fingerprint (edge case: malformed entry).
- Rationale: Persistence (FR-013), no restart (SC-005), and a defined "instance state" for SC-002. The existing gamepack registry store follows the same style.
- Alternatives: `appsettings` (a change needs a restart); the gamepack registry file (mixes concerns and risks the registry compare in SC-002).

## R8. Who may change the settings (clarification A2)

- Decision: The new routes use the same API token rule as all other API routes. No new role. A contract test checks each new route.
- Rationale: No "settings permission" exists in the solution.
- Alternatives: A new role (excluded by Assumptions).

## R9. Result and rejection shape

- Decision: The install and dry-run response gets a `signature` block: `status`, `policy`, `wouldReject`. A rejected install returns HTTP 422 with `valid: false`, `result: "signature_rejected"`, and a problem whose code is `signature_<status>`. The text names the status and gives no hash, no key, and no offset (FR-011). A dry run returns 200 and sets `wouldReject` (FR-014). `wouldReject` is true only for `required` with a status other than `valid`.
- Rationale: The 422 code matches the existing invalid-install code. The new block is additive, so old clients work (FR-015).
- Alternatives: HTTP 403 or 401 (it is not an auth error); a new top-level response type (breaks clients).

## R10. Logging (FR-016)

- Decision: `GamepackSignatureChecker` writes one `LoggerMessage` per check, with `Status` and `Policy` as the only arguments. A new event id in the range after 1431 is used. A log-capture test reads the record and checks that it has the status and the policy and no key text.
- Rationale: Source-generated logging is the project style. No key value is ever an argument.
- Alternatives: Log the fingerprint (a key identifier is not a secret, but the spec says "status and policy", so keep to it).

## R11. Performance (SC-007)

- Decision: Hash each content file from the parsed `JsonElement` once, in one pass, with `IncrementalHash` over the canonical bytes written to a pooled buffer. No copy of the whole gamepack is made.
- Rationale: The work is linear. A 50 MB file is mostly base64 image text, which hashes at hundreds of MB per second.
- Alternatives: Hash in parallel (not needed for the goal).

## R12. Builder aid

- Decision: No signing tool in the product. The documentation shows how to sign with a short script, and the unit tests hold a helper that signs a gamepack with a test key. The test helper is the proof that the format is documented well enough.
- Rationale: Non-goals allow an aid only if the plan finds it needed. The test helper and the docs are enough.
- Alternatives: A CLI tool (more code to own, not asked for).
