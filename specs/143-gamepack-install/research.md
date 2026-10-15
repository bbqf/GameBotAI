# Research: Gamepack Install

All items are resolved. No open NEEDS CLARIFICATION.

## R1. File format

- Decision: One JSON document: `manifest` (`name`, `version`, `changelog`, optional `requiredApiMajor`), `sequences[]`, `commands[]`, `images[]`. A sequence entry and a command entry use the same JSON as the existing object. An image entry has `id` (its name, FR-024), `contentType`, `filename`, and `dataBase64`. The request body is `application/json`.
- Rationale: The spec says one JSON document. The existing object JSON means no new object model. Base64 makes the file self-contained.
- Alternatives: ZIP like the existing backup (more parts, more failure modes); multipart (not needed).

## R2. Where the owner is stored

- Decision: A registry file `<dataRoot>/gamepacks/registry.json`. It holds the installed records and an owner map keyed by `type:name`. No entry means the user owns the object.
- Rationale: Object files and repositories do not change, so manual create, edit, and delete work as today (FR-015). One file gives one atomic write point. The owner read checks that the object exists, so an owner entry without an object is never shown (FR-005). The delete routes of the three stores call a small hook that removes the owner entry at once (FR-015). A later hand-made object with the same name then belongs to the user. If the hook fails, the delete still succeeds, and the owner read check hides the stale entry.
- Alternatives: An `owner` field in each object (changes three stores and old files; a hand edit could drop it); one side file for each object (many files, no single commit point).

## R3. Atomic apply, dry run, and restore failure

- Decision: One `SemaphoreSlim` gate for install and rollback. Steps: (1) parse, (2) validate with the shared validator, (3) if dry run or invalid, return, (4) capture the full live content of the current version as the new previous version (R5), (5) take the undo snapshot of every object that the apply creates, changes, or removes, plus the old registry file and the old previous file, (6) write objects, (7) write the new previous file and then the registry with write-to-temp and replace, (8) on any exception, restore the snapshot in reverse order. If the restore itself fails, log the error with the failed object and return HTTP 500 with result `partial_restore` (FR-021). The restore continues with the other objects after one failure, so the state is as close as possible to the old state.
- Rationale: The three repositories have no shared transaction. A snapshot restore gives the all-or-nothing result. The registry is written last. A crash before it leaves the old registry, and objects not in the registry are then user objects, which is the safe side.
- Alternatives: Stage in a temp data root and swap folders (breaks live repositories and open handles); a database (new storage is not allowed).

## R4. Conflict, reference, and id rules

- Decision: Identity is type and name (clarification 1, F1). Names compare without case. A gamepack object conflicts when an existing object has the same type and name and the registry owner is the user or another gamepack. The same name with the same gamepack as owner is an update, and the update keeps the id of the existing object (FR-020, U2). The platform adds no other conflict kind: the same id with another name is not a conflict (F1). Names are unique for each type in the file (structure error). References: a sequence step that names a command or image, and a command that names an image, are valid when the target is in the file or in the installation after the apply. A target that the apply removes is not valid. When the file id of an updated object differs from the kept id, the apply rewrites references in the file to the kept id. All problems are collected; none stops the check early.
- Rationale: FR-006, FR-013, FR-020, clarification 3.
- Alternatives: Fail on the first problem (breaks SC-003); treat id clashes as conflicts (adds a conflict kind that the spec forbids).

## R5. Update, removal, previous capture, and rollback

- Decision: On update, the objects that the old version owned and the new file lacks are removed (FR-009). The registry keeps for each gamepack the current record and at most one `previous` record. The previous record holds the full content of that version. Before an install of a different version changes anything, the apply path reads the live objects that the current version owns (including hand edits) and writes them to `gamepacks/previous/<name>.json` (FR-022). An install of the same version does not replace the kept previous version (FR-010). The capture and its file write are inside the undo snapshot, so a failed install restores the old previous file. A rollback first validates the kept previous content with the shared validator: conflicts (an object with no owner or an object of another gamepack with the same type and name) and broken references. If there is a problem, the rollback is refused with the problem list and changes nothing (FR-018). If not, it applies the previous content through the same apply path (create, update, remove), then clears previous (FR-011). A rollback with no previous gives `no_previous_version`. An unknown name gives `not_found` (FR-012).
- Rationale: One apply path means one tested atomic path. Capture from live objects keeps the real state, so a rollback equals the state that was live. FR-016 holds for rollback because the check runs first.
- Alternatives: A diff-based undo (more complex, wrong after a hand edit); copy the previous file from the last install file (loses hand edits, breaks U1).

## R6. Owner in read responses

- Decision: Add a read-only `owner` object to the read DTO of sequences, commands, and images: `{ "kind": "user" }` or `{ "kind": "gamepack", "name": "...", "version": "..." }`. A write request ignores it. Lists and single reads show it. A conflict report uses owner text instead: `user` or `<name> <version>`. Both shapes are in the contract.
- Rationale: FR-007. A new field is additive.
- Alternatives: A separate owner endpoint (one extra call for each object).

## R7. Version and name values

- Decision: `version` is a string, with no order rule. An empty or white-space version is a structure error. The gamepack name follows FR-019: 1 to 64 characters from letters, digits, `_`, `-`, `.`, not starting with `.`. Name matching ignores case. A reserved Windows device name (CON, PRN, AUX, NUL, COM1 to COM9, LPT1 to LPT9, with or without an extension) is a structure error. The registry key and the `previous/<name>.json` file name use the lower-case form. The manifest `requiredApiMajor` is optional, stored, shown, and not checked.
- Rationale: Spec assumptions and FR-002, FR-019. The name is also used in a file name, so the rule keeps it file-safe on Windows, where file names ignore case.
- Alternatives: A case-sensitive key (two names that differ in case would share one file on Windows).

## R10. Size and image rules

- Decision: The validator checks each image: `contentType` is an image type (`image/png` or `image/jpeg`), `dataBase64` is valid base64, and the decoded size is at most 10,000,000 bytes (FR-023). These are structure problems, so the dry run reports them too. The total request limit is 100 MB. The install route raises the Kestrel and form body limit for this route only (100 MB). A request above the limit gets a structured 413 problem response. The route reads the body with a length check, so the service does not buffer a body above the limit. The image `id` is the image name for the conflict, owner, and id-keeping rules (FR-024). It must follow the existing image id rule.
- Rationale: FR-023 bounds memory use. A route-level limit does not change other routes (FR-015).
- Alternatives: A global limit change (affects all routes); no limit (memory risk).

## R8. Fault-injection seam, documentation, and tests

- Decision: The seam is an `IGamepackWriteGate` interface with one method, `BeforeWrite(operation, key)`. The applier calls it before each object create, update, delete, each previous-file write, and the registry write. The restore step calls it with a restore operation, so a test can also fail the restore (FR-021). Production registers a no-op. Tests register a gate that throws at the Nth call, with N over every call (SC-001). A dry-run test compares the stored files byte by byte before and after (SC-005). Docs: OpenAPI schemas through a schema filter, `docs/gamepacks.md`, `docs/architecture.md` (API surface, persistence layout, "Last reviewed"), `specs/STATUS.md`, `CHANGELOG.md`. Bump the version file before the push.
- Rationale: FR-017, the spec test seam assumption, constitution Principles II and V.
- Alternatives: A wrapper over each store that counts writes (three wrappers, and the registry write is not covered).

## R9. Concurrency and hand changes

- Decision: The gate lock stops two installs or rollbacks at once. A hand create, edit, or delete during an install is not blocked (FR-015). The accepted limit: such a change can be lost or can fail. The docs tell users to avoid hand changes during an install. The service does not check references from user objects to an object that an install drops. The docs say this. A hand create of an object with the name of a gamepack object is refused as any duplicate name (US2 scenario 5), and the owner record does not change. A hand edit is accepted and the owner record stays (US2 scenario 4).
- Open check for implementation: the sequence and command create routes may not refuse a duplicate name today. If so, the feature adds a narrow guard: a create request is refused with 409 only when the name matches an object that a gamepack owns. A create with a name that no gamepack owns works as today. A contract test covers this (US2 scenario 5).
- Rationale: A global lock on all object routes would change manual handling.
- Alternatives: Lock all object routes (changes manual handling, breaks FR-015).
