# Data Model: Revalidate an image after an overwrite

No stored data changes. No new field, file, or store.

## Derived value

- **Image ETag** (not stored): a strong validator. It is computed on each `GET /api/images/{id}` from the stored bytes: the first 16 bytes of the SHA-256 hash, as 32 lowercase hex characters, in double quotes. Two different byte sequences give two different ETags. The same bytes always give the same ETag.

## Existing values used

- `ImageMetadata.UpdatedAtUtc`: the `Last-Modified` value (unchanged).
- `ImageMetadata.ContentType`: the `Content-Type` value (unchanged).
