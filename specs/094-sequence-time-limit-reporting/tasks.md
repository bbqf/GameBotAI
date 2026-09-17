# Tasks: Distinct reporting of sequence time-limit cancellation

**Input**: Design documents from `specs/094-sequence-time-limit-reporting/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/api-changes.md, quickstart.md

**Tests**: Requested by the spec (issue acceptance: "Contract/unit tests cover …") and required by constitution II. Test
tasks precede the implementation they cover.

**Format**: `[ID] [P?] [Story] Description`

## Phase 1: Setup

- [ ] T001 Confirm a clean baseline build: `dotnet build C:\src\GameBot\GameBot.sln -c Debug` succeeds before any change (record pre-existing warnings, don't fix unrelated ones)

---

## Phase 2: Foundational (blocking prerequisites)

**Purpose**: Shared constants and persisted fields that every story depends on.

- [ ] T002 [P] Create `SequenceTimeLimits` static class (`DefaultWatchdogTimeoutMs = 240_000`, `MaxWatchdogTimeoutMs = 1_800_000`, `Resolve(int? overrideMs)` returning the override when > 0 else the default; XML docs) in src/GameBot.Domain/Commands/SequenceTimeLimits.cs
- [ ] T003 [P] Add nullable `CancellationReason` (string?) and `TimeLimitMs` (int?) with XML docs to `ExecutionLogEntry`, and add static class `ExecutionCancellationReasons` with `public const string SequenceTimeLimit = "sequence_time_limit"`, in src/GameBot.Domain/Logging/ExecutionLogModels.cs
- [ ] T004 Replace the private `MaxWatchdogTimeoutMs` in src/GameBot.Domain/Commands/FileSequenceRepository.cs with `SequenceTimeLimits.MaxWatchdogTimeoutMs` (validation message unchanged)
- [ ] T005 Replace the private `SequenceWatchdogTimeout` default in src/GameBot.Service/Services/QueueExecution/QueueExecutionService.cs with `TimeSpan.FromMilliseconds(SequenceTimeLimits.DefaultWatchdogTimeoutMs)` and make `ResolveWatchdogTimeoutAsync` use `SequenceTimeLimits.Resolve` (fallback behaviour on lookup failure unchanged)
- [ ] T006 [P] Unit test `SequenceTimeLimits.Resolve` (null → 240000, 0/negative → 240000, 1200000 → 1200000) in tests/unit/Sequences/SequenceTimeLimitsTests.cs

**Checkpoint**: build green; existing watchdog tests in tests/unit/Queues/QueueExecutionServiceTests.cs still pass.

---

## Phase 3: User Story 1 - Tell a timeout from a verdict in the execution log (Priority: P1) 🎯 MVP

**Goal**: A queue firing ended by its time bound is logged `failure` + `cancellationReason: sequence_time_limit` + `timeLimitMs`; ordinary failures, successes, user stops and ad-hoc runs are not.

**Independent Test**: Run tests/integration/Sequences/SequenceTimeLimitLogIntegrationTests.cs and the new queue unit tests; the time-limited entry carries the reason and bound, the negatives do not.

### Tests for User Story 1 (write first, expect failure)

- [ ] T007 [P] [US1] Unit tests for `SequenceTimeLimitScope` in tests/unit/Sequences/SequenceTimeLimitScopeTests.cs: `Current` null outside a scope; `Push` sets `Current`/`TimeLimitMs` and dispose restores the previous (nested) value; `HasElapsed` true only when the timer token is cancelled and the stop token is not; false when both are cancelled; false when only stop is cancelled
- [ ] T008 [P] [US1] Queue unit tests in tests/unit/Queues/QueueExecutionServiceTests.cs: (a) a firing with `SetWatchdog("A", 150)` whose handler awaits `Task.Delay(Infinite, ct)` observes, inside the handler after cancellation, `SequenceTimeLimitScope.Current` with `TimeLimitMs == 150` and `HasElapsed == true`, and the run still continues to "B"; (b) a handler that swallows its cancellation and returns a failed result after the bound still sees `HasElapsed == true` at return and the run continues; (c) a firing stopped via `StopAsync` while its handler is blocked sees `HasElapsed == false`; (d) `SequenceTimeLimitScope.Current` is null after the firing completes; (e) with `SequenceRepository.GetThrows` set, the handler observes `TimeLimitMs == 240000` (default fallback is what gets recorded)
- [ ] T009 [P] [US1] Integration tests in tests/integration/Sequences/SequenceTimeLimitLogIntegrationTests.cs (pattern: AbandonedSequenceLogIntegrationTests, `[Collection("ConfigIsolation")]`, `TestEnvironment.PrepareCleanDataDir()`): (a) slow targetless-wait sequence run under a pushed scope whose timer CTS cancels after ~200 ms (linked token passed to `ExecuteAsync`) → entry `failure`, `CancellationReason == "sequence_time_limit"`, `TimeLimitMs == pushed value`; (b) same but the stop CTS is cancelled instead (timer not elapsed) → `CancellationReason` null, `TimeLimitMs` null; (c) swallowed case — a sequence whose only step is a command that fails WITHOUT throwing (non-dispatching command: a PrimitiveTap on a nonexistent reference image, created via `POST /api/commands` as in `NestedRequireDispatchIntegrationTests.CreateNonDispatchingCommandAsync`) run with a not-cancelled token under a scope whose timer is already cancelled → `failure` + reason, and the summary is the normal-end summary (proves the normal finalize path stamps); (d) the same failing sequence under a scope whose timer has NOT fired → no reason; plus (h) the child command entries of (c) carry no reason (FR-004b); (e) a succeeding sequence (`WaitForImage TimeoutMs = 0`) under an elapsed scope → `success`, no reason; (f) ad-hoc run without any scope that fails → no reason; (g) the entry from (a) read through `GET /api/execution-logs?objectType=sequence` (auth header `Bearer test-token`) exposes `cancellationReason` and `timeLimitMs` in `items[]`, through `GET /api/execution-logs/{id}` at top level, and through `GET /api/execution-logs/{id}/subtree` on the root node
- [ ] T010 [P] [US1] Backward-compat unit test in tests/unit/ExecutionLogs/ExecutionLogEntryCompatTests.cs: deserializing a stored entry JSON that lacks `cancellationReason`/`timeLimitMs` (using the same serializer options as src/GameBot.Domain/Logging/FileExecutionLogRepository.cs) yields nulls, and a round-trip preserves set values

### Implementation for User Story 1

- [ ] T011 [US1] Create `SequenceTimeLimitScope` (internal sealed; `AsyncLocal` ambient; `static Current`; `static IDisposable Push(int timeLimitMs, CancellationToken timerToken, CancellationToken stopToken)` restoring the previous value on dispose; `TimeLimitMs`; `HasElapsed`; XML docs referencing feature 094) in src/GameBot.Service/Services/SequenceExecution/SequenceTimeLimitScope.cs
- [ ] T012 [US1] In src/GameBot.Service/Services/QueueExecution/QueueExecutionService.cs `RunOneSequenceAsync`: create a timer-only `CancellationTokenSource` with `CancelAfter(watchdogTimeout)`, link it with `ct` for the firing token (replacing the single linked+CancelAfter CTS), push `SequenceTimeLimitScope.Push((int)watchdogTimeout.TotalMilliseconds, timer.Token, ct)` for the duration of the firing (dispose in `finally`), and keep the existing catch filters/log line semantics (watchdog catch now keyed on the timer)
- [ ] T013 [US1] Add `CancellationReason` (string?) and `TimeLimitMs` (int?) init properties with XML docs to src/GameBot.Service/Services/ExecutionLog/ExecutionLogContext.cs, and copy them onto the entry in `LogSequenceFinalizeAsync` in src/GameBot.Service/Services/ExecutionLog/ExecutionLogService.cs
- [ ] T014 [US1] In src/GameBot.Service/Services/SequenceExecution/SequenceExecutionService.cs add a private helper that returns `(reason, limitMs)` when `SequenceTimeLimitScope.Current?.HasElapsed == true` and the status normalizes to non-success; apply it at the normal finalize (end of `ExecuteCoreAsync`) and in `FinalizeAbandonedAsync`; when stamped in the abandon path, word the summary as "exceeded its time limit of N ms" instead of the generic cancelled text
- [ ] T015 [US1] Expose the fields through the API: add `CancellationReason`/`TimeLimitMs` to `ExecutionLogEntryDto` and `ExecutionTreeNodeDto` in src/GameBot.Service/Models/ExecutionLogs.cs; add them (init props) to `ExecutionTreeNodeProjection` and populate them from the entry for execution-backed nodes in src/GameBot.Service/Services/ExecutionLog/ExecutionLogService.cs; map them in `ToDto`, `ToTreeNodeDto`, and the anonymous object in `ToDetailResponse` in src/GameBot.Service/Endpoints/ExecutionLogsEndpoints.cs
- [ ] T016 [US1] Run T007–T010 plus the whole tests/unit/Queues and tests/integration/Sequences + tests/integration/ExecutionLogs suites; all green (existing `ShortOverrideCancelsAnOverrunningFiringWithoutStoppingTheRun` and `CancelledRunClosesItsEntryInsteadOfLeavingItRunning` must still pass)

**Checkpoint**: US1 independently delivers the diagnostic value.

---

## Phase 4: User Story 2 - Read the effective time bound of a sequence (Priority: P2)

**Goal**: `GET /api/sequences/{id}` returns `effectiveWatchdogTimeoutMs`.

**Independent Test**: tests/integration/Sequences/SequenceWatchdogTimeoutApiIntegrationTests.cs new cases pass.

### Tests for User Story 2

- [ ] T017 [US2] Add tests to tests/integration/Sequences/SequenceWatchdogTimeoutApiIntegrationTests.cs: (a) a sequence created without an override reads `watchdogTimeoutMs: null` and `effectiveWatchdogTimeoutMs: 240000`; (b) with `watchdogTimeoutMs: 1200000` both read 1200000; (c) a PUT/PATCH body carrying `effectiveWatchdogTimeoutMs: 999` and no `watchdogTimeoutMs` leaves the stored override unchanged (reads back null / previous value)

### Implementation for User Story 2

- [ ] T018 [US2] Add `effectiveWatchdogTimeoutMs = SequenceTimeLimits.Resolve(sequence.WatchdogTimeoutMs)` next to `watchdogTimeoutMs` in all three response shapes of the sequence projection in src/GameBot.Service/Endpoints/SequencesEndpoints.cs; confirm no write path reads the property
- [ ] T019 [US2] Run T017 and the existing tests in tests/integration/Sequences and tests/contract/Sequences; all green

**Checkpoint**: US2 independently testable.

---

## Phase 5: User Story 3 - Discover the bound and the new fields from the API document (Priority: P3)

**Goal**: OpenAPI document describes the override (default/min/max), the effective read-out, and the log fields.

**Independent Test**: tests/contract/Sequences/SequenceTimeLimitOpenApiTests.cs passes.

### Tests for User Story 3

- [ ] T020 [US3] Create tests/contract/Sequences/SequenceTimeLimitOpenApiTests.cs (pattern: SequenceWritesOpenApiTests) asserting: `components.schemas.SequenceUpsertRequest.properties.watchdogTimeoutMs` has `minimum` 1, `maximum` 1800000 and a description containing "240000"; same for `SequencePatchContract`; the `/api/sequences/{sequenceId}` `get` operation description contains `effectiveWatchdogTimeoutMs` and "240000"; `components.schemas.ExecutionLogEntryDto` and `ExecutionTreeNodeDto` have `cancellationReason` (description contains `sequence_time_limit`) and `timeLimitMs`

### Implementation for User Story 3

- [ ] T021 [US3] Add `[Range(1, SequenceTimeLimits.MaxWatchdogTimeoutMs)]` to `WatchdogTimeoutMs` on `SequenceUpsertContract` and `SequencePatchContract` and update their XML summaries to state the 240000 ms default and 1800000 ms maximum in src/GameBot.Service/Models/SequenceStepContracts.cs
- [ ] T022 [US3] Create `SequenceTimeLimitSchemaFilter` (ISchemaFilter) setting property descriptions: `watchdogTimeoutMs` on SequenceUpsertContract/SequencePatchContract (queue-firing time bound in ms; default 240000 when absent; max 1800000; exceeding it ends the firing with cancellationReason sequence_time_limit), and `cancellationReason` (null or `sequence_time_limit`) / `timeLimitMs` on ExecutionLogEntryDto/ExecutionTreeNodeDto, in src/GameBot.Service/Swagger/SequenceTimeLimitSchemaFilter.cs; register it next to the document filter in src/GameBot.Service/GameBotServiceSetup.cs
- [ ] T023 [US3] Register `ExecutionLogEntryDto` and `ExecutionTreeNodeDto` via `GenerateSchema` in src/GameBot.Service/Swagger/ConditionalFlowSchemaDocumentFilter.cs, and add a `GetSequence` operation description (effectiveWatchdogTimeoutMs = override when set, else 240000; watchdogTimeoutMs = stored override) in the operation filter in src/GameBot.Service/Swagger/SwaggerConfig.cs
- [ ] T024 [US3] Run T020 and all tests/contract OpenAPI tests (OpenApiContractTests, OpenApiBackwardCompatTests, SwaggerDocsTests); all green

---

## Phase 6: Polish & Cross-Cutting Concerns

- [ ] T025 [P] Update docs/architecture.md: Execution Log bullet (cancellationReason/timeLimitMs, status vocabulary unchanged), queue watchdog text (per-sequence `watchdogTimeoutMs`, default 240000, max 1800000, `effectiveWatchdogTimeoutMs` on GET), refresh "Last reviewed" to 2026-09-17 (feature 094)
- [ ] T026 [P] Set `**Status**: Implemented` in specs/094-sequence-time-limit-reporting/spec.md and add the matching row to specs/STATUS.md
- [ ] T026a [P] Add a user-visible entry to CHANGELOG.md (execution-log `cancellationReason`/`timeLimitMs`, `effectiveWatchdogTimeoutMs` on sequence GET, documented `watchdogTimeoutMs` bounds; refs #182), following the file's existing format
- [ ] T027 Full gate: `dotnet build C:\src\GameBot\GameBot.sln -c Release` with no new warnings, then `dotnet test` for tests/unit, tests/integration and tests/contract; all green
- [ ] T028 Walk quickstart.md automated section to confirm the filters resolve the new tests

---

## Dependencies & Execution Order

- Phase 1 → Phase 2 → (US1, US2, US3). T004/T005 depend on T002. US1 depends on T003 and T005.
- US2 depends only on T002. US3 depends on T015 (DTO fields) for the log schema assertions and on T002 for `[Range]`.
- Within US1: T007–T010 before T011–T015; T011 before T012 and T014; T013 before T014; T015 after T013.
- Polish after all stories.

## Parallel Opportunities

- T002, T003, T006 in parallel (different files).
- T007, T008, T009, T010 in parallel (different test files).
- US2 (T017–T019) can run alongside US1 implementation once T002 is done.
- T025, T026, T026a in parallel.

## Implementation Strategy

1. MVP = Phases 1–3 (US1): the diagnostic gap from the 2026-09-14 outage is closed.
2. Add US2 (read-out), then US3 (documentation).
3. Polish: living docs + full gate before commit.
