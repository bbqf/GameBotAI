# Contract: Signature format

## Signed statement

The signer builds this object, writes it as canonical JSON, and signs the UTF-8 bytes:

```json
{
  "format": 1,
  "manifest": { "name": "...", "version": "..." },
  "entries": [
    { "type": "command", "name": "a", "sha256": "<hex>" },
    { "type": "image", "name": "icon-1", "sha256": "<hex>" },
    { "type": "sequence", "name": "s", "sha256": "<hex>" }
  ]
}
```

## Canonical JSON

One fixed form. Equal content gives equal bytes and equal hashes.

- No white space between tokens.
- Object keys are in ordinal (UTF-16 code unit) order, at every level.
- Array order stays as in the source.
- Strings are UTF-8. Only these are escaped: `"` as `\"`, `\` as `\\`, and control characters below U+0020 as `\b`, `\f`, `\n`, `\r`, `\t`, or else `\u00xx` (lower-case hex). Every other character is written raw, also non-ASCII characters and `/`.
- A whole number with an absolute value up to 2^53 is written as plain digits (no plus sign, no leading zero, no fraction, no exponent). Negative zero is `0`. Any other number uses the shortest round-trip form of a double (invariant culture).
- `true`, `false`, and `null` are lower case.

## Entries

- One entry for each item of `sequences`, `commands`, and `images` of the gamepack file.
- `type` is `sequence`, `command`, or `image`. `name` is the `name` (sequence, command) or the `id` (image).
- `sha256` is the SHA-256 of the canonical JSON of the item, as lower-case hex.
- Sort order: by `type`, then by `name` in lower case (ordinal).

## Algorithm

ECDSA, curve P-256 (nistP256), SHA-256. The signature is 64 bytes in the IEEE P1363 format (r then s), base64 text.
The public key is SPKI DER, base64 text. The fingerprint is the lower-case hex SHA-256 of the SPKI bytes.

## Signature file

```json
{
  "format": 1,
  "algorithm": "ecdsa-p256-sha256",
  "publicKey": "<base64 SPKI>",
  "manifest": { "name": "...", "version": "..." },
  "entries": [ { "type": "...", "name": "...", "sha256": "<hex>" } ],
  "signature": "<base64>"
}
```

## Check order and status

The check starts after the gamepack is parsed. A gamepack that cannot be parsed gives the existing parse error.

1. No signature part: `missing`.
2. The file is empty, is not JSON, has an unknown `format` or `algorithm`, has a bad key or bad base64, or the signature
   does not verify over the statement built from the file with the embedded key: `invalid`.
3. The signed manifest or the signed entries differ from the gamepack (changed, extra, or missing entry): `content_mismatch`.
4. The fingerprint is not on the trusted list: `untrusted`.
5. Else `valid`.

The first fault that applies is the status. With policy `off`, the check does not run, no signature text is read, and the status is `not_checked`.
