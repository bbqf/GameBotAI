# Data Model: Alternate Reference Images (097)

## ImageAlternates (persisted)

| Field | Type | Rules |
|-------|------|-------|
| primaryId | string (file name) | valid reference image id (`ReferenceImageIdValidator`); primary must exist when written |
| alternates | string[] | 0–8 entries; each a valid id of a stored image at write time; not the primary; no duplicates (case-insensitive); order significant |

- File: `<imagesRoot>\.alternates\{primaryId}.json` → `{ "alternates": ["b", "c"] }`.
- Absent file ≡ empty list. Writing an empty list deletes the file.
- Lifecycle: created/replaced by `PUT /api/images/{id}/alternates`; removed when the list is cleared or
  the primary is deleted via `DELETE /api/images/{id}`; kept on primary overwrite.
- An alternate image may be deleted later; its id stays in the list and is reported `exists: false`.

## ReferenceImageSet (in-memory, per detection)

| Field | Type | Notes |
|-------|------|-------|
| PrimaryId | string | the named image |
| Primary | Bitmap | loaded from `IReferenceImageStore` |
| Alternates | IReadOnlyList<(string Id, Bitmap Image)> | existing direct alternates, registered order |
| MissingAlternateIds | IReadOnlyList<string> | listed but not stored |

No transitive expansion: an alternate's own alternates are never loaded.

## TemplateMatch (domain, additive)

| Field | Change |
|-------|--------|
| ReferenceId | new `string?` init-only; set by `ReferenceSetTemplateMatcher`; null from the plain matcher |

## Detect response `MatchResult` (wire, additive)

| Field | Type | Notes |
|-------|------|-------|
| templateId | string | unchanged: the named (primary) image id |
| matchedReferenceId | string | new: id of the reference that produced the match; equals `templateId` for a primary match or an image without alternates |

## Backup archive (additive)

- `images/{id}.png` now also includes each selected image's existing alternates.
- `image-alternates/{primaryId}.json` → `{ "alternates": [...] }` for each selected primary with alternates.
