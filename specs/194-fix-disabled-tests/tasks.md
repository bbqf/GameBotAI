---
description: "Task list for feature 194: turn on and repair the disabled tests"
---

# Tasks: Turn On and Repair the Disabled Tests

**Input**: Design documents from `specs/194-fix-disabled-tests/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/find-disabled-tests.md, quickstart.md

**Tests**: This feature is about tests. The guard script gets unit tests (constitution Principle II). D-5 gets a test that fails before the repair (constitution: a bug fix needs a failing test first).

**Organization**: The tasks are in groups for each user story.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: The task can run in parallel (different files, no dependency).
- **[Story]**: The user story of the task (US1, US2, US3).
- Use absolute Windows paths in PowerShell commands (`C:\src\GameBot\...`). The paths below are relative to the repository root.

---

## Phase 1: Setup

**Purpose**: Make sure that the start state is the state that research.md records.

- [X] T001 Build the solution with `dotnet build "C:\src\GameBot\GameBot.sln"` and make sure that the build has 0 errors.
- [X] T002 [P] Add `src/web-ui/test-results/` and `src/web-ui/playwright-report/` to `.gitignore` (research R-4).

---

## Phase 2: Foundational

**Purpose**: None. Each user story uses different files. Start the user stories after Phase 1.

---

## Phase 3: User Story 1 - All back-end tests run and pass (Priority: P1) 🎯 MVP

**Goal**: No back-end test has a skip marker or is in comments. D-1, D-2, D-3, D-5, D-6, and D-7 pass in a stable way.

**Independent Test**: Run the unit, integration, and contract suites. The result is 0 failed and 0 skipped. Each turned-on or repaired test passes 3 of 3 times.

### D-1, D-2, D-3: turn on the disabled tests

- [X] T003 [P] [US1] Remove the `Skip = "..."` argument from the `[Theory]` attribute of `StepTimesOutWhenImageNotOnScreen` in `tests/integration/WaitForImageCaptureLoopTests.cs` (research R-1).
- [X] T004 [P] [US1] Remove the `Skip = "..."` argument from the `[Fact]` attribute of `ARunBlockedByTheSessionLimitRecordsAnActionableReason` in `tests/integration/Queues/ConcurrentQueueRunTests.cs`. Delete the NOTE comment above the test, because it says that the session-limit setting has no effect (research R-2).
- [X] T005 [P] [US1] In `tests/unit/TextMatchEvaluatorTests.cs`, remove the comment marks from `CropsRegionToExpectedPixelSize`. Use the file style (opening brace on the same line, 2-space indent). Delete the 2 log assertions (`"Cropped image size"`). Add `ocr.LastWidth.Should().BeGreaterOrEqualTo(100);`. Keep the aspect-ratio assertion. Delete the unused `TestLoggerProvider` and `LoggerFactory` lines of this test if the test no longer uses them, and give the evaluator `NullLogger<TextMatchEvaluator>.Instance` (research R-3).
- [X] T006 [US1] Run `dotnet test "C:\src\GameBot\tests\unit\GameBot.UnitTests.csproj" --filter "FullyQualifiedName~TextMatchEvaluatorImagePipelineTests"` 3 times. Run `dotnet test "C:\src\GameBot\tests\integration\GameBot.IntegrationTests.csproj" --filter "FullyQualifiedName~WaitForImageCaptureLoopTests|FullyQualifiedName~ConcurrentQueueRunTests"` 3 times. Each run must have 0 failed and 0 skipped (FR-003). Depends on T003 to T005.

### D-5: order-dependent gamepack type check

- [X] T007 [US1] Reproduce D-5. Run `dotnet test "C:\src\GameBot\tests\contract\GameBot.ContractTests.csproj" --filter "FullyQualifiedName~GamepackBadTypeContractTests"`. Expected before the repair: the case "number type, dryRun False" fails with 422. Run `dotnet test "C:\src\GameBot\tests\unit\GameBot.UnitTests.csproj" --filter "FullyQualifiedName~GamepackFileSerializerInvalidTypeTests"`. Expected before the repair: the case "unknown" fails (`InvalidType` is false). Record the results in research.md, item D-5.
- [X] T008 [US1] Write a unit test in `tests/unit/Gamepacks/GamepackTypeCheckOrderTests.cs`. In one test method, parse the 4 bad-type packs of `GamepackMemberOrderFixtures` (`MissingType`, `UnknownType`, `NumberType`, `DuplicateType`) with `GamepackFileSerializer.Parse`, first in one order and then in the reverse order. Before each parse, also parse one valid pack. Assert that each parse gives `InvalidType` true and the same reason text in both orders. Make sure that the test fails before the repair. If it does not fail, change the order and the valid packs until it reproduces the T007 failure. Depends on T007.
- [X] T009 [US1] Find the shared state that changes the result of `GamepackTypeDiscriminatorCheck.Find` in `src/GameBot.Domain/Gamepacks/GamepackTypeDiscriminatorCheck.cs` or of its caller in `GamepackFileSerializer` (search `src/GameBot.Domain/Gamepacks/` for the caller). Possible cause: a shared `JsonSerializerOptions` object, or a type-info cache, that an earlier read changes, so that `PolymorphismOptions` or the derived types change. Write the cause in research.md, item D-5. Depends on T008.
- [X] T010 [US1] Repair the product code at the cause from T009, so that each order gives the same result. An invalid type member must give `InvalidType` true, and the install endpoint must give 400 with the JSON path (feature 154). The repair must make both T007 commands pass, also the contract case on the install path. Do not change an assertion of a current test. Depends on T009.
- [X] T011 [US1] Run the T007 commands and the T008 test 3 times. Each run must pass. Run the full unit and contract projects once. Each must have 0 failed. Depends on T010.

### D-6: `DeviceRecoveryFlowTests` under load

- [X] T012 [US1] In `tests/integration/DeviceRecoveryTestSupport.cs`, change `DeviceRecoveryHost.WaitForAsync` so that a time-out throws an exception (for example `TimeoutException`) with a message that names the condition and the time-out. Add an optional `string what` parameter for the message. Give a name at each call in `tests/integration/DeviceRecoveryFlowTests.cs` and in the other files that call `DeviceRecoveryHost.WaitForAsync` (search `tests/integration` for the callers).
- [X] T013 [US1] Run the full integration project with `dotnet test "C:\src\GameBot\tests\integration\GameBot.IntegrationTests.csproj"` 3 times. If `DeviceRecoveryFlowTests` fails, read the message from T012 and the log. Measure the time from the queue start to the "live again" message in a failed run and in a passed run. Write the cause in research.md, item D-6. Depends on T012.
  - If the "live again" message is lost (the product does not send it): first write a test that reproduces the loss every time and fails, for example with a delay in `SimulatedEmulatorControl` in `tests/integration/DeviceRecoveryTestSupport.cs`. Then repair the product code that sends the message (search `src/GameBot.Service/Services` for `QueueAlertKind.LiveAgain`). The new test must pass after the repair (constitution II).
  - If the recovery is correct but slow under load: set the time-out of that wait above the measured time, and write the measured time in a code comment (spec, Edge Cases).

### D-7: older CI failures

- [X] T014 [US1] Do this task when no other test run is active, because extra load changes the result of a timing test. Run `StepThroughRunContractTests` 10 times with the filter `FullyQualifiedName~GameBot.ContractTests.StepThrough.StepThroughRunContractTests`. Run `AbandonedSequenceLogIntegrationTests` 10 times with the filter `FullyQualifiedName~GameBot.IntegrationTests.Sequences.AbandonedSequenceLogIntegrationTests`. Then run each one time with its CI shard filter: get the filter with `& "C:\src\GameBot\scripts\ci\get-test-shard-filter.ps1" -Project <project> -Shard <shard>` (the StepThrough contract test is in `contract-6`, the abandoned-sequence test is in `integration-2`). If a run fails, find and repair the cause. Write the result in research.md, item D-7.

### Back-end check

- [X] T015 [US1] Run `dotnet test "C:\src\GameBot\GameBot.sln"`. Each project must have 0 failed and 0 skipped (SC-002). Depends on T006, T011, T013, T014.

**Checkpoint**: All back-end tests run and pass.

---

## Phase 4: User Story 2 - The front-end browser tests run and pass (Priority: P1)

**Goal**: The CI runs the 69 Playwright tests on each push that changes the web UI. A failure fails `ci-gate`.

**Independent Test**: Run `npm --prefix "C:\src\GameBot\src\web-ui" run e2e`. 69 pass. Push the branch. The `web-ui-e2e` job runs and passes, and `ci-gate` requires it.

- [X] T016 [P] [US2] In `src/web-ui/playwright.config.ts`, set `retries: 0`. Set `use.trace` to `'retain-on-failure'`. Set `reporter` to `process.env.CI ? [['list'], ['html', { open: 'never' }]] : 'list'`. Set `reuseExistingServer: !process.env.CI` in both `webServer` entries. Keep `workers: 2` (research R-5).
- [X] T017 [US2] Run `npm --prefix "C:\src\GameBot\src\web-ui" run e2e`. All 69 tests must pass. Make sure that `git -C "C:\src\GameBot" status --short` shows no Playwright output. Depends on T002, T016.
- [X] T018 [P] [US2] Add `[PSCustomObject]@{ Job = "web-ui-e2e"; Required = ($Webui -eq "true") }` to the expectations in `scripts/ci/evaluate-gate.ps1`. Update the rule text in the header comment.
- [X] T019 [P] [US2] In `tests/unit/Ci/GateEvaluationTests.cs`, add `["web-ui-e2e"] = webuiResult` in `AllJobs`. Add a test `AFailedBrowserTestJobFailsTheGate`: all jobs `success`, `web-ui-e2e` is `failure`, `Evaluate("true", "true", ...)` gives 1. Add a test `ABrowserTestJobSkippedByThePathRulePasses`: `Evaluate("true", "false", ...)` with `web-ui-e2e` `skipped` and the web UI job `skipped` gives 0.
- [X] T020 [US2] In `.github/workflows/dotnet.yml`, add the job `web-ui-e2e` after `web-ui-tests`: `needs: changes`, `if: needs.changes.outputs.webui == 'true'`, `runs-on: windows-latest`. Steps: `actions/checkout@v5`; `actions/setup-node@v5` with `node-version: '24.x'`, `cache: 'npm'`, `cache-dependency-path: 'src/web-ui/package-lock.json'`; `npm ci` in `src/web-ui`; `actions/cache@v4` for `~\AppData\Local\ms-playwright` with the key `playwright-${{ runner.os }}-${{ hashFiles('src/web-ui/package-lock.json') }}`; `npx playwright install chromium` in `src/web-ui`; `npm run e2e` in `src/web-ui` with `env: CI: 'true'`; on `failure()`, `actions/upload-artifact` of `src/web-ui/playwright-report` and `src/web-ui/test-results` with the name `playwright-report` and `retention-days: 7`. Add a comment that says why the job runs on Windows (pixel tests and fonts). Add `web-ui-e2e` to the `needs` list of `ci-gate`. Depends on T018.
- [X] T021 [US2] Run `dotnet test "C:\src\GameBot\tests\unit\GameBot.UnitTests.csproj" --filter "FullyQualifiedName~GameBot.UnitTests.Ci"`. All tests must pass. Depends on T018, T019.

**Checkpoint**: The browser tests pass locally, and the workflow runs them.

---

## Phase 5: User Story 3 - Disabled tests cannot come back without a signal (Priority: P2)

**Goal**: The guard fails the build for an unconditional skip marker, a focus marker, or a test in comments.

**Independent Test**: Add `[Fact(Skip = "x")]` to a test. Run the guard. It reports the line and gives exit code 1.

- [X] T022 [US3] Write `tests/unit/Ci/DisabledTestGuardTests.cs`. Use `CiScriptRunner.RunWithExitCode("find-disabled-tests.ps1", "-Root", <temp folder>)` (see `tests/unit/Ci/CiScriptRunner.cs`). Each test makes a temporary folder with `tests/` and `src/web-ui/tests/` files and deletes it after the test. Cover each row of the "Findings" table in `specs/194-fix-disabled-tests/contracts/find-disabled-tests.md`: a C# `Skip =`, `// [Fact]`, `// [Theory(...)]`, `it.skip(`, `test.skip('name', ...)`, `describe.skip(`, `test.describe.skip(`, `xit(`, `test.fixme('name', ...)`, `test.only(`, `fit(`, `// it(`. Cover the permitted cases: `test.skip(browserName !== 'chromium', 'reason')`, a string that contains `.skip(`, a `bin/`, `obj/`, or `node_modules/` file. Assert the output format `<path>:<line>: <kind>: <source>`, the sort order, the summary line, and the exit codes 0, 1, and 2. Put each sample line in a string that does not start a source line (for example `var line = "[Fact(Skip = \"x\")]";`), so that the guard does not report this file (contract, "Findings"). The tests must fail before T023.
- [X] T023 [US3] Write `scripts/ci/find-disabled-tests.ps1` to the contract in `specs/194-fix-disabled-tests/contracts/find-disabled-tests.md`. Add a header comment with the purpose, the parameter, the output, and the exit codes. Use `Set-StrictMode -Version Latest` and `$ErrorActionPreference = "Stop"`, as the other scripts in `scripts/ci/` do. The script must run under `pwsh` on Linux and under Windows PowerShell 5.1. Read each file as UTF-8. Depends on T022.
- [X] T024 [US3] Add `"GameBot.UnitTests.Ci.DisabledTestGuardTests"` to the `include` list of `unit-2` in `scripts/ci/test-shards.json`. Run the T022 tests and `tests/unit/Ci/TestShardTests.cs`. All must pass. Depends on T023.
- [X] T025 [US3] Run `& "C:\src\GameBot\scripts\ci\find-disabled-tests.ps1"` on the repository. The result must be `find-disabled-tests: no disabled test found.` and exit code 0. If it reports an item, turn the test on or delete it, as in US1. Depends on T003 to T005, T023.
- [X] T026 [US3] In `.github/workflows/dotnet.yml`, add the step `Find disabled tests` to the `changes` job, after the step `Security and secret scans`, with `shell: pwsh` and `run: ./scripts/ci/find-disabled-tests.ps1`. Depends on T023, T020 (same file).

**Checkpoint**: The guard runs on each push and the repository has no disabled test.

---

## Phase 6: Polish and Cross-Cutting Concerns

- [X] T027 [P] Update `docs/ci-time.md`: add the `web-ui-e2e` job to the job table, add the guard step to the `changes` row, add `web-ui-e2e` to the "Web UI job" column of the path groups (rename the column to "Web UI jobs"), and add `scripts/ci/find-disabled-tests.ps1` to "Related". Use STE.
- [X] T028 [P] Update the job list of `.NET CI` in `INSTALL.md` (line with `ci-gate` is the single pass signal) with `web-ui-e2e`.
- [X] T029 [P] Add the row for feature 194 to `specs/STATUS.md` with the status `Implemented`. Set `**Status**: Implemented` in `specs/194-fix-disabled-tests/spec.md`. Do this in the implementation commit, because the pipeline has no commit after the CI.
- [X] T030 Run `scripts/ci/measure-shards.ps1 -Project integration` after a Release build (see `docs/ci-time.md`, "Shard balance rule"). Write the result in the PR text. If a shard is above the limit, split it in `scripts/ci/test-shards.json`.
- [X] T031 Run `dotnet build "C:\src\GameBot\GameBot.sln" -warnaserror` and `npm --prefix "C:\src\GameBot\src\web-ui" run lint`. Both must have 0 errors.
- [X] T032 Run all steps of `specs/194-fix-disabled-tests/quickstart.md` (1 to 4). If no code changed after T015, the T015 result is the result of step 2.
- [ ] T033 After the first CI run of the branch, read the time of `web-ui-e2e` and of the full run (`& "C:\src\GameBot\scripts\ci\measure-ci.ps1" -Branch 194-fix-disabled-tests -Limit 1`). If a web-UI-only push can take more than 3 min, set `workers: process.env.CI ? 4 : 2` in `src/web-ui/playwright.config.ts` (research R-5). Write the measured times in `docs/ci-time.md`.
- [ ] T034 Write the PR text with one row for each item D-1 to D-7: location, how it was disabled or failed, root cause, repair (data-model.md, "Disabled test record"; FR-010).
- [X] T035 Bump the version before the push (constitution, "Version bump"). Run `git -C "C:\src\GameBot" fetch origin master`. Compare `installer/versioning/version.override.json` with `origin/master`. If `minor` or `major` changed, set `patch` to `"0"`. If not, set `patch` to the `patch` on `origin/master` plus 1. Set `updatedBy` and `updatedAtUtc`. Add an entry for feature 194 to `CHANGELOG.md` that names the new version.
- [ ] T036 After the first green CI run of the branch, run the `.NET CI` workflow 2 more times on the same commit (`gh workflow run ".NET CI" --ref 194-fix-disabled-tests`, then `gh run watch <id> --exit-status`). All 3 runs must pass (FR-003). Write the 3 run IDs in the PR text. If a run fails, find and repair the cause as in US1. Depends on T033.

---

## Dependencies and Execution Order

### Phase dependencies

- **Phase 1 (Setup)**: No dependency.
- **Phase 2 (Foundational)**: Empty.
- **Phase 3 (US1)**, **Phase 4 (US2)**, **Phase 5 (US3)**: Start after Phase 1. They use different files, except `.github/workflows/dotnet.yml` (T020 before T026).
- **Phase 6 (Polish)**: After all user stories. T035 comes before the push. T033, T034, and T036 need a CI run.

### User story dependencies

- **US1**: No dependency on another story.
- **US2**: No dependency on another story. T017 needs T002.
- **US3**: T025 needs T003 to T005 (US1), because the guard must find 0 items. T026 needs T020 (same file).

### Inside each story

- D-5: reproduce (T007), failing test (T008), cause (T009), repair (T010), check (T011).
- Guard: tests (T022) before the script (T023).

---

## Parallel Example: User Story 1

```text
Task: "T003 Remove the Skip argument in tests/integration/WaitForImageCaptureLoopTests.cs"
Task: "T004 Remove the Skip argument in tests/integration/Queues/ConcurrentQueueRunTests.cs"
Task: "T005 Turn on CropsRegionToExpectedPixelSize in tests/unit/TextMatchEvaluatorTests.cs"
```

## Parallel Example: User Story 2

```text
Task: "T016 CI settings in src/web-ui/playwright.config.ts"
Task: "T018 Add web-ui-e2e in scripts/ci/evaluate-gate.ps1"
Task: "T019 Add the gate tests in tests/unit/Ci/GateEvaluationTests.cs"
```

---

## Implementation Strategy

### MVP first (User Story 1)

1. Do Phase 1.
2. Do Phase 3 (US1). The newest `master` build fails on D-5, so D-5 has the highest value.
3. Stop and check: the back-end suites have 0 failed and 0 skipped.

### Incremental delivery

1. US1: the back-end tests pass.
2. US2: the CI runs the browser tests.
3. US3: the guard stops new disabled tests.
4. Polish: docs, measurements, PR text.

---

## Notes

- [P] tasks use different files and have no dependency on an incomplete task.
- Do not make an assertion weaker to get a pass (FR-004).
- A red local or CI result stops the work until it is repaired (constitution, "Release blocker").
- Use STE in all new text: code comments, docs, commit messages, and the PR text.
