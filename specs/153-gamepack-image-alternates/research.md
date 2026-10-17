# Research: Gamepack image carries its alternates list

No item in the Technical Context needed clarification. This file records the design decisions.

## R1. Where to detect an unknown image member

- **Decision**: Detect it in `GamepackFileSerializer.Parse`, in the read of the `images` list, by comparing the property names of the JSON object with a fixed set of known names.
- **Rationale**: Only the parse still sees the raw JSON. After `Deserialize<GamepackImage>` an unknown member is already gone. Parse problems already flow into `ValidateAsync` and block the install.
- **Alternatives considered**: (a) `JsonUnmappedMemberHandling.Disallow` on the image type. It throws one `JsonException` with a generic text, and it applies to every use of the type. The message could not name the member in STE. (b) A `[JsonExtensionData]` dictionary on `GamepackImage`. It adds public API only to detect a name. Rejected as more surface than needed.

## R2. How to apply the existing alternates rules

- **Decision**: Call `ImageAlternatesValidator.Validate(primaryId, list, exists)` for each image. Pass an `exists` function over the ids of the file and the stored ids that the install keeps.
- **Rationale**: FR-003 asks for the same rules as the API. The validator already takes an `exists` function, so no change to it is needed. The limit of 8 applies (clarification 1).
- **Alternatives considered**: Copy the rules into the gamepack validator. Rejected: two copies drift apart.

## R3. When to store the list

- **Decision**: Store it in `WriteObjectAsync` for an image, right after `SaveAsync`.
- **Rationale**: The image write loop runs before commands and sequences. The alternates repository does not check that the target image exists, so the order of images in the file does not matter. A failure after this point triggers the existing undo.
- **Alternatives considered**: A second pass after all images are saved. Not needed, and it adds a second place to undo.

## R4. Absent member versus empty list

- **Decision**: Null (absent) keeps the stored list. A present empty list clears it. A new image with an absent member has an empty list.
- **Rationale**: Clarification 5. The declared list is the source of truth only when it is present.
- **Alternatives considered**: Absent clears the list. Rejected: a gamepack made before this feature would erase lists that the user set by API.

## R5. Undo and removal

- **Decision**: Keep the old list in the image `Snapshot` (also for a take-over, see R9). Restore sets it again, or deletes the list when the image did not exist. A removal also deletes the list.
- **Rationale**: FR-006 and SC-004 need a clean result after a failure. The image delete endpoint already deletes the list, so a removal must do the same to avoid a stale sidecar file.
- **Alternatives considered**: Leave stale lists. Rejected: a later image with the same id would inherit a wrong list.

## R6. Capture for rollback and update

- **Decision**: `CaptureLiveAsync` writes the stored list into the captured image only when the list is not empty.
- **Rationale**: A previous-version file then restores the lists. A file without lists stays byte for byte the same as today.
- **Known limit**: A rollback to a version that had no lists does not clear lists that a later version added, because absence keeps the list (R4). This matches the clarified rule. A user can clear a list with `PUT /api/images/{id}/alternates`.

## R8. Error text of the alternates list

- **Decision**: For each failed list, the problem message is the `Message` of `ImageAlternatesValidationResult`, then the image id, then the offending ids from `Ids`, then the `Hint`. The problem name is the image id. Example: `Image 'img-a': An image cannot be its own alternate. Ids: img-a. Remove the image's own id from the list.`
- **Rationale**: Clarification loop 2 A1: one message source for the API and the gamepack. The author gets the same advice in both places.
- **Alternatives considered**: New message text for the gamepack. Rejected: two sources drift apart.

## R9. Take-over backup, undo, and restore (feature 149 path)

- **Decision**: The image `Snapshot` holds the stored list (`ImageAlternates`, a list of text). `TakeSnapshotAsync` reads it with `GetAlternates`. `TakeOverBackupImage` gets an optional `Alternates` list. `ToBackupEntry` writes it when it is not empty. `ToOldSnapshot` reads it. `RestoreObjectAsync` sets the list with `SetAlternates`, which removes the sidecar file for an empty list.
- **Rationale**: The spec Assumptions require it. A take-over writes the list of the gamepack to the live image (when the member is present). A restore or undo must give the list of the user back. Without it, the list of the gamepack stays on an image that is the user's again.
- **Details**: The post-install hash (`PostHash`) of an image covers the bytes and content type only. It does not change. A change of the list alone after the install is not detected as "object changed". This keeps the rule of the hash as it is today and avoids a new hash format.
- **Alternatives considered**: Add the list to the post-install hash. Rejected: it changes the hash of every old backup and gives a false `ObjectChanged` refusal at restore.

## R7. Signing

- **Decision**: No change. `GamepackContentHasher` hashes each image entry from its raw JSON, so the new member is part of the hash.
- **Rationale**: Read of `Signing\GamepackContentHasher.cs`: the `images` list is read as generic JSON by the key `id`.
