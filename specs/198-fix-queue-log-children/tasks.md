---

description: "Task list for B-044: show the newest runs of a running queue in the execution log"
---

# Tasks: Show the newest runs of a running queue in the execution log (B-044)

**Input**: Design documents from `specs/198-fix-queue-log-children/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/execution-log-store.md, quickstart.md

**Tests**: Required. This is a bug fix (Constitution Principle II): write the race test first and make sure that it fails before the fix.

**Organization**: Tasks are grouped by user story. Each story can be tested alone.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: The user story of the task (US1, US2, US3)

---

## Phase 1: Setup

**Purpose**: Confirm a green start.

- [X] T001 Build the solution with `dotnet build "C:\src\GameBot\GameBot.sln" -c Debug` and run `dotnet test "C:\src\GameBot\tests\unit\GameBot.UnitTests.csproj" --filter "FullyQualifiedName~ExecutionLog"`. Record the result. Stop if the start is not green.

---

## Phase 2: Foundational

**Purpose**: The test hook that makes the race test deterministic (research R-007).

- [X] T002 Add `internal Func<Task>? RemoveStepHookForTests { get; set; }` to `src/GameBot.Domain/Logging/FileExecutionLogRepository.cs`. In the current `DeleteExpiredAsync`, await the hook (when it is not null) after the `foreach` over `_entries` and before `_entries = remaining.ToImmutable()`. Do not change other behaviour in this task. Add an STE doc comment: "Test hook. Runs inside the remove step of DeleteExpiredAsync, after the read of the list and before the set of the new list."

**Checkpoint**: The solution builds. All current tests pass.

---

## Phase 3: User Story 1 - The newest log of a running queue is in the list while the queue runs (Priority: P1) MVP

**Goal**: A write during the retention cleanup is never lost from the in-memory list (FR-001 to FR-005).

**Independent Test**: `ExecutionLogRepositoryCleanupRaceTests` and `ExecutionLogQueueSubtreeSizeTests` pass. Before T005, the race tests fail.

### Tests for User Story 1 (write first, make sure that they fail)

- [X] T003 [P] [US1] Create `tests/unit/ExecutionLogs/ExecutionLogRepositoryCleanupRaceTests.cs` (namespace `GameBot.UnitTests.ExecutionLogs`, temp storage root as in `ExecutionLogRepositoryQueryTests`). Tests:
  1. `AddDuringCleanupRemoveStepIsNotLost`: write 1,000 expired entries (`RetentionExpiresUtc` 1 day in the past) and 1 live queue root `old-root`; call `QueryAsync` one time to load the list; set `RemoveStepHookForTests` to a hook that sets a `TaskCompletionSource` "entered" and awaits a second `TaskCompletionSource` "release"; start `DeleteExpiredAsync(DateTimeOffset.UtcNow)` without await; await "entered"; start `AddAsync` of a live queue root `new-root` (type `queue`, object id `q-1`, no parent) and `UpsertAsync` of `old-root` with `FinalStatus` `success`; wait 200 ms (`Task.Delay`); set "release"; await all three tasks. Assert: `QueryAsync(new ExecutionLogQuery { ObjectId = "q-1", RootsOnly = true })` gives `new-root`; `GetSubtreeAsync("old-root")` gives `old-root` with `FinalStatus` `success`; `QueryAsync` gives none of the 1,000 expired ids; the return value of `DeleteExpiredAsync` is 1,000.
  2. `RootAndChildrenAddedDuringCleanupAreReadable` (SC-001): 1,000 expired entries; during the hook, add a live queue root `run-root` (object id `q-2`, no parent) and 10 children (`Hierarchy` root `run-root`, parent `run-root`, depth 1). Assert: the list with `ObjectId = "q-2"` gives `run-root`, and `GetSubtreeAsync("run-root")` gives 11 entries.
  3. `CleanupWithNoExpiredEntriesKeepsConcurrentAdd`: no expired entries; add one entry during the hook. Assert: the entry is in `QueryAsync`, and the return value is 0.
  4. `ExpiredEntriesAreRemovedFromListAndDisk`: 5 expired and 5 live entries, no hook. Assert: the 5 expired files do not exist, `QueryAsync` gives only the 5 live entries.
  5. `AddDuringFirstLoadAndCleanupIsNotLost` (US1 AS-3, FR-004, the start path): write 200 expired entries and do not read the list before the call. Set the hook as in test 1. Start `DeleteExpiredAsync`, and at the same time start `AddAsync` of a live queue root `start-root` (object id `q-3`). Await "entered", start a second `AddAsync` of `start-root-2` (object id `q-3`), wait 200 ms, release, await all. Assert: the list with `ObjectId = "q-3"` gives both roots.
  6. `ConcurrentWritesDuringCleanupLoop` (SC-001, no hook): repeat 20 times on a new temp root: write 200 expired entries (not 1,000, so that the test stays fast; Principle II), load the list, start `DeleteExpiredAsync` and, at the same time, add one root and 10 children with `Task.WhenAll`. Assert in each run that the root is in the list and its subtree has 11 entries.
  Run the tests now. Tests 1, 2, 3, and 5 MUST fail before T005 (record the failure in the PR text). Tests 4 and 6 can pass before the fix (test 6 depends on timing).
- [X] T004 [P] [US1] Create `tests/unit/ExecutionLogs/ExecutionLogQueueSubtreeSizeTests.cs`: add one queue root and 500 sequence children (parent and root = the queue root, `SequenceIndex` 1 to 500) to a `FileExecutionLogRepository`, build `ExecutionLogService` with it and an `ExecutionLogRetentionPolicyRepository` on the same temp root, call `GetSubtreeAsync(rootId)`, and assert that `Root.Children` has 500 nodes in `SequenceIndex` order (FR-005, SC-003). This test passes before and after the fix.

### Implementation for User Story 1

- [X] T005 [US1] In `src/GameBot.Domain/Logging/FileExecutionLogRepository.cs`, change `DeleteExpiredAsync`: call `EnsureEntriesLoadedAsync` first (as now), then `await _mutex.WaitAsync(ct)`; in `try`, read the current `_entries`, delete the file of each entry with `RetentionExpiresUtc <= nowUtc` (keep the current `try`/`catch` around `File.Delete` and the count of deleted files), build the list of the entries that are not expired, await `RemoveStepHookForTests` when it is not null, and set `_entries` to the new list; release `_mutex` in `finally`. Keep the cancellation check in the loop. Add an STE comment that explains why the lock is held (a write during the cleanup must not be lost).
- [X] T006 [US1] Run T003 and T004 and all tests with `FullyQualifiedName~ExecutionLog` in `tests/unit` and `tests/integration`. All MUST pass, also `ExecutionLogRetentionIntegrationTests` and `ExecutionLogRotationIntegrationTests` with no change of their expected results (SC-005).

**Checkpoint**: The defect of the issue is fixed. A root written during the cleanup is in the list.

---

## Phase 4: User Story 2 - A run of an earlier service process does not stay "running" (Priority: P2)

**Goal**: At service start, each `running` entry of an earlier process gets `failure` and the summary "... run ended when the service stopped." (FR-006, FR-006a, FR-007).

**Independent Test**: `ExecutionLogAbandonedRunTests` and `ExecutionLogStartSweepIntegrationTests` pass.

### Tests for User Story 2

- [X] T007 [P] [US2] Create `tests/unit/ExecutionLogs/ExecutionLogAbandonedRunTests.cs`. Tests for `ExecutionLogAbandonedRun.Close`: a `queue` entry gets `failure` and `Queue 'PNS.Production (Exo)' run ended when the service stopped.`; a `sequence` entry gets `Sequence '<name>' run ended when the service stopped.`; a `command` entry gets `'<name>' run ended when the service stopped.`; an empty display name uses the object id; a name of 300 characters gives a summary of 240 characters; all other fields (id, timestamp, hierarchy, details, step outcomes, retention expiry, rotation links, cancellation fields, origin) are the same; an entry with `success` comes back with no change. Tests for `FileExecutionLogRepository.CloseAbandonedRunningAsync(processStart)`: a `running` queue root and a `running` sequence child with timestamps before `processStart` are closed on disk (`GetAsync`) and in the list (`QueryAsync`, `GetSubtreeAsync`); a `running` entry at or after `processStart` keeps `running`; a `success` entry before `processStart` does not change; the return value is the number of closed entries; a second call returns 0.
- [X] T008 [P] [US2] Create `tests/integration/ExecutionLogs/ExecutionLogStartSweepIntegrationTests.cs` (collection `ConfigIsolation`, `TestEnvironment.PrepareCleanDataDir()`).
  1. `StartSweepClosesRunningEntriesOfEarlierProcess`: seed the entries before the host starts. Create `new FileExecutionLogRepository(dataDir)` with the path that `TestEnvironment.PrepareCleanDataDir()` returns (the service uses this path as `storageRoot`, and the repository adds `execution-logs`), call `AddAsync` for each entry, and dispose the repository. Seed a `running` queue root with a timestamp 1 day before `Process.GetCurrentProcess().StartTime` and a `RetentionExpiresUtc` 30 days in the future, and a `running` sequence child of it. Start the default host with `WebApplicationFactory<Program>` (no service replacement: the default process start time is later than the seeded timestamps). Poll `IExecutionLogRepository.GetAsync` for up to 10 s. Assert: both entries have `failure` and the new summaries; `GET /api/execution-logs?objectId=<queue id>` gives the root with `finalStatus` `failure`.
  2. `StartSweepKeepsEntriesAfterProcessStart`: in `WithWebHostBuilder(b => b.ConfigureTestServices(s => ...))`, remove the `IHostedService` descriptor whose `ImplementationType` is `ExecutionLogRetentionCleanupService`, and add `s.AddHostedService(sp => new ExecutionLogRetentionCleanupService(sp, sp.GetRequiredService<ILogger<ExecutionLogRetentionCleanupService>>(), processStartUtc))` with `processStartUtc` = 2 hours in the past. Seed one `running` root with a timestamp 3 hours in the past and one with a timestamp 1 hour in the past. Assert after the sweep (poll up to 10 s for the first one): the first has `failure`, the second keeps `running`.
  3. `StartSweepWritesLogLine` (FR-006a): a separate test with the same seed as test 1. Add `tests/integration/Helpers/TestLoggerProvider.cs` to the host logging in `WithWebHostBuilder`, poll until the root is closed, and assert one log entry with event id 7102 that contains the count 2.

### Implementation for User Story 2

- [X] T009 [P] [US2] Create `src/GameBot.Domain/Logging/ExecutionLogAbandonedRun.cs`: `public static class ExecutionLogAbandonedRun` with `public static ExecutionLogEntry Close(ExecutionLogEntry entry)` and `public static bool IsAbandoned(ExecutionLogEntry entry, DateTimeOffset processStartUtc)` (`FinalStatus` is `running`, ordinal ignore case, and `TimestampUtc < processStartUtc`). `Close` returns the same entry when its status is not `running`; else a new `ExecutionLogEntry` with all fields copied, `FinalStatus = "failure"`, and the summary of data-model.md (trim to 240 characters). STE doc comments.
- [X] T010 [US2] Add `Task<int> CloseAbandonedRunningAsync(DateTimeOffset processStartUtc, CancellationToken ct = default);` with an STE doc comment to `src/GameBot.Domain/Logging/IExecutionLogRepository.cs`, and implement it in `src/GameBot.Domain/Logging/FileExecutionLogRepository.cs`: `EnsureEntriesLoadedAsync`, then under `_mutex` find each entry with `ExecutionLogAbandonedRun.IsAbandoned`, write `Close(entry)` with `WriteEntryFileAsync`, replace it in `_entries` (one new array at the end), and return the count.
- [X] T011 [US2] In `src/GameBot.Service/Hosted/ExecutionLogRetentionCleanupService.cs`: add a field `_processStartUtc`; the public constructor sets it from `Process.GetCurrentProcess().StartTime.ToUniversalTime()`; add an `internal` constructor that takes the time (for tests). At the start of `ExecuteAsync`, before the loop, in its own `try`/`catch`: create a scope, get `IExecutionLogRepository`, call `CloseAbandonedRunningAsync(_processStartUtc, stoppingToken)`, and write the new log line 7102 (Information, `Execution log start sweep closed {Count} running entries of an earlier service process.`) when the count is more than 0. On `OperationCanceledException` return; on another exception write the new log line 7103 (Warning, `Execution log start sweep failed.`) and continue with the loop. The sweep runs also when the retention policy is off.
- [X] T012 [US2] Run T007 and T008, and all tests with `FullyQualifiedName~ExecutionLog` and `FullyQualifiedName~Queue` in `tests/unit` and `tests/integration`. All MUST pass.

**Checkpoint**: After a restart, the old root of a queue run is closed, and the root of the current run is the only `running` root of the queue.

---

## Phase 5: User Story 3 - The rules of a queue log are documented (Priority: P3)

**Goal**: The OpenAPI description states the queue log rules (FR-008).

**Independent Test**: `ExecutionLogsApiDescriptionTests` passes.

- [X] T013 [P] [US3] Create `tests/contract/ExecutionLogs/ExecutionLogsApiDescriptionTests.cs`: get `/swagger/v1/swagger.json` from a `WebApplicationFactory<Program>` client, as `tests/contract/KeyInputOpenApiTests.cs` does. Assert that the `description` of `get` on `/api/execution-logs` and on `/api/execution-logs/{id}/subtree` contains "no limit on the number of children", "rotatedToExecutionId", and "ended when the service stopped". The subtree description also still contains "cancellationReason".
- [X] T014 [US3] In `src/GameBot.Service/Swagger/SwaggerConfig.cs`: add `internal const string QueueLogRulesDescription` with the text of research R-006. In `ApplyExecutionLogExamples`, set `operation.Description ??= QueueLogRulesDescription` for the list operation, and for the subtree operation set the description to `QueueLogRulesDescription + " " + <current cancellation text>`. Run T013.

**Checkpoint**: The API description documents the rules.

---

## Phase 6: Polish & Cross-Cutting Concerns

- [X] T015 [P] Update `docs/architecture.md`: in the "Execution Log" bullet of the domain model, add in STE that the in-memory list of the file store changes only under the write lock (also in the retention cleanup), that there is no limit on the number of children of a queue log, and that at service start each `running` entry of an earlier service process gets `failure` with the summary "... run ended when the service stopped.". Refresh the "Last reviewed" line with feature 198, issue 408.
- [X] T016 [P] Add a `### Fixed` entry to `## [Unreleased]` in `CHANGELOG.md` (198-fix-queue-log-children, #408) in STE: the cause, the change, the start sweep, the API description, "Not changed" (response shapes, rotation, delete rule, queue engine), and a perf note (a write waits at most for the remove step of one cleanup, less than 1 s in production; the start sweep runs one time).
- [X] T017 [P] Add row 198 to `specs/STATUS.md` (Status `Implemented`), and set `**Status**: Implemented` in `specs/198-fix-queue-log-children/spec.md`.
- [X] T018 Run the full unit and integration test suites (`dotnet test "C:\src\GameBot\tests\unit\GameBot.UnitTests.csproj"`, `dotnet test "C:\src\GameBot\tests\integration\GameBot.IntegrationTests.csproj"`, `dotnet test "C:\src\GameBot\tests\contract\GameBot.ContractTests.csproj"`) and `dotnet build -c Release` with analyzers. Fix each failure and each new warning. Then do the quickstart checks in `specs/198-fix-queue-log-children/quickstart.md`.

---

## Dependencies & Execution Order

- Phase 1 → Phase 2 → Phase 3 (US1). US1 is the MVP.
- US2 (Phase 4) needs T005 only for the shared file `FileExecutionLogRepository.cs` (T010 edits the same file after T005). T007 and T009 can start after Phase 2.
- US3 (Phase 5) has no dependency on US1 or US2.
- Phase 6 after all stories.

Within a story: tests first (they fail), then the implementation, then the test run.

## Parallel Example

```text
# After T002:
T003 (race tests), T004 (subtree size test), T007 (abandoned run tests), T009 (close rule), T013 (API description test)
# After all stories:
T015, T016, T017
```

## Implementation Strategy

1. MVP: Phase 1, Phase 2, Phase 3 (US1). This fixes the issue: a new root is in the list during and after a cleanup.
2. Add US2 so that the old root of a restarted run does not say `running`.
3. Add US3 for the API description.
4. Polish: docs, changelog, status, full test run.
