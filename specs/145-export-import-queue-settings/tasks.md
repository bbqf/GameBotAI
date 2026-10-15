# Tasks: Export and import queue settings by name

**Input**: Design documents in `specs/145-export-import-queue-settings/` (plan.md, spec.md, research.md, data-model.md, contracts/queue-settings-api.md, quickstart.md)
**Tests**: Included. The plan and quickstart.md ask for unit, contract, and integration tests.

## Format: `- [ ] [ID] [P?] [Story] Description with file path`

- [P] means the task can run in parallel (different files, no open dependency).
- [US1], [US2], [US3] map to the user stories in spec.md.
- Test seams (research R11): the recording decorator and the failing decorator live in the test project. Task T016 makes them.

## Phase 1: Setup

- [ ] T001 Check the `CommandSequence` and `Command` models in `src/GameBot.Domain/` for every id-like or time stamp field (command ids, image ids, sequence ids, created and updated times). Keep the list for T007. Do not write a file in this task.
- [ ] T002 Create the folder `src/GameBot.Domain/QueueSettings/` and the test folder `tests/unit/QueueSettings/`.
- [ ] T003 [P] Add the `QueueSettings` route constant (`/api/queue-settings`) to `src/GameBot.Service/ApiRoutes.cs`.
- [ ] T004 [P] Check the gamepack gate lock in `src/GameBot.Domain/Gamepacks/GamepackService.cs`. If it is private, add a public `RunExclusiveAsync` method that uses the same lock. Make no behavior change (FR-013).

## Phase 2: Foundational (blocks all user stories)

- [ ] T005 [P] Create the document records in `src/GameBot.Domain/QueueSettings/SettingsDocument.cs`: `SettingsDocument`, `QueueSettings`, `QueueTemplateDoc`, `QueueTemplateEntryDoc`, `NameReference` (see data-model.md). No id and no time stamp field.
- [ ] T006 [P] Create the report records in `src/GameBot.Domain/QueueSettings/ImportReport.cs`: `ImportItem` (with `restartRequired`), `UnresolvedReference`, summary counts, outcome values, and the `failed` and `notApplied` fields for the HTTP 500 partial report (FR-018).
- [ ] T007 [P] Create `src/GameBot.Domain/QueueSettings/SettingsNormalizer.cs`: the id-free, key-sorted, time-stamp-free normalized form for sequences, commands, templates, and queues. It covers each id field found in T001. In this same task, write a comment block (STE) at the top of the file that lists every id-like and time stamp field found in T001 and says how the normalizer handles each one (removed, or replaced by a name).
- [ ] T008 [P] Create `src/GameBot.Domain/QueueSettings/NameMap.cs`: case-insensitive name-to-id maps per type (sequence, command, image, queueTemplate, game, queue).
- [ ] T009 [P] Write unit tests for the normalizer in `tests/unit/QueueSettings/SettingsNormalizerTests.cs`: no id, no time stamp, stable key order, same input gives same output.
- [ ] T010 Register the services in `src/GameBot.Service/Endpoints/QueueSettingsServiceCollectionExtensions.cs` and call it from the service start-up with one line (keep `Program.cs` thin).

**Checkpoint**: records, normalizer, and name map are ready.

## Phase 3: User Story 1 - Export queue settings and own artifacts (P1)

**Goal**: One document with all queue settings and all non-gamepack artifacts. Gamepack-owned objects appear only as name references.
**Independent test**: Export with one gamepack sequence and one hand-made sequence in use. The document has the hand-made sequence in full, the gamepack sequence only in `references`, and no `id` field.

- [ ] T011 [P] [US1] Write unit tests in `tests/unit/QueueSettings/QueueSettingsExporterTests.cs`: no `id` or time stamp in the JSON (FR-004, SC-001); owned sequence and command only in `references` (FR-003); empty data gives a valid document; references from queues, templates, sequences, and commands are all listed (FR-015); machine-specific queue fields (serial, instance name, index) are exported as stored. Add a separate test for the gamepack-owned image reference: when an owned sequence or command uses an image that a gamepack owns, the export has one `references` entry of type `image` with the image name, `usedBy`, and the gamepack name and version, and has no image body and no id (FR-003, FR-015).
- [ ] T012 [US1] Create `src/GameBot.Domain/QueueSettings/QueueSettingsExporter.cs`: read queues, runtime entries, templates, sequences, commands, and games through the existing repositories. Use `GamepackOwnerResolver` and the registry for ownership. Write entries by sequence name, links by name, and the `references` list with gamepack name and version.
- [ ] T013 [US1] Create `src/GameBot.Service/Endpoints/QueueSettingsEndpoints.cs` with `GET /api/queue-settings/export` (200, `Content-Disposition: attachment; filename="gamebot-settings.json"`). Use a named handler method, and `WithName`, `WithSummary`, `WithDescription` in STE. Map it from the service collection extension.
- [ ] T014 [P] [US1] Write contract tests in `tests/contract/QueueSettingsExportContractTests.cs`: route returns 200 with `formatVersion` 1, the header, and the empty-data case. Use a temp data root for the test host.

**Checkpoint**: export works alone (MVP).

## Phase 4: User Story 2 - Import settings by name (P1)

**Goal**: Create or update queue settings and non-gamepack artifacts by name. Never touch a gamepack-owned object. Never delete. Idempotent. Exclusive with gamepack install. Safe on a repository failure.
**Independent test**: Export on host A, import on empty host B with the same gamepack, check by name. Import twice and check that the second report has all items `unchanged`.

- [ ] T015 [P] [US2] Create `src/GameBot.Domain/QueueSettings/SettingsDocumentValidator.cs`: format version 1, JSON object shape, non-empty names, unique names per type (no case), the same shape rules as the create routes for sequences and commands. A duplicate error names the duplicate.
- [ ] T016 [P] [US2] Create the test decorators in `tests/unit/QueueSettings/TestDoubles/`: a recording decorator over the repository interfaces (counts writes and deletes, records the time of each write) and a failing decorator (throws on a chosen write, FR-018). Add a blocking decorator that holds a write until the test releases it (used by the FR-017 install test).
- [ ] T017 [P] [US2] Write unit tests in `tests/unit/QueueSettings/SettingsDocumentValidatorTests.cs`: bad JSON, `formatVersion` 2, empty name; zero writes (FR-009, FR-014). Duplicate-name test: two sequences with the same name, and two with names that differ only in case, are both refused; the error names the duplicate; the recording repository sees zero writes (spec edge case, data-model validation rules).
- [ ] T018 [P] [US2] Write unit tests in `tests/unit/QueueSettings/QueueSettingsImporterTests.cs`: create, update, unchanged, skipped (FR-005, FR-010); owned name with full content is skipped and the owned object and owner record stay the same (FR-006, SC-002); a recording repository sees no delete call (FR-007); import twice gives zero writes the second time (FR-011, SC-005); a running queue update sets `restartRequired` and calls no stop or start (FR-016); machine-specific queue fields are imported as given and add no report entry.
- [ ] T019 [P] [US2] Write FR-017 lock tests in `tests/unit/QueueSettings/QueueSettingsImporterLockTests.cs`. Test 1: the test holds the gamepack gate lock with `RunExclusiveAsync`, starts the import, waits a short time, and checks that the recording repositories saw no write; then it releases the lock and checks that the import finishes with the writes. Test 2: the blocking decorator holds a write inside the import; the test starts a gamepack install and checks that the install does not finish; it then releases the write and checks that the import and the install both finish, one after the other.
- [ ] T020 [P] [US2] Write FR-018 failure tests in `tests/unit/QueueSettings/QueueSettingsImporterFailureTests.cs`: the failing decorator throws on a chosen write in the middle of the order (commands, sequences, templates, queues). Check that the importer returns a partial report with `items` (applied), one `failed` item with a short STE reason, and the `notApplied` items; the writes before the failure stay; the importer calls no delete and does not undo; a repeat of the import with a working repository finishes the rest (idempotency). Add a separate re-import-after-failure test: send the same document again after the failure with a working repository; the second report has the earlier applied items as `unchanged`, the earlier failed and not-applied items as `created` or `updated`, no `failed` item, and no duplicate object in the repositories.
- [ ] T021 [US2] Create `src/GameBot.Domain/QueueSettings/QueueSettingsImporter.cs`: run the validator first (zero writes on error). Then take the gamepack gate lock with `RunExclusiveAsync` (from T004) before the plan step, and hold it until the last write (FR-017). Plan each item with the owner resolver, then apply commands, sequences, templates, and queues in that order with the name-to-id map. Compare on the normalized form. Replace queue entries only when the resolved list differs (by sequence id order). Make new ids in the repositories, never take them from the document. Catch a repository exception, stop the apply, and return the partial report with applied, failed, and not-applied items (FR-018). Include no delete call and no undo.
- [ ] T022 [US2] Add `POST /api/queue-settings/import` to `src/GameBot.Service/Endpoints/QueueSettingsEndpoints.cs`: 20 MB body limit, 200 with the `ImportReport`, 400 problem response for an invalid document, 413 for a large body, 500 problem response that carries the partial report (FR-018). Use a named handler. Add STE metadata.
- [ ] T023 [P] [US2] Write contract tests in `tests/contract/QueueSettingsImportContractTests.cs`: status codes 200, 400, 413; status 500 with a partial report when a failing repository is registered in the test host (FR-018); the guard test of export on host A and import on host B with the same gamepack, same queue settings by name (FR-012, SC-003); the existing gamepack contract tests still pass unchanged (FR-013).

**Checkpoint**: export and import work for a full round trip.

## Phase 5: User Story 3 - Partial import with unresolved references (P2)

**Goal**: A missing target does not fail the import. The report lists it.
**Independent test**: Import a document that points to a gamepack sequence on an instance without that gamepack. The rest is applied and the response lists the reference.

- [ ] T024 [P] [US3] Write unit tests in `tests/unit/QueueSettings/UnresolvedReferenceTests.cs`: one case for each place of use in research.md R6 (queue entry, template entry, sequence, command, linked template, linked game, image); a missing game, a missing user-owned image, and a missing gamepack each give a listed reference; the empty list when all targets exist (FR-008, SC-004).
- [ ] T025 [US3] Add unresolved-reference handling to `src/GameBot.Domain/QueueSettings/QueueSettingsImporter.cs`: apply the rules of research.md R6 per place of use, fill type, name, usedBy, and gamepack name and version from the document `references` list. This task follows T021.
- [ ] T026 [US3] Write an integration test in `tests/integration/QueueSettingsRoundTripTests.cs`: install a gamepack, export, wipe user data, import, compare; update the gamepack version, import, check that owned objects did not change; remove the gamepack, import, check the unresolved list. In the round-trip test, assert the request count (SC-006): the test sends exactly one export request and exactly one import request (count the HTTP calls with a counting handler) and needs no other call to restore the settings.

**Checkpoint**: all three stories work.

## Phase 6: Polish and cross-cutting

- [ ] T027 [P] Add a schema filter with STE descriptions for the new types in `src/GameBot.Service/Swagger/` (as feature 143 did). Add an OpenAPI contract test for the descriptions and the 500 response in `tests/contract/`.
- [ ] T028 [P] Add the section "Back up and restore settings" to `docs/gamepacks.md`. Include one line for the recovery step: "If an import fails with status 500, send the same document again. The import is safe to repeat and it finishes the items that it did not apply."
- [ ] T029 [P] Update `docs/architecture.md` (API surface and "Last reviewed").
- [ ] T030 [P] Update `specs/STATUS.md` and set the spec Status in `specs/145-export-import-queue-settings/spec.md`.
- [ ] T031 Run the manual check in `specs/145-export-import-queue-settings/quickstart.md` on the API at port 8080.
- [ ] T032 FINAL. Release and gates, in this order:
  1. Run `git fetch origin master`. Compare `installer/versioning/version.override.json` with `origin/master`.
  2. Apply the patch bump rule from CLAUDE.md and the constitution ("Version bump"): if `minor` or `major` changed, set `patch` to `"0"`; if not, set `patch` to the `patch` on `origin/master` plus 1. Do nothing if the file already has this value. Set `updatedBy` and `updatedAtUtc`.
  3. Add one line for this feature to `CHANGELOG.md` (STE).
  4. Run `dotnet build` for the solution and fix all errors.
  5. Run `dotnet test` for the solution (unit, contract, integration). Fix all failures. Confirm the existing gamepack tests pass unchanged (FR-013).

## Dependencies and order

- Phase 1 then Phase 2 then stories. Phase 2 blocks all stories.
- T007 needs the list from T001.
- US1 (export) has no dependency on US2 or US3.
- US2 needs Phase 2 and T004. T021 needs T015 and T004. Tests T017 to T020 need T016 (decorators). The round-trip test (T023) needs US1.
- US3 extends the importer from US2 (T025 after T021).
- T032 is the last task and runs after all others.

## Parallel examples

- Phase 2: T005, T006, T007, T008 together.
- US1: T011 and T014 while T012 is in progress.
- US2: T015, T016 together; then T017, T018, T019, T020 together before T021.
- Polish: T027 to T030 together.

## Implementation strategy

- MVP: Phase 1, Phase 2, and US1 (export only).
- Then US2 (restore, with lock and failure handling), then US3 (robust restore), then Polish and the final task T032.
