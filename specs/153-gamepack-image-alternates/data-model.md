# Data Model: Gamepack image carries its alternates list

## GamepackImage (changed)

| Member | Type | Rule |
|--------|------|------|
| `id` | text | Required. Valid image id. Unique without case in the file. Unchanged. |
| `contentType` | text | `image/png` or `image/jpeg`. Unchanged. |
| `filename` | text | Optional. Unchanged. |
| `dataBase64` | text | Required. Unchanged. |
| `alternates` | list of text, or absent | New. Optional. See the rules below. |

Known member names (compare without case): `id`, `contentType`, `filename`, `dataBase64`, `alternates`. Any other name
is an error.

### Rules for `alternates`

- Absent (null): the install does not change the stored list. A new image has an empty list.
- Present and empty: valid. The install clears the stored list.
- Present: at most 8 ids. Each id is a valid image id. No id equals the id of the image. No duplicate (without case).
  Each id names an image of the file, or a stored image that the install does not remove.
- Wrong type for the member, or a non-text item: Structure problem of type Image that names the image.

These rules come from `ImageAlternatesValidator`. This feature adds no rule.

## Alternates list (stored, unchanged)

One sidecar file per primary image: `<imageRoot>\.alternates\{id}.json`. Interface: `IImageAlternatesRepository`
(`GetAlternates`, `SetAlternates`, `DeleteAlternates`). An empty list removes the file.

## TakeOverBackupImage (changed)

New optional member `Alternates` (list of text). It holds the stored list of the live image before the take-over. Null or
absent means that the image had no stored list. An old backup file has no member and reads as no list. The restore sets
the list (or removes it when empty). `PostHash` does not change.

## Image snapshot (internal, changed)

The undo snapshot of an image also holds the stored list. Install undo and take-over restore both use it.

## Problem (existing type, new cases)

Category `Structure`, type `Image`, name = image id.

| Case | Message content |
|------|-----------------|
| Unknown member | Image id, member name, list of known members |
| Alternates rule failure | Message of `ImageAlternatesValidator`, image id, offending ids, hint of `ImageAlternatesValidator` |
| Wrong shape | Existing "wrong shape" text |

## State transitions

| Event | Stored list of the image |
|-------|--------------------------|
| Install, new image, list present | Set to the declared list |
| Install, new image, list absent | Empty |
| Re-install, list present (bytes changed or not) | Set to the declared list |
| Re-install, list absent | Not changed |
| Image removed by the install | Deleted |
| Apply failure | Restored to the list before the install |
| Take-over of an image | Set to the declared list when present; the old list goes to the backup |
| Restore of a take-over backup | Set to the old list from the backup (removed when it was empty) |
