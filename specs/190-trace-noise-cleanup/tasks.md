---

description: "Task list for feature 190: trace noise cleanup"
---

# Tasks: Trace Noise Cleanup

**Input**: Design documents from `/specs/190-trace-noise-cleanup/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/trace-messages.md, quickstart.md

**Tests**: The spec requests tests (FR-008). Each story phase starts with its test task. Write the test first and make sure that it fails before the change.

**Organization**: Tasks are grouped by user story. Each story is a separate increment that you can test alone.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: The task can run in parallel with other tasks (different files, no dependency on an open task).
- **[Story]**: The user story of the task (US1, US2, US3).

## Path Conventions

- Service and domain code: `src/GameBot.Service/`, `src/GameBot.Domain/`
- Unit tests: `tests/unit/` (project `tests/unit/GameBot.UnitTests.csproj`)

---

## Phase 1: Setup

**Purpose**: Make sure that the start point is green.

- [X] T001 Build the solution and run the unit tests that this feature touches: `dotnet test "C:\src\GameBot\tests\unit\GameBot.UnitTests.csproj" --filter "FullyQualifiedName~ImageMatchEvaluator|FullyQualifiedName~CommandExecutorPrimitiveTap"`. All tests must pass before you continue. Record a failure as a blocker (constitution, release blocker rule).

---

## Phase 2: Foundational

No shared prerequisite. Each story changes its own code and its own tests. Continue to Phase 3.

---

## Phase 3: User Story 1 - No image-match duration messages in the traces (Priority: P1) 🎯 MVP

**Goal**: An image-match evaluation writes no trace line about its duration, at any level (FR-001). The evaluation result does not change (FR-002).

**Independent Test**: The new test in T002 passes. The current `ImageMatchEvaluator*` tests pass with no change.

### Tests for User Story 1

- [X] T002 [US1] Add the test `SlowEvaluationWritesNoDurationLine` to `tests/unit/Triggers/ImageMatchEvaluatorAlternatesTests.cs`. Add a private `SlowScreenSource` class that implements `IScreenSource`, calls `Thread.Sleep(60)` in `GetLatestScreenshot()`, and then returns a new copy of `NightScreen()`. Create the evaluator with `Store()`, the `SlowScreenSource`, `new TemplateMatcher()`, a `CapturingLogger<ImageMatchEvaluator>`, and `Alternates("anchor-night")`. Call `Evaluate(AnchorTrigger(), DateTimeOffset.UtcNow)`. Assert that `logger.Inner.Entries` has no entry whose message contains "Slow image match" and no entry at `LogLevel.Warning`. Assert that the result `Status` is `TriggerStatus.Satisfied`, as in the current alternate test. Run the test and make sure that it fails before T003.

### Implementation for User Story 1

- [X] T003 [US1] In `src/GameBot.Domain/Triggers/Evaluators/ImageMatchEvaluator.cs`, change `Evaluate` so that it calls `ComputeSimilarity(p)` directly. Remove the `Stopwatch` start, the `sw.Stop()` call, and the `if` block that calls `ImageMatchLog.SlowMatch`. Remove the `SlowMatch` definition and its `[LoggerMessage]` attribute from the `ImageMatchLog` class. Keep `AlternateMatched`. Remove `using System.Diagnostics;` if no other code in the file uses it. Do not change the result values (`Status`, `Similarity`, `EvaluatedAt`, `Reason`).
- [X] T004 [US1] Run `dotnet test "C:\src\GameBot\tests\unit\GameBot.UnitTests.csproj" --filter "FullyQualifiedName~ImageMatchEvaluator"`. All tests, T002 included, must pass. The build must have no new warning.

**Checkpoint**: User Story 1 is complete. The "Slow image match" message does not exist.

---

## Phase 4: User Story 2 - Normal "target not found" tap results do not show as warnings (Priority: P2)

**Goal**: Event 6012 is at Debug level (FR-003). Its text and data do not change (FR-004). The step outcome does not change (FR-005).

**Independent Test**: The new test in T005 passes. The current "never match" test in `CommandExecutorPrimitiveTapTests.cs` continues to show `skipped_detection_failed` and `detection_failed_after_3_retries`. The execution log entry of a step comes from this step outcome, and `ExecutionStepOutcomeIntegrationTests` continues to pass.

### Tests for User Story 2

- [X] T005 [P] [US2] Add the test `ExhaustedRetriesLogAtDebugNotWarning` to `tests/unit/Commands/CommandExecutorPrimitiveTapTests.cs`. Add a private `EventListLogger` class that implements `ILogger<CommandExecutor>`. Make `IsEnabled` return `true`. Make each `Log` call record `(LogLevel Level, int EventId, string Message)` in a list. For the test setup, use the stubs of the current "never match" test: `TemplateMatcherStub(Array.Empty<TemplateMatch>())`, `ScreenSourceStub`, and `ReferenceImageStoreStub(("img-1", bmp))`. Use `new AppConfig { CaptureIntervalMs = 10, TapRetryCount = 1, TapRetryProgression = 1.0 }`. Give the `EventListLogger` to the `CommandExecutor` constructor. Call `ForceExecuteDetailedAsync("sess-1", command.Id, CancellationToken.None)`. Assert that exactly one entry has `EventId == 6012` and that its level is `LogLevel.Debug`. Assert that its message is "PrimitiveTap step 0: target not detected after 1 retry cycles." (`CreatePrimitiveTapCommand()` uses `Order = 0`). Assert that no entry is at `LogLevel.Warning`. Assert that the step outcome status is `skipped_detection_failed`. Add `using Microsoft.Extensions.Logging;` if the file needs it. Run the test and make sure that it fails before T006.

### Implementation for User Story 2

- [X] T006 [US2] In `src/GameBot.Service/Services/CommandExecutor.cs`, change the `[LoggerMessage]` attribute of `TapRetryExhausted` (event 6012) from `Level = LogLevel.Warning` to `Level = LogLevel.Debug`. Do not change the event id, the message text, the parameters, or the call site in the PrimitiveTap retry loop.
- [X] T007 [US2] Run `dotnet test "C:\src\GameBot\tests\unit\GameBot.UnitTests.csproj" --filter "FullyQualifiedName~CommandExecutorPrimitiveTap"`. All tests, T005 included, must pass. Then run `dotnet test "C:\src\GameBot\tests\integration\GameBot.IntegrationTests.csproj" --filter "FullyQualifiedName~ExecutionStepOutcomeIntegrationTests"`. These tests must pass. They show that the execution log entry of a step that skips does not change (FR-005).

**Checkpoint**: User Story 2 is complete. A tap step that does not find its target writes no Warning.

---

## Phase 5: User Story 3 - Remove trace message definitions that no code uses (Priority: P3)

**Goal**: The definitions of events 6002, 6006, and 6007 are not in the code (FR-006).

**Independent Test**: The search in T009 gives no result, and the solution builds.

### Implementation for User Story 3

- [X] T008 [US3] In `src/GameBot.Service/Services/CommandExecutor.cs`, remove these three methods and their `[LoggerMessage]` attributes from the `Log` class: `TriggerBypassed` (6002), `SessionCacheHit` (6006), and `SessionCacheMiss` (6007). Do not change the other definitions or their event ids. This task edits the same file as T006, so do it after T006.
- [X] T009 [US3] Run `Get-ChildItem "C:\src\GameBot\src" -Recurse -Filter *.cs | Select-String -Pattern "Slow image match|SessionCacheHit|SessionCacheMiss|TriggerBypassed"`. The result must be empty. Run `git -C "C:\src\GameBot" diff --stat -- src`. Only `ImageMatchEvaluator.cs` and `CommandExecutor.cs` must show changes. Run `git -C "C:\src\GameBot" diff -- src/GameBot.Service/Services/CommandExecutor.cs`. The diff must show only the level change of 6012 and the removal of 6002, 6006, and 6007 (FR-007). Build the solution with `dotnet build "C:\src\GameBot\GameBot.sln"`. The build must pass with no new warning.

**Checkpoint**: All user stories are complete.

---

## Phase 6: Polish & Cross-Cutting Concerns

**Purpose**: Documents, version, and the full test run.

- [X] T010 [P] In `specs/037-tap-wait-retry/spec.md`, change the `**Status**:` line from `Implemented` to `Implemented (iterated by 190)`. In `specs/STATUS.md`, change the status of row 037 to the same text. Add a row for 190 ("Trace Noise Cleanup") with the status `Implemented`, in the same format as row 189.
- [X] T011 [P] In `specs/190-trace-noise-cleanup/spec.md`, change the `**Status**:` line from `Draft` to `Implemented`.
- [X] T012 [P] Bump the version in `installer/versioning/version.override.json`. Run `git fetch origin master`. Compare the file with `origin/master`. If `minor` or `major` changed, set `patch` to `"0"`. If not, set `patch` to the `patch` on `origin/master` plus 1. Set `updatedBy` and `updatedAtUtc`. Use the Edit tool, not `Set-Content`.
- [X] T013 In `CHANGELOG.md`, under `## [Unreleased]` and `### Changed`, add an entry "Remove noise from the service traces (190-trace-noise-cleanup)" at the top of the list. Use the format of the 188 entry, in STE. Add these sub-items: **Change** (the "Slow image match" Warning is removed; event 6012 is now Debug; events 6002, 6006, and 6007 are removed because no code wrote them), **Not changed** (all other messages, the execution log, the image-match results, the logging policy file), **Perf note** (each image-match evaluation does one time measurement less and writes no console line), and **Version** (the new patch version from T012).
- [X] T014 Run all unit tests: `dotnet test "C:\src\GameBot\tests\unit\GameBot.UnitTests.csproj"`. All tests must pass. Record a failure as a blocker.
- [X] T015 Do the automated steps 1 and 2 of `specs/190-trace-noise-cleanup/quickstart.md`. Record in the PR description that steps 3 and 4 (live console check) are manual steps for the operator.

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependency. Start immediately.
- **Foundational (Phase 2)**: Empty.
- **User Stories (Phases 3 to 5)**: Start after T001.
- **Polish (Phase 6)**: Start after all stories. T013 needs the version from T012. T014 and T015 come last.

### User Story Dependencies

- **US1 (P1)**: No dependency on other stories. It changes `ImageMatchEvaluator.cs` and its test file only.
- **US2 (P2)**: No dependency on other stories. It changes `CommandExecutor.cs` and `CommandExecutorPrimitiveTapTests.cs`.
- **US3 (P3)**: No functional dependency. It edits `CommandExecutor.cs`, the same file as US2. Thus do T008 after T006.

### Within Each User Story

- Write the test first and make sure that it fails.
- Make the change.
- Run the tests of the story.

### Parallel Opportunities

- US1 (T002 to T004) and US2 (T005 to T007) change different files. You can do them in parallel.
- T002 and T005 are in different test files. You can write them in parallel.
- T010, T011, and T012 change different files. You can do them in parallel.

---

## Parallel Example: User Story 1 and User Story 2

```text
Task: "T002 Add SlowEvaluationWritesNoDurationLine in tests/unit/Triggers/ImageMatchEvaluatorAlternatesTests.cs"
Task: "T005 Add ExhaustedRetriesLogAtDebugNotWarning in tests/unit/Commands/CommandExecutorPrimitiveTapTests.cs"
```

---

## Implementation Strategy

### MVP First (User Story 1 Only)

1. Do T001.
2. Do Phase 3 (T002 to T004).
3. Stop and check: the "Slow image match" message is gone, and all image-match tests pass. This removes most of the noise.

### Incremental Delivery

1. US1: remove the "Slow image match" message (most of the noise).
2. US2: move event 6012 to Debug.
3. US3: remove the dead definitions.
4. Polish: documents, version, full test run.

---

## Notes

- [P] tasks change different files and have no dependency on an open task.
- Use the Edit tool for all changes to UTF-8 files (CLAUDE.md rule).
- All new text (comments, CHANGELOG, spec status) must obey STE (constitution, Principle VI).
