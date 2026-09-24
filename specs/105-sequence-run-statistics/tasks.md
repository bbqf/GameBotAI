---

description: "Task list for feature 105: per-sequence run statistics per queue"
---

# Tasks: Per-Sequence Run Statistics per Queue

**Input**: Design documents from `specs/105-sequence-run-statistics/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/lastrun-condition.md, contracts/queue-sequence-stats.md, quickstart.md
**Issue**: [#224](https://github.com/bbqf/GameBotAI/issues/224)

**Tests**: The constitution (Principle II) requires tests. Each phase has unit tests, and most phases have contract tests. User Story 2 also has one integration test. Write the tests first. Make sure that they fail before you write the implementation.

**Organization**: The tasks are in groups, one group for each user story. You can test each story independently.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: You can do the task in parallel with other [P] tasks (different files, no dependency on a task that is not complete).
- **[Story]**: The user story of the task (US1, US2, US3).
- Each task gives the exact file path.

## Path Conventions

- Domain library: `src/GameBot.Domain/`
- Service host: `src/GameBot.Service/`
- Tests: `tests/unit/`, `tests/integration/`, `tests/contract/`
- Build and test: `dotnet build "C:\src\GameBot\GameBot.sln" -c Debug` and `dotnet test "C:\src\GameBot\GameBot.sln" -c Debug --no-build`
- Use `FakeTimeProvider` from `tests/unit/Queues/FakeTimeProvider.cs` for all time-dependent unit tests.
- Write all code comments, API descriptions and error messages in ASD-STE100 Simplified Technical English (Constitution Principle VI).

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Make sure that the start point is green.

- [ ] T001 Build `C:\src\GameBot\GameBot.sln` and run all tests. Record each test that fails before this feature starts. The constitution blocks progress on a red build. Do not change code in this task.

---

## Phase 2: Foundational (Prerequisites for All Stories)

**Purpose**: The statistics model, the statistics store and the `lastRun` condition model. All user stories need these parts.

**CRITICAL**: Do not start a user story before this phase is complete.

### Tests for the foundation

- [ ] T002 [P] Write unit tests for `SequenceRunStatistics.Apply` and `HasRunInWindow` in `tests/unit/Queues/SequenceRunStatisticsTests.cs`. Test these cases:
  - One record sets the last-run fields and one counter.
  - 101 records keep 100 records (the oldest record is removed), and the counters show 101.
  - `LastSuccessAt` does not change on a `failure` or `cancelled` record.
  - The window check includes both ends (`from <= EndedAt <= to`).
  - A record with the wrong status does not match.
- [ ] T003 [P] Write unit tests for `FileSequenceRunStatisticsStore` in `tests/unit/Queues/FileSequenceRunStatisticsStoreTests.cs`. Use a new temp folder for each test. Test these cases:
  - Record, then `GetAsync` and `GetForQueueAsync`.
  - A new store instance on the same folder reads the same values (service restart).
  - An absent file gives an empty map.
  - A damaged file gives an empty map and one Warning log. A file with a higher `schemaVersion` gives the same result.
  - The next record heals the damaged file.
  - `DeleteQueueAsync` removes the file and the cache. It gives no error when no file exists.
  - A returned copy does not change the cache.
  - A `queueId` with a path separator or `..` throws `ArgumentException`.
  - The file uses lower-case status text.
- [ ] T004 [P] Write unit tests for `LastRunConditionRules` in `tests/unit/Sequences/LastRunConditionRulesTests.cs`. Test these cases:
  - `TryParseSince` accepts `00:00`, `11:00` and `23:59`. It rejects `9:00`, `24:00`, `11:00:00` and empty text.
  - `TryParseWithin` reads `24:00:00` as 24 hours (not 24 days). It reads `1.00:00:00` as 24 hours.
  - `TryParseWithin` accepts `366.00:00:00` as the maximum.
  - `TryParseWithin` rejects `00:00:00`, `-01:00:00`, `367.00:00:00`, `1:00` and `abc`.
  - `Validate` returns each message tail of the table in `contracts/lastrun-condition.md`. The cases are:
    - absent or blank `sequence`,
    - unknown `status`,
    - both `since` and `within`, and neither,
    - bad `since`, and bad `within`.
  - `Validate` accepts `status` in upper case, lower case and mixed case.
- [ ] T005 [P] Extend `tests/unit/Sequences/CompositeConditionSerializationTests.cs` with JSON round trips (the domain type and the discriminator `lastRun`). Test these cases:
  - A `lastRun` condition with `since`.
  - A `lastRun` condition with `within` and `negate: true`.
  - The same conditions as a child of `all`, `any` and `none`.
  - The `since` and `within` text stays as written.

### Implementation for the foundation

- [ ] T006 [P] Create the enum `SequenceRunStatus` (`Success`, `Failure`, `Cancelled`) in `src/GameBot.Domain/Queues/SequenceRunStatus.cs`. Add XML docs (data model section 1).
- [ ] T007 Create `SequenceRunRecord` and `SequenceRunStatistics` in `src/GameBot.Domain/Queues/SequenceRunStatistics.cs` (data model sections 2 and 3; depends on T006). Add these members:
  - `MaxRecentRuns = 100`,
  - `Apply(SequenceRunRecord)`,
  - `HasRunInWindow(SequenceRunStatus, DateTimeOffset, DateTimeOffset)`,
  - a deep-copy method.
- [ ] T008 Create the interface `ISequenceRunStatisticsStore` in `src/GameBot.Domain/Queues/ISequenceRunStatisticsStore.cs` (data model section 5; depends on T007). Add `RecordAsync`, `GetForQueueAsync`, `GetAsync` and `DeleteQueueAsync`.
- [ ] T009 Create `FileSequenceRunStatisticsStore` in `src/GameBot.Domain/Queues/FileSequenceRunStatisticsStore.cs` (data model sections 4 and 5, research R-003 and R-004; depends on T008). Use these parts:
  - The folder `<dataRoot>/queue-sequence-stats/`, with one `<queueId>.json` for each queue and `schemaVersion: 1`.
  - An in-memory cache.
  - One `SemaphoreSlim` for disk reads and writes.
  - A write to `<file>.tmp` and then a replace. Use the same method as `src/GameBot.Domain/Queues/FileQueueRunStateStore.cs`.
  - `JsonStringEnumConverter` with a camel-case policy.
  - A safe-file-name check on `queueId`.
  - A Warning log for a damaged file, never an exception.
  - `IDisposable`.

  Make T002 and T003 pass.
- [ ] T010 Register `FileSequenceRunStatisticsStore` as the singleton `ISequenceRunStatisticsStore` with `storageRoot` (depends on T009). Do this in `GameBotServiceSetup.RegisterRepositories` in `src/GameBot.Service/GameBotServiceSetup.cs`.
- [ ] T011 Add `LastRunStepCondition` to `src/GameBot.Domain/Commands/SequenceStepCondition.cs` (data model section 6). Add these parts:
  - the fields `Sequence`, `Status`, `Since` and `Within`, and the inherited `Negate`,
  - `[JsonDerivedType(typeof(LastRunStepCondition), "lastRun")]` on `SequenceStepCondition`,
  - the `[JsonIgnore]` override of `Type` that the other leaf types use.
- [ ] T012 Create the static class `LastRunConditionRules` in `src/GameBot.Domain/Commands/LastRunConditionRules.cs` (depends on T011). Add these methods:
  - `Validate(LastRunStepCondition)`,
  - `TryParseSince(string, out TimeOnly)`,
  - `TryParseWithin(string, out TimeSpan)`.

  Use the strict patterns of research R-008 and R-009, not `TimeSpan.Parse`. Use the exact message tails of research R-011. Make T004 pass.
- [ ] T013 Add the `LastRunStepCondition` case to `GuardConditionLeaves` in `src/GameBot.Domain/Commands/FileSequenceRepository.cs` (depends on T012). The case calls `LastRunConditionRules.Validate`. It throws `InvalidOperationException` on the first error. This guard is the last guard only.
- [ ] T014 Add `LastRunConditionContract` to `src/GameBot.Service/Models/SequenceStepContracts.cs` (data model section 7; depends on T011). Add these parts:
  - the `string?` fields `Sequence`, `Status`, `Since` and `Within`, and `Negate`,
  - its `JsonDerivedType` value `lastRun` on `SequenceStepConditionContract`.
- [ ] T015 In `src/GameBot.Service/Endpoints/SequencesEndpoints.cs`, map `LastRunConditionContract` to `LastRunStepCondition` in `MapPerStepCondition` (depends on T014). Map it back in `MapPerStepConditionToDto`. Leave out `since` or `within` when it is null. Make T005 pass.

**Checkpoint**: The store persists statistics, and the service can save and read a `lastRun` condition. User story work can start.

---

## Phase 3: User Story 1 - Read the run statistics of each sequence in a queue (Priority: P1) MVP

**Goal**: The queue records each completed sequence run. `GET /api/queues/{id}` returns `sequenceStats` for each sequence. The values stay after a queue restart and a service restart.

**Independent Test**: Start a queue that runs a sequence that succeeds and a sequence that fails. Read the queue. Make sure that each sequence shows the correct last status, times and counts. Stop and start the queue, then restart the service. Read the queue again. The values must not change.

### Tests for User Story 1

- [ ] T016 [P] [US1] Write unit tests for the status classification in `tests/unit/Queues/QueueExecutionServiceRunStatisticsTests.cs`. Use the table of research R-002. Use the current harness style of `tests/unit/Queues/QueueExecutionServiceTests.cs`. Test these cases:
  - `Succeeded` gives `success`.
  - A run that a Break step ends gives `success`.
  - `Failed` gives `failure`. An exception gives `failure`.
  - A stop by hand gives `cancelled`. A failure-policy stop gives `cancelled`.
  - The sequence time limit (watchdog) gives `cancelled`.
  - `Failed` after the watchdog timer fired gives `cancelled`.
  - A host shutdown (`_appStopping` cancelled) records nothing. Test a stop exception and a `Failed` result with the stop token cancelled.
  - A fault before the call to `ExecuteAsync` records nothing.
  - A store that throws does not change the result of `RunOneSequenceAsync`. It logs one Warning.
  - `startedAt` and `endedAt` come from the `FakeTimeProvider`.
  - Two entries of the same sequence share one statistics entry.
  - A guard sequence (`EveryStep`, `BeforeEachRun`) gets its own entry.
- [ ] T017 [US1] Write a unit test for queue restart in `tests/unit/Queues/QueueExecutionServiceRunStatisticsTests.cs` (FR-004, FR-014). Do this task after T016, because both tasks change the same file. Do these steps:
  - Record runs, stop the queue and start it again.
  - Make sure that the statistics are the same.
  - Before the stop, let a `reschedule-self` run add a time slot.
  - After the restart, make sure that the runtime entries are equal to the template entries.
  - Make sure that the time slot that `reschedule-self` added before the restart is gone.
- [ ] T018 [P] [US1] Write contract tests in `tests/contract/Queues/QueueSequenceStatsContractTests.cs`. Each test creates its own queue with a new ID (shared bin data folder). Test these cases:
  - `GET /api/queues/{id}` returns `sequenceStats: {}` for a new queue.
  - The test writes records through the registered `ISequenceRunStatisticsStore`. Then the read returns the entry keyed by sequence ID.
  - The entry has `sequenceName`, the times, a lower-case `lastRunStatus` and the counts.
  - Keys are in ordinal order.
  - `PUT /api/queues/{id}/entries`, `PUT /api/queues/{id}/template` and `PUT /api/queues/{id}/game` also return `sequenceStats`.
  - `DELETE /api/queues/{id}` removes the statistics file.
  - Record a run, then call `POST /api/queues/{id}/duplicate`. Its `QueueResponse` has no `sequenceStats`. Then `GET /api/queues/{newId}` returns `sequenceStats: {}`.
  - Record a run, then call `PUT /api/queues/{id}/entries` without that sequence. The entry of that sequence is still in `sequenceStats` (spec edge case "Sequence removed from the queue template").
  - A damaged statistics file gives `{}` and not a 500.
  - A sequence that no longer exists gives `sequenceName: null`.
- [ ] T019 [P] [US1] Write OpenAPI tests in `tests/contract/Queues/QueueSequenceStatsOpenApiTests.cs`. Use the style of `tests/contract/Queues/QueueHealthOpenApiTests.cs`. Test these cases:
  - The schema `QueueSequenceStatsResponse` exists, and each field has a description.
  - `lastRunStatus` has the enum `success`, `failure`, `cancelled`.
  - `QueueDetailResponse.sequenceStats` is an object with a description. Its `additionalProperties` is `QueueSequenceStatsResponse`.
  - The `GET /api/queues/{id}` description names `sequenceStats`.

### Implementation for User Story 1

- [ ] T020 [US1] Change `src/GameBot.Service/Services/QueueExecution/QueueExecutionService.cs` (research R-002). Do these steps:
  - Add `ISequenceRunStatisticsStore?` as an optional last constructor parameter (default null).
  - In `RunOneSequenceAsync`, take `startedAt` from `_timeProvider.GetLocalNow()` just before `_sequenceExecution.ExecuteAsync`.
  - After the call and in each `catch`, compute the status with the table of research R-002.
  - Then call a new small private helper `RecordRunAsync(queueId, sequenceId, startedAt, status)`.
  - In the helper, take `endedAt` from `_timeProvider`. Call the store with `CancellationToken.None`. Catch and log all errors.
  - Do not record on host shutdown.
  - Keep the return values and the rethrow as they are.
  - Keep the new lines in `RunOneSequenceAsync` to a minimum (analyzer cost).

  Make T016 and T017 pass.
- [ ] T021 [US1] Add a Warning log message for a failed statistics write, with the next free EventId (depends on T020). Add it to the `QueueExecutionLog` class in `src/GameBot.Service/Services/QueueExecution/`.
- [ ] T022 [P] [US1] Create `QueueSequenceStatsResponse` in `src/GameBot.Service/Contracts/Queues/QueueSequenceStatsResponse.cs` (data model section 11). Add these fields:
  - `sequenceName`,
  - `lastRunStartedAt` and `lastRunEndedAt`,
  - `lastRunStatus` as lower-case text,
  - `lastSuccessAt`,
  - `successCount`, `failureCount` and `cancelledCount`.
- [ ] T023 [US1] Add `SequenceStats` to `src/GameBot.Service/Contracts/Queues/QueueDetailResponse.cs` (depends on T022). Use `SortedDictionary<string, QueueSequenceStatsResponse>` with an ordinal comparer. Use the JSON name `sequenceStats`. The value is never null.
- [ ] T024 [US1] Change `src/GameBot.Service/Endpoints/QueuesEndpoints.cs` (depends on T023). Do these steps:
  - Give `BuildDetailAsync` an `ISequenceRunStatisticsStore` parameter.
  - Fill `SequenceStats` from `GetForQueueAsync`, with `sequenceName` from the `namesById` lookup.
  - Add the parameter to the handler signatures of each caller: GET, PUT entries, PUT template and PUT game.
  - In `DELETE /api/queues/{id}`, call `DeleteQueueAsync(id)` after `repo.DeleteAsync`.

  Make T018 pass.
- [ ] T025 [US1] Create `QueueSequenceStatsSchemaFilter` (ISchemaFilter) in `src/GameBot.Service/Swagger/QueueSequenceStatsSchemaFilter.cs` (depends on T022). Add these parts:
  - a description for each field of `QueueSequenceStatsResponse`,
  - the `lastRunStatus` enum,
  - the description of `QueueDetailResponse.sequenceStats`, with the key and the rules of `contracts/queue-sequence-stats.md`,
  - `additionalProperties` on `sequenceStats`.
- [ ] T026 [US1] Register `QueueSequenceStatsSchemaFilter` next to `QueueHealthSchemaFilter` in `src/GameBot.Service/GameBotServiceSetup.cs` (depends on T025). In `src/GameBot.Service/Swagger/SwaggerConfig.cs`, extend the `GET /api/queues/{id}` operation description. Also add one `sequenceStats` entry to `QueueDetailExample()`. Make T019 pass.

**Checkpoint**: User Story 1 is complete. A script can read the last status and last success time of each sequence with one queue read.

---

## Phase 4: User Story 2 - Guard a sequence with a condition on its own earlier runs (Priority: P1)

**Goal**: A `lastRun` condition in a queue run is true when the named sequence has a completed run with the given status in the window. The window is the `since` window or the `within` window. In a run that has no queue, the condition is false.

**Independent Test**: Make a sequence whose first step breaks the run when `{ "type": "lastRun", "sequence": "self", "status": "success", "since": "11:00" }` is true. Run it in a queue two times after 11:00. The first run does the work. The second run stops at the guard.

### Tests for User Story 2

- [ ] T027 [P] [US2] Write unit tests for `LastRunWindow.SinceStart` in `tests/unit/Sequences/LastRunWindowTests.cs`. Test these cases:
  - Now 14:00 and `since` 11:00 gives 11:00 on the same day.
  - Now 10:00 gives 11:00 on the previous day.
  - Now exactly 11:00 gives now.
  - A spring-forward day with `since` in the gap gives the previous real occurrence.
  - A fall-back day with `since` in the repeated hour gives the most recent real instant at or before now.

  Use a real zone with DST, for example "W. Europe Standard Time" or "Europe/Berlin". Use the zone that the host resolves.
- [ ] T028 [P] [US2] Write unit tests for `LastRunConditionEvaluator` in `tests/unit/Sequences/LastRunConditionEvaluatorTests.cs`. Use a `FakeTimeProvider` and an in-memory or temp-folder store. Test these cases:
  - Spec User Story 2 scenarios 1 to 5:
    - 14:00/12:00 gives true,
    - 10:00/previous 12:00 gives true,
    - 11:30/10:30 gives false,
    - only failures gives false,
    - `within` `24:00:00` gives true at 23 h and false at 25 h.
  - `self` resolves to the own sequence ID.
  - Another sequence ID.
  - An unknown ID gives false.
  - A record from before a queue restart (new store instance) still gives true (scenario 6).
- [ ] T029 [P] [US2] Extend `tests/unit/Sequences/CompositeConditionEvaluatorTests.cs` for `SequenceStepConditionEvaluator.EvaluateAsync`. Test these cases:
  - A `lastRun` leaf calls the delegate.
  - With no delegate, the leaf is false and nothing throws (FR-012).
  - `negate: true` inverts the result.
  - A `lastRun` child in `all`, `any` and `none` gives the correct result (scenario 7).
  - `Describe` gives `lastRun(sequence=self, status=success, since=11:00)`.
  - `Describe` gives `NOT lastRun(sequence=seq-daily-train, status=failure, within=24:00:00)`.
- [ ] T030 [P] [US2] Extend `tests/unit/Sequences/CompositeConditionRunnerTests.cs`. Test these cases:
  - Push a `SequenceRunContext`. Then a `lastRun` condition reads the context delegate in each of these places:
    - a step condition,
    - an If condition,
    - a `while` loop condition,
    - a Break condition.
  - Run a sequence with a `lastRun` Break condition in a loop body. When the delegate gives true, the loop stops at the Break. When it gives false, the loop body continues.
  - Run a sequence with a `lastRun` Break condition in an If branch in a loop body. When the delegate gives true, the loop stops at the Break. When it gives false, the loop body continues after the branch.
  - For these two cases, make sure that the execution log shows `break` or `no_break` for the Break step.
  - With no context (ad-hoc run), the condition is false and the run does not fail.
  - The Break reason in the execution log uses the `Describe` text.
  - The step-guard entry in the execution log has `conditionType: "lastRun"` (`contracts/lastrun-condition.md`, section "Execution log").
  - `SequenceRunContext.Current` is null again after the run.
- [ ] T031 [P] [US2] Write unit tests for the context push in `SequenceExecutionService` in `tests/unit/Sequences/SequenceRunContextTests.cs`. Test these cases:
  - A queue run (`OriginatingQueueId` set, `dryRun` false) pushes a context with the queue ID and the sequence ID.
  - An ad-hoc run and a dry-run push no context.
  - A simulated nested run pushes its own context with the nested sequence ID. Thus `self` names the nested sequence (spec edge case "Nested sequence run"). No step type runs another sequence at this time. Thus the test makes a second direct `Push` or `ExecuteAsync` call inside the outer run. Write this in a test comment.
  - The outer context comes back after the simulated nested run.
  - `Push(...).Dispose()` puts back the earlier value.
  - With no evaluator registered, no context is pushed.
- [ ] T032 [P] [US2] Write an integration test in `tests/integration/Queues/QueueLastRunGuardRestartTests.cs` (SC-002, User Story 2 scenario 6). Use the harness style of `tests/integration/Queues/QueueFailurePolicyRunTests.cs`. Do these steps:
  - Create a queue and a sequence, each with a new ID.
  - Make the first step a Break step with the guard `lastRun`, `self`, `success`, `since: "11:00"`.
  - Make a later step do the work. Count each run of the work step.
  - Clock: add a private nested `TimeProvider` subclass to the test file. Copy the shape of `tests/unit/Queues/FakeTimeProvider.cs` (that class is internal to the unit test project). Its `LocalTimeZone` is UTC.
  - In `WithWebHostBuilder(...).ConfigureServices`, remove the registered `TimeProvider` singleton and add the test clock. Use the same method that `QueueFailurePolicyRunTests.NewApp` uses for `IFailureNotifier`. `QueueExecutionService` gets `TimeProvider` from DI, so the queue uses the test clock.
  - Schedule mode: link a template with one `OncePerRun` entry, and create the queue with `cycleExecution: false`. Each start then runs the sequence one time, and the queue stops itself (`CompletedFullRun`). The run loop does not wait for the clock, so a clock that does not move does not block it.
  - Between two starts, wait for the status `Stopped` (the `WaitForStatusAsync` poll of `QueueCyclesEndpointTests`). Then move the clock forward with `Advance(TimeSpan.FromHours(1))`. Keep all times between 11:00 and 23:00 UTC of one day.
  - Restart before the first success: create the queue and the template in application instance 1 at 13:00. Do not start the queue. Dispose instance 1.
  - Start application instance 2 on the same data folder, with the clock at 14:00. Start the queue and wait for `Stopped`. The guard is false, so the work runs one time.
  - Dispose instance 2. Start application instance 3 on the same data folder, with the clock at 15:00. The store is now a new instance (service restart).
  - Start the queue, wait for `Stopped`, advance the clock one hour, and do this again. The guard is true in the two runs, so the work does not run.
  - Make sure that the work ran exactly one time in the window.
  - Make sure that `sequenceStats` shows `successCount: 3` for the sequence.

  No fallback to the real clock. Reason: DI replacement of a singleton already works in this harness (`IFailureNotifier`). A real clock makes the window depend on the time of the test run. This test also needs the run record of US1 (T020).

### Implementation for User Story 2

- [ ] T033 [P] [US2] Create `SequenceRunContext` in `src/GameBot.Domain/Services/SequenceRunContext.cs` (data model section 8). Add these members:
  - `QueueId`, `SequenceId` and `LastRunEvaluator`,
  - static `Current` on `AsyncLocal<T>`,
  - static `Push`, which returns an `IDisposable`.

  Use the same pattern as `SequenceTimeLimitScope` in `src/GameBot.Service/Services/SequenceExecution/SequenceTimeLimitScope.cs`.
- [ ] T034 [P] [US2] Create the pure static method `LastRunWindow.SinceStart(DateTimeOffset now, TimeOnly since, TimeZoneInfo zone)` in `src/GameBot.Domain/Services/LastRunWindow.cs`. Use the algorithm of research R-007 (at most two days back). Make T027 pass.
- [ ] T035 [US2] Create `LastRunConditionEvaluator` in `src/GameBot.Domain/Services/LastRunConditionEvaluator.cs` (data model section 9; depends on T034). Do these steps:
  - Take `ISequenceRunStatisticsStore` and `TimeProvider` as dependencies.
  - Add `EvaluateAsync(queueId, ownSequenceId, condition, ct)`.
  - Use `LastRunConditionRules.TryParseSince` and `LastRunConditionRules.TryParseWithin`.
  - For a stored value that does not parse, throw `ConditionEvaluationException` with kind `UnsupportedCondition`.
  - Do not apply `Negate` here.

  Make T028 pass.
- [ ] T036 [US2] Change `src/GameBot.Domain/Services/SequenceStepConditionEvaluator.cs` (depends on T011). Do these steps:
  - Add the optional parameter `Func<LastRunStepCondition, CancellationToken, Task<bool>>? lastRunEvaluator = null` to `EvaluateAsync`.
  - Pass the parameter down the private walk.
  - Add the `LastRunStepCondition` leaf case. The case gives false when the delegate is null.
  - Add the `Describe` case of `contracts/lastrun-condition.md`, section "Execution log".

  Make T029 pass.
- [ ] T037 [US2] Change `src/GameBot.Domain/Services/SequenceRunner.cs` (research R-005; depends on T033 and T036). Do these steps:
  - Pass `SequenceRunContext.Current?.LastRunEvaluator` at the two calls of `SequenceStepConditionEvaluator.EvaluateAsync`.
  - These two calls are the step guard and `EvaluateLoopConditionAsync`.
  - In `DescribeBreakCondition`, use `SequenceStepConditionEvaluator.Describe` for a `LastRunStepCondition`.
  - Do not add a parameter to other private methods.

  Make T030 pass.
- [ ] T038 [US2] Change `src/GameBot.Service/Services/SequenceExecution/SequenceExecutionService.cs` (depends on T035, T037). Do these steps:
  - Add `LastRunConditionEvaluator?` as an optional last constructor parameter (default null).
  - In `ExecuteCoreAsync`, after the device scope, push `SequenceRunContext` with a small private helper.
  - Push the context only when `OriginatingQueueId` is not empty, `dryRun` is false and the evaluator is not null.
  - Do not add a statistics record here. Only the queue records runs. A later nested run is also not recorded (spec edge case "Nested sequence run").

  Make T031 pass.
- [ ] T039 [US2] Register `LastRunConditionEvaluator` as a singleton in `src/GameBot.Service/GameBotServiceSetup.cs` (depends on T038). Make sure that the DI container gives it to `SequenceExecutionService`. Make T032 pass (T032 also needs T020).

**Checkpoint**: User Stories 1 and 2 work. A daily task with a `since` guard runs its work one time in each window, also after a queue restart.

---

## Phase 5: User Story 3 - Author and validate the new condition like the other types (Priority: P2)

**Goal**: Sequence create, update, PATCH and `dryRun` accept a correct `lastRun` condition. They reject an incorrect condition with a 400 that names the problem. The OpenAPI document describes the `lastRun` type.

**Independent Test**: Send correct and incorrect `lastRun` conditions to sequence create, update, PATCH and `dryRun`. The service accepts the correct ones. Incorrect ones get a 400 with a message that names the problem, and never a 500. Read the OpenAPI document and find the `lastRun` type.

### Tests for User Story 3

- [ ] T040 [P] [US3] Extend `tests/unit/Sequences/CompositeConditionValidationTests.cs`. Test these cases:
  - Each message of the table in `contracts/lastrun-condition.md`, with the `Step '<label>' condition at <path>: ` prefix.
  - Each message at the root (`$`) of each of the six slots:
    - step condition,
    - Break condition,
    - If condition,
    - loop condition,
    - loop-body Break condition,
    - If-branch Break condition.
  - Each message in `all`, `any` and `none` (`$.children[i]`).
  - A correct `lastRun` passes.
  - The composite limits (16 children, depth 4) still apply with `lastRun` children.
  - `imageVisible` and `commandOutcome` root leaves keep their current messages (no double message).
- [ ] T041 [P] [US3] Write contract tests in `tests/contract/Sequences/LastRunConditionContractTests.cs`. Test these cases:
  - `POST /api/sequences` with `dryRun: true` and a correct `lastRun` returns `valid: true`.
  - POST, PUT and PATCH with each bad input of the contract table return 400 with the message tail. They never return 500. The bad inputs are:
    - no `sequence`,
    - `status: "failed"`,
    - both `since` and `within`,
    - neither `since` nor `within`,
    - `since: "9:00"` and `since: "24:00"`,
    - `within: "00:00:00"`, `within: "400.00:00:00"` and `within: "abc"`.
  - A saved condition reads back with the same `since` or `within` text. The field that is not set is not in the response.
  - A `lastRun` condition in a composite saves and reads back.
- [ ] T042 [P] [US3] Write OpenAPI tests in `tests/contract/Sequences/LastRunConditionOpenApiTests.cs`. Test these cases:
  - The schema `LastRunCondition` exists.
  - The `SequenceStepCondition` discriminator maps `lastRun` to it.
  - Each field has a description.
  - `status` has the enum `success`, `failure`, `cancelled`.
  - `since` and `within` have the patterns of `contracts/lastrun-condition.md`.
  - The schema description states the "exactly one of `since` or `within`" rule and the "no queue means false" rule.

### Implementation for User Story 3

- [ ] T043 [US3] Change `src/GameBot.Domain/Services/CompositeConditionValidator.cs` (research R-011). Do these steps:
  - Add a `LastRunStepCondition` case to `Walk`. The case calls one small helper.
  - In the helper, call `LastRunConditionRules.Validate`, and add each error with the path.
  - Change the root gate to `condition is not CompositeStepCondition and not LastRunStepCondition && !validateLeafAtRoot`.

  Make T040 pass.
- [ ] T044 [US3] Check the validation path of the six slots (depends on T043). Do these checks:
  - In `src/GameBot.Domain/Services/SequenceStepValidationService.cs`, make sure that each of the six slots goes through `CompositeConditionValidator.Validate`.
  - In `src/GameBot.Service/Endpoints/SequencesEndpoints.cs`, make sure that the create, update, PATCH and `dryRun` paths turn the errors into a 400.
  - Make sure that each path does this before `FileSequenceRepository` runs.
  - If a slot or a path does not do this, fix it.
  - Write the result of each check as a note under this task.

  Make T041 pass.
- [ ] T045 [P] [US3] Create `LastRunConditionSchemaFilter` (ISchemaFilter) in `src/GameBot.Service/Swagger/LastRunConditionSchemaFilter.cs`. Add these parts:
  - the field descriptions,
  - the `status` enum,
  - the `since` and `within` patterns,
  - the schema description of the two rules.
- [ ] T046 [US3] In `src/GameBot.Service/Swagger/ConditionalFlowSchemaDocumentFilter.cs`, add a `GenerateSchema` call for `LastRunConditionContract` (depends on T045). Add the alias `LastRunCondition`. Register `LastRunConditionSchemaFilter` in `src/GameBot.Service/GameBotServiceSetup.cs`. Make T042 pass.

**Checkpoint**: All user stories work independently.

---

## Phase 6: Polish & Shared Concerns

**Purpose**: Documentation, full test run and hand check.

- [ ] T047 [P] Update `docs/architecture.md` with these items:
  - the `lastRun` condition (domain model and capability),
  - `sequenceStats` on the queue read (API surface),
  - the folder `queue-sequence-stats/` (persistence layout),
  - a new "Last reviewed" date.
- [ ] T048 [P] Add an `Added` entry to `CHANGELOG.md` for the run statistics and the `lastRun` condition. Include the web UI limit (research R-016): write `lastRun` conditions through the API.
- [ ] T049 [P] Add row 105 to `specs/STATUS.md`.
- [ ] T050 Build `C:\src\GameBot\GameBot.sln` and run all tests. Fix each failure that this feature causes. Compare with the list of T001. If a known flaky test fails (for example `MaskedTemplateMatchTests`), run it again. Then measure the performance goal of the plan:
  - Make a store with one queue of 50 sequences at 100 records each.
  - Measure the time of 20 `RecordAsync` calls with a `Stopwatch`.
  - Make sure that the p95 is below 50 ms.
  - Put 100 records for one sequence in the store, and read the queue file one time.
  - Measure one `LastRunConditionEvaluator.EvaluateAsync` call over the 100 records with a `Stopwatch`. Do a warm-up call first.
  - Make sure that the time is below 1 ms (plan performance goal for `lastRun`).
  - Write the results as a note under this task.

  Then do the lint and format check:
  - CI uses `dotnet build GameBot.sln -c Release -warnaserror` (`.github/workflows/dotnet.yml`) as its lint gate. Run this command.
  - Run `dotnet format whitespace "C:\src\GameBot\GameBot.sln" --verify-no-changes --no-restore --include <changed .cs files>`. This is the format check of `scripts/analyze-test-results.ps1`.
  - Fix each problem in the changed files. Write the result of the two commands as a note under this task.

  For code coverage (constitution Principle II), the CI coverage gate is the check. Optionally, collect local coverage for the new files with `dotnet test --collect "XPlat Code Coverage"`. Write the result as a note under this task.
- [ ] T051 Do the steps of `specs/105-sequence-run-statistics/quickstart.md` against a local service on port 8080 where possible. Record the result.
- [ ] T052 Set **Status** to `Implemented` in `specs/105-sequence-run-statistics/spec.md` (depends on T050).

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies.
- **Foundational (Phase 2)**: Depends on Phase 1. Blocks all user stories.
- **User Story 1 (Phase 3)**: Depends on Phase 2 only.
- **User Story 2 (Phase 4)**: Depends on Phase 2. The integration test T032 and the end-to-end independent test also need the run record of US1 (T020). The unit tests of US2 write records directly to the store, so they do not need US1.
- **User Story 3 (Phase 5)**: Depends on Phase 2 only.
- **Polish (Phase 6)**: Depends on all user stories.

### Task Dependencies in Each Story

- Tests first. Make sure that they fail. Then do the implementation.
- Phase 2: T006 -> T007 -> T008 -> T009 -> T010; T011 -> T012 -> T013; T011 -> T014 -> T015.
- US1: T016 -> T017 (same file); T020 -> T021; T022 -> T023 -> T024; T022 -> T025 -> T026.
- US2: T033, T034 and T036 in parallel; T034 -> T035; T011 -> T036 -> T037; T033 -> T037; T035 + T037 -> T038 -> T039. T032 passes after T039 and T020.
- US3: T043 -> T044; T045 -> T046.

### Shared Files (do not change in parallel)

- `src/GameBot.Service/GameBotServiceSetup.cs`: T010, T026, T039, T046.
- `tests/unit/Queues/QueueExecutionServiceRunStatisticsTests.cs`: T016, T017.

### Parallel Opportunities

- Phase 2 tests T002 to T005 in parallel. T006 and T011 in parallel.
- After Phase 2, US1, US2 and US3 can go in parallel. They change different files, apart from `GameBotServiceSetup.cs`.
- In each story, all test tasks marked [P] in parallel.

---

## Parallel Example: User Story 1

```text
Task: "T016 Classification unit tests in tests/unit/Queues/QueueExecutionServiceRunStatisticsTests.cs"
Task: "T018 Contract tests in tests/contract/Queues/QueueSequenceStatsContractTests.cs"
Task: "T019 OpenAPI tests in tests/contract/Queues/QueueSequenceStatsOpenApiTests.cs"
Task: "T022 QueueSequenceStatsResponse in src/GameBot.Service/Contracts/Queues/QueueSequenceStatsResponse.cs"
```

## Parallel Example: User Story 2

```text
Task: "T027 Window unit tests in tests/unit/Sequences/LastRunWindowTests.cs"
Task: "T028 Evaluator unit tests in tests/unit/Sequences/LastRunConditionEvaluatorTests.cs"
Task: "T032 Guard restart integration test in tests/integration/Queues/QueueLastRunGuardRestartTests.cs"
Task: "T033 SequenceRunContext in src/GameBot.Domain/Services/SequenceRunContext.cs"
Task: "T034 LastRunWindow in src/GameBot.Domain/Services/LastRunWindow.cs"
```

## Parallel Example: User Story 3

```text
Task: "T040 Validator unit tests in tests/unit/Sequences/CompositeConditionValidationTests.cs"
Task: "T041 Contract tests in tests/contract/Sequences/LastRunConditionContractTests.cs"
Task: "T045 LastRunConditionSchemaFilter in src/GameBot.Service/Swagger/LastRunConditionSchemaFilter.cs"
```

---

## Implementation Strategy

### MVP First (User Story 1 Only)

1. Do Phase 1 and Phase 2.
2. Do Phase 3 (US1).
3. Stop and test US1 independently. The queue read must show correct statistics after a queue restart and a service restart.
4. The external restart script can now read `sequenceStats` in place of the execution logs.

### Incremental Delivery

1. Setup + Foundational: the store and the condition model are ready.
2. US1: statistics on the queue read (MVP).
3. US2: the `lastRun` guard works in queue runs. This removes the need for the external script (SC-005).
4. US3: full save-time validation with clear 400 messages, and the OpenAPI description.
5. Polish: documentation and the full test run.

---

## Notes

- [P] tasks change different files and have no dependency on a task that is not complete.
- Keep new logic in small private helpers. Do not make `RunOneSequenceAsync`, `ExecuteCoreAsync` or the large `SequenceRunner` methods larger than necessary (build-time analyzers).
- Contract tests and integration tests create their own queue and sequence with new IDs. Thus they do not change the shared bin data folder for other tests.
- Do not change the web UI (spec Assumptions).
- Do not add other condition types (FR-015).
