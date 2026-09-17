# Tasks: Resume Queues After a Service Restart

**Input**: Design documents from `specs/098-resume-queues-on-restart/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/queue-resume-option.md, quickstart.md

**Tests**: Required by the constitution (Principle II): tests are written first and must fail before the implementation they cover.

**Organization**: Grouped by user story. US1 and US2 share one mechanism (record on start / clear on run end / resume pass), so US2 is expressed as the negative tests and the clear-path of that mechanism.

## Format: `[ID] [P?] [Story] Description`

## Phase 1: Setup

- [X] T001 Confirm the baseline builds and the queue unit tests pass: `dotnet build C:\src\GameBot\GameBot.sln -c Debug` and `dotnet test C:\src\GameBot\tests\unit --filter FullyQualifiedName~Queues`

---

## Phase 2: Foundational (blocking prerequisites)

**Purpose**: The persisted flag and the run-state store that every story depends on.

- [X] T002 [P] Write failing store tests in `tests/unit/Queues/FileQueueRunStateStoreTests.cs`: mark adds an id idempotently; clear removes it and is a no-op for unknown ids; ids persist across a new store instance over the same data root; missing file lists empty; a 0-byte file lists empty; a corrupt (non-JSON) `queue-run-state.json` makes `ListRunningAsync` throw `InvalidDataException`, while `MarkRunningAsync`/`ClearAsync` treat it as empty and rewrite a valid file; 20 concurrent marks of distinct ids all survive
- [X] T003 [P] Add failing round-trip tests in `tests/unit/Queues/FileQueueRepositoryTests.cs`: `ResumeOnServiceStart = true` survives create/get; a queue JSON file written without the property reads `false`
- [X] T004 Add `public bool ResumeOnServiceStart { get; set; }` with an XML doc comment (opt-in, default false, read at service start, feature 098) to `src/GameBot.Domain/Queues/ExecutionQueue.cs`
- [X] T005 Create `src/GameBot.Domain/Queues/IQueueRunStateStore.cs` with `MarkRunningAsync(string queueId)`, `ClearAsync(string queueId)`, `ListRunningAsync()` and XML docs per data-model.md
- [X] T006 Create `src/GameBot.Domain/Queues/FileQueueRunStateStore.cs`: file `Path.Combine(dataRoot, "queue-run-state.json")` holding `{ "runningQueueIds": [...] }`; a `SemaphoreSlim(1,1)` around every read-modify-write; write to `<path>.tmp` then `File.Move(tmp, path, overwrite: true)`; missing or 0-byte file → empty; unparseable file (`JsonException`) → `ListRunningAsync` throws `InvalidDataException` (message names the file) while mark/clear start from empty and overwrite it; clear of an absent id does not rewrite the file; ids compared ordinally; implements `IDisposable` for the semaphore if the analyzers require it
- [X] T007 Register `IQueueRunStateStore` as a singleton `new FileQueueRunStateStore(storageRoot)` in `RegisterRepositories` in `src/GameBot.Service/GameBotServiceSetup.cs`
- [X] T008 Run T002/T003 tests green: `dotnet test C:\src\GameBot\tests\unit --filter "FullyQualifiedName~FileQueueRunStateStoreTests|FullyQualifiedName~FileQueueRepositoryTests"`

**Checkpoint**: flag persists; store works in isolation.

---

## Phase 3: User Story 1 - An opted-in queue comes back after a restart (Priority: P1) 🎯 MVP

**Goal**: Starting a queue records it; a service shutdown keeps the record; the next service start resumes opted-in recorded queues.

**Independent Test**: With the flag on, start a queue, stop the host, build a new host over the same data dir: the queue reports Running.

### Tests for User Story 1

- [X] T009 [P] [US1] Write failing engine tests in `tests/unit/Queues/QueueExecutionServiceRunStateTests.cs` (build `QueueExecutionService` the way `tests/unit/Queues/QueueExecutionServiceTests.cs` does, passing an in-memory or temp-dir `FileQueueRunStateStore` and a fake `IHostApplicationLifetime` whose `ApplicationStopping` source the test controls): (a) a successful `StartAsync` records the id before the run ends; (b) a refused start (`NotFound`, `DeviceInUse`) records nothing; (c) cancelling `ApplicationStopping` ends the run and the id is still recorded; (d) a store whose `MarkRunningAsync`/`ClearAsync` throws does not fail `StartAsync` nor leave status Running after stop
- [X] T010 [P] [US1] Write failing resume-pass tests in `tests/unit/Queues/QueueResumeOnStartupServiceTests.cs` against `internal Task ResumeAsync(IQueueRunStateStore, IQueueRepository, IQueueExecutionService, CancellationToken)` with a fake `IQueueExecutionService` recording calls: (a) recorded + flag on → `StartAsync` called once; (b) two eligible queues where the first `StartAsync` throws → the second is still started and the first record is cleared; (c) `DeviceInUse` outcome → record cleared; (d) `ListRunningAsync` throws → no starts, no exception escapes, one 7304 log; (e) using a capturing `ILogger` (a `List<(EventId, LogLevel)>`-backed test logger), each processed id produces exactly one log entry with the event id from contracts/queue-resume-option.md (7300 started/device-in-use, 7303 throw); (f) a token cancelled after the first id stops the pass and the second id's record is untouched; (g) `ExecuteAsync` does not read the store until the fake lifetime's `ApplicationStarted` token fires
- [X] T011 [P] [US1] Write a failing integration test `tests/integration/Queues/QueueResumeOnRestartTests.cs` (`[Collection("ConfigIsolation")]`, `GAMEBOT_USE_ADB=false`, `TestEnvironment.PrepareCleanDataDir()`): in the first `WebApplicationFactory<Program>` host create a cycling queue (`cycleExecution: true`, `resumeOnServiceStart: true`) linked to a one-entry template `seq-loop` (the stub-mode pattern `StartCyclingQueueAsync` in `tests/integration/Queues/QueueExecutionEndpointTests.cs` uses to hold Running), start it, wait for `Running`, dispose the host; assert `queue-run-state.json` in the data dir still lists the id; create a second host over the same data dir and poll `GET /api/queues/{id}` (up to 30 s) until `status` is `Running`; stop the queue at the end and dispose

### Implementation for User Story 1

- [X] T012 [US1] In `src/GameBot.Service/Services/QueueExecution/QueueExecutionService.cs`: add optional constructor parameter `IQueueRunStateStore? runState = null` (appended last) and field; keep a reference to the host `ApplicationStopping` token (already `_appStopping`)
- [X] T013 [US1] In `QueueExecutionService.StartAsync`, after the linked template is resolved and before `_runtime.SetStatus(... Running)`, `await` a new private `RecordRunningAsync(queueId)` that calls `_runState.MarkRunningAsync` inside try/catch and logs `QueueExecutionLog.RunStateRecordFailed` (EventId 1128, Warning) on failure
- [X] T014 [US1] In `QueueExecutionService.RunAsync` `finally`, after `_runtime.SetStatus(... Stopped)`, call a new private `ForgetRunningUnlessShuttingDownAsync(queue.Id)` that returns without touching the store when `_appStopping.IsCancellationRequested`, and otherwise calls `_runState.ClearAsync` inside try/catch logging `QueueExecutionLog.RunStateClearFailed` (EventId 1129, Warning)
- [X] T015 [US1] Create `src/GameBot.Service/Hosted/QueueResumeOnStartupService.cs` (`internal sealed partial class : BackgroundService`, constructor `IServiceProvider`, `IHostApplicationLifetime`, `ILogger<QueueResumeOnStartupService>`): `ExecuteAsync` awaits `ApplicationStarted` (TaskCompletionSource registered on the token, honouring `stoppingToken`), creates a scope, resolves `IQueueRunStateStore`, `IQueueRepository`, `IQueueExecutionService`, and calls `ResumeAsync`
- [X] T016 [US1] Implement `internal async Task ResumeAsync(...)` in the same file per data-model.md lifecycle: read ids (on exception log 7304 and return); for each id, in its own try/catch: `GetAsync` → null ⇒ clear + log 7302; `!ResumeOnServiceStart` ⇒ clear + log 7301; else `StartAsync` ⇒ log 7300 with the `QueueStartOutcome`, and clear when the outcome is `NotFound` or `DeviceInUse`; exception (not `OperationCanceledException`) ⇒ clear (best effort) + log 7303. `LoggerMessage` definitions in a nested `private static partial class Log` as in `QueueDeviceWatchdogService.cs`
- [X] T017 [US1] Register `builder.Services.AddHostedService<GameBot.Service.Hosted.QueueResumeOnStartupService>();` in `RegisterHostedServices` in `src/GameBot.Service/GameBotServiceSetup.cs`, with a one-line comment
- [X] T018 [US1] Run T009–T011 green: `dotnet test C:\src\GameBot\tests\unit --filter "FullyQualifiedName~QueueExecutionServiceRunStateTests|FullyQualifiedName~QueueResumeOnStartupServiceTests"` and `dotnet test C:\src\GameBot\tests\integration --filter FullyQualifiedName~QueueResumeOnRestartTests`

**Checkpoint**: MVP — opted-in queues resume after a restart (the flag is settable through stored JSON only until US3).

---

## Phase 4: User Story 2 - Queues that should stay stopped stay stopped (Priority: P1)

**Goal**: Operator-stopped, self-ended, never-started and non-opted-in queues are not resumed.

**Independent Test**: Stop an opted-in queue before restarting, and restart with a non-opted-in running queue: both stay Stopped.

- [X] T019 [P] [US2] Add failing engine tests to `tests/unit/Queues/QueueExecutionServiceRunStateTests.cs`: after `StopAsync` (app not stopping) the id is no longer recorded; a run that completes on its own (empty template, non-cycling) clears it; a run that fails at run level (no linked template) clears it
- [X] T020 [P] [US2] Add failing resume-pass tests to `tests/unit/Queues/QueueResumeOnStartupServiceTests.cs`: recorded + flag off → no `StartAsync`, record cleared; recorded id with no queue → no `StartAsync`, record cleared; queue with flag on but not recorded → no `StartAsync`
- [X] T021 [US2] Extend `tests/integration/Queues/QueueResumeOnRestartTests.cs` with: an opted-in queue started then stopped before the host is disposed stays `Stopped` on the second host; a cycling queue with `resumeOnServiceStart: false` started in the first host (so the shutdown keeps its record) stays `Stopped` on the second host and its id is gone from `queue-run-state.json` once the second host is up (poll up to 30 s)
- [X] T022 [US2] Fix any failing path found by T019–T021 in `QueueExecutionService.cs` / `QueueResumeOnStartupService.cs`, then run the US1 and US2 test filters green

**Checkpoint**: resume is safe — nothing the operator halted comes back.

---

## Phase 5: User Story 3 - The operator can configure and see the option (Priority: P2)

**Goal**: The flag is settable and visible through the API and the web UI.

**Independent Test**: Create with `resumeOnServiceStart: true` via API, read it back; toggle it in the UI form.

### Tests for User Story 3

- [X] T023 [P] [US3] Add failing contract tests to `tests/contract/Queues/QueuesApiContractTests.cs`: create with `resumeOnServiceStart: true` → 201 body and subsequent GET and list report `true`; PUT toggles it to `false`; create without the field reports `false`; the OpenAPI document's `CreateQueueRequest`, `UpdateQueueRequest` and `QueueResponse` schemas contain the `resumeOnServiceStart` boolean property (follow how the file already checks `pauseWhenIdle`)
- [X] T024 [P] [US3] Add a failing test to `tests/integration/Queues/QueuesDuplicateEndpointTests.cs`: duplicating a source with `resumeOnServiceStart: true` yields a copy reporting `true`
- [X] T025 [P] [US3] Add a failing test to `src/web-ui/src/components/queues/__tests__/QueueForm.test.tsx`: the "Resume after service restart" checkbox reflects `value.resumeOnServiceStart` and toggling it calls `onChange` with the flipped value

### Implementation for User Story 3

- [X] T026 [P] [US3] Add `public bool ResumeOnServiceStart { get; set; }` with XML docs to `src/GameBot.Service/Contracts/Queues/CreateQueueRequest.cs`, `UpdateQueueRequest.cs` and `QueueResponse.cs`
- [X] T027 [US3] In `src/GameBot.Service/Endpoints/QueuesEndpoints.cs` map the flag in create (`ResumeOnServiceStart = req.ResumeOnServiceStart`), update (`queue.ResumeOnServiceStart = req.ResumeOnServiceStart`), duplicate (`ResumeOnServiceStart = source.ResumeOnServiceStart`), `BuildResponse` and `BuildDetailAsync`
- [X] T028 [P] [US3] Add `resumeOnServiceStart: boolean` to the queue type and `resumeOnServiceStart?: boolean` to the create/update request types in `src/web-ui/src/services/queues.ts`
- [X] T029 [US3] Add `resumeOnServiceStart: boolean` to `QueueFormValue` and a labelled checkbox "Resume after service restart" (help text: "If the queue is running when the service stops, start it again when the service comes back.") beside the idle-pause checkbox in `src/web-ui/src/components/queues/QueueForm.tsx`
- [X] T030 [US3] In `src/web-ui/src/pages/QueuesPage.tsx` add `resumeOnServiceStart` to `emptyForm` (false), to the edit-form population from the queue, and to the `createQueue` and `updateQueue` payloads; update any other `QueueFormValue` literals the TypeScript build flags (e.g. `src/web-ui/src/pages/__tests__/QueuesPage.spec.tsx`)
- [X] T031 [US3] Run green: `dotnet test C:\src\GameBot\tests\contract --filter FullyQualifiedName~QueuesApiContractTests`, `dotnet test C:\src\GameBot\tests\integration --filter FullyQualifiedName~QueuesDuplicateEndpointTests`, and in `C:\src\GameBot\src\web-ui`: `npx vite build` and `npx jest src/components/queues src/pages/__tests__/QueuesPage.spec.tsx`

**Checkpoint**: all three stories work independently.

---

## Phase 6: Polish & Cross-Cutting Concerns

- [X] T032 [P] Update `docs/architecture.md`: queue configuration field list gains `ResumeOnServiceStart`; describe the run-state record (`queue-run-state.json`: written on start, cleared on run end unless the host is stopping, read once after startup) in the queue execution section and the persistence layout; list `QueueResumeOnStartupService` among hosted services; refresh "Last reviewed" to 2026-09-17
- [X] T033 [P] Add an Unreleased entry to `CHANGELOG.md`: queues can opt in to resume after a service restart (#203)
- [X] T034 [P] Add the `098-resume-queues-on-restart` row to `specs/STATUS.md` and set `**Status**: Implemented` in `specs/098-resume-queues-on-restart/spec.md`
- [X] T035 Full gate: `dotnet build C:\src\GameBot\GameBot.sln -c Debug` with no new warnings, `dotnet test` for `tests\unit`, `tests\integration`, `tests\contract`; web-ui `npx vite build` + `npx jest`. Fix any failure before completing

---

## Dependencies & Execution Order

- Phase 1 → Phase 2 → US1 (Phase 3) → US2 (Phase 4) → Polish. US3 (Phase 5) depends only on Phase 2 (T004) and can run in parallel with US1/US2.
- Within a story: tests first (must fail), then implementation, then the run-green task.
- T012 → T013 → T014 (same file). T015 → T016 → T017.
- T027 depends on T026; T030 depends on T029.

## Parallel Opportunities

- T002 and T003 (different test files).
- T009, T010, T011 (different test files).
- T019 and T020; T023, T024 and T025.
- T026 and T028 (C# contracts vs TypeScript types).
- T032, T033, T034 (different docs).

## Implementation Strategy

- **MVP**: Phases 1–3 (US1) give automatic resume for queues whose flag is set.
- **Safe release**: add Phase 4 (US2) before shipping — resume must never revive a deliberately stopped queue.
- **Complete**: Phase 5 exposes the option in API/UI; Phase 6 keeps the living docs honest.
