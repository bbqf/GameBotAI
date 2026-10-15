# Tasks: API Version and Gamepack Compatibility

**Input**: Design documents from `C:\src\GameBot\specs\144-api-version-compat\`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/api-version.md

**Format**: `- [ ] [TaskID] [P?] [Story?] Description with file path`. `[P]` means the task can run in parallel (different files, no open dependency).

## Phase 1: Setup

**Purpose**: Check the starting state.

- [X] T001 Build the solution and run the existing tests in `tests/unit/Gamepacks` and `tests/contract/Gamepacks` to record the baseline result.

---

## Phase 2: Foundational (blocks all user stories)

**Purpose**: The single source of truth for the API version (FR-002).

- [X] T002 Create the static class `ApiVersion` with the constants `Major = 1`, `Minor = 0`, `Patch = 0`, `SchemaFormat = 1` in `src/GameBot.Domain/Gamepacks/ApiVersion.cs`. Add STE comments that state when each number changes.

**Checkpoint**: `ApiVersion` exists and builds. The docs-sync test (task T018) comes after the docs task (T017), so it never runs red.

---

## Phase 3: User Story 1 - Read the platform API version (Priority: P1)

**Goal**: A client reads major, minor, patch, and schema format with one request.

**Independent Test**: Call `GET /api/version` two times. Both responses have the four values and are equal.

- [X] T003 [P] [US1] Add the contract test `ApiVersionContractTests` in `tests/contract/Gamepacks/ApiVersionContractTests.cs`: the response is 200 with `major`, `minor`, `patch`, `schemaFormat` equal to `ApiVersion`; two calls give equal bodies; a call with no token follows the existing API token rule; the swagger document has the path `/api/version` (operation `GetApiVersion`, tag `Version`) and the `ApiVersionResponse` field descriptions (FR-009). Do not assert the `requiredApiMajor` text here (task T016 does it).
- [X] T004 [P] [US1] Create the DTO `ApiVersionResponse` (`major`, `minor`, `patch`, `schemaFormat`) in `src/GameBot.Service/Endpoints/Dto/VersionDtos.cs`.
- [X] T005 [P] [US1] Add the route constant `Version` (`/api/version`) in `src/GameBot.Service/ApiRoutes.cs`.
- [X] T006 [US1] Create `VersionEndpoints` with the extension `MapVersionEndpoints` and a named handler `GetApiVersion` for `GET /api/version` (`WithName("GetApiVersion")`, `WithTags("Version")`, `WithSummary`, like `GamepacksEndpoints`) in `src/GameBot.Service/Endpoints/VersionEndpoints.cs`. Needs T004 and T005.
- [X] T007 [US1] Add one line `app.MapVersionEndpoints();` in `src/GameBot.Service/Program.cs`. Keep `Program.cs` thin. Needs T006.
- [X] T008 [P] [US1] Create the schema filter `ApiVersionSchemaFilter` with the OpenAPI descriptions for `ApiVersionResponse` and its fields (same pattern as `GamepackSchemaFilter`) in the new, separate file `src/GameBot.Service/Swagger/ApiVersionSchemaFilter.cs`, and register it where `GamepackSchemaFilter` is registered (FR-009). It does not change `GamepackSchemaFilter.cs`.

**Checkpoint**: US1 works alone. T003 passes.

---

## Phase 4: User Story 2 - Refuse a gamepack for another major version (Priority: P1)

**Goal**: The install and the dry run refuse a gamepack for another major version. The rollback does not check.

**Independent Test**: Install a gamepack with `requiredApiMajor` different from `ApiVersion.Major`. The install returns 422, the message names both versions, and no object, owner entry, or registry entry changes. The dry run gives the same problem.

- [X] T009 [P] [US2] Add the unit tests `GamepackValidatorApiVersionTests` in `tests/unit/Gamepacks/GamepackValidatorApiVersionTests.cs`: field absent gives no problem; field equal to the running major gives no problem; field below 1 (0, -1) gives the structure problem "must be a whole number of 1 or more"; a different major gives the structure problem on object type `manifest` with both versions in the message; minor and patch are not compared; `checkApiVersion: false` skips the check; for a non-integer JSON value (`1.5`, `"x"`), call `GamepackFileSerializer.Parse` and assert it returns a `Manifest` of null, `InvalidJson` false, and one structure problem on object type `manifest` with the message "The manifest has a wrong shape." (the existing `ReadManifest` catch). The validator does not run on this input. The service then gives `GamepackInstallStatus.Invalid` (422 on install, 200 with `valid: false` on dry run) (R4). Do not change the parse code.
- [X] T010 [P] [US2] Add the contract tests `GamepackApiVersionContractTests` in `tests/contract/Gamepacks/GamepackApiVersionContractTests.cs`: install of another major returns 422 with a message that names both versions and changes nothing; the dry run returns 200 with `valid: false` and the same problem; install of the running major and install with no field work as before. Add a rollback test (FR-007): the test builds its state by writing the previous-version file (with a `requiredApiMajor` that differs from the running major) and the registry entry directly to the store, and it does not use the install (the install would refuse that file). The rollback must succeed.
- [X] T011 [US2] Add the version check to `CheckStructure` and the last optional parameter to `ValidateAsync`, so the signature is `ValidateAsync(file, parseProblems = null, ct = default, checkApiVersion = true)`, in `src/GameBot.Domain/Gamepacks/GamepackValidator.cs`. Use the messages from `plan.md` (design decision 3). Problem form: category `structure`, object type `manifest`, name = gamepack name.
- [X] T012 [US2] In `src/GameBot.Domain/Gamepacks/GamepackService.cs`, change the `ValidateAsync` call in `RollbackAsync` (about line 254) to pass `checkApiVersion: false`. Confirm that the call in `InstallAsync` (about line 174, install and dry run) keeps the default `true`. Needs T011.
- [X] T013 [P] [US2] Change the test `AManifestWithRequiredApiMajorIsStoredAndShownButNotChecked` in `tests/contract/Gamepacks/GamepackImageRulesContractTests.cs`: use `ApiVersion.Major` instead of 99, and rename it to state that the field is stored and shown (R7).
- [X] T014 [P] [US2] Change the `requiredApiMajor: 3` value in `tests/contract/Gamepacks/GamepackListContractTests.cs` to the running major version (R7).
- [X] T015 [US2] Change `FileDescription` in `src/GameBot.Service/Swagger/GamepackSchemaFilter.cs`: remove "does not check it". State that the install refuses another major version, and that the field is optional.
- [X] T016 [US2] Add a swagger assertion to `tests/contract/Gamepacks/GamepackApiVersionContractTests.cs`: the swagger document has the `requiredApiMajor` text that says the install refuses a value that differs from the running major version (FR-009). Needs T015 and T010.

**Checkpoint**: US1 and US2 work. T009, T010, T013, T014, T016 pass.

---

## Phase 5: User Story 3 - Know what a version change may break (Priority: P2)

**Goal**: An author finds the compatibility rule in the docs.

**Independent Test**: Read `docs/gamepacks.md`. Find the rule, the meaning of the three numbers, and `GET /api/version`.

- [X] T017 [US3] Add the section "API version and compatibility" to `docs/gamepacks.md`: the one line in the format that `contracts/api-version.md` defines (section "Docs line"; do not define it again here); the rule (inside one major version, an existing valid sequence stays valid; a change that breaks this rule raises the major version); which number changes in which case; `GET /api/version`; the manifest field `requiredApiMajor`; the install refusal and the message; the rollback exception. Update the existing field text and install refusal text. Use STE.
- [X] T018 [US3] Add the unit test `ApiVersionTests` in `tests/unit/Gamepacks/ApiVersionTests.cs`: the constants have the expected values, and the docs line (format in `contracts/api-version.md`) in `docs/gamepacks.md` equals the constants. Needs T002 and T017, so it is never red.
- [X] T019 [P] [US3] Add a short note and a link to the new section in `docs/architecture.md`, section "Gamepack install". Use STE.

**Checkpoint**: All stories work. T018 passes.

---

## Phase 6: Polish and cross-cutting

- [X] T020 Run the full build and all tests in `tests/unit` and `tests/contract`. Fix any failure that this feature causes.
- [X] T021 Run the steps in `specs/144-api-version-compat/quickstart.md` to check the end result.
- [X] T022 Check that all new or changed text (code comments, messages, docs) uses STE (constitution Principle VI).

---

## Dependencies and execution order

- Phase 1 first. Phase 2 (T002) blocks T003, T006, T009, T011, T013, T014, T018.
- US1 and US2 are independent after T002. T008 uses its own file (`ApiVersionSchemaFilter.cs`) and T015 uses `GamepackSchemaFilter.cs`, so they run in parallel.
- US3 docs (T017) is independent of code. T018 needs T002 and T017.
- T004 and T005 before T006. T006 before T007. T011 before T012. T015 and T010 before T016.
- Polish after all stories.

## Parallel examples

- After T002: T003, T004, T005, T008, T009, T010, T015 run in parallel (different files).
- T013 and T014 run in parallel with T011.
- T017 and T019 run in parallel with US1 and US2 work.

## Implementation strategy

- MVP: Phase 1, Phase 2, US2 (the protection). US1 follows because the check needs a visible running version.
- Then US3 (docs), then Polish. Commit and bump the version only at push time (CLAUDE.md).
