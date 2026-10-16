# Research: Take over same-named user objects on gamepack install

All items from the Technical Context are resolved. No NEEDS CLARIFICATION remains.

## R1. How the validator finds a take-over

**Decision**: Add a `takeOver` flag to `GamepackValidator.ValidateAsync` (default false). In `Decide`, an existing object with the same type and name has one of three owners:

- The same gamepack: it is an update (as today).
- The user, and `takeOver` is true: it is a take-over. The decision keeps the id of the user object and marks the write as a take-over.
- Anything else: it is a conflict (as today).

If two user objects have the same name (case-insensitive), the first one is taken over. The other one stays a conflict. This keeps one object for one name.

**Rationale**: The conflict rule lives in one place (`Decide`). A dry run, an install, and a rollback all use it. A rollback passes `takeOver` false, so it does not change.

**Alternatives considered**: (a) Delete the user object and create a new one. Rejected: a deleted object breaks every link while it is missing (the reason for this feature). (b) Do the take-over as a step before the validator. Rejected: the dry run and the install would use different code.

## R2. How a take-over is counted

**Decision**: A take-over write is a write with `ExistingId` set, as an update is. It has the new flag `IsTakeOver`. `GamepackPlan.Updated` counts only writes with `ExistingId` set and `IsTakeOver` false. New property `GamepackPlan.TakenOver` counts the take-over writes. `Created` does not change.

**Rationale**: SC-007 needs a count that equals the replaced objects. The spec says an object that the pack already owns is not a take-over (FR-012 in the clarifications). Separate counts keep the old `Updated` meaning, so the tests with the option off stay the same (SC-002).

**Alternatives considered**: Count a take-over in `Updated` and in `TakenOver`. Rejected: the sum of the counts would not match the number of writes.

## R3. How "identical" is decided

**Decision**: New class `GamepackContentComparer`. It makes a canonical text of an object and compares the two texts.

- Sequence and command: serialize the object with the same options as `GamepackFileSerializer` (`SequenceJsonOptions`, `CommandJsonOptions`). Parse the text to a `JsonElement`. Remove the volatile members (`createdAt`, `updatedAt`, `id` is kept because the plan sets the file id to the final id). Write the canonical text with `CanonicalJson`.
- Image: identical when the content type is equal and the bytes are equal (ordinal compare of the byte arrays). The file name is ignored.
- The compare runs after `BuildPlan`. At that point, the references in the file object are already rewritten to the final ids. So an unchanged user object gives the same text.

**Rationale**: `CanonicalJson` is already tested for signing. The compare uses the same rule as the Assumptions in the spec: "canonical content equals canonical content". The same function gives the post-install hash (R5).

**Alternatives considered**: A deep compare of the C# objects. Rejected: the polymorphic step types need a custom compare. The JSON route reuses the serializer and its converters.

## R4. Where the backup lives and its format

**Decision**: New `ITakeOverBackupStore` and `TakeOverBackupStore`. File: `<dataRoot>\gamepacks\takeover-backups\<backupId>.json`. The id has the form `to-yyyyMMddTHHmmssfff-<8 hex>` (regex `^to-[0-9]{8}T[0-9]{9}-[0-9a-f]{8}$`). The id has no path character. The store checks the id against the regex before every read, write, or delete. The store writes to a temporary file, then moves it over the target (the same pattern as `ManagedBackupStore`). The store has no retention rule (spec Assumptions).

Backup content: gamepack name and version, the time, and one entry for each replaced object. An entry has the type, name, stored id, the old content, and the post-install hash (R5). The old content is the sequence JSON, the command JSON, or the image (base64, content type, file name). The old content comes from the undo snapshots that the applier already takes.

**Rationale**: The platform keeps the data next to the other gamepack data (clarification Q1). A separate folder and a separate store keep the feature 148 backup (queue templates, 20 files) unchanged. A random suffix stops two backups in the same millisecond from clashing.

**Alternatives considered**: Reuse `ManagedBackupStore`. Rejected: it stores a `SettingsDocument`, it prunes to 20 files, and its id form is different. A prune rule could delete a user backup, which the spec forbids.

## R5. Atomic install and the order of writes

**Decision**: In `GamepackApplier.ApplyAsync`:

1. Take the undo snapshots (as today).
2. If the plan has take-overs, write the backup file. Add a new gate operation `GamepackWriteOperations.TakeOverBackup` so a test can inject a fault. If the write fails, return `Failed`. Nothing changed yet.
3. Write the objects, the previous-version file, and the registry (as today). The registry update sets the owner entries for the take-over writes, as for all writes.
4. If a step fails, restore as today. After a successful restore, delete the backup file (best effort, logged). After a partial restore, keep the file and log its id.

The outcome has `TakenOver` and `TakeOverBackupId`. The id is set only when at least one object was replaced and the apply worked.

**Rationale**: The backup exists before the first object changes (FR-008). A failed install leaves no backup that the client must use (US2 scenario 3). The existing restore code already puts back user objects, because a take-over write is an update with a snapshot.

**Alternatives considered**: Write the backup after the objects. Rejected: a crash between the two steps would lose the user content.

## R6. Restore operation and its safety check

**Decision**: New route `POST /api/gamepacks/takeover-backups/{backupId}/restore`. New methods `GamepackService.RestoreTakeOverAsync` and `GamepackApplier.RestoreTakeOverAsync`. The service holds the gate (FR-016).

Strict mode (the route) refuses the restore, and changes nothing, when one of these is true:

- `object_missing`: a backed-up object no longer exists.
- `object_changed`: the content hash of the live object differs from the post-install hash in the backup.
- `owner_changed`: the live object is not owned by the gamepack in the backup, or the gamepack is not installed.

The route gives 404 for an unknown or malformed id, 409 with the reasons for a refusal, 200 for success, and 500 for a failed write. The restore is atomic: it takes undo snapshots, writes the old content, updates the registry (removes the owner entries and the object refs of the gamepack record), and restores the snapshots on any failure.

Force mode (internal, for the managed update, R8) skips the check `owner_changed` only. It still checks `object_missing` and `object_changed`. The public route never uses force. The backup file stays after a restore. A second restore gets `owner_changed` and changes nothing.

A rollback of the gamepack can delete a taken-over object (it is not in the previous version). A later strict restore then gives `object_missing`. This follows the spec Assumptions: a restore after a deleted object is refused.

**Rationale**: The hash check stops the restore from overwriting an edit that the user made after the install. The hash is the same canonical text as in R3, so no new rule exists. The reasons are codes, so a client can act on them.

**Alternatives considered**: A restore that always overwrites. Rejected: it can lose newer user edits. A restore that also recreates deleted objects in strict mode. Rejected: the spec asks for a refusal.

## R7. Where the option enters the API

**Decision**:

- `POST /api/gamepacks/install`: query `takeOver=true` (same style as `dryRun`).
- `POST /api/gamepacks/update` (managed, file): query `takeOver=true`.
- `POST /api/gamepacks/catalog/install` and `POST /api/gamepacks/catalog/update`: body field `takeOver` (same style as `dryRun` in those bodies).
- Managed rollback (`managed=true` on the rollback route): the option does not apply. A rollback never takes over an object.

A missing value means false (US1 scenario 6).

**Rationale**: Each route keeps the style that it already has for `dryRun`.

**Alternatives considered**: One new header. Rejected: no other option uses a header.

## R8. Managed update: pass-through and failure restore

**Decision**: `ManagedUpdateRequest.TakeOver` goes to `GamepackService.InstallAsync` in both the dry-run check and the install step. The report gets `TakenOver`, `TakeOverBackupId`, and `TakeOvers` (dry run). The field `BackupId` keeps its meaning (the queue template backup).

If a managed call fails after the install worked (for example in the booking step), the flow already rolls the gamepack back or removes it. That step can delete a taken-over object. So the flow calls the force restore of the take-over backup BEFORE it rolls back or removes the gamepack. At that point the live objects still hold the content that the install wrote, so `object_missing` and `object_changed` pass. Force skips `owner_changed` only, for the case that the registry no longer lists the gamepack as owner. The restore removes the owner entries and object refs of the restored objects, so the later rollback or removal does not touch them. This puts the user objects back, owned by the user. The flow adds a step `takeover_restore` to the restore report. The failed report does not set `TakeOverBackupId`.

The option does not call `StopAsync` or `StartAsync`. The scope of stopped queues comes from the same rule as before (SC-008).

**Rationale**: Without this step, a failed managed update would lose the user content, which breaks the aim of FR-007.

**Alternatives considered**: Leave the user objects deleted and tell the client to restore. Rejected: the client cannot restore an object that the rollback deleted in strict mode.

## R9. What the dry run shows

**Decision**: A dry run with the option on, and no problem, returns the list `takeOvers`: type, name, and `identical` (true or false). A dry run with a problem (for example a conflict with another gamepack) returns the problems and no list, because the plan does not exist. A dry run with the option off returns the user conflict as today and no list (clarification Q5). A dry run changes nothing and writes no backup file (FR-011).

**Rationale**: The list needs the plan (the rewritten references). The plan exists only when there is no problem.

## R10. Compatibility and tests with the option off

**Decision**: New parameters have defaults. `InstallAsync(json, dryRun, ct)` and `InstallAsync(json, dryRun, signatureText, ct)` stay and call the new overload `InstallAsync(json, GamepackInstallOptions, ct)`. The `GamepackApplier` constructor gets an optional store parameter. Without a store and with a take-over in the plan, the apply returns `Failed`. The response fields `takenOver` and `takeOverBackupId` are new. `takenOver` is always present (0 when the option is off). `takeOverBackupId` and `takeOvers` are omitted when null.

**Rationale**: SC-002 needs the old tests to pass without a change. Adding fields to a JSON response does not break the existing contract tests, which read named fields.

## R12. Tests for the loop 1 clarifications

**Decision**: Add these tests:

- Upgrade: the pack version 1 owns object A. Version 2 adds object B, and a user object B exists. The upgrade takes B over. `TakenOver` is 1 and A counts as updated, not taken over (FR-003, FR-012).
- Large case: install over 13 sequences and 131 commands with the option on. Check `TakenOver` is 144, each link by name is valid, and no queue changes its run state (SC-001, SC-008).
- Second restore of one backup gives `owner_changed` and changes nothing.
- Force restore: skips `owner_changed`, but refuses on `object_missing` and `object_changed`.
- Conflict with another gamepack, option on: 422 for an install, 200 with `valid: false` for a dry run, the same as with the option off.
- Catalog install and catalog update accept `takeOver`. Managed rollback does not.

**Rationale**: Each test maps to a clarification in the spec.

## R13. Loop 2 decisions

**Decision**:

- Catalog install result: the catalog install returns the same install result, with `takenOver` and `takeOverBackupId`. A catalog client can then restore the backup (FR-012, FR-013). The catalog update returns the managed report, which has the same fields (R8).
- Performance (SC-001): one test installs the same gamepack twice on the same machine: once with the option off over no conflict, once with the option on over 144 user objects. It checks that the take-over time is at most 2 times the normal time. The test has no fixed number of seconds. The test uses fresh data folders for both runs and a warm-up run, so the first-run cost does not skew the compare.
- Restore checks: exactly three (`object_missing`, `object_changed`, `owner_changed`). The code has no other refusal reason. A test checks that the reason codes are only these three.
- Force restore gate: the force restore runs inside the gate that the managed update already holds. It does not take the gate a second time. The public restore route takes the gate by itself (FR-016).
- Step name: the managed restore report uses the step name `takeover_restore` (R8).

**Rationale**: Each item removes an open point from the loop 2 clarifications. A compare of two runs on one machine gives a stable test without a fixed time. A second gate take inside the managed update would block itself.

**Alternatives considered**: A fixed limit of 5 seconds. Rejected: it depends on the machine. A separate gate for the force restore. Rejected: it would allow a second install in the middle of a managed update.

## R11. Security and logging

**Decision**: The backup id comes from the client in the restore route. The store checks it against a strict regex and never builds a path from raw input. Log messages use new event ids in the gamepack range (14xx, next free). A log line for a take-over has the gamepack name, the count, and the backup id. It has no object content.

**Rationale**: This closes path traversal. It also keeps user content out of the log.
