# Data Model: Disk-backed Reference Images

## Entities

- ReferenceImage
  - id: string (1..128, `^[A-Za-z0-9_-]+$`)
  - format: enum (`png`, `jpg`, `jpeg`)
  - path: string (`data/images/{id}.{ext}`)
  - bytes: byte[] (optional during upload; not retained in memory by default)

## Validation Rules

- id MUST match regex; no `.` or path separators; trim whitespace; case-sensitive.
- data MUST decode as valid image (System.Drawing or ImageSharp equivalent built-in); else `invalid_image`.
- Overwrite allowed when `id` exists; write is atomic; replace file contents.

## State & Transitions

- Created → Persisted (file exists)
- Deleted → Removed (404 on resolve)
- Missing → Pending in evaluator with `reference_missing`

## Relationships

- Triggers (image-match) reference `ReferenceImage` by `referenceImageId`.