# Implementation Plan: Gamepack Install

**Branch**: `143-gamepack-install` | **Date**: 2026-10-15 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `/specs/143-gamepack-install/spec.md` (with the loop 1 and loop 2 clarifications)

## Summary

A gamepack is one JSON file: a manifest plus sequences, commands, and images. The service installs it as one atomic unit. It records the owner (gamepack name and version) of each object in a registry file. It lists installed gamepacks, rolls one back to the kept previous version, and checks a file with a dry run.

Technical approach (details in [research.md](research.md)):

- One shared validator gives the same result for dry run, install, and rollback (FR-013, FR-014, FR-018). It checks structure (including name, size, and image rules of FR-019, FR-023, FR-024), references, and conflicts. It collects all problems.
- One apply path does install and rollback. It takes an undo snapshot, writes objects, the previous-version file, and the registry (last), and restores the snapshot on any failure. A failed restore gives HTTP 500 with result `partial_restore` (FR-021).
- Owners live in `<dataRoot>/gamepacks/registry.json`. Object files and stores do not change. A hand delete of an owned object removes the owner entry at once, and the owner read checks that the object exists (FR-005, FR-015).
- A test seam, `IGamepackWriteGate`, lets tests inject a fault at each write (SC-001).

## Technical Context

**Language/Version**: C# on .NET (the version of the existing solution)
**Primary Dependencies**: ASP.NET Core minimal endpoints, `System.Text.Json`, Swashbuckle (schema filter for OpenAPI). No new package.
**Storage**: JSON files under the data root: `gamepacks/registry.json` and `gamepacks/previous/<name>.json`. Existing sequence, command, and image stores are not changed.
**Testing**: xUnit. Unit tests (validator, applier, fault injection), contract tests (endpoints, OpenAPI), integration tests (install, update, rollback on real stores).
**Target Platform**: Windows service (GameBot.Service)
**Project Type**: Web service (backend only; no web UI change)
**Performance Goals**: Install of 100 objects: soft check with a generous threshold, not a CI gate.
**Constraints**: One image at most 10,000,000 bytes decoded. One request at most 100 MB (413 problem above). Install and rollback run one at a time (gate lock).
**Scale/Scope**: One registry file. Tens of gamepacks, hundreds of objects.

## Constitution Check

*GATE: passed before Phase 0 and passed again after Phase 1.*

- **I. Code quality**: New code goes in small classes (validator, applier, registry store, endpoints). `Program.cs` stays thin (taint analyzers fail on big methods). No new package. PASS.
- **II. Testing**: Tests for every FR and SC are in [quickstart.md](quickstart.md): fault injection at each write (SC-001), restore failure (FR-021), N conflicts (SC-003), rollback equals the captured state (SC-004), byte-equal dry run (SC-005), and the hand-edit, hand-create, and hand-delete scenarios of US2. PASS.
- **III. UX consistency / phone check**: No web UI change. Phone support level is not applicable. The API uses the existing problem-response style. PASS.
- **IV. Performance**: A soft check for 100 objects. Memory is bounded by the size rules (FR-023). PASS.
- **V. Living documentation**: OpenAPI schemas, `docs/gamepacks.md`, `docs/architecture.md` (API surface, persistence layout, "Last reviewed"), `specs/STATUS.md`, `CHANGELOG.md`, and the spec Status are in scope (FR-017). The version file `installer/versioning/version.override.json` gets the patch bump before the push. PASS.
- **VI. STE**: All artifacts and messages use STE. PASS.
- **Build and test gate**: No known failing build or required test blocks this work.

No violation. The Complexity Tracking table is not needed.

## Project Structure

### Documentation (this feature)

```text
specs/143-gamepack-install/
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
│   └── gamepack-api.md
├── checklists/
│   └── requirements.md
└── tasks.md            # created by /speckit-tasks
```

### Source Code (repository root)

```text
src/GameBot.Domain/Gamepacks/
├── GamepackFile.cs             # file, manifest, image records
├── GamepackRegistry.cs         # registry, installed record, previous, owner entries
├── GamepackRegistryStore.cs    # atomic read and write of registry.json and previous files
├── GamepackValidator.cs        # structure, reference, conflict (shared by dry run, install, rollback)
├── GamepackNameRules.cs        # FR-019 name rule, case-insensitive match, reserved names
├── GamepackApplier.cs          # capture, snapshot, write, restore
├── GamepackService.cs          # install, dry run, rollback, list, owner lookup; gate lock
└── IGamepackWriteGate.cs       # test seam (no-op in production)

src/GameBot.Service/Endpoints/
├── GamepacksEndpoints.cs       # install, list, rollback routes; 413 handling
└── Dto/GamepackDtos.cs         # request and response shapes; owner DTO

src/GameBot.Service/Swagger/    # schema filter for the new schemas
src/GameBot.Service/Endpoints/  # SequencesEndpoints, CommandsEndpoints, image endpoints:
                                #   add read-only `owner`; remove owner entry on delete;
                                #   narrow 409 guard on create (name owned by a gamepack)

tests/unit/Gamepacks/           # validator, name rules, applier, fault injection
tests/contract/                 # endpoints, OpenAPI, owner field, 413, hand-edit rules
tests/integration/              # install, update, rollback on real stores

docs/gamepacks.md               # new user doc
docs/architecture.md            # update
specs/STATUS.md                 # update
```

**Structure Decision**: Backend only. Domain logic goes in a new `Gamepacks` folder in `GameBot.Domain`. HTTP routes go in `GameBot.Service/Endpoints`. The existing object stores and their file layout do not change.

## Design Notes by Requirement

| Requirement | Where it is handled |
|-------------|--------------------|
| FR-003, FR-021 | `GamepackApplier` snapshot and reverse restore; `partial_restore` result with HTTP 500 |
| FR-005, FR-015 | Owner read checks the object exists; delete hook removes the owner entry |
| FR-006, FR-018 | `GamepackValidator` conflict check, also run on the rollback content |
| FR-010, FR-022 | Capture of live content before the install; same-version install keeps previous |
| FR-019 | `GamepackNameRules`: 1 to 64 characters, no leading `.`, no case, reserved Windows names |
| FR-020 | Applier keeps the stored id and rewrites references in the file objects |
| FR-023 | Validator image rules; request-size check returns a 413 problem; the dry run checks the same rules |
| FR-024 | Image id is the name for conflict, owner, and id-keeping rules |
| Test seam (spec assumption) | `IGamepackWriteGate`; a test gate throws at the Nth call |
| Hand changes during an install (F5, F6) | Not blocked; docs tell users to avoid them and state that references from user objects to dropped objects are not checked |

## Complexity Tracking

No constitution violation. No entry.
