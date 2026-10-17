# Tasks: Gamepack command reference check

**Input**: Design documents in `specs/155-gamepack-command-reference-check/` (plan.md, spec.md, research.md, data-model.md, contracts/reference-problem.md, quickstart.md)
**Tests**: Required by FR-008. They cover: unresolved by id (each id form), unresolved by name, resolved in the gamepack, resolved on the instance, name-only rename-after-conflict, unchanged command-to-command reference, and the install integration test.

Format: `- [ ] T### [P?] [US?] Description with file path`

## Phase 1: Setup

- [X] T001 Read `src/GameBot.Domain/Gamepacks/GamepackReferences.cs` (`CommandIds(CommandSequence)`, `StepCommandIds`, `RewriteCommandIds`, `Flatten`) and `src/GameBot.Domain/Gamepacks/GamepackValidator.cs` (`CheckReferences`, `ReportMissing`). Read `tests/integration/Gamepacks/GamepackDryRunIntegrationTests.cs`, `tests/integration/Gamepacks/GamepackInstallIntegrationTests.cs`, and `tests/unit/Gamepacks/GamepackValidatorStructureTests.cs` to copy their helpers. Find how the install renames a command id on a conflict (see `tests/unit/Gamepacks/GamepackValidatorConflictTests.cs`). No file change.

## Phase 2: Foundational

No blocking work. Both user stories use the same check in `CheckReferences`.

## Phase 3: User Story 1 - Dry run finds a dangling command reference (P1)

**Goal**: The dry run reports a `reference` problem for each unresolved command id or name in a sequence step.
**Independent Test**: A dry run on a gamepack with a step that runs an unknown command gives a `reference` problem and `valid: false`.

### Tests for User Story 1 (write first, expect failure)

All tests go in the new file `tests/unit/Gamepacks/GamepackValidatorCommandReferenceTests.cs`. Each problem case asserts category `reference`, object type `sequence`, the message "Command 'x' is not in the gamepack or in the installation.", and `valid: false`.

- [X] T002 [US1] Create `tests/unit/Gamepacks/GamepackValidatorCommandReferenceTests.cs` with its helpers (copy from `GamepackValidatorStructureTests.cs`). Add the regression test: unresolved step `commandId` gives a `reference` problem (already works today).
- [X] T003 [US1] In the same file, add tests for unresolved by id: `commandReference.commandId` unknown, and Action payload `commandId` (type `command`) unknown, as string and as `JsonElement` string. Add a case where the three id forms hold different values and only one is unknown.
- [X] T004 [US1] In the same file, add tests for unresolved by name: a name-only `commandReference.commandName` that is in no list gives a problem. Add name match case-insensitive and trimmed.
- [X] T005 [US1] In the same file, add tests for resolved cases that give no new problem: command in the gamepack (by id and by name), command on the instance (by id and by name), primitive step with no command reference, and nested step in a loop body.
- [X] T006 [US1] In the same file, add edge-case tests: name next to an id is not checked; same name but different id does not resolve a missing id; a command that the plan removes is not a target; a duplicate missing value gives one problem per sequence.
- [X] T007 [US1] In the same file, add the rename-after-conflict test: the install renames a gamepack command id because of a conflict with an instance command, and a name-only step that names this command still passes (name check runs on the file names). Also add the test that an id step to the old id is judged as before.
- [X] T008 [US1] In the same file, add the unchanged command-to-command test: a command that refers to another command gives the same result as before this feature (resolved gives no problem, unresolved gives the same problem as before). Compare with the output that the code gave before the change.

### Implementation for User Story 1

- [X] T009 [US1] In `src/GameBot.Domain/Gamepacks/GamepackReferences.cs`, extend `StepCommandIds` (used by `CommandIds(CommandSequence)`) to also yield `CommandReference.CommandId` and the Action payload `commandId` (type `command`; string or `JsonElement` string). Skip blank values. Read the same fields that `RewriteCommandIds` rewrites.
- [X] T010 [US1] In `src/GameBot.Domain/Gamepacks/GamepackReferences.cs`, add `CommandNames(CommandSequence)`. It yields the trimmed `CommandReference.CommandName` only for a step that has no id in any of the three id forms. It covers nested steps through the existing `Flatten`.
- [X] T011 [US1] In `src/GameBot.Domain/Gamepacks/GamepackValidator.cs` `CheckReferences`, build a case-insensitive `commandNameTargets` set from the gamepack command names (file names) and the instance command names, without commands that the plan removes. Call `ReportMissing` for each sequence with `GamepackReferences.CommandNames(sequence)`. Keep the message. Do not change the image check. Do not add a second parallel check.
- [X] T012 [US1] Run `dotnet test --filter "FullyQualifiedName~GamepackValidator"` and fix failures until all tests from T002 to T008 pass.

**Checkpoint**: The dry run finds all four command reference forms.

## Phase 4: User Story 2 - Install refuses a dangling command reference (P2)

**Goal**: The install reports the same `reference` problem as the dry run and installs nothing.
**Independent Test**: An install of a gamepack with an unknown command in a step reports the problem and writes nothing.

- [X] T013 [US2] Add the install integration test in `tests/integration/Gamepacks/GamepackInstallIntegrationTests.cs`: a gamepack with an unresolved command in a sequence step (cover an id form and a name-only form) gives the same `reference` problem in the dry run and in the install, and the install writes nothing (no command, image, or sequence is added).
- [X] T014 [US2] In the same file, add the check that a gamepack with all command references resolved installs as before. No production code change is expected, because the install uses the same validator.
- [X] T015 [US2] Run `dotnet test --filter "FullyQualifiedName~GamepackInstall|FullyQualifiedName~GamepackDryRun"`. Fix failures.

**Checkpoint**: Install and dry run agree (SC-003).

## Phase 5: Polish

- [X] T016 [P] Confirm that existing valid gamepack fixtures stay valid (SC-002). Run the full `tests/unit/Gamepacks`, `tests/contract/Gamepacks`, and `tests/integration/Gamepacks` suites with `dotnet test --filter "FullyQualifiedName~Gamepack"`.
- [X] T017 [P] If a docs file lists the reference problem kinds, update it in STE. Search `docs/` for the reference problem text. If none lists them, make no change.
- [X] T018 Run the steps in `specs/155-gamepack-command-reference-check/quickstart.md` and confirm the results.

The version bump (FR-009) is not a code task. The pipeline does it before the push.

## Dependencies

- T001 first. T002 before T003 to T008 (same file, same helpers). T009 and T010 before T011. T011 before T012.
- Tests T002 to T008 can be written before or in parallel with T009 to T011 (different files).
- US2 (T013 to T015) depends on T011. Polish depends on both stories.

## Parallel example

After T002: T009 and T010 (`GamepackReferences.cs`) and T003 to T008 (test file) touch different files and can run together. T016 and T017 can run together.

## Implementation strategy

MVP is User Story 1 (T001 to T012). It changes the shared check, so US2 is proof by one integration test. Deliver US1, then US2, then polish.
