# Implementation Plan: Export and import queue settings by name

**Branch**: `145-export-import-queue-settings` | **Date**: 2026-10-15 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `/specs/145-export-import-queue-settings/spec.md` (with the corrected clarifications and FR-001 to FR-018)

## Summary

The user asks for one export. The service writes one JSON document. The document holds the settings of all queues and the full definition of each object that no gamepack owns (sequences, commands, queue templates). An object that a gamepack owns appears only as a name reference. The document holds no id that the instance assigned. All links use names.

The user sends the document to the import. The service validates the whole document first. It then creates or updates queues and non-gamepack artifacts by name. It never creates, changes, or deletes a gamepack-owned object. A reference without a target does not fail the import. The report lists it.

Technical approach (details in [research.md](research.md)):

- Read and write through the existing repositories. Add no new store.
- Ownership comes from the gamepack registry of feature 143 on the instance that reads or writes. The document is never trusted for ownership.
- One normalized form is used by the export and by the "unchanged" test of the import. This gives idempotency (FR-011).
- The import holds the gamepack gate lock (the private `SemaphoreSlim` in `GamepackService`, exposed through `RunExclusiveAsync`) while it applies items, so an install and an import never run at the same time (FR-017).
- A write failure gives HTTP 500 with a partial report: applied, failed, and not-applied items (FR-018).
- The import does not stop or start a queue. It reports `restartRequired` for a running queue that it updates (FR-016).

## Technical Context

**Language/Version**: C# on .NET (the version of the existing solution)
**Primary Dependencies**: ASP.NET Core minimal endpoints, `System.Text.Json`, Swashbuckle (schema filter for OpenAPI). No new package.
**Storage**: Existing stores only (queues, queue runtime entries, queue templates, sequences, commands, images, games, gamepack registry). No new file.
**Testing**: xUnit. Unit tests (normalizer, validator, exporter, importer, lock, fault injection), contract tests (routes, status codes, OpenAPI, round trip), integration tests (real stores with a gamepack).
**Target Platform**: Windows service (GameBot.Service)
**Project Type**: Web service (backend only; no web UI change)
**Performance Goals**: An export or import of hundreds of objects finishes in a few seconds. No CI gate.
**Constraints**: Request body at most 20 MB on the import route. Import and gamepack install run one at a time. No delete call anywhere in the import.
**Scale/Scope**: Tens of queues and hundreds of artifacts.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

*NON-NEGOTIABLE*: If `build` or required `test` runs are failing (local or CI), implementation progression is blocked until failures are fixed or a documented maintainer waiver exists. Status: acknowledged. The final task runs the full build and test suites.

*NON-NEGOTIABLE*: All text in this plan and in the artifacts it produces obeys Simplified Technical English (Principle VI). Status: pass.

| Principle | Result | Note |
|-----------|--------|------|
| I. Code quality | Pass | New code is small classes with one job. Method names use CamelCase with no underscores. `Program.cs` stays thin: one registration call. Endpoint handlers are named methods (taint analyzers). |
| II. Testing | Pass | Tests for each requirement, including FR-017 (lock held by the test) and FR-018 (failing repository). See "Test Strategy and Task Notes". |
| III. User experience | Pass | No web UI change. Phone check: not applicable. API text and OpenAPI descriptions are in STE. |
| IV. Performance | Pass | Goal stated above. The export is one read pass. No hot path changes. |
| V. Living documentation | Pass | Tasks update `docs/architecture.md` (API surface, "Last reviewed"), `docs/gamepacks.md`, `specs/STATUS.md`, `CHANGELOG.md`, and the spec Status. |
| VI. STE | Pass | All artifacts, messages, and comments in STE. |
| Version bump | Pass | The version bump task changes `patch` before the push. |

Post-design re-check (after Phase 1): no violation. No entry in Complexity Tracking.

## Project Structure

### Documentation (this feature)

```text
specs/145-export-import-queue-settings/
├── plan.md              # This file
├── research.md          # Phase 0 output
├── data-model.md        # Phase 1 output
├── quickstart.md        # Phase 1 output
├── contracts/
│   └── queue-settings-api.md   # Phase 1 output
└── tasks.md             # Phase 2 output (/speckit-tasks)
```

### Source Code (repository root)

```text
src/
├── GameBot.Domain/
│   ├── Gamepacks/GamepackService.cs        # add RunExclusiveAsync (no behavior change)
│   └── QueueSettings/                      # new
│       ├── SettingsDocument.cs
│       ├── SettingsNormalizer.cs
│       ├── NameMap.cs
│       ├── ImportReport.cs
│       ├── SettingsDocumentValidator.cs
│       ├── QueueSettingsExporter.cs
│       └── QueueSettingsImporter.cs
└── GameBot.Service/
    ├── ApiRoutes.cs                        # add QueueSettings constant
    ├── Endpoints/
    │   ├── QueueSettingsEndpoints.cs       # new
    │   └── QueueSettingsServiceCollectionExtensions.cs   # new
    └── Swagger/                            # schema filter for the new types

tests/
├── unit/QueueSettings/
├── contract/
└── integration/
```

**Structure Decision**: Domain logic in `GameBot.Domain/QueueSettings/`, HTTP in `GameBot.Service/Endpoints/`. This is the layout that feature 143 used for gamepacks.

## Test Strategy and Task Notes

- Each functional requirement has at least one test. The map is in [quickstart.md](quickstart.md).
- FR-017 (exclusion): the test takes the gamepack gate lock with `RunExclusiveAsync` (or a held `InstallAsync`), starts the import, and checks that a recording repository sees no write until the test releases the lock. A second test starts an install while an import holds the lock and checks that the install waits.
- FR-018 (repository failure): the test wraps a repository in a decorator that throws on a chosen write. The import must return HTTP 500 with a report that lists applied items, the failed item, and the not-applied items. The test checks that no earlier write is undone and that no delete call exists.
- FR-006 and SC-002: the test snapshots the gamepack-owned objects and the registry before and after an import, and compares them.
- FR-007: a recording repository fails the test on any delete call.
- FR-011: import twice; the second report has only `unchanged` items and the recording repository sees zero writes.
- FR-008: one unit test for each place of use in research R6, including a missing game, a missing image, and a missing gamepack.
- SC-006 (clarification C1): the round-trip test makes exactly one export request and one import request, and checks that the target state matches by name. The test counts the requests.
- Gamepack-owned image (clarification C2): the export lists it in the same reference list as other owned objects, with type `image` and the image name (its id). It has no content in the document. A unit test checks this entry.
- Recovery after HTTP 500 (clarification D1): the user fixes the cause and sends the same document again. Earlier writes stay and the import is idempotent. `docs/gamepacks.md` states this. A test imports the same document again after a failure and checks that the rest is applied.
- Duplicate names (spec edge case): the validator test checks the error names the duplicate and zero writes happen. Name matching ignores case.
- Contract tests share a bin data directory. Use a temp data root for each test host so that other tests do not see the data.
- Task notes:
  - The first research task finds all id-like and time stamp fields before the normalizer is written.
  - The lock task exposes the existing gate lock (`RunExclusiveAsync` on `GamepackService`); it must not change install, list, or rollback behavior (FR-013). Existing gamepack contract tests run unchanged.
  - Keep `Program.cs` thin: one registration line.
  - The unresolved-reference handling extends the importer. It follows the importer task.
  - Add the FR-017 and FR-018 tests to the importer test class and a 500-status contract test to the import contract tests.
  - The docs tasks and the patch bump task come last.

## Complexity Tracking

No constitution violation. This table is empty.
