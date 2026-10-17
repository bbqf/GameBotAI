# Tasks: Gamepack dry run skips parameter placeholder image ids

**Input**: Design documents in `C:\src\GameBot\specs\152-gamepack-dry-run-placeholder-image\`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts\dry-run-reference-check.md, quickstart.md

**Tests**: Tests are required (spec FR-007). Write each test before the code that makes it pass.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: The task can run in parallel (different files, no open dependency).
- **[Story]**: The user story of the task (US1, US2).

## Rules for all phases

- Run the build and the tests green before each commit.
- A known flaky CI test is rerun once before it counts as a failure.
- All new text (code comments, messages, docs) uses STE.
- Do not change: command image checks, `POST /api/sequences` validation, queue template save, other dry run issues (#322, #323, #324, #325).

## Phase 1: Setup

- [X] T001 Run the baseline build: `dotnet build C:\src\GameBot\GameBot.sln`. Confirm it passes before any edit.
- [X] T002 [P] Read `C:\src\GameBot\src\GameBot.Domain\Gamepacks\GamepackValidator.cs` (`CheckReferences`, `ReportMissing`), `C:\src\GameBot\src\GameBot.Domain\Utils\TemplateSubstitutor.cs`, and `C:\src\GameBot\src\GameBot.Domain\Services\ParameterValidationService.cs` (private `IsWholePlaceholder`). Read `C:\src\GameBot\tests\integration\Gamepacks\GamepackDryRunIntegrationTests.cs` and `C:\src\GameBot\tests\unit\Gamepacks\GamepackTestKit.cs` for the test helpers to reuse.

## Phase 2: Foundational (blocks all user stories)

**Tests first**

- [X] T003 [P] Add unit tests for `TemplateSubstitutor.IsWholePlaceholder` in `C:\src\GameBot\tests\unit\Sequences\TemplateSubstitutorTests.cs`. Cases: `{{name}}` gives true and key `name`; ` {{name}} ` (outer white space) gives true; `{{a.b}}` gives true; `pre-{{name}}` gives false; `{{name}}-x` gives false; `{{a}}{{b}}` gives false; `{{ name }}` gives false; null and empty give false. They must fail to compile or fail before T005.

**Implementation**

- [X] T004 Check that the existing field-template tests pass before the change (guard): `dotnet test C:\src\GameBot\tests\unit\GameBot.UnitTests.csproj --filter "FullyQualifiedName~ParameterValidation"` and `dotnet test C:\src\GameBot\tests\contract\GameBot.ContractTests.csproj --filter "FullyQualifiedName~FieldTemplate"`. Record the result. Do not edit these tests.
- [X] T005 Add the public method `IsWholePlaceholder(string? value, out string key)` with XML docs in `C:\src\GameBot\src\GameBot.Domain\Utils\TemplateSubstitutor.cs`. Rule: after trim the text is exactly `{{key}}` and holds one key (engine pattern `\{\{(\w+(?:\.\w+)*)\}\}`, ordinal).
- [X] T006 Change the private `IsWholePlaceholder` in `C:\src\GameBot\src\GameBot.Domain\Services\ParameterValidationService.cs` to call `TemplateSubstitutor.IsWholePlaceholder`. The `invalid_field_template_value` result stays the same. Run the T004 commands again and the T003 tests. All pass.

**Checkpoint**: The shared helper exists and the field-template result is unchanged.

## Phase 3: User Story 1 - Dry run accepts a parameterized image id (P1)

**Goal**: A declared whole placeholder image id gives no `reference` problem in the dry run. The install works.

**Independent test**: `POST /api/gamepacks/install?dryRun=true&takeOver=true` with the minimal JSON of issue #326 (sequence `PNS.NovaInteract`, parameter `novaOptionImage`, condition `{"type":"imageVisible","imageId":"{{novaOptionImage}}","minSimilarity":0.85,"negate":false}`) gives `valid: true` and an empty problem list.

**Tests first (they must fail before T012)**

- [X] T007 [P] [US1] Add the integration dry run test in `C:\src\GameBot\tests\integration\Gamepacks\GamepackDryRunPlaceholderImageTests.cs`. Use the issue #326 JSON as fixture. Expect `valid: true` and 0 problems (SC-001).
- [X] T008 [P] [US1] Add a separate integration install test in the same file or `C:\src\GameBot\tests\integration\Gamepacks\GamepackInstallPlaceholderImageTests.cs`. Use the same issue #326 JSON fixture (share one fixture helper). Install without dry run. Expect success as for any valid gamepack (acceptance scenario 3).
- [X] T009 [P] [US1] Add validator unit tests in `C:\src\GameBot\tests\unit\Gamepacks\GamepackValidatorPlaceholderImageTests.cs` (use `GamepackTestKit`) for a declared whole placeholder with no problem in:
  - a step condition;
  - a nested step (Loop body);
  - a Loop `breakCondition`;
  - an `if` branch condition;
  - a step in an `if` `elseBody`.
- [X] T010 [P] [US1] Add a validator unit test (same file as T009) for outer white space around a declared whole placeholder (` {{name}} `): no problem.

**Implementation**

- [X] T011 [US1] In `C:\src\GameBot\src\GameBot.Domain\Gamepacks\GamepackValidator.cs`, `CheckReferences`: in the sequence loop, build the set of declared parameter names (ordinal, null-safe, so null or empty parameters give an empty set). Remove each image id from `GamepackReferences.ImageIds(sequence)` that is a declared whole placeholder (`TemplateSubstitutor.IsWholePlaceholder`, key in the set) before `ReportMissing`. The set is built for each sequence (FR-006). Do not change `GamepackReferences.ImageIds`, the command check, the problem text, or the seed template checks. This is the one merged validator task.
- [X] T012 [US1] Run T007 to T010 tests until they pass: `dotnet test C:\src\GameBot\tests\unit\GameBot.UnitTests.csproj --filter "FullyQualifiedName~PlaceholderImage"` and `dotnet test C:\src\GameBot\tests\integration\GameBot.IntegrationTests.csproj --filter "FullyQualifiedName~PlaceholderImage"`.

**Checkpoint**: US1 works alone. MVP is Phase 1 to Phase 3.

## Phase 4: User Story 2 - Other image ids are still checked (P1)

**Goal**: The skip does not hide real errors. Each case gives the same result as before.

**Independent test**: Each case below gives a `reference` problem (or no problem for the plain id in the file).

**Tests (add the cases that guard the unchanged behavior; they should pass with T011, and the case list is the lock against a too wide skip)**

- [X] T013 [P] [US2] In `C:\src\GameBot\tests\unit\Gamepacks\GamepackValidatorPlaceholderImageTests.cs`, add `reference` problem tests for:
  - `{{name}}` with no parameter `name` declared;
  - `pre-{{name}}` and `{{name}}-x` with `name` declared;
  - a plain image id not in the file and not in the installation;
  - `{{b}}` with only `a` declared;
  - `{{ name }}` (spaces inside the braces) with `name` declared;
  - `{{Name}}` with `name` declared (ordinal compare);
  - `{{iteration}}` (built-in name, no declared parameter);
  - a parameter that only another sequence declares (FR-006);
  - null parameters and empty parameters: the placeholder image id is checked as before.
- [X] T014 [P] [US2] In the same file, add tests for: a plain image id that is in the file gives no problem; a command image id with a placeholder (`{{name}}`) still gives a `reference` problem (not skipped, commands unchanged).
- [X] T015 [P] [US2] Add an integration dry run test in `C:\src\GameBot\tests\integration\Gamepacks\GamepackDryRunPlaceholderImageTests.cs`: change the parameter name in the issue #326 fixture, expect `valid: false` with a `reference` problem for `{{novaOptionImage}}` (SC-003).
- [X] T016 [US2] Run: `dotnet test C:\src\GameBot\tests\unit\GameBot.UnitTests.csproj --filter "FullyQualifiedName~PlaceholderImage"` and `dotnet test C:\src\GameBot\tests\integration\GameBot.IntegrationTests.csproj --filter "FullyQualifiedName~PlaceholderImage"`. All pass. If a test fails, fix `GamepackValidator.cs` (T011), not the test.

**Checkpoint**: US1 and US2 pass.

## Phase 5: Polish and cross-cutting

- [X] T017 Check the OpenAPI text in `C:\src\GameBot\src\GameBot.Service\Swagger\GamepackSchemaFilter.cs`. Search for `dry run` and `reference`. If a description lists the image reference rules, add one STE sentence: an image id that is one whole placeholder of a parameter declared in the same sequence is not checked. If no description lists the rules, make no change and record "no change needed" in this task result. If you edit the text, add or update a test in `C:\src\GameBot\tests\contract` that asserts it.
- [X] T018 Run the full suites with the exact commands:
  - `dotnet build C:\src\GameBot\GameBot.sln`
  - `dotnet test C:\src\GameBot\tests\unit\GameBot.UnitTests.csproj`
  - `dotnet test C:\src\GameBot\tests\contract\GameBot.ContractTests.csproj`
  - `dotnet test C:\src\GameBot\tests\integration\GameBot.IntegrationTests.csproj`

  All must pass (SC-004), including the existing tests for the gamepack dry run, `POST /api/sequences`, and queue template save. Note: rerun a known flaky CI test once before it counts as a failure.
- [X] T019 [P] Do the quickstart steps in `C:\src\GameBot\specs\152-gamepack-dry-run-placeholder-image\quickstart.md` that apply. Confirm the problem message text is unchanged.
- [X] T020 Final task: bump the version in `C:\src\GameBot\installer\versioning\version.override.json`. Run `git fetch origin master` and compare the file with `origin/master`. If `minor` or `major` changed, set `patch` to `"0"`. If not, set `patch` to the `patch` on `origin/master` plus 1. Do nothing if the file already has this value. Set `updatedBy` and `updatedAtUtc`. Use the Edit tool. Run the build and tests green before the commit.

## Dependencies and order

- Phase 1 then Phase 2 then Phase 3 then Phase 4 then Phase 5.
- Phase 2: T003 before T005; T005 before T006; T004 before T006.
- Phase 3: T007 to T010 (tests) before T011; T011 before T012.
- Phase 4: T013 to T015 are independent of each other; T016 after them and after T011.
- T018 after T017; T020 is last.

## Parallel examples

- T002 with T001.
- T007, T008, T009, T010 (different files or independent test classes; T009 and T010 share a file, so write them in one edit).
- T013, T014, T015.

## Implementation strategy

- MVP: Phase 1 to Phase 3 (the issue #326 case works).
- Then Phase 4 locks the unchanged cases, and Phase 5 does the OpenAPI check, the full suites, and the version bump.
