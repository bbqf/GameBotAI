# Quickstart and test plan: Export and import queue settings

## Manual check (API on port 8080)

1. Install a gamepack and make a queue that uses one gamepack sequence and one hand-made sequence.
2. `GET /api/queue-settings/export` and save the body.
3. Check the body: the hand-made sequence has its full definition; the gamepack sequence is only in `references`; no `id` field exists anywhere.
4. On an empty instance (same gamepack installed), `POST /api/queue-settings/import` with the body. Check that the queue, template, and hand-made sequence exist by name.
5. Send the same body again. Check that all items are `unchanged`.
6. Remove the gamepack, import again. Check that the response lists the missing sequence in `unresolvedReferences` and that other items are applied.

## Automated tests (by requirement)

Unit (`tests/unit/QueueSettings/`):

- Exporter: no `id` or time stamp in the JSON (FR-004, SC-001). Gamepack-owned sequence and command appear only in `references` (FR-003). Empty data gives a valid document (edge case). References from queues, templates, sequences, and commands are all listed (FR-015).
- Importer plan: create, update, unchanged, skipped (FR-005, FR-010). Owned name with full content is skipped and the owned object and owner record are the same after (FR-006, SC-002). No delete call on a recording repository (FR-007).
- Unresolved references for each place of use in research R6 (FR-008, SC-004).
- Document refusal: bad JSON, `formatVersion` 2, duplicate name; zero writes (FR-009, FR-014).
- Two-request round trip: exactly one export request and one import request, then the target state matches by name (SC-006).
- A gamepack-owned image is one `image` entry in the reference list, with no content (FR-003).
- Recovery after HTTP 500: send the same document again; earlier writes stay and the rest is applied (FR-011).
- Idempotency: import twice, second report all `unchanged`, zero writes (FR-011, SC-005).
- Running queue update sets `restartRequired` and does not call stop or start (FR-016).
- Lock (FR-017): hold the gamepack gate lock with `RunExclusiveAsync`, start an import, and check that the recording repositories see no write until the lock is released. Start an install while an import holds the lock and check that the install waits.
- Repository failure (FR-018): a failing repository decorator throws on a chosen write. The import returns a partial report with applied, failed, and not-applied items. Earlier writes stay and no delete call is made.
- Duplicate name: the error names the duplicate, and no write happens. Case differences count as the same name.
- Missing game, missing user-owned image, and missing gamepack are each listed as unresolved references (FR-008).
- Machine-specific queue fields (serial, instance name, index) are imported as given and cause no report entry.

Contract (`tests/contract/`):

- Routes, status codes 200, 400, 413, 500 (partial report) and the OpenAPI schemas and descriptions (FR-009, SC-006).
- Guard test: export then import on a second host instance with the same gamepack gives the same queue settings by name (FR-012, SC-003).
- Existing gamepack contract tests run and pass unchanged (FR-013).

Integration (`tests/integration/`):

- Install a gamepack, export, wipe user data, import, compare. Update the gamepack version, import, check that owned objects did not change.

## Test notes

Contract tests share a bin data directory. Use a temp data root for each test host so that other tests do not see the data.

## Docs and release tasks

`docs/gamepacks.md` (new section "Back up and restore settings"), `docs/architecture.md` (API surface and "Last reviewed"), `specs/STATUS.md`, `CHANGELOG.md`, spec Status, and the patch bump in `installer/versioning/version.override.json` before the push.
