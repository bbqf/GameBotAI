# Tasks: Accept any member order in gamepack files

**Input**: Design documents from `/specs/154-gamepack-any-member-order/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/install-endpoint.md, quickstart.md

**Tests**: Required by spec FR-007. Each case runs for dry run and for real install.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependency on an incomplete task)
- **[Story]**: US1 = install a pack whose type member is not first (P1). US2 = clear 400 for a missing or bad type (P2).

## Build order rule (spec I2)

Phase 2 creates the new types `GamepackTypeDiscriminatorCheck` and `GamepackParseResult.InvalidType`. Every test task that names one of these types is in Phase 4 or later, so it compiles only after Phase 2. Phase 3 tests use only existing types.

## Phase 1: Setup

- [ ] T001 Read `src/GameBot.Domain/Gamepacks/GamepackFileSerializer.cs`, `src/GameBot.Domain/Gamepacks/GamepackService.cs`, and `src/GameBot.Domain/QueueSettings/SettingsDocument.cs` (class `SettingsDocumentJson`). Note where `SequenceOptions`, `CommandOptions`, `ReadList`, and `GamepackParseResult` are defined. Run the existing gamepack tests once with `dotnet test` to get a green baseline (SC-004).

## Phase 2: Foundational (blocks all user stories)

- [ ] T002 Add the `InvalidType` bool member (default false) to `GamepackParseResult` in `src/GameBot.Domain/Gamepacks/GamepackFileSerializer.cs`. Add the type problem JSON path and the reason code to the same result.
- [ ] T003 Create `src/GameBot.Domain/Gamepacks/GamepackTypeDiscriminatorCheck.cs` (internal class). Input: entry `JsonElement`, target `Type`, `JsonSerializerOptions`. Output: no problem, or JSON path with a reason code (`Missing`, `Unknown`, `NotString`, `Duplicate`). It reads `JsonTypeInfo.PolymorphismOptions` (discriminator name, derived type discriminators) and the property and element types from the metadata. It follows object members, arrays, dictionaries, and nullable types. At each polymorphic node it counts the discriminator members (0 = missing, more than 1 = duplicate), checks the value kind (not a string = `NotString`), and checks the value against the derived list (not in list = `Unknown`). It does not read exception text (FR-009).
- [ ] T004 Make `GamepackTypeDiscriminatorCheck` visible to the unit test project: add `InternalsVisibleTo` for the unit test assembly in the Domain project if it is not set. Check with `dotnet build`.

**Checkpoint**: `dotnet build` passes. The check type exists and no code calls it yet.

## Phase 3: User Story 1 - Install a pack whose type member is not first (P1)

**Goal**: A pack with `type` in any position gives the same result as `type` first, for dry run and real install, with equal hash and signature results.

**Independent Test**: Send one pack with a step condition in the order `imageId`, `minSimilarity`, `negate`, `type` to both install routes. The result equals the result for the `type`-first pack.

### Tests for User Story 1

- [ ] T005 [P] [US1] Add unit tests in `tests/unit/Gamepacks/GamepackFileSerializerMemberOrderTests.cs`: a condition with `type` first, in the middle, and last parses to equal objects; a condition nested in an `all` condition with `type` last; a `loopType` loop config with `loopType` last (FR-001, FR-006).
- [ ] T006 [P] [US1] Add a unit test in `tests/unit/Gamepacks/GamepackMemberOrderHashTests.cs`: two files that differ only in member order give an equal content hash and an equal signature check result (FR-003, SC-002).
- [ ] T007 [P] [US1] Add a unit test in `tests/unit/Gamepacks/GamepackPolymorphicTypesReflectionTests.cs`. It walks the type graph of `CommandSequence` and `Command` by reflection (properties, array and generic element types, dictionary value types, nullable types, derived types from `JsonDerivedType`). It collects every type that has `JsonPolymorphicAttribute`. It asserts the set equals exactly `SequenceStepCondition` and `LoopConfig`. The failure message names the unlisted type (FR-006).
- [ ] T008 [P] [US1] Add a unit test in `tests/unit/QueueSettings/SettingsDocumentImportRegressionTests.cs`. It reads one valid queue settings document with `SettingsDocumentValidator` (queue settings import). It asserts the result equals the expected result that is fixed in the test (same document, same normalized output, same hash, no extra problem). This shows that the option change on `SettingsDocumentJson.Options` has no visible effect on the import (plan, Loop 2 F1). Run it before T012 and after T012; both runs pass.
- [ ] T009 [P] [US1] Add contract tests in `tests/contract/Gamepacks/GamepackMemberOrderContractTests.cs`: dry run and real install of a pack with `type` first, in the middle, and last each answer 200 and not 500 (FR-007, SC-001).
- [ ] T010 [P] [US1] Add an integration test in `tests/integration/Gamepacks/GamepackMemberOrderIntegrationTests.cs`: a real install in canonical order stores the same objects as the `type`-first install (FR-002).
- [ ] T011 [P] [US1] Add a smoke test in `tests/integration/Gamepacks/GamepackLargePackSmokeTests.cs`. It builds one pack with 300 sequences, each with a condition that has `type` as the last member, and one small pack with 1 sequence of the same form. It installs both by dry run. It asserts that both give status 200 and `valid:true`. The test makes no assertion on the number of parse passes or on time (spec C1).

### Implementation for User Story 1

- [ ] T012 [US1] Set `AllowOutOfOrderMetadataProperties = true` on `SequenceOptions` and `CommandOptions` in `src/GameBot.Domain/Gamepacks/GamepackFileSerializer.cs`, and on `ReadOptions` (returned by `SettingsDocumentJson.Options`) in `src/GameBot.Domain/QueueSettings/SettingsDocument.cs`. Do not change `ManifestOptions`, `WriteOptions`, or any API endpoint options (FR-008).
- [ ] T013 [US1] Run T005 to T011 and the existing gamepack tests. All must pass.

**Checkpoint**: US1 works alone. Any member order installs.

## Phase 4: User Story 2 - Clear 400 for a missing or bad type (P2)

**Goal**: A missing, unknown, not-string, or duplicate `type` gives 400 `InvalidJson` with the JSON path and a short reason. It never gives 500.

**Independent Test**: Send packs with a step condition that has no `type`, an unknown `type`, a number `type`, and a duplicate `type`. Each gives 400 with the path, for dry run and real install.

### Tests for User Story 2

- [ ] T014 [P] [US2] Add unit tests for `GamepackTypeDiscriminatorCheck` in `tests/unit/Gamepacks/GamepackTypeDiscriminatorCheckTests.cs`: missing, unknown, not-string (number and null), duplicate; the path and the reason code for each; a valid node gives no problem; a `loopType` loop config with a bad value gives the path of the loop (FR-004, FR-005, FR-009).
- [ ] T015 [P] [US2] Add the nested bad-type unit test in `tests/unit/Gamepacks/GamepackTypeDiscriminatorCheckTests.cs` (same file as T014, so run after T014). Input: a sequence at `$.sequences[0]` with `steps[0].breakCondition` of type `all` whose `conditions` array holds two items. Item 0 is a valid `imageVisible` condition. Item 1 has the members in this order: `imageId`, `negate`, `type` with the value `"nope"`. So `type` is not the first member. Expected: reason code `Unknown` and path `$.sequences[0].steps[0].breakCondition.conditions[1]`.
- [ ] T016 [P] [US2] Add unit tests in `tests/unit/Gamepacks/GamepackFileSerializerInvalidTypeTests.cs`: `ReadList` sets `InvalidType` with the path and the reason; a forced `NotSupportedException` gives `InvalidType` with the entry path and no thrown exception (safety net); a metadata case that the check does not know (test options object with an unlisted polymorphic type) reaches the safety net.
- [ ] T017 [P] [US2] Add contract tests in `tests/contract/Gamepacks/GamepackBadTypeContractTests.cs`: missing, unknown, not-string, and duplicate `type` each give 400 with error code `InvalidJson` and the JSON path in the message, for dry run and for real install (FR-004, FR-005, FR-007, SC-003). Add the same nested bad-type case as T015 at endpoint level: status 400, `InvalidJson`, message holds `$.sequences[0].steps[0].breakCondition.conditions[1]`, for dry run and for real install.
- [ ] T018 [P] [US2] Add an integration test in `tests/integration/Gamepacks/GamepackBadTypeIntegrationTests.cs`: a real install of a pack with a missing `type` answers 400 and stores no object.

### Implementation for User Story 2

- [ ] T019 [US2] Call `GamepackTypeDiscriminatorCheck` in `ReadList` before `Deserialize` in `src/GameBot.Domain/Gamepacks/GamepackFileSerializer.cs`. A found problem becomes a `structure` problem with the JSON path and the reason text, and sets `InvalidType`. Use these reason texts: `the type member is missing`, `the type value is unknown`, `the type value must be a string`, `the type member is duplicated`.
- [ ] T020 [US2] Catch `NotSupportedException` in `ReadList` in `src/GameBot.Domain/Gamepacks/GamepackFileSerializer.cs`. Report the entry path, set `InvalidType`, and use the exception type only (no message text).
- [ ] T021 [US2] Map `InvalidType` to `GamepackInstallStatus.InvalidJson` in `InstallAsync` and in the dry run path in `src/GameBot.Domain/Gamepacks/GamepackService.cs`. Other wrong-shape problems keep their 422 and `valid:false` behavior (FR-008).
- [ ] T022 [US2] Run T014 to T018 and the existing gamepack tests. All must pass.

**Checkpoint**: US1 and US2 both work.

## Phase 5: Polish and cross-cutting

- [ ] T023 [P] Add one description line to the OpenAPI text of the manual install route and the catalog install route in `src/GameBot.Service/Endpoints/GamepacksEndpoints.cs`: status 400 also covers a missing or bad type member. Keep the schema. Update the gamepack docs in `docs/` that describe the install errors.
- [ ] T024 [P] Check that `specs/154-gamepack-any-member-order/contracts/install-endpoint.md` matches the final 400 text. Edit the file if the text differs.
- [ ] T025 Run `dotnet build` and the unit, contract, and integration test projects. All must pass (constitution gate).
- [ ] T026 Bump the version in `installer/versioning/version.override.json` per CLAUDE.md before the push.
- [ ] T027 Run the steps in `specs/154-gamepack-any-member-order/quickstart.md` to confirm the behavior.

## Dependencies and order

- Phase 1, then Phase 2, then US1 (Phase 3), then US2 (Phase 4), then Phase 5.
- Phase 2 blocks all test tasks that name `GamepackTypeDiscriminatorCheck` or `InvalidType` (T014 to T018).
- T012 edits `GamepackFileSerializer.cs`, and T019 and T020 edit the same file: run them in this order.
- T008 runs before and after T012.
- T019, T020, T021 run in order (shared files).

## Parallel examples

- US1 tests T005 to T011 touch different files and can run together.
- US2 tests T014 (with T015 after it), T016, T017, T018 touch different files and can run together.

## Implementation strategy

- MVP = Phase 1, 2, and US1 (the reported 500 is gone).
- Then add US2 for clear 400 answers. Then polish.
