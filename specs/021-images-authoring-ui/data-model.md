# Data Model: Images Authoring UI

## Entities

### ImageAsset
- **id**: string (author-supplied; unique; allowed chars: A-Z, a-z, 0-9, dash, underscore)
- **filename**: string (optional; original client filename)
- **contentType**: string (must be image/png or image/jpeg)
- **sizeBytes**: integer (0 < sizeBytes ≤ 10_000_000)
- **createdAtUtc**: datetime (set on first save)
- **updatedAtUtc**: datetime (set on each overwrite)
- **data**: binary (stored content; served via GET /api/images/{id} with correct content type)

**Constraints & Rules**
- ID uniqueness enforced across the image store.
- Reject uploads where sizeBytes exceeds 10 MB or contentType is not allowed.
- Overwrite keeps the same ID but updates filename/contentType/sizeBytes/updatedAtUtc.

### TriggerReference
- **triggerId**: string (identifier of the trigger)
- **imageId**: string (references ImageAsset.id)

**Constraints & Rules**
- A TriggerReference requires the target ImageAsset to exist.
- Deleting an ImageAsset is blocked if any TriggerReference exists for its id; conflict response includes referencing triggerIds.

## Relationships
- One ImageAsset can be referenced by many TriggerReferences; deletion requires zero references.
- TriggerReference.imageId uses the same ID format rules as ImageAsset.id.
