---
description: "Task list for feature 079 - Concurrent Queue Execution"
---

# Tasks: Concurrent Queue Execution

**Input**: Design documents from `/specs/079-concurrent-queue-execution/`
**Prerequisites**: [plan.md](./plan.md), [spec.md](./spec.md), [research.md](./research.md),
[data-model.md](./data-model.md), [contracts/api.md](./contracts/api.md)

**Tests**: REQUIRED. Constitution Principle II makes tests mandatory for executable logic, and this
feature is a concurrency bug fix, so every defect gets a regression test that fails before the fix.

**Organization**: Tasks are grouped by user story so each story can be implemented, tested and
delivered independently.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies on incomplete tasks)
- **[Story]**: Which user story this task belongs to (US1..US4)

## Path Conventions

Web application layout per [plan.md](./plan.md): .NET backend under `src/GameBot.Domain`,
`src/GameBot.Emulator`, `src/GameBot.Service`; React SPA under `src/web-ui/src`; tests under
`tests/unit`, `tests/integration`, `tests/contract`.

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Confirm the baseline is green before changing concurrency-sensitive code.

- [ ] T001 Run `dotnet build C:\src\GameBot\GameBot.sln -c Debug` and record that the pre-change build is green (Constitution: red build blocks progression)
- [ ] T002 [P] Run `dotnet test C:\src\GameBot\GameBot.sln` and record the pre-change pass/fail baseline so new failures are attributable to this feature
- [ ] T003 [P] Run `npm ci` then `npm run build` and `npm test` in `src/web-ui` and record the baseline (lint and `tsc --noEmit` have known pre-existing failures and are NOT the gate)

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: The device-identity primitives every user story depends on. No user story can start until
this phase is complete.

- [ ] T004 [P] Create `DeviceContext` record (`SessionId`, `DeviceSerial?`) with XML docs in `src/GameBot.Domain/Sessions/DeviceContext.cs`
- [ ] T005 [P] Create `IDeviceContextAccessor` (`Current`, `IDisposable Push(DeviceContext)`) with XML docs in `src/GameBot.Domain/Sessions/IDeviceContextAccessor.cs`
- [ ] T006 Implement `AsyncLocalDeviceContextAccessor` over `AsyncLocal<DeviceContext?>`, with a nested-safe disposable scope that restores the previous value, in `src/GameBot.Domain/Sessions/AsyncLocalDeviceContextAccessor.cs`
- [ ] T007 [P] Add `IScreenSourceFactory` with `IScreenSource ForSession(string sessionId)` and XML docs to `src/GameBot.Domain/Triggers/Evaluators/ScreenSourceAbstractions.cs`
- [ ] T008 [P] Unit-test the accessor (flows across `await`, flows into `Task.Run`, nested push/pop restores, child flow does not leak to parent, double-dispose is a no-op) in `tests/unit/Sessions/DeviceContextAccessorTests.cs`
- [ ] T009 Register `IDeviceContextAccessor` -> `AsyncLocalDeviceContextAccessor` as a singleton in `RegisterSessionServices` in `src/GameBot.Service/GameBotServiceSetup.cs`

**Checkpoint**: Device identity primitives exist and are tested; nothing else has changed behavior yet.

---

## Phase 3: User Story 1 - Two queues on two emulators run independently (Priority: P1)

**Goal**: Every observation and every action in a run resolves against that run's own device.

**Independent test**: Start two queues on two emulator serials whose screens differ, each running a
sequence that waits for an image present only on its own device. Both succeed; neither observes the
other's screen.

### Tests for User Story 1

- [ ] T010 [P] [US1] Unit-test `SessionScopedScreenSource`: returns its own session's frame, returns null when that session has no cached frame, returns null when the session stopped, never returns another session's frame — in `tests/unit/Sessions/SessionScopedScreenSourceTests.cs`
- [ ] T011 [P] [US1] Unit-test the narrowed session resolution for `ensure-game-running`, `go-to-home-screen` and primitive `tap`/`swipe`/`key`: explicit session wins with N sessions active; 1 active resolves silently; 0 active keeps today's message; N>1 fails with `"<N> device sessions are active; specify a sessionId for '<step>'"` — in `tests/unit/Sequences/SessionResolutionTests.cs`
- [ ] T012 [P] [US1] Unit-test `CommandExecutor.ForceExecuteStepAsync` and `ResolveSessionIdAsync` under the same four cases in `tests/unit/Commands/CommandExecutorSessionResolutionTests.cs`
- [ ] T013 [P] [US1] Integration-test screen isolation: two sessions with distinct stub frames, two concurrent sequence executions, each observes only its own frame — in `tests/integration/Sessions/ConcurrentScreenIsolationTests.cs`

### Implementation for User Story 1

- [ ] T014 [P] [US1] Implement `SessionScopedScreenSource` over `BackgroundScreenCaptureService.GetCachedFrame(sessionId)`, decoding from the frame's immutable PNG bytes (keeps the concurrent-disposal fix), in `src/GameBot.Emulator/Session/SessionScopedScreenSource.cs`
- [ ] T015 [US1] Implement `BackgroundCaptureScreenSourceFactory : IScreenSourceFactory` returning a `SessionScopedScreenSource` per session id in `src/GameBot.Emulator/Session/BackgroundCaptureScreenSourceFactory.cs`
- [ ] T016 [US1] Rewrite `BackgroundCaptureScreenSource.GetLatestScreenshot()` to resolve ambient context -> single running session -> null, deleting the `FirstOrDefault` first-session scan, in `src/GameBot.Emulator/Session/BackgroundCaptureScreenSource.cs`
- [ ] T017 [US1] Register `IScreenSourceFactory` -> `BackgroundCaptureScreenSourceFactory` and pass `IDeviceContextAccessor` into the `IScreenSource` singleton factory in `src/GameBot.Service/GameBotServiceSetup.cs` (both the ADB branch and the stub/test branch)
- [ ] T018 [US1] Narrow the three `runningSessions.Count != 1` guards in `DispatchEnsureGameRunningAsync`, `DispatchGoToHomeScreenAsync` and `DispatchPrimitiveInputAsync` to the FR-007 rule, with the new multi-session message, in `src/GameBot.Service/Services/SequenceExecution/SequenceExecutionService.cs`
- [ ] T019 [US1] Push the run's `DeviceContext` for the duration of a sequence execution when `sessionId` is supplied, in `SequenceExecutionService.ExecuteAsync` in `src/GameBot.Service/Services/SequenceExecution/SequenceExecutionService.cs`
- [ ] T020 [US1] Narrow `ForceExecuteStepAsync` and `ResolveSessionIdAsync` to the FR-007 rule with the new message, and use `IScreenSourceFactory.ForSession(resolvedSessionId)` for the detect-and-tap and image-detection paths instead of the injected singleton `_screen`, in `src/GameBot.Service/Services/CommandExecutor.cs`
- [ ] T021 [US1] Add a `string? sessionId` parameter to `IGameReadinessProbe.WaitUntilReadyAsync` in `src/GameBot.Service/Services/EnsureGameRunning/IGameReadinessProbe.cs`
- [ ] T022 [US1] Resolve the screen via `IScreenSourceFactory.ForSession(sessionId)` when a session id is supplied (falling back to the injected `IScreenSource` when null) in `src/GameBot.Service/Services/EnsureGameRunning/GameReadinessProbe.cs`, and pass the handler's session id through in `src/GameBot.Service/Services/EnsureGameRunning/EnsureGameRunningActionHandler.cs`
- [ ] T023 [US1] Push the run's `DeviceContext` around each firing in `RunOneSequenceAsync` in `src/GameBot.Service/Services/QueueExecution/QueueExecutionService.cs`, so loops, nested sequences and trigger-based conditions inherit it

**Checkpoint**: Two queues on two emulators observe and act only on their own devices. US1 is
independently demonstrable.

---

## Phase 4: User Story 2 - The same emulator cannot be claimed by two runs (Priority: P1)

**Goal**: One device, one run — enforced atomically, released on every exit path.

**Independent test**: Two queues on one serial; start the first, watch the second be refused with a
message naming the device and the holder; stop the first; the second then starts.

### Tests for User Story 2

- [ ] T024 [P] [US2] Unit-test `DeviceClaimRegistry`: claim/refuse/release round trip; serial normalization (trim + case-insensitive); blank serial never blocks; `Release` by a non-holder is a no-op; concurrent `TryClaim` from many threads yields exactly one winner; a freshly constructed registry holds no claims (FR-013, in-memory only) — in `tests/unit/Queues/DeviceClaimRegistryTests.cs`
- [ ] T025 [P] [US2] Unit-test `QueueExecutionService` claim behavior: `DeviceInUse` returned without launching a run and without changing runtime status; claim released after completion, manual stop, failure and cancellation; a refused start leaves no run-registry residue — in `tests/unit/Queues/QueueExecutionServiceDeviceClaimTests.cs`
- [ ] T026 [P] [US2] Integration-test two concurrent runs: same-serial start refused, different-serial starts both succeed, stopping one leaves the other's status and schedule unchanged (FR-020) — in `tests/integration/Queues/ConcurrentQueueRunTests.cs`
- [ ] T027 [P] [US2] Contract-test `POST /api/queues/{id}/start` returning `409 {"error":"device_in_use"}` with the device and holder named, and `already_running` still distinct, in `tests/contract/QueueConcurrencyContractTests.cs`
- [ ] T027a [P] [US2] Jest-test that a 409 `device_in_use` start rejection renders its message on the queue row, alongside the existing `already_running` case, in `src/web-ui/src/pages/__tests__/QueuesPage.execution.spec.tsx`

### Implementation for User Story 2

- [ ] T028 [P] [US2] Create `DeviceClaim` record (`DeviceSerial`, `QueueId`, `QueueName`, `ClaimedAtUtc`) and `IDeviceClaimRegistry` (`TryClaim`, `Release`, `TryGetHolder`) with XML docs in `src/GameBot.Service/Services/QueueExecution/IDeviceClaimRegistry.cs`
- [ ] T029 [US2] Implement `DeviceClaimRegistry` over `ConcurrentDictionary<string, DeviceClaim>` with `StringComparer.OrdinalIgnoreCase`, trimmed keys, `TryAdd`-based atomic claim and holder-checked release, in `src/GameBot.Service/Services/QueueExecution/DeviceClaimRegistry.cs`
- [ ] T030 [US2] Add `QueueStartOutcome.DeviceInUse` and correct the `IQueueExecutionService` XML doc that currently states same-emulator concurrency is allowed, in `src/GameBot.Service/Services/QueueExecution/IQueueExecutionService.cs`
- [ ] T031 [US2] Claim the device in `StartAsync` after the run registry accepts the queue and before launching the run; on refusal remove the registry entry, dispose the CTS and return `DeviceInUse`; release the claim in `RunAsync`'s `finally` — in `src/GameBot.Service/Services/QueueExecution/QueueExecutionService.cs`
- [ ] T032 [US2] Register `IDeviceClaimRegistry` -> `DeviceClaimRegistry` as a singleton next to `IQueueRunRegistry` in `src/GameBot.Service/GameBotServiceSetup.cs`
- [ ] T033 [US2] Map `QueueStartOutcome.DeviceInUse` to `409 device_in_use`, building the message from `IDeviceClaimRegistry.TryGetHolder`, in the `{id}/start` handler in `src/GameBot.Service/Endpoints/QueuesEndpoints.cs`
- [ ] T033a [US2] Add a `LoggerMessage`-based application-log entry for a refused start naming the refused queue, the device and the holding queue (FR-017, second sentence) in `src/GameBot.Service/Endpoints/QueuesEndpoints.Logging.cs` and call it from the `{id}/start` handler
- [ ] T034 [US2] Surface the `device_in_use` message on the queue Start action, reusing the existing 409 error rendering used for `already_running`, in `src/web-ui/src/pages/QueuesPage.tsx`

**Checkpoint**: Same-device double-start is impossible; different-device starts are unaffected. US2 is
independently demonstrable.

---

## Phase 5: User Story 3 - Concurrency is visible and diagnosable (Priority: P2)

**Goal**: The operator can see which run holds which device, every concurrency refusal or failure reads
as plain language, and per-run state is provably isolated under concurrency.

**Independent test**: With two runs active, read each monitor response and confirm per-run device and
current sequence; force a capacity failure and read the recorded reason; poll the monitor while both
runs execute and confirm no register leaks between them.

### Tests for User Story 3

- [ ] T035 [P] [US3] Unit-test the capacity message shape (`session capacity reached: N of N sessions are open (Service:Sessions:MaxConcurrentSessions)`) and that the default `MaxConcurrentSessions` is 8 in `tests/unit/Sessions/SessionCapacityMessageTests.cs`
- [ ] T036 [P] [US3] Unit-test that `QueueMonitorSnapshot`/response carries the running run's device serial and `null` when not running, in `tests/unit/Queues/QueueMonitorServiceTests.cs` (extend the existing file)
- [ ] T037 [P] [US3] Integration-test that a capacity-exceeded run finalizes with the actionable reason in its execution-log entry (FR-016, FR-017) in `tests/integration/Queues/ConcurrentQueueRunTests.cs`
- [ ] T037a [P] [US3] Integration-test run-state isolation under concurrency (FR-019, FR-020, and the idle-eviction edge case): two runs execute concurrently while the monitor is polled on a third thread; each run's schedule registers, self-reschedule registers, idle-pause flag and current-sequence indicator stay its own, and a session lost by one run fails only that run — in `tests/integration/Queues/ConcurrentRunStateIsolationTests.cs`
- [ ] T037b [P] [US3] Integration-test execution-log attribution under concurrency (FR-021): two concurrent runs write interleaved entries; every entry resolves to exactly one run's root id and no entry is lost — in `tests/integration/Queues/ConcurrentRunStateIsolationTests.cs`

### Implementation for User Story 3

- [ ] T038 [P] [US3] Change `MaxConcurrentSessions` default from 3 to 8 in `src/GameBot.Emulator/Session/SessionOptions.cs`
- [ ] T039 [US3] Replace the bare `capacity_exceeded` exception message with the actionable text naming active/max and the config key in `SessionManager.CreateSession` in `src/GameBot.Emulator/Session/SessionManager.cs` (keep the exception type and the existing API mapping)
- [ ] T040 [P] [US3] Add `DeviceSerial` to `QueueMonitorSnapshot` in `src/GameBot.Service/Services/QueueExecution/QueueMonitorSnapshot.cs` and populate it from the run handle in `src/GameBot.Service/Services/QueueExecution/QueueMonitorService.cs`
- [ ] T041 [US3] Add the nullable `deviceSerial` field to `QueueMonitorResponse` and its projection in `src/GameBot.Service/Contracts/Queues/QueueMonitorResponse.cs` and `src/GameBot.Service/Endpoints/QueuesEndpoints.cs`
- [ ] T042 [US3] Add `deviceSerial?: string | null` to `QueueMonitorDto` in `src/web-ui/src/services/queues.ts` and render the run's device in `src/web-ui/src/components/queues/QueueMonitor.tsx`

**Checkpoint**: Concurrency state is legible in the UI and in the logs.

---

## Phase 6: User Story 4 - Operator tooling targets a chosen device (Priority: P3)

**Goal**: Screen capture and cropping never silently show the wrong emulator.

**Independent test**: With two sessions active, capture by `sessionId` and by `serial` and confirm the
right device; capture with no selector and confirm `409 ambiguous_session`; with one session, confirm
the bare call still works.

### Tests for User Story 4

- [ ] T043 [P] [US4] Contract-test `GET /api/emulators/screenshot`: explicit `sessionId`, explicit `serial`, 0 sessions -> `503 emulator_unavailable`, 1 session -> unchanged success, N>1 with no selector -> `409 ambiguous_session`, unknown `serial` -> `404 session_not_found` — in `tests/contract/EmulatorScreenshotSelectorContractTests.cs`

### Implementation for User Story 4

- [ ] T044 [US4] Add an optional `serial` query parameter, replace `PickSession`'s `FirstOrDefault` chain with explicit-selector -> single-session -> ambiguity-error resolution, and return the `ambiguous_session` / `session_not_found` bodies, in `src/GameBot.Service/Endpoints/EmulatorImageEndpoints.cs`
- [ ] T045 [US4] Always send the selected session/serial selector on screenshot requests from the authoring UI in `src/web-ui/src/services/images.ts` and its callers, so the ambiguity error can never be triggered from the app itself

**Checkpoint**: All four user stories complete.

---

## Phase 7: Polish & Cross-Cutting Concerns

- [ ] T046 [P] Add a micro-benchmark holding `TryClaim`/`Release` under the declared 0.05 ms budget in `tests/unit/Performance/DeviceClaimBenchmarkTests.cs` (Constitution Principle IV)
- [ ] T047 [P] Update `docs/architecture.md` with the device-claim and session-scoped-observation model and refresh its "Last reviewed" date (Constitution Principle V, NON-NEGOTIABLE)
- [ ] T048 [P] Add the 079 entry to `specs/STATUS.md` and set `specs/079-concurrent-queue-execution/spec.md` **Status** to `Implemented`
- [ ] T049 [P] Update `specs/051-queue-execution-runtime/spec.md` **Status** to record that its FR-013 (same-emulator concurrency allowed, no guard) is superseded by 079; FR-013a is unchanged
- [ ] T050 Run `dotnet build C:\src\GameBot\GameBot.sln -c Debug` and `dotnet test C:\src\GameBot\GameBot.sln` and fix every failure and every new analyzer warning (red build/test blocks completion)
- [ ] T051 Run `npm run build` and `npm test` in `src/web-ui` and fix any failure introduced by T034/T042/T045
- [ ] T052 Walk [quickstart.md](./quickstart.md) end to end against a running service and correct anything it gets wrong

---

## Dependencies

```text
Phase 1 (T001-T003)  Setup / baseline
        |
Phase 2 (T004-T009)  Foundational  <-- BLOCKS every user story
        |
        +--> Phase 3 US1 (T010-T023)          device-scoped observation + action
        |
        +--> Phase 4 US2 (T024-T034, +T027a/T033a)  exclusive device claim
        |
        +--> Phase 5 US3 (T035-T042, +T037a/T037b)  visibility, capacity, state isolation
        |                                            [T040 reads the run handle, so after T031]
        +--> Phase 6 US4 (T043-T045)          operator tooling
        |
Phase 7 (T046-T052)  Polish, docs, gates
```

**Story independence**:

- **US1** depends only on Phase 2. Fully deliverable alone: two queues on two emulators stop
  corrupting each other's observations even without the claim registry.
- **US2** depends only on Phase 2. Fully deliverable alone: same-device double-start becomes
  impossible even before observation is scoped.
- **US3** depends on Phase 2; T040 additionally reads the run handle's session/serial, so run it after
  T031 if both are in flight.
- **US4** depends only on Phase 2.

**Within a story**: tests are written first and must fail before the matching implementation task.

---

## Parallel Execution Examples

**Phase 2 (after T004/T005 exist)**: T007 and T008 in parallel with T006.

**Phase 3**: T010, T011, T012, T013 are four different test files — all parallel. Then T014 in
parallel with T018/T020/T021 (different projects and files); T015, T016, T017 are sequential because
they touch the same registration and source pair.

**Phase 4**: T024, T025, T026, T027, T027a all parallel (five files). T028 parallel with the Phase 3
implementation; T029-T033a sequential within the claim path, then T034.

**Phase 5**: T035, T036, T037, T037a, T037b parallel; T038 and T040 parallel; T039, T041, T042
sequential after them.

**Phase 7**: T046, T047, T048, T049 all parallel; T050-T052 last and sequential.

---

## Implementation Strategy

**MVP scope**: Phase 1 + Phase 2 + **Phase 3 (US1)** + **Phase 4 (US2)**. Both are P1 and together they
are the actual feature: correct observation plus a device that cannot be double-booked. US3 and US4
make the result legible and safe to author against, and can land in a follow-up increment if needed.

**Order of work**: baseline green -> primitives -> US1 -> US2 -> US3 -> US4 -> polish. Every phase ends
at a checkpoint that is independently demonstrable, and no phase may be marked complete while the
build or the test run is red.
