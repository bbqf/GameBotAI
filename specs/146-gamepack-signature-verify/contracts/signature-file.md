# Contract: Signature file

The signature file is a UTF-8 JSON document. It is separate from the gamepack file.

```json
{
  "format": "gamepack-signature-1",
  "algorithm": "ecdsa-p256-sha256",
  "publicKey": "<base64 SubjectPublicKeyInfo of a P-256 key>",
  "manifestSha256": "<64 hex characters>",
  "files": [
    { "type": "command", "name": "Help All", "sha256": "<64 hex>" },
    { "type": "image", "name": "pit-title", "sha256": "<64 hex>" },
    { "type": "sequence", "name": "Pit Mining", "sha256": "<64 hex>" }
  ],
  "signature": "<base64 of 64 bytes: r then s>"
}
```

## Hashes

- `manifestSha256`: SHA-256 of the canonical JSON of the `manifest` object of the gamepack file.
- Each `files` item: SHA-256 of the canonical JSON of one entry of `sequences`, `commands`, or `images`. The `name` is the `name` field of a sequence or command, or the `id` of an image. Names are matched without case.
- The hash reads the raw JSON of the gamepack, so a field that the platform does not know is part of the hash.
- Hex is lower case.

## Canonical JSON

- Object keys sorted by ordinal compare. No white space.
- String escapes: `\"`, `\\`, `\b`, `\f`, `\n`, `\r`, `\t`, and `\u00xx` (lower case hex) for other control characters below U+0020. Other characters are written as UTF-8, not escaped.
- Integer numbers: no fraction, no exponent, no `+`. Other numbers: shortest round-trip `double` text, invariant culture, lower case `e`.
- `true`, `false`, `null` as literals.

## Signed bytes

The UTF-8 canonical JSON of `{ "files": [...], "format": "...", "manifestSha256": "..." }`. Keys are sorted. `files` is sorted by `type`, then by lower case `name`, both ordinal. Each item has the keys `name`, `sha256`, `type`.

## Key and fingerprint

- Key: ECDSA P-256, SubjectPublicKeyInfo, base64.
- Fingerprint: SHA-256 of the SPKI bytes, lower case hex.

## Status rules

See `research.md` R6. The order is: `missing`, `invalid`, `content_mismatch`, `untrusted`.

## Signing aid (for the docs)

1. Build the `files` list and `manifestSha256` from the gamepack file with the rules above.
2. Build the signed bytes.
3. Sign with `ECDsa.SignData(bytes, HashAlgorithmName.SHA256)` (P1363 form). Write it as base64.
4. Write the signature file.
