# Tasks: Truthful tap outcome and an execution log for a single step

**Input**: Design documents from `specs/112-truthful-tap-outcome-log/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/steps-execute.md, quickstart.md

**Tests**: The spec and the constitution require tests. The issue asks for named unit tests and an integration test. Test tasks come before the implementation tasks in each story.

**Organization**: Tasks are in groups by user story. Each story can be tested alone.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependency on an incomplete task)
- **[Story]**: The user story of the task (US1, US2, US3)

---

## Phase 1: Setup

**Purpose**: Get a known baseline.

- [ ] T001 Build the solution with `dotnet build GameBot.sln -c Release -warnaserror` from the repo root, and record the baseline result. Record also which tests fail on the local platform before any change (for example, `tests/unit/Commands/CommandExecutorPrimitiveTapTests.cs` needs Windows for `System.Drawing`), so that later runs can separate environment failures from real failures.

---

## Phase 2: Foundational (blocking prerequisites)

**Purpose**: The log record type and the new log method exist. US2 needs them. US1 does not need them.

- [ ] T002 Create the record `StepExecutionLogRecord(string SessionId, string StepType, PrimitiveTapStepOutcome Outcome, int Accepted, DateTimeOffset StartedAtUtc, long DurationMs)` in the new file `src/GameBot.Service/Services/ExecutionLog/StepExecutionLogRecord.cs`, with an STE XML comment.
- [ ] T003 Add `Task LogStepExecutionAsync(StepExecutionLogRecord record, CancellationToken ct = default);` to `IExecutionLogService` in `src/GameBot.Service/Services/ExecutionLog/ExecutionLogService.cs`. Implement it in `ExecutionLogService` as data-model.md, section "ExecutionLogEntry for a single step call", describes: execution type `step`, object ref `("step", SessionId, "<StepType> step")`, final status `success` only for `executed`, one `ExecutionStepOutcome` (step type in camel case; outcome `executed`, `dispatch_unknown`, `timeout` or `not_executed`; reason code = status; reason text = reason), one detail item of kind `step` with the attributes `sessionId`, `stepType`, `status`, `reason`, `resolvedX`, `resolvedY`, `executedX`, `executedY`, `detectionConfidence`, `accepted`, `startedAtUtc`, `durationMs`, a root hierarchy, the retention of the other entries, and the summary `Step '<StepType>' on session '<SessionId>' ended with <status>.` (plus ` Reason: <reason>.` when a reason exists).
- [ ] T004 [P] Implement `LogStepExecutionAsync` in the test doubles that implement `IExecutionLogService`: `tests/unit/Queues/RecordingExecutionLog.cs` and `tests/unit/Queues/QueueMonitorServiceTests.cs`. Use `Task.CompletedTask`, or record the call where the double records other calls.

**Checkpoint**: The solution builds. No behavior changes.

---

## Phase 3: User Story 1 - A "not executed" tap outcome is true (Priority: P1) MVP

**Goal**: After a dispatch, the outcome shows that the input was sent. Without a dispatch, the outcome is "not executed". During a failed dispatch, the outcome is `dispatch_unknown`.

**Independent Test**: The new tests in `CommandExecutorPrimitiveTapTests` pass on Windows.

### Tests for User Story 1

- [ ] T005 [US1] In `tests/unit/Commands/CommandExecutorPrimitiveTapTests.cs`, add a session stub `ThrowingReadBackSessionManagerStub` that returns 1 from `SendInputsAsync` and sets `x1` in the dispatched args to a value whose `IConvertible.ToInt32` throws a given exception (research R-002). Add the test `PrimitiveTapErrorAfterDispatchReportsExecutedWithAcceptedCount`: with an `InvalidOperationException`, the outcome has the status `executed`, the reason `executed_then_error`, `Accepted` = 1, the resolved point (0,0), and the executed point null.
- [ ] T006 [US1] In the same file, add `PrimitiveTapCancellationAfterDispatchReportsExecutedWithAcceptedCount`: the stub throws `OperationCanceledException`. The outcome has the status `executed`, the reason `executed_then_cancelled`, `Accepted` = 1 and the resolved point (0,0). The status is not `cancelled` or `skipped_detection_failed`.
- [ ] T007 [US1] In the same file, add a session stub that throws from `SendInputsAsync`, and the tests `PrimitiveTapErrorDuringDispatchReportsDispatchUnknown` (`InvalidOperationException` gives `dispatch_unknown` / `dispatch_error`, `Accepted` = 0, the resolved point) and `PrimitiveTapCancellationDuringDispatchReportsDispatchUnknown` (`OperationCanceledException` gives `dispatch_unknown` / `dispatch_cancelled`).
- [ ] T008 [US1] In the same file, add a session stub that counts the calls to `SendInputsAsync`, and the test `PrimitiveTapDetectionFailureOnEveryAttemptSendsNoInput`: with a matcher that never matches and `TapRetryCount = 3`, the outcome is `skipped_detection_failed` / `detection_failed_after_3_retries`, `Accepted` = 0, and the call count is 0.

### Implementation for User Story 1

- [ ] T009 [US1] In `src/GameBot.Service/Services/CommandExecutor.cs`, add a private sealed class `TapDispatchState` with the fields of data-model.md. Change `TryDetectAndTap` to take a `TapDispatchState` in place of `ref int totalAccepted`. Read the detection confidence before the dispatch. Set `ResolvedPoint`, `DetectionConfidence` and `HoldMs` and then `Started = true` immediately before `SendInputsAsync`. Set `Accepted` and `Completed = true` immediately after it returns. Set `ExecutedPoint` after the read-back. Keep the outcome of a tap without an error the same as before.
- [ ] T010 [US1] In `ExecuteOneStepAsync` of the same file, make one `TapDispatchState` for each attempt, and use its `Accepted` count for a successful tap. In the `catch (OperationCanceledException)` block and the `catch (Exception)` block, return the outcome from the state: `Completed` gives `executed` with `executed_then_cancelled` or `executed_then_error` and the state's accepted count and points; `Started` without `Completed` gives `dispatch_unknown` with `dispatch_cancelled` or `dispatch_error`, `accepted` 0 and the resolved point; otherwise the current outcomes. Keep the current service-log messages, and add a `LoggerMessage` for a problem after or during a dispatch. Write STE comments.

**Checkpoint**: T005 to T008 pass on Windows. The existing tap tests pass.

---

## Phase 4: User Story 2 - A single step call writes an execution-log entry (Priority: P1)

**Goal**: Each `POST /api/steps/execute` call that passes the session check writes exactly one `step` entry, also for a timeout.

**Independent Test**: `CommandExecutorStepLogTests` and `StepExecutionLogIntegrationTests` pass.

### Tests for User Story 2

- [ ] T011 [P] [US2] Create `tests/unit/Commands/CommandExecutorStepLogTests.cs` with an `IExecutionLogService` double that records each call. Tests: `ForceExecuteStepWritesOneEntryWithTheOutcome` (a `KeyInput` step on `sess-1` writes one record with session id `sess-1`, step type `KeyInput`, status `executed`, `Accepted` = 1, a start time and a duration); `ForceExecuteStepTimeoutWritesOneTimeoutEntryAndThrowsTimeoutException` (a `WaitForImage` step with no image and `TimeoutMs = 5000`, a timeout of 100 ms); `ForceExecuteStepCallerCancellationWritesOneCancelledEntry`; `ForceExecuteStepErrorWritesOneFailedEntryAndRethrows` (a `Command` step type throws `InvalidOperationException`); `ForceExecuteStepLogWriteFailureDoesNotChangeTheResult` (the double throws on write); `ForceExecuteStepSessionErrorWritesNoEntry` (an unknown session id gives `KeyNotFoundException` and no record).
- [ ] T012 [P] [US2] Create `tests/integration/Commands/StepExecutionLogIntegrationTests.cs` (collection `ConfigIsolation`, `GAMEBOT_USE_ADB=false`, the same setup as `StepsEndpointsTests`). Create a game and a session. Test `ExecuteStepWritesAnExecutionLogEntryVisibleWithTheTimeFilter`: send a `KeyInput` step, then read `GET /api/execution-logs?fromUtc=<before>&toUtc=<after>&objectType=step&objectId=<sessionId>`; expect one item with `executionType` `step`, and read `GET /api/execution-logs/{id}` for the step outcome and the detail attributes `sessionId`, `stepType`, `status` and `accepted`. Test `ExecuteStepTimeoutWritesAnExecutionLogEntry`: send a `WaitForImage` step with no image and `timeoutMs: 12000`; expect 200 with `status: "timeout"`, and one entry whose step outcome is `timeout`. Test `ExecuteStepWithoutSessionWritesNoEntry`: with no session, expect 400 and no `step` entry.

### Implementation for User Story 2

- [ ] T013 [US2] Add the overload `Task<CommandForceExecutionResult> ForceExecuteStepAsync(string? sessionId, CommandStep step, TimeSpan? timeout, CancellationToken ct = default);` to `src/GameBot.Service/Services/ICommandExecutor.cs`, with an STE XML comment that tells the log and the `TimeoutException`.
- [ ] T014 [US2] In `src/GameBot.Service/Services/CommandExecutor.cs`, implement the overload as research R-003 describes. The old overload calls it with `timeout: null`. After the session check: make the timeout and linked token sources, record the start time, run `ExecuteOneStepAsync`, and write exactly one record through a private `WriteStepLogAsync` in all four cases (outcome, timeout, caller cancellation, other error). Use `CancellationToken.None` for the write. Catch a write exception and write a warning with a new `LoggerMessage`. On a timeout, throw `TimeoutException("step_execution_timeout")`. Rethrow the other exceptions. Use step type names from `CommandStepType`.
- [ ] T015 [US2] In `src/GameBot.Service/Endpoints/StepsEndpoints.cs`, remove the endpoint timeout token sources. Call `exec.ForceExecuteStepAsync(req.SessionId, domainStep, TimeSpan.FromSeconds(10), requestCt)`. Replace the `OperationCanceledException when timeoutCts.IsCancellationRequested` catch with a `TimeoutException` catch that returns the same timeout body as before. Do not change the other response bodies (FR-012).

**Checkpoint**: T011 and T012 pass. `StepsEndpointsTests` passes.

---

## Phase 5: User Story 3 - The API documentation states the contract (Priority: P2)

**Goal**: The Swagger text and the docs state the contract.

**Independent Test**: The Swagger document of `POST /api/steps/execute` has the description.

- [ ] T016 [US3] In `ApplyStepExamples` of `src/GameBot.Service/Swagger/SwaggerConfig.cs`, set `operation.Description` for `POST /api/steps/execute` in STE: a "not executed" status (`skipped_*` or `cancelled`) means that the service sent no input; `executed` with `executed_then_error` or `executed_then_cancelled` means that the input was sent and a problem occurred after that; `dispatch_unknown` means that the service cannot know; each call that passes the session check writes one execution-log entry of the type `step` (object id = session id), also for a timeout.
- [ ] T017 [P] [US3] Create the contract test `tests/contract/StepsExecuteOpenApiTests.cs` (same form as `tests/contract/PrimitiveTapHoldOpenApiTests.cs`) with the test `SwaggerStepsExecuteDescribesTheOutcomeContract`, that checks that the description of `POST /api/steps/execute` contains "no input" and "execution-log".

---

## Phase 6: Polish and cross-cutting concerns

- [ ] T018 [P] Update `docs/architecture.md`: add to the primitive action paragraph (after the `holdMs` text) the outcome contract and the `step` log entry of `POST /api/steps/execute`, and set the "Last reviewed" line to 2026-09-28 (feature 112, #222). Use STE.
- [ ] T019 [P] Add an entry under `## [Unreleased]` in `CHANGELOG.md` (a `### Fixed` section) for feature 112 (#222). Use STE.
- [ ] T020 [P] Add the row `| 112 | Truthful tap outcome and an execution log for a single step | Implemented |` to `specs/STATUS.md`, and set `**Status**: Implemented` in `specs/112-truthful-tap-outcome-log/spec.md`.
- [ ] T021 Build with `dotnet build GameBot.sln -c Release -warnaserror` and run `dotnet test GameBot.sln -c Release`. Compare with the T001 baseline. Fix every new failure. Record what runs only on Windows (CI).

---

## Dependencies and execution order

- Phase 1 → Phase 2 → Phases 3 and 4 → Phase 5 → Phase 6.
- US1 (Phase 3) does not need Phase 2, but T009 and T014 change the same file, so do US1 before US2.
- T011 and T012 can run in parallel (different files).
- T018, T019 and T020 can run in parallel.

## Parallel example

```text
T011 CommandExecutorStepLogTests.cs  |  T012 StepExecutionLogIntegrationTests.cs
T018 docs/architecture.md  |  T019 CHANGELOG.md  |  T020 specs/STATUS.md
```

## Implementation strategy

MVP: Phase 1, Phase 3 (US1). This closes the false "not executed" outcome. Then Phase 2 and Phase 4 (US2) add the log. Then Phase 5 and Phase 6.
