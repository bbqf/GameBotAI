# Quickstart: Gamepack Install

1. Make a gamepack file `pns-daily-1.json` with a manifest, sequences, commands, and images (see contracts/gamepack-api.md). The name uses letters, digits, `_`, `-`, and `.` only.
2. Check it: `POST /api/gamepacks/install?dryRun=true` with the file as body. Read `valid` and `problems`. No data changes.
3. Fix each problem. A `conflict` means a user object or another gamepack owns the name. A `reference` means a target is missing.
4. Install: `POST /api/gamepacks/install`. All objects are made, or none.
5. Read an object, for example `GET /api/sequences/{id}`. The `owner` field shows `pns-daily` and the version.
6. List gamepacks: `GET /api/gamepacks`. See name, version, and changelog.
7. Install version 2 (`pns-daily-2.json`). Objects are updated and keep their ids. Objects that version 2 drops are removed. The service keeps the live content of version 1 as the previous version.
8. Go back: `POST /api/gamepacks/pns-daily/rollback`. The service checks conflicts and references first. The list shows version 1 again. A second rollback gives `no_previous_version`.

## Check list for tests

- Install into an empty installation: all objects exist and show the owner (SC-002).
- Fault injection: the write gate throws at each write N (objects, previous file, registry). Files and registry equal the state before (SC-001, FR-003).
- Fault injection in the restore step: the result is `partial_restore` and the log has the failed object (FR-021).
- N conflicts in one file: N problems in one answer (SC-003).
- Install v1, install v2, rollback: objects and owners equal v1 (SC-004).
- Hand edit between v1 and v2: the kept previous version has the hand edit, and a rollback restores it (FR-022).
- Failed install of v2: the previous file and registry are as before (FR-022 atomic).
- Rollback conflict: a user makes an object with a name that v1 needs, then rollback is refused with the problem list and nothing changes (FR-018, FR-016).
- Rollback with a broken reference: refused, nothing changes (FR-018).
- Re-install of the same version: the kept previous version stays (FR-010).
- Update with a different file id for the same type and name: the stored id stays, and references work (FR-020).
- Images: a wrong content type, bad base64, and data above 10,000,000 bytes give a structure problem, also in a dry run (FR-023). A request above 100 MB gives a 413 problem and nothing changes.
- Image id is the name: an image with the name of a user image is a conflict; an update keeps the image id (FR-024).
- Names: case-only differences are the same gamepack; `CON`, `nul.txt`, and `COM1` give a structure error (FR-019).
- Hand delete of an owned object, then hand create with the same name: the owner entry is gone at the delete, and the new object shows the user as owner (US2 scenario 6, FR-005).
- A failed restore returns HTTP 500 with `partial_restore` (FR-021).
- Names: 64 characters pass; 65, empty, a leading `.`, and a `/` give a structure error (FR-019).
- Manifest without `requiredApiMajor`: valid. With it: stored and shown, not checked (FR-002).
- Same id with another name: not a conflict (F1).
- Dry run: stored files are byte equal before and after; result equals the real install (SC-005, FR-014).
- List with 0 and 2 gamepacks (SC-006).
- Hand-made object with a pack name: install refused, object not changed (FR-016).
- Hand edit of an owned object: accepted, owner record stays. Hand create with an owned name: 409, owner stays (US2).
- Hand delete of an owned object: the next install re-creates it, and a rollback restores it (FR-015).
- Rollback with no previous version, and with an unknown name.
- Soft performance check: install of 100 objects, generous threshold, not a CI gate.
- Docs: `docs/architecture.md` has the new API surface, persistence layout, and Last reviewed date. `specs/STATUS.md` has a row for 143 (FR-017).
