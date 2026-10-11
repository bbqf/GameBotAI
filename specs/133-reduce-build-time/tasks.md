# Tasks: Reduce Build Time

**Input**: Design documents in `C:\src\GameBot\specs\133-reduce-build-time\` (plan.md, spec.md, research.md, data-model.md, quickstart.md)
**Prerequisites**: plan.md (see "Ordering rules"), spec.md

**Tests**: No new automated test is requested. The existing test suite is the check (SC-005). `scripts/measure-build.ps1` has one manual run as its check (plan.md, Constitution Check, Principle II).

**Format**: `- [ ] [TaskID] [P?] [Story?] Description with file path`

- [P] means the task edits a different file and has no open dependency. A timed build is NEVER [P]. Two tasks that edit the same file are NEVER [P].
- All text that you write MUST use ASD-STE100 Simplified Technical English.
- Use absolute paths. Do not run another heavy program during a timed build.
- Record the PC description with each result. Do not commit binlog files.
- Do not relax any analysis rule, analysis level, warning-as-error setting, or pinned analyzer version (FR-008).

## Phase 1: Setup

**Purpose**: Create the record file.

- [X] T001 Create `C:\src\GameBot\specs\133-reduce-build-time\build-baseline.md` with empty sections from `data-model.md`: Build baseline, Project timing, Analyzer cost, Cause, Result. Fill the PC description (CPU model, cores, RAM, disk type) and the output of `dotnet --version`.

---

## Phase 2: Foundational (blocking prerequisites)

**Purpose**: Test baseline. It MUST run BEFORE every clean step, because a test run fills `bin` and `obj`.

- [X] T002 Run the full test suite (`tests/unit`, `tests/contract`, `tests/integration`) on the unchanged code. Record the pass and fail counts, and the names of failing tests, in `C:\src\GameBot\specs\133-reduce-build-time\build-baseline.md` (SC-005 baseline).
- [X] T003 Run `dotnet restore C:\src\GameBot\GameBot.sln`. Do not time it.

**Checkpoint**: Test baseline is recorded. Packages are restored.

---

## Phase 3: User Story 2 - Find and show the cause of the build time (Priority: P1)

**Goal**: A measured, ranked list of causes (FR-001, FR-002).

**Independent Test**: Each named cause in `build-baseline.md` has a measured time and a share of the total.

- [X] T004 [US2] Clean step, then timed clean build 1: delete the `bin` and `obj` folders under `C:\src\GameBot\src` and `C:\src\GameBot\tests`. Run `Measure-Command { dotnet build C:\src\GameBot\GameBot.sln }` (Debug, no extra option). Record the time in `C:\src\GameBot\specs\133-reduce-build-time\build-baseline.md`.
- [X] T005 [US2] Clean step, then timed clean build 2. Same command as T004. Record the time in `C:\src\GameBot\specs\133-reduce-build-time\build-baseline.md`.
- [X] T006 [US2] Clean step, then timed clean build 3. Same command as T004. Record the time. Compute worst and median. Record in `C:\src\GameBot\specs\133-reduce-build-time\build-baseline.md` if SC-003 applies (baseline above 4 minutes).
- [X] T007 [US2] Clean step, then one cause-data run: `dotnet build C:\src\GameBot\GameBot.sln /bl:C:\src\GameBot\baseline.binlog /p:ReportAnalyzer=true -clp:PerformanceSummary`. Open the binlog in MSBuild Structured Log Viewer. Record per-project time (`CoreCompile` and total), per-target time, the analyzer table, and the build count per project in `C:\src\GameBot\specs\133-reduce-build-time\build-baseline.md`.
- [X] T008 [US2] Check `C:\src\GameBot\src\web-ui` and the project files for a web UI step in the default build. Record the result in `C:\src\GameBot\specs\133-reduce-build-time\build-baseline.md` (for example, "no web UI step in the default build"). Check also if a project builds more than once (FR-005) or if a step has no effect (FR-003).
- [X] T009 [US2] Write the ranked cause list in `C:\src\GameBot\specs\133-reduce-build-time\build-baseline.md`. Each item has: id, kind, measured seconds, share of the clean build, planned action, and a target figure for each method or file split (FR-004). State if the list names a project-wide cause (for the FR-013 trigger). Add the share of `Microsoft.CodeAnalysis.Metrics` and of the floating `*` package versions.

**Checkpoint**: The ranked cause list is done. No fix task in Phase 4 starts before this point.

---

## Phase 4: User Story 1 - Fast local build without tests (Priority: P1)

**Goal**: Clean build under 4 minutes (worst of 3), median 3 minutes or less, incremental build under 30 seconds.

**Independent Test**: Three clean runs of `dotnet build C:\src\GameBot\GameBot.sln` end under 4 minutes.

### Fix redundant steps and packages

- [X] T010 [US1] Remove or merge each redundant build step and each duplicate project build named in the ranked list, unless CI, the installer, or the release flow needs it (FR-003, FR-005, FR-010). Files: `C:\src\GameBot\Directory.Build.props` and the `.csproj` files named in the list. Record time before and after for each step in `C:\src\GameBot\specs\133-reduce-build-time\build-baseline.md`.
- [X] T011 [US1] Pin the floating `*` package versions in `C:\src\GameBot\src\GameBot.Emulator\GameBot.Emulator.csproj`. Run `dotnet restore C:\src\GameBot\GameBot.sln` again. Check that no restore fails.
- [X] T012 [US1] Test the removal of `Microsoft.CodeAnalysis.Metrics` in `C:\src\GameBot\Directory.Build.props`: one clean timed build, compared to the baseline. Keep the change only if the gain is measured. Record the decision in `C:\src\GameBot\specs\133-reduce-build-time\build-baseline.md`.

### Test build settings (timed builds, run one after the other)

- [X] T013 [US1] Test `UseSharedCompilation` (true and false): one clean timed build for each value. Link the result to the per-project timing. Keep a value only if the gain is measured. File: `C:\src\GameBot\Directory.Build.props`. Record in `C:\src\GameBot\specs\133-reduce-build-time\build-baseline.md`.
- [X] T014 [US1] Test `-maxcpucount` (and the CPU count value) on the command line: one clean timed build for each value you try. Record the result. If you keep a value, state in the record how developers set it.
- [X] T015 [US1] Test `BuildInParallel` on the project references or targets named in the binlog: one clean timed build. Record the result. Files: `C:\src\GameBot\Directory.Build.props` or the `.csproj` files named in the record.
- [X] T016 [US1] Test `Deterministic` (true and false): one clean timed build for each value. Check that CI and the installer accept the value. File: `C:\src\GameBot\Directory.Build.props`. Record the result.

### Method and file splits (one task per ranked item)

Rule: ONE task per method or file named in the ranked list of T009, after T009 is done. There is no fixed count. Add or remove placeholders to match the list. Each task names its method or file and its measured target figure (FR-004). Do not add a suppression. Existing tests are the check (FR-009). Splits can edit the same file as another split task, so these tasks are NOT [P].

- [X] T017 [US1] (done for the Domain method ExecuteSingleStepAsync, 9 percent gain kept; see build-baseline.md) Split ranked item 1 from the list in `C:\src\GameBot\specs\133-reduce-build-time\build-baseline.md` (method or file name, path, and target figure from T009). Extract lambdas into named private static methods. Build the project. Record the analyzer time before and after.
- [X] T018 [US1] (item 2 = Service methods RunAsync/ExecuteCoreAsync: no gain measured, reverted; see build-baseline.md) Split ranked item 2 (same rule as T017). Remove this task if the list has only one item.
- [X] T019 [US1] (item 3 = endpoint Register* split: no gain measured, reverted; see build-baseline.md) Split ranked item 3 (same rule as T017). Remove this task if the list has fewer than three items.

### Project split (CONDITIONAL, last resort)

- [X] T020 [US1] Check the FR-013 trigger: the ranked list names a project-wide cause AND a clean build after T017 to T019 is still 4 minutes or more (one timed clean build). Record the result in `C:\src\GameBot\specs\133-reduce-build-time\build-baseline.md`. If the trigger is false, mark T021 and T022 as skipped with the reason.
- [X] T021 [US1] SKIPPED (T020 trigger is false: worst clean build 152.8 s). Only if T020 trigger is true: split `GameBot.Service` into several projects along the cause in the ranked list. Files: `C:\src\GameBot\GameBot.sln` and the `.csproj` files under `C:\src\GameBot\src`. Keep all analysis rules in the new projects.
- [X] T022 [US1] SKIPPED (T021 did not run). Only if T021 ran: change `C:\src\GameBot\docs\architecture.md` in the same pull request and set a new "Last reviewed" date. Run the FR-007 incremental checks again (T023 to T025).

### Incremental checks (FR-007, SC-004)

- [X] T023 [US1] Measure an incremental build with no source change: run the command of T004 again with no change. Limit: under 30 seconds. Record in `C:\src\GameBot\specs\133-reduce-build-time\build-baseline.md`. Fix the cause if the limit fails (for example, a target with no inputs and outputs).
- [X] T024 [US1] Per-project check 1: change one source file in `C:\src\GameBot\src\GameBot.Domain`, build, and note which projects compile again. Undo the change. Only projects that depend on `GameBot.Domain` may compile again. Record the result.
- [X] T025 [US1] Per-project check 2: change one source file in `C:\src\GameBot\src\GameBot.Service`, build, and note which projects compile again. Undo the change. Record the result.

### Final measurement (after ALL splits)

- [X] T026 [US1] Final clean timed build 1, with the kept settings and packages: clean step, then the exact command of T004. Record in `C:\src\GameBot\specs\133-reduce-build-time\build-baseline.md`.
- [X] T027 [US1] Final clean timed build 2. Same as T026.
- [X] T028 [US1] Final clean timed build 3. Same as T026. Compute worst and median. Check SC-001 (worst under 4 minutes), SC-002 (median 3 minutes or less), SC-003 (fall of 30% if the baseline is above 4 minutes). If 4 minutes cannot be reached with all rules kept, record the measured floor and its cause (FR-008).
- [X] T029 [US1] Confirm each kept setting and package decision from T011 to T016 against the final measurement (FR-014). Revert each decision that the final measurement does not confirm, in `C:\src\GameBot\Directory.Build.props` and the `.csproj` files. If you revert anything, repeat T026 to T028. Record the confirmed and reverted decisions.

**Checkpoint**: The build limits are met or the floor is recorded. Code and build configuration are final.

---

## Phase 5: User Story 3 - Build time stays low (Priority: P2)

**Goal**: A maintainer can measure the build time and compare it to the limit.

**Independent Test**: Follow `docs/build-time.md` and get a time value to compare with the limit, in less than 10 minutes of effort.

- [X] T030 [US3] Write `C:\src\GameBot\scripts\measure-build.ps1` as ONE task (one file). It does quickstart steps 2 to 5: restore, clean step, the timed command `dotnet build C:\src\GameBot\GameBot.sln`, an incremental run, and prints the result against the limits (4 minutes, 3 minutes, 30 seconds). Use absolute paths. Use STE in comments and messages.
- [X] T031 [US3] Run `C:\src\GameBot\scripts\measure-build.ps1` one time as the manual check. Compare the printed time with T026 to T028. Fix the script if the values differ by a large margin without a reason.
- [X] T032 [US3] Write the final text of `C:\src\GameBot\docs\build-time.md` (maintainer guide, FR-012): the causes found (from the ranked list), the rules to keep the build time low (large methods and files, new build steps, package changes, no relaxed analysis rules), how to measure (the script and the manual steps), the stated limits, and one line: the limits apply to the developer PC, and the PC description is recorded with each result.
- [X] T033 [US3] Guide check (SC-007): after the final guide text of T032, follow `C:\src\GameBot\docs\build-time.md` from the start. Record the effort time (not counting the build run) in `C:\src\GameBot\specs\133-reduce-build-time\build-baseline.md`. Limit: under 10 minutes. Fix the guide and repeat if the limit fails.

**Checkpoint**: Script and guide work.

---

## Phase 6: User Story 4 - CI and other tools keep their quality checks (Priority: P2)

**Goal**: The same rules and the same test result after the change.

**Independent Test**: Full tests give the same result as T002. A Release build gives no new warning.

- [X] T034 [US4] CI pre-check (local, not the proof): run the commands of the workflows in `C:\src\GameBot\.github\workflows` (Release build, no new warnings, no errors). Check that the installer build and release flow still work (FR-010). Record the result. Do not edit a workflow without a stated reason.
- [X] T035 [US4] Test re-run: run the full test suite again after all code and configuration changes (after T029). Compare with the baseline of T002. No test that passed before may fail (SC-005, FR-009). Record the result in `C:\src\GameBot\specs\133-reduce-build-time\build-baseline.md`.

**Checkpoint**: The CI pre-check and the test re-run are done. Both are inputs of the CI proof (T040).

---

## Phase 7: Polish and cross-cutting (BEFORE the CI proof)

**Purpose**: Make all records and docs final. No commit may follow the CI proof.

- [X] T036 Write the Result section in `C:\src\GameBot\specs\133-reduce-build-time\build-baseline.md`: baseline and final values side by side (clean worst, clean median, fall in percent, incremental time, test result) (FR-011).
- [X] T037 Delete `C:\src\GameBot\baseline.binlog` and any other binlog file. Check with `git status` that no binlog is staged.
- [X] T038 Add links to `C:\src\GameBot\docs\build-time.md` from `C:\src\GameBot\README.md` or the docs index that exists in the repo. Check that all links in the new docs work.
- [X] T039 Update the status: set the `Status` line in `C:\src\GameBot\specs\133-reduce-build-time\spec.md` to `Implemented`, and update the entry for feature 133 in `C:\src\GameBot\specs\STATUS.md`. If T021 ran, check that `C:\src\GameBot\docs\architecture.md` has the change and the new "Last reviewed" date (T022).

---

## Phase 8: CI proof (LAST task)

- [ ] T040 Commit all changes. Push the branch `133-reduce-build-time`. Wait for all CI workflow runs on the final commit. They MUST finish with success (SC-006). This task depends on BOTH T034 (CI pre-check) and T035 (test re-run), and on T036 to T039. No commit may follow it. A later commit needs a new CI run.

---

## Dependencies and execution order

- Phase 1 -> Phase 2 -> Phase 3 -> Phase 4. T002 (test baseline) comes before every clean step (T004 on).
- T009 (ranked list) blocks all of Phase 4. T017 to T019 (splits) start only after T009.
- T012 to T016 are timed builds: run them one after the other, never in parallel.
- T020 (trigger) follows the last split task. T021 and T022 run only if the trigger is true.
- T023 to T025 follow the splits. T026 to T028 follow all splits. T029 follows T028.
- T030 can start after T009 (different file, no timed build), but T031 follows T028.
- T032 follows T029 (final causes). T033 follows T032.
- T034 and T035 follow T029. T035 follows any revert of T029.
- T036 to T039 follow T033, T034, and T035.
- T040 is last and depends on T034 AND T035.

## Parallel opportunities

- No task is marked [P]. T030 (script) edits a separate file and can be written while fix tasks are open, but it is not marked [P] because its check (T031) needs the final measurement.
- All other tasks edit `build-baseline.md` or `Directory.Build.props`, or are timed builds.

## Implementation strategy

- MVP: Phases 1 to 4 (measure, fix, final measurement). The 4-minute limit is the main goal.
- Then Phase 5 (guard), Phase 6 (checks), Phase 7 (polish), Phase 8 (CI proof).
- If a split placeholder has no ranked item, delete it. If the ranked list has more items, add one task per item.
