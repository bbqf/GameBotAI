# Tasks: Split the Largest CI Test Shard

**Input**: Design documents from `/specs/172-ci-test-shard-split/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/measure-shards-cli.md, quickstart.md

**Tests**: The constitution requires tests for executable logic. The tasks add unit tests for the new script. `TestShardTests` already proves one shard for each class.

**Format**: `- [ ] T### [P?] [Story?] Description with file path`. `[P]` means the task can run in parallel with other `[P]` tasks (other files, no open dependency).

## Phase 1: Setup

- [X] T001 Run `git fetch origin master`, then confirm the branch `172-ci-test-shard-split` is clean apart from the spec files. Run `dotnet build "C:\src\GameBot\GameBot.sln" -c Release -p:RunAnalyzers=false` and confirm that it succeeds
- [X] T002 Run `dotnet test "C:\src\GameBot\tests\unit\GameBot.UnitTests.csproj" --filter "FullyQualifiedName~GameBot.UnitTests.Ci"` and confirm that the shard tests pass before any change (baseline)

---

## Phase 2: Foundational (blocks all user stories)

**Purpose**: Get the measured class times that the split needs (research R1), and the CI time before the change.

- [X] T003 Run the contract tests once with `--logger "trx;LogFileName=contract.trx"` and a results folder outside the repository (the session scratchpad). Command base: `dotnet test "C:\src\GameBot\tests\contract\GameBot.ContractTests.csproj" -c Release --no-build`
- [X] T004 Sum the `UnitTestResult` durations of `contract.trx` by namespace prefix. Write the table (prefix, seconds, current shard) to `specs/172-ci-test-shard-split/measurements.md`. Include the time of each current contract shard, measured with the filter from `scripts/ci/get-test-shard-filter.ps1`
- [X] T005 Record the CI time before the change. Run `& "C:\src\GameBot\scripts\ci\measure-ci.ps1" -Branch master -Limit 5 -Conclusion success`. Write the median critical path and the longest `test-contract` job time to `specs/172-ci-test-shard-split/measurements.md` (baseline for SC-002)

**Checkpoint**: The measurements show which prefixes of `contract-4` are heavy. The CI baseline is saved.

---

## Phase 3: User Story 1 - Shorter CI runs (Priority: P1) MVP

**Goal**: Replace the shard `contract-4` by two or more smaller parallel shards. No test is lost or run twice.

**Independent Test**: `TestShardTests` passes. The local time of the longest contract shard is at most 1.2 times the contract average.

- [X] T006 [US1] Choose the new shards from `specs/172-ci-test-shard-split/measurements.md`. Use the number of shards that gives a longest contract shard of at most 1.2 times the average. Stop at the number where the longest shard plus the fixed cost (about 25 s for each shard) is lowest. Record the choice and the expected times in `specs/172-ci-test-shard-split/measurements.md`
- [X] T007 [US1] Edit `scripts/ci/test-shards.json`. Move the heavy prefixes of the old `contract-4` into new named shards (`contract-4`, `contract-5`, and `contract-6` if needed). Keep the last shard as `"complement": true` with a small remainder. The contract shards have no duty, so do not add `collectsCoverage` or `performanceGate` to any new shard (FR-004)
- [X] T008 [US1] Run `dotnet test "C:\src\GameBot\tests\unit\GameBot.UnitTests.csproj" --filter "FullyQualifiedName~GameBot.UnitTests.Ci"`. Confirm that every contract class is in exactly one shard and that a new class falls into the last shard
- [X] T009 [US1] Run `& "C:\src\GameBot\scripts\ci\get-test-shard-filter.ps1" -Project contract -List`. Confirm that the new shard names are in the list. Confirm that `.github/workflows/dotnet.yml` needs no change (FR-010) and has no call to `measure-shards.ps1` (FR-009a)
- [X] T010 [US1] Run the full contract project with the old complement filter and with the new shard filters. Confirm that the sum of the test counts is equal before and after (SC-003). Write both counts to `specs/172-ci-test-shard-split/measurements.md`

**Checkpoint**: The shard list is split and valid. The check of the local times follows in Phase 6.

---

## Phase 4: User Story 2 - A rule that keeps shards balanced (Priority: P1)

**Goal**: A written, binding rule: a shard more than 20% above the average of its project MUST be split again.

**Independent Test**: A reader finds the 20% limit, the way to measure, and the action in the guide and in the constitution.

- [X] T011 [P] [US2] Add a section "Shard balance rule" to `docs/ci-time.md`. State the 20% limit over the average of the shards of the same project, the local measurement with `scripts/ci/measure-shards.ps1`, when to measure (a change that adds many tests or changes the shard list, or slow CI), and the action (split the shard in the same change). Update the job table and the shard counts in the guide for the new contract shards. Write in STE. Write the part about the script after T015 exists
- [X] T012 [P] [US2] Add one bullet to "Development Workflow and Review Process" in `.specify/memory/constitution.md`. It states the 20% limit and points to `docs/ci-time.md`. Raise the version to 1.6.0, set "Last Amended" to the current date, and update the Sync Impact Report
- [X] T013 [P] [US2] Create `.github/pull_request_template.md` with a short STE check list. It has one check box: "Ran `scripts/ci/measure-shards.ps1` when the change adds many tests or changes `scripts/ci/test-shards.json`. Result: ..." (FR-009b)

**Checkpoint**: The rule is written in one place that contributors read (SC-005) and is binding.

---

## Phase 5: User Story 3 - A check that shows the shard times (Priority: P2)

**Goal**: One command prints the local time of each shard and names each shard above the limit.

**Independent Test**: `measure-shards.ps1 -Project contract -TimesFile <file>` gives the expected table and exit status without a test run.

- [X] T014 [US3] Create `tests/unit/Ci/ShardBalanceTests.cs`. Use `CiScriptRunner.RunWithExitCode` (it exists). Cases: all shards within the limit gives status 0 and no `Split` line; one shard above the limit gives status 1 and a `Split <name>` line; a project with one shard gives status 0; an unknown project and a shard with no time give status 2. Use temporary `-TimesFile` files. Confirm that the tests fail before T015
- [X] T015 [US3] Create `scripts/ci/measure-shards.ps1` as in `specs/172-ci-test-shard-split/contracts/measure-shards-cli.md`. Parameters `-Project`, `-TimesFile`, `-Limit` (default 0.2), `-Manifest`. Read the shard names with the logic of `get-test-shard-filter.ps1`. Without `-TimesFile`, run each shard with `dotnet test --no-build` and the shard filter, and measure the wall time. Repeat a shard above the limit once and use the lower time. Print the table, the average, the limit, and one `Split` line for each shard above the limit. Return the exit status 0, 1, or 2. Add a header comment in the style of the other scripts
- [X] T016 [US3] Run `dotnet test "C:\src\GameBot\tests\unit\GameBot.UnitTests.csproj" --filter "FullyQualifiedName~GameBot.UnitTests.Ci"`. Confirm that `ShardBalanceTests` and `TestShardTests` pass

**Checkpoint**: The check works and the unit tests pass.

---

## Phase 6: Polish and cross-cutting

- [X] T017 Run `& "C:\src\GameBot\scripts\ci\measure-shards.ps1" -Project contract` on the new shard list. Confirm exit status 0 (the longest shard is at most 1.2 times the average, SC-001). If a shard is above the limit, go back to T006 and change the split. Write the table to `specs/172-ci-test-shard-split/measurements.md`
- [X] T018 Run `& "C:\src\GameBot\scripts\ci\measure-shards.ps1" -Project unit` and `-Project integration`. Write any shard above the limit as a finding in `specs/172-ci-test-shard-split/measurements.md`. Do not change those shards (FR-003a)
- [X] T019 [P] Set the `**Status**:` line in `specs/172-ci-test-shard-split/spec.md` to `Implemented`. Add feature 172 to `specs/STATUS.md`
- [X] T020 [P] Add a changelog entry (STE) for the shard split and the rule. Find the file with `git ls-files "*CHANGELOG*"` and ignore `node_modules`. If the repository has no changelog file, write the entry in the pull request description and skip this file
- [X] T021 Bump the version before the push. Run `git fetch origin master` and compare `installer/versioning/version.override.json` with `origin/master`. If `minor` or `major` changed, set `patch` to `"0"`. If not, set `patch` to the `patch` on `origin/master` plus 1. Set `updatedBy` and `updatedAtUtc`. Mention the bump in the changelog text
- [X] T022 Run the full unit test project and the build with analyzers. Command: `dotnet build "C:\src\GameBot\GameBot.sln" -c Release -warnaserror`, then `dotnet test "C:\src\GameBot\tests\unit\GameBot.UnitTests.csproj" -c Release --no-build`. Confirm that both are green
- [X] T023 Read all new and changed text against Principle VI (STE): plan files, `docs/ci-time.md`, the constitution bullet, the PR template, script comments, and the changelog. Fix each violation
- [X] T024 After the push, wait for the `.NET CI` run. Read the time of the longest `test-contract` job with `& "C:\src\GameBot\scripts\ci\measure-ci.ps1" -Branch 172-ci-test-shard-split -Limit 5`. Compare it with the baseline from T005. Confirm that the CI time is lower than before (SC-002, FR-011). Write the numbers to `specs/172-ci-test-shard-split/measurements.md`

---

## Dependencies and Order

- Phase 1 before Phase 2. Phase 2 before Phase 3.
- Phase 3 (US1) is the MVP. It has no dependency on Phase 4 or Phase 5.
- Phase 4 (US2): T011, T012, and T013 run in parallel. The script part of T011 comes after T015.
- Phase 5 (US3): T014 before T015 (tests first). T016 after T015.
- Phase 6 needs Phase 3 to Phase 5. T017 can send you back to T006.
- T021 comes after all file changes and before the push. T024 comes after the push.

## Parallel Example

```text
After Phase 3:
  T011 docs/ci-time.md            (US2)
  T012 constitution.md            (US2)
  T013 pull_request_template.md   (US2)
  T014 ShardBalanceTests.cs       (US3)
```

## Implementation Strategy

1. **MVP**: Phases 1 to 3. The split alone shortens CI.
2. **Increment 2**: Phase 4 (rule). Increment 3: Phase 5 (check script).
3. **Close**: Phase 6. Verify with the check script and with the CI run.
