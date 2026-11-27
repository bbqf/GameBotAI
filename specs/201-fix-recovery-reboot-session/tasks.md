# Tasks: Keep the queue Running and bind a new session after a recovery reboot (B-047)

**Input**: Design documents from `specs/201-fix-recovery-reboot-session/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/queue-lost-session.md, quickstart.md

**Tests**: Required. The constitution (Principle II) says that a bug fix MUST include a test that shows the defect: the test fails before the fix. Write each test task first and make sure that it fails before the fix task of the same story.

**Organization**: One phase for each user story of the spec (US1 P1, US2 P2), then the polish phase.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: The task can run in parallel (different files, no dependency on an open task).
- **[Story]**: The user story of the task (US1, US2).

---

## Phase 1: Setup

**Purpose**: Make sure that the base is green before the change.

- [X] T001 Build the solution with `dotnet build "C:\src\GameBot\GameBot.sln" -c Debug` and run `dotnet test "C:\src\GameBot\tests\unit\GameBot.UnitTests.csproj" --filter "FullyQualifiedName~QueueLivenessWatch|FullyQualifiedName~QueueDeviceRecoveryRunner|FullyQualifiedName~DeviceLivenessReader|FullyQualifiedName~QueueExecutionServiceTests|FullyQualifiedName~QueueExecutionServiceRebindTests|FullyQualifiedName~QueueLivenessGateTests"` and `dotnet test "C:\src\GameBot\tests\integration\GameBot.IntegrationTests.csproj" --filter "FullyQualifiedName~DeviceRecovery"`. Record that the base is green (Constitution, release blocker).

---

## Phase 2: Foundational

**Purpose**: The shared state and the shared bind method that both stories use (research R-003, R-004, data-model).

- [X] T002 Add to `QueueRunHandle` in `src/GameBot.Service/Services/QueueExecution/QueueRunHandle.cs`: `public object SessionBindLock { get; } = new();`, a volatile `bool DeviceRecoveryReboots` property, and a `bool LostSessionWarned` property. Write a STE doc comment for each (data-model "QueueRunHandle").
- [X] T003 Extend the device recovery test host in `tests/integration/DeviceRecoveryTestSupport.cs`: `SimulatedDevices` gets a way to make one serial "not listed" (for example `Unlist(serial)` and `List(serial)`), and `SerialSessionManager.CreateSession` throws `KeyNotFoundException($"ADB device '{serial}' not found")` for a serial that is not listed. Keep the current behaviour for each listed serial. Give `SimulatedEmulatorControl` a hook that the test sets, for example `Action<string>? OnReboot`, so that the test unlists the serial in the reboot and lists it again when the test wants (no fixed delay). If the host has no way to set `Service:DeviceLiveness:QueueCheckIntervalMs` and `RebootReadyTimeoutMs`, add it, so that the tests use short and known values.
- [X] T004 In `src/GameBot.Service/Services/QueueExecution/QueueExecutionService.cs` add the static `NoSessionReport` (`DeviceLivenessReport(DeviceLivenessStates.NotLive, DeviceLivenessReasons.TransportNotReady, null, null, true, null, null, false)`) and the method `internal string? TryBindLostSession(ExecutionQueue queue, QueueRunHandle handle, out Exception? error)` with the five steps of research R-003 (`error` is the exception of a failed bind, else null), in `lock (handle.SessionBindLock)`. Add the two `LoggerMessage` entries of research R-007 to `QueueExecutionLog` (warning for the first failed bind of a period, debug for the next). Use a free event ID (search the file for the highest `EventId` of `QueueExecutionLog`).
- [X] T005 In `RebindSessionAsync` of `src/GameBot.Service/Services/QueueExecution/QueueExecutionService.cs`: take `handle.SessionBindLock`, stop the capture and the old session as today, set `handle.SessionId = null` before `BindQueueSession`, then set the new ID. A bind that throws leaves `handle.SessionId` null (research R-003). Update the doc comment. In `tests/unit/Queues/QueueExecutionServiceRebindTests.cs`, add the test `RebindSession_LeavesNoSessionIdWhenTheBindFails` (write it first; it fails before the change).
- [X] T006 In `RunAsync` of `src/GameBot.Service/Services/QueueExecution/QueueExecutionService.cs`, set `handle.DeviceRecoveryReboots = queue.DeviceRecovery is { Reboots: true };` before the session bind of the run start.
- [X] T007 [P] Add `tests/unit/Queues/QueueLostSessionTests.cs` with tests of `TryBindLostSession`: (a) a bound session that exists gives its ID and binds nothing; (b) a lost session and a listed device binds one new session, sets `handle.SessionId`, and clears `LostSessionWarned`; (c) a lost session and a device that is not listed gives null, sets `handle.SessionId` to null, and sets `LostSessionWarned`; (d) while `handle.Liveness` has a running recovery attempt, it gives null and binds nothing; (e) two parallel calls bind only one session (FR-011). Use the fakes in `tests/unit/Queues/Fakes` and the construction pattern of `tests/unit/Queues/QueueExecutionServiceRebindTests.cs`.

**Checkpoint**: The build passes, and T007 passes.

---

## Phase 3: User Story 1 - The queue binds a new session after a recovery reboot and continues (Priority: P1) MVP

**Goal**: A queue with `reboot-instance` that loses its session during a reboot does not end its run. It holds its firings, binds a new session when the device is listed again, and runs the held firings (FR-001, FR-002, FR-002a, FR-003, FR-007, FR-008, FR-010, FR-010a, FR-011).

**Independent Test**: The integration test of T008: the queue status stays `Running`, a new session for the serial exists after the device is listed again, and the held firing runs.

### Tests for User Story 1 (write first)

T008, T010a and T011a fail before T010 and T011. T009 is a guard test: it passes before and after the fix.

- [X] T008 [P] [US1] Add `tests/integration/DeviceRecoveryLostSessionTests.cs` (class `DeviceRecoveryLostSessionTests`, `[Collection("ConfigIsolation")]`, same setup as `DeviceRecoveryFlowTests`). Test `ARecoveryRebootThatRemovesTheSessionKeepsTheQueueRunningAndRunsTheHeldFiring`: block the device, and make the `OnReboot` hook (T003) unlist the serial. Wait until `GET /api/queues/{id}` gives `status: Running` and `health.deviceLiveness.reason` `transport_not_ready`. Assert at that time: a second `POST /api/queues/{id}/start` does not start a second run (the current "already running" answer), so the queue keeps its run and its device claim (FR-001). Then list the serial again. Assert: after the serial is listed, one session for the serial exists (`ISessionManager.ListSessions()`); the sequence log has an entry whose summary is not a `device_not_live:` summary (the held firing ran); the queue log has no root with "emulator connection lost mid-run".
- [X] T009 [P] [US1] In the same file, add test `AQueueWithRecoveryOffStillStopsWhenTheSessionIsLost` (FR-008 guard of the current behaviour; it passes before and after the fix): a queue with `deviceRecovery.action = none`, remove its session through `ISessionManager.StopSession` and unlist the serial. Assert that the run ends and the root summary has "emulator connection lost mid-run ('<serial>')".
- [X] T010a [P] [US1] In the same file, add test `AStopWhileTheQueueWaitsForItsDeviceEndsTheRun` (FR-010): while the serial is unlisted and the queue has no session, `POST /api/queues/{id}/stop`. Assert that the status becomes `Stopped`, that no session for the serial is left, and that the device claim is free (a second start of the queue gives HTTP 200).

### Implementation for User Story 1

- [X] T010 [US1] In `RunAsync` of `src/GameBot.Service/Services/QueueExecution/QueueExecutionService.cs`, change the local function `EnsureSessionBound()` to `bool TryEnsureSessionBound()`. Keep the first check (follow `handle.SessionId` when it names a session that exists). When the local session exists, return true. Else call `TryBindLostSession(queue, handle, out var error)` (T004). When it gives an ID, set the local `sessionId` and return true. When it gives null: if `_liveness is not null && handle.DeviceRecoveryReboots`, return false; else throw `new QueueConnectionLostException(error?.Message ?? "no session", error)`. The catch keeps the current message "emulator connection lost mid-run ('<serial>')".
- [X] T011 [US1] In the same method, change each call site (research R-005): (a) `HoldIfNotLiveAsync`: when `_liveness` is not null and `TryEnsureSessionBound()` is false, call `handle.Liveness.Observe(NoSessionReport, now)` and `TryGateOnLivenessAsync(..., holdOnlyReport: NoSessionReport, ...)`, set `held = true` and `holdReport = NoSessionReport`, and return true; (b) `FireGroupAsync`: `if (!TryEnsureSessionBound()) { held = true; holdReport ??= NoSessionReport; return null; }`; (c) `RunEveryStepPassAsync` and `RunBeforeEachRunPassAsync`: `if (!TryEnsureSessionBound()) return;` before each entry; (d) the daily retry: `if (!TryEnsureSessionBound()) continue;`; (e) remove the extra `EnsureSessionBound()` before `FireGroupAsync` in the at-start pass and in the once-per-run pass; (f) before the idle hold: when `TryEnsureSessionBound()` is false, `await Task.Delay(HeldIterationWait(), _timeProvider, ct)` and `continue`; pass `handle.SessionId ?? sessionId` to `IdlePauseHoldAsync`. Add one STE comment "Feature 201" at the changed gate.
- [X] T011a [US1] In `tests/unit/Queues/QueueLivenessGateTests.cs` (its setup makes the service with a liveness service, which the fix needs), add the test `ALostSessionWithRecoveryOnHoldsTheFiringAndTheRunContinuesAfterABind` (write it before T010; it fails before T010 and T011): the queue has `deviceRecovery.action = reboot-instance` and two once-per-run entries A and B. After A runs, make the session fake throw `KeyNotFoundException` on `CreateSession` and remove the session (add this control to the session fake of the file when it has none). Assert that the run does not end, that B does not run, and that the sequence log of B has one entry `device_not_live: transport_not_ready`. Then make `CreateSession` work again. Assert that B runs on a new session, and that the run ends with `success`.
- [X] T012 [US1] Run T005 (rebind test), T007, T008, T009, T010a and T011a, the tests of Phase 1, and the guard tests `ConnectionLostMidRunFailsTheRun`, `ReboundFailsOnCapacityFailsTheRun` and `SessionEvictedBetweenFiringsIsReboundAndTheRunContinues` in `tests/unit/Queues/QueueExecutionServiceTests.cs`, and all of `tests/unit/Queues/QueueExecutionServiceRebindTests.cs` and `tests/unit/Queues/QueueLivenessGateTests.cs`. All pass. Fix each failure before the next phase.

**Checkpoint**: US1 is complete. A recovery reboot does not stop the queue.

---

## Phase 4: User Story 2 - The operator sees why the queue does not run, and gets the alert (Priority: P2)

**Goal**: While no session is bound, the watch continues its checks, recovery attempts and alerts, and `GET /api/queues/{id}` shows `health.deviceLiveness` (FR-004, FR-005, FR-006).

**Independent Test**: The unit tests of T013 and T014, and the integration test of T015.

### Tests for User Story 2 (write first)

T013, T014 and T015 fail before T016 to T018.

- [X] T013 [P] [US2] In `tests/unit/Queues/QueueLivenessWatchTests.cs` add tests: (a) with `handle.SessionId` null, `DeviceRecoveryReboots` true and a `tryBind` callback that gives null, `CheckOnceAsync` observes `not_live` with reason `transport_not_ready`, claims the alert after `AlertAfterMs`, and starts a recovery attempt after `afterMs`; (b) after `maxAttempts` failed attempts with no session, exactly one "recovery failed" alert is sent; (c) a `tryBind` callback that gives an ID makes the watch evaluate that session; (d) with `DeviceRecoveryReboots` false and no session, `CheckOnceAsync` returns false and observes nothing (no change); (e) a stored queue with `deviceRecovery.action = none` sets `handle.DeviceRecoveryReboots` to false at the next check.
- [X] T014 [P] [US2] In `tests/unit/Queues/DeviceLivenessReaderRecoveryStateTests.cs` add a test: `Project` with `handle.SessionId` null and an episode that observed `NoSessionReport` gives `state: not_live`, `reason: transport_not_ready` and the `recoveryState` of the snapshot. Add a test for a session ID that names a removed session: the same result.
- [X] T015 [P] [US2] In `tests/integration/DeviceRecoveryLostSessionTests.cs` add test `AQueueThatCannotBindAfterAllAttemptsSendsOneRecoveryFailedAlertAndStaysRunning`: `maxAttempts: 2`, `cooldownMs: 0`, and the `OnReboot` hook keeps the serial unlisted after each reboot. Assert: exactly one `QueueAlertKind.RecoveryFailed` alert, `status: Running`, `health.deviceLiveness.recoveryState: exhausted`. Then list the serial. Assert: one session for the serial, and one `QueueAlertKind.LiveAgain` message.

### Implementation for User Story 2

- [X] T016 [US2] In `src/GameBot.Service/Services/QueueExecution/QueueLivenessWatch.cs`: add the optional constructor parameter `Func<ExecutionQueue, QueueRunHandle, string?>? tryBind = null` (last position). In `RefreshRecoverySettingsAsync`, set `_handle.DeviceRecoveryReboots = stored.DeviceRecovery is { Reboots: true };`. In `CheckOnceAsync`, when the session ID is null or the session is gone, call `tryBind`; with an ID, evaluate that session; with null and `_handle.DeviceRecoveryReboots` true, use `QueueExecutionService.NoSessionReport` as the report for `Observe`, `SendAlerts`, `StartRecoveryIfDue` and the fault cycle claim; else return false. Update the class doc comment (STE).
- [X] T017 [US2] In `StartLivenessWatch` of `src/GameBot.Service/Services/QueueExecution/QueueExecutionService.cs`, pass `(q, h) => TryBindLostSession(q, h, out _)` as `tryBind`. The watch does not need the error.
- [X] T018 [US2] In `src/GameBot.Service/Services/QueueExecution/DeviceLivenessReader.cs`, change `Project`: evaluate and observe the session only when `handle.SessionId` names a session that exists; else use the snapshot as it is. Return null only when the snapshot has no `LastReport`. Update the doc comment.
- [X] T019 [US2] In `src/GameBot.Service/Services/QueueExecution/QueueDeviceRecoveryRunner.cs`, update the class doc comment: a failed rebind leaves the run with no session; the run loop holds its firings and the watch starts the next attempt; the queue stays Running. No code change.
- [X] T020 [US2] Run T013, T014 and T015, and all tests of T012. All pass.

**Checkpoint**: US1 and US2 are complete.

---

## Phase 5: Polish & Cross-Cutting Concerns

- [X] T021 [P] Update `docs/architecture.md`: in the device recovery text of feature 121 (search "deviceRecovery"), add one paragraph (STE) for feature 201: a queue with `reboot-instance` that has no bound session holds its firings with the reason `transport_not_ready`, tries a bind at each check and before each firing, and stays Running; a queue with `none` keeps the current end of the run. Refresh the "Last reviewed" date to 2026-11-27.
- [X] T022 [P] Set the status of `specs/121-device-not-live-alert-recovery/spec.md` to `Implemented (iterated by 199, 201)`. In `specs/STATUS.md`, change the row of 121 to the same value and add the row `| 201 | Recovery reboot keeps the queue Running and binds a new session | Implemented |`. Set the status of `specs/201-fix-recovery-reboot-session/spec.md` to `Implemented`.
- [X] T023 [P] Add a `CHANGELOG.md` entry (STE) under the newest unreleased section: "Fixed: a recovery reboot (`deviceRecovery` `reboot-instance`) no longer stops the queue with 'emulator connection lost mid-run'. The queue stays Running, holds its firings, and binds a new session when the device is available again (#416)."
- [X] T024 Run the full unit and integration test projects: `dotnet test "C:\src\GameBot\tests\unit\GameBot.UnitTests.csproj"` and `dotnet test "C:\src\GameBot\tests\integration\GameBot.IntegrationTests.csproj"`. Run `dotnet test "C:\src\GameBot\tests\contract\GameBot.ContractTests.csproj"` (FR-009: no API schema change; the device recovery contract tests pass with no change). Run `dotnet build "C:\src\GameBot\GameBot.sln" -c Release` with zero warnings that are new. Collect coverage for the unit tests (`--collect:"XPlat Code Coverage"`) and check ≥80% line and ≥70% branch coverage for the changed methods in `QueueExecutionService.cs`, `QueueLivenessWatch.cs` and `DeviceLivenessReader.cs`. Add tests when the values are lower. All pass (Constitution, release blocker).
- [X] T025 Check the shard balance rule (`docs/ci-time.md`, "Shard balance rule"): when the new integration test file adds to a CI shard, run `scripts/ci/measure-shards.ps1` and split the shard if it is more than 20% above the average of its project.
- [X] T026 Check the quickstart steps in `specs/201-fix-recovery-reboot-session/quickstart.md` that run in tests (the "Automatic tests" section). Record the result.

---

## Dependencies & Execution Order

- Phase 1 → Phase 2 → Phase 3 (US1) → Phase 4 (US2) → Phase 5.
- T002 before T004 to T007. T003 before T008 to T010a and T015. T004 before T005, T007, T010, T016, T017.
- US2 uses `TryBindLostSession` and `NoSessionReport` from Phase 2 and the "no throw" path of US1 (T010, T011). Without US1, the run ends before the watch can act, so do US1 first.
- In each story: the test tasks first (they fail), then the implementation tasks, then the run task. In US1, write T011a before T010, although its ID comes after T011.

## Parallel Opportunities

- Phase 2: T003 and T007 are in different files ([P] for T007 after T004).
- US1: T008, T009 and T010a are in one new file; write them together. Write them before T010. They must fail before T010.
- US2: T013, T014 and T015 are in three different files and can be written in parallel.
- Phase 5: T021, T022 and T023 are in different files.

## Implementation Strategy

- **MVP**: Phase 1, Phase 2 and US1. This removes the stop of the queue, which is the P1 defect of the issue.
- **Increment 2**: US2 makes the "no session" state visible and makes the second attempt and the "recovery failed" alert work.
- **Then**: Phase 5 (documentation, status lines, changelog, full test run). Phase 2 of the pipeline does the version bump before the commit.
