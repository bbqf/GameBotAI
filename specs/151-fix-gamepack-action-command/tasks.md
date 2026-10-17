# Tasks: Keep the command action of an Action step in gamepack install

**Input**: Design documents from `C:\src\GameBot\specs\151-fix-gamepack-action-command\`
**Prerequisites**: plan.md, spec.md, research.md (R1 to R13), data-model.md, contracts\gamepack-step-json.md, quickstart.md

**Tests**: Tests are required (FR-006, FR-008, Constitution Principle II). Write each test first and see it fail before the fix.

**Organization**: Tasks are grouped by user story. Backend only, so no web UI task and no phone check.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependency on an incomplete task).
- Tasks that write the same file are NOT [P]. They run in the order of their IDs.
- A task that says "fix if needed" has a done check. The task is complete only when the check passes.
- A test task that needs a type that does not exist yet may fail to compile until the stub (T006) exists. This is expected.
- Run commands with absolute paths. Do not use `cd`.

## Path Conventions

- Domain: `C:\src\GameBot\src\GameBot.Domain\Gamepacks\`
- Service: `C:\src\GameBot\src\GameBot.Service\Endpoints\`
- Unit tests: `C:\src\GameBot\tests\unit\Gamepacks\`
- Contract tests: `C:\src\GameBot\tests\contract\Gamepacks\`
- Integration tests: `C:\src\GameBot\tests\integration\Gamepacks\`

---

## Phase 1: Setup

**Purpose**: Know the state before the change.

- [X] T001 Run `dotnet build` for the GameBot solution and the three test projects (unit, contract, integration). Done check: the build has 0 errors. Record any existing test failure in the PR notes so it is not blamed on this change.
- [X] T002 Read `C:\src\GameBot\src\GameBot.Domain\Gamepacks\GamepackFileSerializer.cs`, `GamepackReferences.cs`, `GamepackValidator.cs`, and `C:\src\GameBot\tests\unit\Gamepacks\GamepackTestKit.cs`. Note how the contract and integration projects link `GamepackTestKit.cs`, so T004 and T005 use the same method. No file change.

---

## Phase 2: Foundational (blocks all user stories)

**Purpose**: Shared fixture, links to the fixture in each test project, a stub reader, and the test that a step problem blocks sequence creation.

**CRITICAL**: No user story task starts before this phase is complete.

- [X] T003 Create the shared fixture `C:\src\GameBot\tests\unit\Gamepacks\GamepackStepFixtures.cs`. It holds: the issue #321 reproduction file (3 objects, step `s1`, command in the same file); builders for each step form (`primitiveAction` only, `commandReference` only, `commandId` only, all three, `commandId` + `primitiveAction` with no `commandReference`, stored form `action` + `commandId` + `commandReference`); the same forms inside a Loop `body` and in the `body` and `elseBody` of a conditional step; conflict forms (`commandReference` vs `primitiveAction`, `commandId` vs `primitiveAction`, `action` vs `primitiveAction`); an unresolvable command ID for each of the three forms; an unknown member on a step; an alias on a non-Action step (Loop, Conditional, Command). Done check: the unit project builds.
- [X] T004 [P] Link `GamepackStepFixtures.cs` in `C:\src\GameBot\tests\contract\GameBot.ContractTests.csproj` with a `Compile Include` item, as for `GamepackTestKit.cs`. Done check: the contract project builds and can use the fixture type.
- [X] T005 [P] Link `GamepackStepFixtures.cs` in `C:\src\GameBot\tests\integration\GameBot.IntegrationTests.csproj` with a `Compile Include` item, as for `GamepackTestKit.cs`. Done check: the integration project builds and can use the fixture type.
- [X] T006 Create a stub of the new class in `C:\src\GameBot\src\GameBot.Domain\Gamepacks\GamepackStepReader.cs` with its public signature (input: sequence `JsonObject`, sequence name, list of problems; output: converted object) and a body that returns the input unchanged. Add XML comments on public members. Done check: the Domain project builds, and the stub changes no behavior (T001 test results stay the same).
- [X] T007 Write the foundational test in `C:\src\GameBot\tests\unit\Gamepacks\GamepackValidatorStepTests.cs`: a file with a step problem (unknown member on a step) gives a `structure` problem in the dry run, and the plan holds no sequence, so the install does not create the sequence. Use the fixture from T003. Done check: the test compiles and fails (no problem is reported yet).

**Checkpoint**: Fixture reachable from unit, contract, and integration projects. The stub exists. The blocking test fails for the right reason.

---

## Phase 3: User Story 1 - Install a sequence that runs a command (Priority: P1) MVP

**Goal**: An Action step keeps its command action after install, in every form, at every depth, and the step names the installed command.

**Independent Test**: Install the issue #321 file. `GET /api/sequences/{id}` returns step `s1` with `primitiveAction` and a `commandReference` equal to the file in `commandId` and `commandName`.

### Tests for User Story 1 (write first, see them fail)

- [X] T008 [US1] In `C:\src\GameBot\tests\unit\Gamepacks\GamepackStepReaderTests.cs`, create the file and add tests for the forms in FR-002: `primitiveAction` only; `commandReference` only; `commandId` only; all three; stored form given as is (kept as given); `commandId` + `primitiveAction` for the same command with no `commandReference` (result: `primitiveAction` of type `command` and a `commandReference` with that ID). Also assert: `commandReference` keeps `commandId` and `commandName`; `isResolved` is accepted and not stored. Done check: tests compile and fail.
- [X] T009 [US1] In `C:\src\GameBot\tests\unit\Gamepacks\GamepackStepReaderTests.cs`, add tests for nesting: Action step in a Loop `body`; Action step in the `body` of a conditional step; Action step in the `elseBody` of a conditional step. Done check: tests compile and fail.
- [X] T010 [US1] In `C:\src\GameBot\tests\unit\Gamepacks\GamepackStepReaderTests.cs`, add conflict tests (structure problem with sequence name, step ID, field name; no sequence created): `commandReference` vs `primitiveAction` for different commands; `commandId` vs `primitiveAction` for different commands; `action` vs `primitiveAction` that differ in type or command; `action` wins when `primitiveAction` agrees. Done check: tests compile and fail.
- [X] T011 [US1] In `C:\src\GameBot\tests\unit\Gamepacks\GamepackStepReaderTests.cs`, add tests for aliases on a step that is not an Action step (Loop, Conditional, Command with `primitiveAction` or `commandReference`; Loop with `commandId`): each gives an unknown-member `structure` problem. Add a test that a stored Command step with `commandId` reads as before (no problem, value unchanged). Add a test that `stepType` absent counts as an Action step. Done check: tests compile and fail.
- [X] T012 [P] [US1] Create `C:\src\GameBot\tests\unit\Gamepacks\GamepackReferencesRewriteTests.cs`: when the install gives a command a new ID, `CommandId`, `CommandReference.CommandId`, and `Action.Parameters["commandId"]` all change to the new ID, in the root list, a Loop `body`, and the `body` and `elseBody` of a conditional step. Add a test that the ID stays when no other command uses it. Done check: tests compile; the payload test fails.
- [X] T013 [P] [US1] Add unreferenced-command tests to `C:\src\GameBot\tests\unit\Gamepacks\GamepackValidatorStepTests.cs` (the same file as T007, so run after T007): a step that names a command that is not in the file and not on the system gives a `reference` problem, for `commandReference`, `primitiveAction`, and `commandId` (3 tests). Done check: tests compile and fail.
- [X] T014 [P] [US1] Create `C:\src\GameBot\tests\contract\Gamepacks\GamepackActionStepInstallContractTests.cs`: install the issue #321 file through the API, then `GET /api/sequences/{id}`; assert `primitiveAction` is not null, `commandReference` equals the file in `commandId` and `commandName`, and the command exists with the file ID. Add contract tests for the dry-run problems: conflict (structure), unresolvable command for the three forms (reference), alias on a non-Action step (structure). Done check: tests compile and fail.

### Implementation for User Story 1

- [X] T015 [US1] Implement `C:\src\GameBot\src\GameBot.Domain\Gamepacks\GamepackStepReader.cs` (replace the stub): walk the root `steps`, the Loop `body`, and the `body` and `elseBody` of a conditional step. For an Action step (or `stepType` absent), convert `primitiveAction` (type, schemaVersion, `payload` to `Parameters`), `commandReference` (`commandId`, `commandName`; ignore `isResolved`), and `commandId` to the stored form. Apply the rules of R5 for a missing source and for a stored form. Done check: the tests of T008 and T009 pass.
- [X] T016 [US1] In `C:\src\GameBot\src\GameBot.Domain\Gamepacks\GamepackStepReader.cs`, add the conflict rules (R5) and the alias rule for non-Action steps (R11). Each problem is a `structure` problem with the sequence name, the step ID, and the field name. Done check: the tests of T010 and T011 pass.
- [X] T017 [US1] In `C:\src\GameBot\src\GameBot.Domain\Gamepacks\GamepackFileSerializer.cs`, call the step reader on each sequence object before `Deserialize<CommandSequence>`, and pass its problems on to the caller. Done check: the tests of T013 and T014 for the happy path and the reference problems pass. The signature hash code is not changed.
- [X] T018 [US1] In `C:\src\GameBot\src\GameBot.Domain\Gamepacks\GamepackReferences.cs`, change `RewriteCommandIds` so it also changes `Action.Parameters["commandId"]` when `Action.Type` is `command` (R7). Done check: the tests of T012 pass.
- [X] T019 [US1] Fix if needed: if a test of T013 still fails after T017, find the cause in `C:\src\GameBot\src\GameBot.Domain\Gamepacks\GamepackValidator.cs` (`CheckReferences` must read the `CommandId` that the reader sets) and fix it. Done check: all tests of T013 pass. If no test fails, mark this task done with the note "no change needed".
- [X] T020 [US1] Run the Gamepack unit tests and the contract tests of T014. Done check: all pass, and the test of T007 now passes too (a step problem blocks the creation of the sequence).

**Checkpoint**: US1 is complete. The issue #321 file installs and keeps the command action (SC-001, SC-002).

---

## Phase 4: User Story 2 - No silent loss of step fields (Priority: P1)

**Goal**: A step member that the install cannot keep gives a dry-run problem, and the install does not create the sequence.

**Independent Test**: Dry run with an unknown member on a step. The result lists a `structure` problem with the sequence name, the step ID, and the field name, and `valid` is false.

### Tests for User Story 2

- [X] T021 [US2] In `C:\src\GameBot\tests\unit\Gamepacks\GamepackStepReaderTests.cs`, add tests: an unknown member on an Action step, on a Loop step, and on a nested step (in a Loop `body` and in an `elseBody`) each give a `structure` problem. Names match without case (`PrimitiveAction` and `primitiveaction` work). A member inside a nested payload or a condition is not checked. Done check: tests compile and fail before T023.
- [X] T022 [P] [US2] In `C:\src\GameBot\tests\contract\Gamepacks\GamepackActionStepInstallContractTests.cs`, add contract tests (the same file as T014, so run after T014): the dry run with an unknown member returns `valid: false` and a `structure` problem that names the field and the step; the install returns the same problem and the sequence does not exist after the call. Done check: tests compile and fail before T023.

### Implementation for User Story 2

- [X] T023 [US2] In `C:\src\GameBot\src\GameBot.Domain\Gamepacks\GamepackStepReader.cs`, add the known-member check (R4): known names are the members of the stored step plus the aliases `primitiveAction`, `commandId`, `commandReference`. Match names without case. Check each step, including nested steps, and no nested payload. Done check: the tests of T021 and T022 pass.
- [X] T024 [US2] Fix if needed: if a conditions, loops, or `breakCondition` (`imageVisible`) test fails after T023, change the known-member list so the stored members are all accepted. Done check: all existing gamepack tests for conditions, loops, and `breakCondition` pass with no change in expected values (SC-005). If none fails, mark this task done with the note "no change needed".

**Checkpoint**: US2 is complete. No install run drops a step field without a message (SC-003).

---

## Phase 5: User Story 3 - Know the JSON of an Action step in a gamepack file (Priority: P2)

**Goal**: The OpenAPI install description has a sample Action step that runs a command, with the matching command in the file.

**Independent Test**: Read the OpenAPI description of the install endpoint. It has the sample.

- [X] T025 [US3] Create `C:\src\GameBot\tests\contract\Gamepacks\GamepackActionStepOpenApiTests.cs`: read the OpenAPI document and assert that the description of the `InstallGamepack` operation contains a sample Action step with `primitiveAction` and `commandReference`, and a matching command. Done check: the test compiles and fails.
- [X] T026 [US3] In `C:\src\GameBot\src\GameBot.Service\Endpoints\GamepacksEndpoints.cs`, add the sample to the `WithDescription` text of `InstallGamepack`, in STE. The sample must be valid: a test in T014 can install it. Done check: the test of T025 passes.
- [X] T027 [US3] Fix if needed: if the sample JSON does not install, change the sample text, not the reader. Done check: a test installs the sample from the description (parse it from the description or copy it into the fixture) and gets `valid: true`.

**Checkpoint**: US3 is complete (FR-005).

---

## Phase 6: User Story 4 - Export then install keeps the command action (Priority: P2)

**Goal**: A gamepack exported from a system installs on a clean system with the same command action.

**Independent Test**: Export a gamepack with a command Action step, install it on a clean data directory, compare the steps.

- [X] T028 [US4] Create `C:\src\GameBot\tests\integration\Gamepacks\GamepackActionStepRoundTripTests.cs` with the round-trip test: build a sequence with a command Action step, export, install on a clean system, compare `primitiveAction` and `commandReference` (`commandId`, `commandName`) with the source (SC-004). Done check: the test compiles; record the result.
- [X] T029 [US4] In `C:\src\GameBot\tests\integration\Gamepacks\GamepackActionStepRoundTripTests.cs`, add a test that runs the install a second time (update or take over) and keeps the command action correct. Add a test that an Action step in a Loop `body` and in a conditional `body` and `elseBody` keeps its action when the install gives the command a new ID. Done check: tests compile; record the result.
- [X] T030 [US4] In `C:\src\GameBot\tests\integration\Gamepacks\GamepackActionStepRoundTripTests.cs`, add a measure test for other action types (for example `wait-for-image`): install a file with the action and compare with `POST /api/sequences`. Do not fix a loss here. If the install loses data, write the result in the PR notes for a new GitHub issue. Done check: the test runs and the result is recorded.
- [X] T031 [US4] Fix if needed: if T028 or T029 fails, find the cause (export form, `GamepackFileSerializer.Serialize`, the reader rule for the stored form) and fix it in the Domain `Gamepacks` folder. Done check: T028 and T029 pass. If both pass, mark this task done with the note "no change needed".

**Checkpoint**: US4 is complete.

---

## Phase 7: Polish and cross-cutting

- [X] T032 [P] Update `C:\src\GameBot\docs\gamepacks.md`: the step forms (Action step with `primitiveAction`, `commandReference`, `commandId`; stored form; errors for unknown members and conflicts). Use STE.
- [X] T033 [P] Update `C:\src\GameBot\specs\STATUS.md`: add the feature row for 151. Use STE.
- [X] T034 Run the FULL unit, contract, and integration suites (not a Gamepack filter only). They include the `POST /api/sequences` tests and the gamepack performance tests (`GamepackPerformanceTests`, `GamepackTakeOverPerformanceTests`, `GamepackCatalogPerformanceTests`). Done check: all pass, or each failure is shown to exist before this change (T001 record). Do not change `POST /api/sequences` (FR-008).
- [X] T035 Run the checks of `C:\src\GameBot\specs\151-fix-gamepack-action-command\quickstart.md`. Done check: each step gives the result that quickstart.md states.
- [X] T036 Check all new text in code comments, tests, docs, and the OpenAPI sample for ASD-STE100 STE (Principle VI). Done check: no long sentence, no passive form where an active form works.
- [x] T037 Before the push: run `git fetch origin master`, compare `C:\src\GameBot\installer\versioning\version.override.json` with `origin/master`, and bump it. If `minor` or `major` changed, set `patch` to `"0"`. If not, set `patch` to the `patch` on `origin/master` plus 1. Set `updatedBy` and `updatedAtUtc`. Do nothing if the file already has this value.

---

## Dependencies and execution order

- Phase 1 then Phase 2. Phase 2 blocks all stories.
- Phase 2 order: T003 first (T004, T005 need it). T004 and T005 are [P]. T006 can run with T004 and T005 but is shown after T003. T007 needs T003 and T006.
- US1 (Phase 3) is the MVP. Tests T008 to T011 write the same file, so they run in ID order. T012, T013, T014 are [P] with each other (T013 writes the T007 file, so it runs after T007, which is already done in Phase 2). T015, T016 write the same file and run in order. T017 needs T015. T018 is independent of T015 to T017 in code, but it shares the Domain project build, so run it after T017 if one agent works alone.
- US2 (Phase 4) needs T015 and T016 (same reader file). T023 runs after T016.
- US3 (Phase 5) needs T017. It is independent of US2.
- US4 (Phase 6) needs T017 and T018. T028 to T030 write the same file and run in order.
- Phase 7 needs all stories. T032 and T033 are [P]. T034 runs last before T037.

### Files written by more than one task (never [P] with each other)

- `GamepackStepReader.cs`: T006, T015, T016, T023.
- `GamepackStepReaderTests.cs`: T008, T009, T010, T011, T021.
- `GamepackValidatorStepTests.cs`: T007, T013.
- `GamepackActionStepInstallContractTests.cs`: T014, T022.
- `GamepackActionStepRoundTripTests.cs`: T028, T029, T030.

## Parallel examples

- Phase 2: T004 and T005 together after T003.
- US1 tests: T012, T013, and T014 together, while T008 to T011 run in order on the reader test file.
- Phase 7: T032 and T033 together.

## Implementation strategy

- MVP: Phase 1, Phase 2, and Phase 3 (US1). This fixes issue #321.
- Next: US2 (no silent loss, also P1), then US3 and US4 (P2), then Phase 7.
- Always: see the test fail first, make the change, see the test pass, run the full suites at the end (T034).
