# Quickstart: Remote gamepack catalog

## Try it

1. Make a folder `C:\catalog` with `index.json` (see contracts/catalog-index.md) and the gamepack files.
2. `PUT /api/gamepacks/catalog/settings` with `{ "location": "C:\\catalog", "allowInsecure": false, "token": null }`.
3. `GET /api/gamepacks/catalog`. Check the versions, changelog text, and the installed marker.
4. `POST /api/gamepacks/catalog/install` with `"dryRun": true`. Check that nothing changes.
5. Repeat with `"dryRun": false`. Check `GET /api/gamepacks`.
6. Open the "Gamepacks" area (`/gamepacks`) from the navigation in the web UI. Record the elapsed time from step 2 to the end and compare it with the 3-minute target (SC-001). Set the "dry run" check box and press the button: nothing changes. Press "Refresh": the list reloads. Clear the check box and press Update on a newer version.

## Requirement to test map

| Requirement | Test owner |
|-------------|------------|
| FR-001, FR-003 | Contract: settings round trip, change takes effect with no restart |
| FR-002, FR-002a, SC-004 | Contract: `tokenSet` only; canary scan of responses and logs; scheme rules |
| FR-004 | Unit: index parser |
| FR-005 | Integration: test HTTP server sees bearer header |
| FR-006 | Unit and integration: folder source and path escape |
| FR-007 | Contract: list and installed marker |
| FR-008, SC-006 | Integration: install, signature policy `required`; entry with a signature file, entry with none ("missing"), signature download failure |
| FR-009 | Integration: dry run, state compare |
| FR-010 | Contract: 404 before download (server sees no file request) |
| FR-011 | Unit and integration: path escape, redirect to other host |
| FR-012 | Integration: oversize index, oversize signature file (over 1 MB), oversize gamepack file, per-request timeout on a file download |
| FR-013, SC-003 | Integration: each failure kind with state compare |
| FR-014 | Integration: hash mismatch |
| FR-015, SC-005 | Integration: many lists, wait, same version; DI registration check: no hosted service or timer type depends on the catalog types, directly or through the transitive constructor graph (not a text scan) |
| FR-016, FR-017 | Jest: page tests (Update button, "dry run" check box, "Refresh" button, no action without an operator action) |
| FR-010, entry_not_listed | Contract: unlisted name or version gives `entry_not_listed`; `not_found` only for a listed file that the catalog answers "not found" |
| Missing `index.json` | Contract: folder and web both give `index_invalid` (HTTP 502) |
| Edge case: parallel installs | Integration: two parallel catalog installs of one name follow the feature 143 rules |
| FR-018 | Contract: token rule on each route |
| FR-019 | Contract: log capture, no token |
| SC-001, SC-002 | Manual walk and timing test (50 x 10) |
