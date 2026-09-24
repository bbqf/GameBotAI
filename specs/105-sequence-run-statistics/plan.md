# Implementation Plan: Per-Sequence Run Statistics per Queue

**Branch**: `105-sequence-run-statistics` | **Date**: 2026-09-24 | **Spec**: [spec.md](./spec.md)
**Input**: Feature specification from `specs/105-sequence-run-statistics/spec.md`
**Issue**: [#224](https://github.com/bbqf/GameBotAI/issues/224). The PR closes it.

## Summary

A queue records the result of each sequence run that it starts: start time, end time and status (`success`, `failure` or `cancelled`). A new file store keeps, for each (queue, sequence) pair, the last-run values, the total counts and the 100 most recent run records. The store persists one JSON file for each queue under the service data root. Thus the values stay after a queue restart and a service restart. `GET /api/queues/{id}` returns the values in a new `sequenceStats` object, keyed by sequence ID.

A new step condition type `lastRun` reads these values. It is true when the named sequence (`self` or an ID) has a completed run with a given status in the current queue. The end time of that run must be in a time window. The window starts at one of these two points:

- the last occurrence of a local time of day (`since: "HH:mm"`),
- now minus a duration (`within: "hh:mm:ss"`).

The queue context gets to the runner through an ambient `SequenceRunContext`. `SequenceExecutionService` sets it for queue runs only. An ad-hoc run has no context, so the condition is false. Save-time validation gives a 400 for each bad field. The OpenAPI document describes the new condition and the new queue field.

## Technical Context

**Language/Version**: C# 12 / .NET 9 (`net9.0`)
**Primary Dependencies**: ASP.NET Core Minimal APIs, System.Text.Json (polymorphic `JsonDerivedType`), Swashbuckle (schema and document filters), `TimeProvider` / `FakeTimeProvider`, xUnit + FluentAssertions
**Storage**: JSON files under the service data root: new folder `queue-sequence-stats/`, one `<queueId>.json` for each queue (research R-003)
**Testing**: `dotnet test GameBot.sln` (unit, integration, contract)
**Target Platform**: Windows service host, localhost REST API on port 8080
**Project Type**: Web service with a shared domain library (`GameBot.Domain`) and a service host (`GameBot.Service`)
**Performance Goals**:
- Record of one run: one in-memory update plus one file write of the queue document. The p95 is below 50 ms for a queue with 50 sequences at 100 records each (about 500 KB). A queue starts a sequence at most a few times each minute, so this is not a hot path. Task T050 measures this goal.
- `lastRun` evaluation: in-memory scan of at most 100 records; no disk access after the first read of the queue file; below 1 ms. Task T050 measures this goal.
- Queue read: one in-memory copy of the queue entries; no change to the p95 of `GET /api/queues/{id}` that a client can see.
**Constraints**:
- No new parameter through the private methods of `SequenceRunner` (analyzer cost on large methods; research R-005).
- A write failure of the store never makes a sequence run of the queue fail, and never makes the queue fail.
- A damaged statistics file never makes a queue start or a queue read fail (spec edge case).
- No web UI change (spec Assumptions).
- FR-014: no change to `reschedule-self` or template schedule behavior.
**Scale/Scope**: About 8 new source files, about 12 changed source files, about 12 new test files, `docs/architecture.md`, `CHANGELOG.md`, `specs/STATUS.md`.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

*NON-NEGOTIABLE*: If `build` or required `test` runs fail (local or CI), implementation progression is blocked. This block stays until the failures are fixed or a documented maintainer waiver exists.

*NON-NEGOTIABLE*: All text in this plan and in the artifacts it produces MUST obey Simplified Technical English (Constitution Principle VI). These artifacts are research, data model, contracts, quickstart, tasks, code comments and messages for users.

| Principle | Assessment |
|---|---|
| **I. Code Quality Discipline** | PASS. Each new rule has one home. `LastRunConditionRules` holds the field rules and parsers. `LastRunWindow` holds the window math. `LastRunConditionEvaluator` holds the lookup. `FileSequenceRunStatisticsStore` holds the persistence. The validator change is one case in `Walk` and one gate change, not six copies. `RunOneSequenceAsync` gets one call to a small private helper. `SequenceRunner` gets one argument at two call sites. New public members get XML docs. No new package. Method names in CamelCase only. |
| **II. Testing Standards** | PASS. Unit tests cover the window math (normal day, before and after the `since` time, exact minute, spring-forward gap, fall-back hour). Unit tests also cover the `within` parser (`24:00:00`, `1.00:00:00`, zero, negative, over 366 days, bad text). Unit tests also cover the rules and the validator messages at the root and in composites. Unit tests also cover the evaluator (true, false, no context, `self`, other ID, negate). Unit tests also cover the store (record, cap at 100, counters, restart = new instance on the same folder, damaged file, delete). Unit tests also cover the queue classification (success, Break success, failure, fault, stop, sequence time limit, host shutdown not recorded). One integration test runs a queue with a `lastRun` guard across a queue restart. Contract tests cover the 400 messages, `dryRun`, round trip, `sequenceStats` shape and OpenAPI schemas. All tests use `FakeTimeProvider` and a temp data folder or a new queue ID. Thus the tests are deterministic and isolated. |
| **III. UX Consistency** | PASS. Error messages use the `Step '<label>' condition at <path>:` form of the other condition errors. They name the field and state the permitted values. The new API field is additive. No current field changes. The status words are the same as the execution-log words (`success`, `failure`). |
| **IV. Performance** | PASS. Goals are in Technical Context. The store keeps an in-memory copy, so the condition and the queue read do not read the disk. The per-step path of the runner gets only one null check for a non-`lastRun` condition. |
| **V. Living Documentation** | PASS. `docs/architecture.md` gets the `lastRun` condition (domain model and capability), `sequenceStats` (API surface) and `queue-sequence-stats/` (persistence layout). It also gets a new "Last reviewed" date. `spec.md` Status goes to Implemented at the end, and `specs/STATUS.md` gets row 105. `CHANGELOG.md` gets an `Added` entry, with the web UI limit. No earlier spec is superseded. |
| **VI. Simplified Technical English** | PASS. This plan, research, data model, contracts and quickstart use STE. Tasks, code comments, API descriptions and error messages MUST also use STE. |

No violations. **Complexity Tracking** is not necessary.

**Post-Phase-1 re-check**: unchanged. The design adds one ambient context type. This is the third use of a current pattern (device context, time-limit scope), not a new pattern.

## Project Structure

### Documentation (this feature)

```text
specs/105-sequence-run-statistics/
├── spec.md
├── plan.md                         # this file
├── research.md                     # Phase 0
├── data-model.md                   # Phase 1
├── quickstart.md                   # Phase 1
├── contracts/
│   ├── lastrun-condition.md        # Phase 1
│   └── queue-sequence-stats.md     # Phase 1
├── checklists/
└── tasks.md                        # Phase 2 (/speckit-tasks)
```

### Source Code (repository root)

```text
src/GameBot.Domain/
├── Queues/
│   ├── SequenceRunStatus.cs                      # NEW enum success|failure|cancelled
│   ├── SequenceRunStatistics.cs                  # NEW SequenceRunRecord + SequenceRunStatistics (Apply, HasRunInWindow)
│   ├── ISequenceRunStatisticsStore.cs            # NEW
│   └── FileSequenceRunStatisticsStore.cs         # NEW queue-sequence-stats/<queueId>.json, cache, atomic write
├── Commands/
│   ├── SequenceStepCondition.cs                  # + LastRunStepCondition, + JsonDerivedType "lastRun"
│   ├── LastRunConditionRules.cs                  # NEW field rules, TryParseSince, TryParseWithin
│   └── FileSequenceRepository.cs                 # GuardConditionLeaves: + lastRun case
└── Services/
    ├── SequenceRunContext.cs                     # NEW AsyncLocal ambient context
    ├── LastRunWindow.cs                          # NEW since-window math (DST)
    ├── LastRunConditionEvaluator.cs              # NEW store + TimeProvider lookup
    ├── SequenceStepConditionEvaluator.cs         # + lastRunEvaluator param, + leaf case, + Describe case
    ├── CompositeConditionValidator.cs            # + Walk case, root gate walks a lastRun leaf
    └── SequenceRunner.cs                         # pass SequenceRunContext.Current?.LastRunEvaluator at 2 sites; DescribeBreakCondition case

src/GameBot.Service/
├── GameBotServiceSetup.cs                        # register store + evaluator; 2 schema filters
├── Models/SequenceStepContracts.cs               # + LastRunConditionContract, + JsonDerivedType
├── Endpoints/SequencesEndpoints.cs               # MapPerStepCondition / MapPerStepConditionToDto: + lastRun
├── Endpoints/QueuesEndpoints.cs                  # BuildDetailAsync fills sequenceStats; DELETE deletes stats
├── Contracts/Queues/QueueDetailResponse.cs       # + SequenceStats
├── Contracts/Queues/QueueSequenceStatsResponse.cs  # NEW
├── Services/SequenceExecution/SequenceExecutionService.cs  # push SequenceRunContext for queue runs
├── Services/QueueExecution/QueueExecutionService.cs        # record each run in RunOneSequenceAsync
└── Swagger/
    ├── ConditionalFlowSchemaDocumentFilter.cs    # + GenerateSchema + alias LastRunCondition
    ├── LastRunConditionSchemaFilter.cs           # NEW
    ├── QueueSequenceStatsSchemaFilter.cs         # NEW
    └── SwaggerConfig.cs                          # GET queue description + example

tests/
├── unit/Queues/SequenceRunStatisticsTests.cs               # NEW Apply, cap, counters, window check
├── unit/Queues/FileSequenceRunStatisticsStoreTests.cs      # NEW persistence, reload, damaged file, delete
├── unit/Queues/QueueExecutionServiceRunStatisticsTests.cs  # NEW classification table (research R-002), queue restart
├── unit/Sequences/LastRunWindowTests.cs                    # NEW since math with DST
├── unit/Sequences/LastRunConditionRulesTests.cs            # NEW parsers + messages
├── unit/Sequences/LastRunConditionEvaluatorTests.cs        # NEW evaluator: spec scenarios 1 to 6, self, other ID, unknown ID
├── unit/Sequences/CompositeConditionEvaluatorTests.cs      # extend: lastRun leaf, no delegate, negate, all/any/none, Describe
├── unit/Sequences/CompositeConditionRunnerTests.cs         # extend: context read in the four condition places, ad-hoc false
├── unit/Sequences/SequenceRunContextTests.cs               # NEW context push for queue, ad-hoc, dry-run and simulated nested runs
├── unit/Sequences/CompositeConditionValidationTests.cs     # extend: lastRun at root and in composites, all six slots
├── unit/Sequences/CompositeConditionSerializationTests.cs  # extend: lastRun round trip
├── integration/Queues/QueueLastRunGuardRestartTests.cs     # NEW since guard across a queue restart (SC-002)
├── contract/Sequences/LastRunConditionContractTests.cs     # NEW 400s, dryRun, round trip
├── contract/Sequences/LastRunConditionOpenApiTests.cs      # NEW schema, discriminator, enum, patterns
├── contract/Queues/QueueSequenceStatsContractTests.cs      # NEW sequenceStats {} on new queue, delete removes file
└── contract/Queues/QueueSequenceStatsOpenApiTests.cs       # NEW schema + descriptions

docs/architecture.md, CHANGELOG.md, specs/STATUS.md
```

**Structure Decision**: No new project. Domain types are next to their neighbors:

- the store in `Domain/Queues` (next to `FileQueueRunStateStore`),
- the condition in `Domain/Commands` (next to the other conditions),
- the evaluator parts in `Domain/Services` (next to `SequenceStepConditionEvaluator`).

## Implementation Approach

### A. Statistics model and store (FR-001 to FR-004)

1. Add `SequenceRunStatus`, `SequenceRunRecord` and `SequenceRunStatistics` (data model sections 1 to 3). `Apply` keeps the cap of 100 and the counters. `HasRunInWindow` checks `from <= EndedAt <= to`.
2. Add `ISequenceRunStatisticsStore` and `FileSequenceRunStatisticsStore` (data model sections 4 and 5, research R-003, R-004).
   - Serialize the enum as lower-case text (`JsonStringEnumConverter` with a camel-case policy).
   - Make sure that `queueId` is a safe file name. Reject path separators and `..`.
   - Queue IDs come from the queue repository. Thus a bad ID is a defect in the code, and the store throws `ArgumentException`.
3. Register the store as a singleton in `GameBotServiceSetup.RegisterRepositories` with `storageRoot`.

### B. Record each run in the queue (FR-001, FR-006)

In `QueueExecutionService.RunOneSequenceAsync`:

1. Take `startedAt = _timeProvider.GetLocalNow()` just before `_sequenceExecution.ExecuteAsync`.
2. After the call, and in each `catch`, compute the status with the table in research R-002. Then call a new private helper `RecordRunAsync(queueId, sequenceId, startedAt, status)`. The helper does these steps:
   - It takes `endedAt` from `_timeProvider`.
   - It calls the store with `CancellationToken.None`.
   - It logs a Warning on each exception (new `QueueExecutionLog` message, next free EventId).
3. Do not record when the host is in shutdown (`_appStopping.IsCancellationRequested` is true). This rule applies to each end: a `Succeeded` or `Failed` result, a stop and an exception.
4. Keep the return values and the rethrow of the stop exception as they are.
5. Record a run only when the call to `ExecuteAsync` started. A fault in the foreground guard or in the root rotation before that point is not a sequence run.

The store is an optional constructor dependency of `QueueExecutionService`. It is null in the current test harnesses, so the current tests do not change.

### C. The `lastRun` condition model and validation (FR-007, FR-009 to FR-011)

1. Add `LastRunStepCondition` with `[JsonDerivedType(typeof(LastRunStepCondition), "lastRun")]` on `SequenceStepCondition`. Also add the `[JsonIgnore]` override of `Type` (see the comment in that file).
2. Add `LastRunConditionRules` (research R-008, R-009, R-011).
3. `CompositeConditionValidator`: add the `LastRunStepCondition` case in `Walk`. Change the root gate to `condition is not CompositeStepCondition and not LastRunStepCondition && !validateLeafAtRoot`. Keep `Walk` small: the case calls one helper.
4. `FileSequenceRepository.GuardConditionLeaves`: add the case, and throw on the first rule error.
5. Service: add `LastRunConditionContract` and its `JsonDerivedType`. The contract has `string?` fields, so an absent field gives a validation message and not a JSON error. Map it in `MapPerStepCondition` and `MapPerStepConditionToDto`.

### D. Evaluation (FR-008, FR-009, FR-012)

1. Add `SequenceRunContext` (data model section 8), `LastRunWindow` (research R-007) and `LastRunConditionEvaluator` (data model section 9). Register the evaluator as a singleton.
2. `SequenceStepConditionEvaluator.EvaluateAsync`:
   - Add the optional parameter `Func<LastRunStepCondition, CancellationToken, Task<bool>>? lastRunEvaluator = null`, and pass it down the private walk.
   - The leaf case returns `false` when the delegate is null. Otherwise, it awaits the delegate.
   - `Negate` stays in `EvaluateNodeAsync`.
   - Add the `Describe` case (contract "Execution log").
3. `SequenceRunner`: at the two calls of `SequenceStepConditionEvaluator.EvaluateAsync`, pass `SequenceRunContext.Current?.LastRunEvaluator`. In `DescribeBreakCondition`, use `SequenceStepConditionEvaluator.Describe` for a `LastRunStepCondition`.
4. `SequenceExecutionService.ExecuteCoreAsync`: push the context after the device scope, when `originatingQueueId` is not empty and `dryRun` is false. Use this code: `using var runContext = SequenceRunContext.Push(new SequenceRunContext(queueId, sequenceId, (c, t) => _lastRunEvaluator.EvaluateAsync(queueId, sequenceId, c, t)))`. `_lastRunEvaluator` is an optional constructor dependency. When it is null, no context is pushed and `lastRun` is false.

### E. API (FR-005)

1. Add `QueueSequenceStatsResponse` and `QueueDetailResponse.SequenceStats` (data model section 11).
2. `QueuesEndpoints.BuildDetailAsync`: get `ISequenceRunStatisticsStore` as a new parameter. Add it to the handler signatures of the four callers. Fill the map, with `sequenceName` from the `namesById` lookup that the method already builds.
3. `DELETE /api/queues/{id}`: after `repo.DeleteAsync`, call `stats.DeleteQueueAsync(id)`.

### F. OpenAPI (FR-013)

As in research R-015:

- `ConditionalFlowSchemaDocumentFilter`: generate the schema and add the alias `LastRunCondition`.
- New `LastRunConditionSchemaFilter`.
- New `QueueSequenceStatsSchemaFilter`.
- `SwaggerConfig`: GET-queue description and example.
- Registration in `GameBotServiceSetup`.

### G. Documentation

- `docs/architecture.md`: condition list, queue read fields, persistence layout, "Last reviewed" date.
- `CHANGELOG.md`: `Added`, with the web UI limit from research R-016.
- `specs/STATUS.md`: row 105.
- Spec Status at the end.

## Test Plan

| Area | Test | Requirement |
|---|---|---|
| Store | record then read; 101 records keep 100 and the counters keep 101; `lastSuccessAt` does not change on a failure; a new store instance on the same folder reads the same values; a damaged file gives `{}` and a warning, and the next record heals it; delete removes the file | FR-001 to FR-004, edge cases |
| Queue record | Succeeded → success; Break end → success; Failed → failure; exception → failure; stop → cancelled; failure-policy stop → cancelled; sequence time limit (watchdog) → cancelled; Failed after the watchdog timer fired → cancelled; host shutdown → no record; store throws → result of the sequence run unchanged | FR-006, edge cases |
| Queue restart | record, stop, start (new run), read: same values; template schedule still applies | FR-004, FR-014 |
| Queue guard end to end | a queue with a `since` guard; stop and start with a new store instance; the work runs exactly one time in the window | SC-002, US2 scenario 6 |
| Window | spec scenarios 1 to 3 (14:00/12:00 true, 10:00/previous 12:00 true, 11:30/10:30 false); exact minute; spring-forward day with `since` in the gap; fall-back day with `since` in the repeated hour | FR-008 |
| Within | 23 h true and 25 h false for `24:00:00` (scenario 5); `1.00:00:00` = 24 h | FR-007, FR-008 |
| Evaluator | only failures in window → false (scenario 4); `self` resolves to own ID; other ID; unknown ID → false; no context → false and no failure; negate; in `none` | FR-008, FR-009, FR-012 |
| Runner | a guard in a step condition, an If condition, a `while` loop condition and a Break condition each read the context; an ad-hoc run gets false | FR-010, FR-012 |
| Validation | each message in the contract table, at the root of each of the six slots and in `all`/`any`/`none`; composite limits still apply; valid condition passes with `dryRun` | FR-011, SC-003 |
| Contract | POST/PUT/PATCH with bad fields → 400 (never 500); round trip keeps the text; `sequenceStats` is `{}` for a new queue; delete of a queue removes its file | FR-005, FR-011 |
| OpenAPI | `LastRunCondition` schema, discriminator value, `status` enum, `since`/`within` patterns; `QueueSequenceStatsResponse` schema and descriptions; `sequenceStats` on `QueueDetailResponse` | FR-013 |

Test isolation: unit tests for the store use a new temp folder for each test. Contract tests and integration tests create their own queue (new ID). Thus their statistics file does not touch other tests in the shared bin data folder (memory "GameBot test-harness gotchas").

## Risks and Mitigations

| Risk | Mitigation |
|---|---|
| The web UI drops a `lastRun` condition when an author saves the sequence in the UI | Out of scope by the spec. Document it in the quickstart and the CHANGELOG. Composites had the same limit. |
| `AsyncLocal` context leaks to work that a run starts and does not await | The runner awaits all steps. `Push` returns an `IDisposable` that puts back the earlier value. A test checks that the context is null after the run. |
| A large statistics file slows each sequence run of the queue | The cap of 100 limits each entry. 50 sequences give about 500 KB, with one write for each sequence run. The write is outside the watchdog budget (after the run) and uses `CancellationToken.None`. |
| Two queues record at the same time | One `SemaphoreSlim` serializes all writes. Each queue runs one sequence at a time, so the wait is short. |
| `TimeSpan.Parse` reads `24:00:00` as 24 days | Own strict parser (research R-008), with a unit test for this exact value. |
| A new `QueueExecutionService` or `SequenceExecutionService` constructor parameter breaks the test harnesses | Both new dependencies are optional (default null) parameters at the end of the constructor. |
| The `RunOneSequenceAsync` or `ExecuteCoreAsync` method grows and the analyzers slow the build | The new logic goes into small private helpers. Each method gets only a few lines. |

## Phase Status

- [x] Phase 0: research complete ([research.md](./research.md))
- [x] Phase 1: design complete ([data-model.md](./data-model.md), [contracts/](./contracts/), [quickstart.md](./quickstart.md); agent context updated in `CLAUDE.md`)
- [x] Phase 2: tasks complete ([tasks.md](./tasks.md))
