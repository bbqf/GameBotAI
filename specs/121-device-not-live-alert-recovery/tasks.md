# Tasks: Device Not-Live Alert, Optional Recovery, and Capture Pile-Up Guard

**Input**: Design documents from `C:\src\GameBot\specs\121-device-not-live-alert-recovery\`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/device-recovery-api.md, quickstart.md

**Tests**: The spec and the constitution (Principle II) need tests. Each story has test tasks. Write each test first and see it fail before the code.

**Organization**: Tasks are grouped by user story. The coordinator interface and the coordinator (owner of the recovery slot) are in Phase 2, so they exist before the recovery runner `QueueDeviceRecoveryRunner` in Phase 4.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: The task can run in parallel (different files, no open dependency).
- **[Story]**: The user story of the task (US1 to US4).
- **(VERIFICATION ONLY)**: The task only runs tests or checks. It changes no file, unless a check fails.
- All paths are from the repository root `C:\src\GameBot`.

## Path Conventions

- Source: `src\GameBot.Domain`, `src\GameBot.Emulator`, `src\GameBot.Service`
- Tests: `tests\unit`, `tests\contract`, `tests\integration`
- Earlier liveness feature (issue #220): spec folder `specs\106-wedged-device-liveness`

**Registration rule**: Task T017 is the ONLY task that edits `src\GameBot.Service\GameBotServiceSetup.cs`. No other task registers a service. Parallel work then does not conflict on that file.

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Read the earlier liveness feature and add the new service settings.

- [ ] T001 (VERIFICATION ONLY) Read `specs\106-wedged-device-liveness\spec.md`, `specs\106-wedged-device-liveness\plan.md`, and `specs\106-wedged-device-liveness\research.md`. Keep the detection rules unchanged (FR-015).
- [ ] T002 Add `AlertAfterMs` (default 300000, minimum 1000), `RecoveryStaggerMs` (default 180000, minimum 0), and `RebootReadyTimeoutMs` (default 180000, minimum 1000) to `src\GameBot.Domain\Sessions\DeviceLivenessOptions.cs`. Apply each minimum in `Normalized()`.
- [ ] T003 [P] Add tests for the three new options (defaults and minimums) in `tests\unit\Sessions\DeviceLivenessOptionsTests.cs`.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: The shared parts all stories use. The coordinator interface and the coordinator come here, before any runner.

**CRITICAL**: No user story starts before this phase ends.

- [ ] T004 [P] Write failing tests for the episode claims and counters in `tests\unit\Queues\QueueLivenessEpisodeTests.cs`. Cover `TryClaimAlert`, `TryClaimLiveAgain`, `TryClaimRecoveryFailed` (each true one time), `attempts`, `lastAttemptEndedAt`, `recoveryRunning`, and the clear rules for `live` and `unknown`.
- [ ] T005 Extend `src\GameBot.Service\Services\QueueExecution\QueueLivenessEpisode.cs` with the fields `alertSent`, `liveAgainPending`, `recoveryFailedSent`, `attempts`, `lastAttemptEndedAt`, `recoveryRunning` and the three claim methods under the existing lock (data-model section 3).
- [ ] T006 [P] Write failing tests for `IEmulatorControl.RebootAsync` returning `bool` (exit code 0 gives true; a missing tool, a non-zero exit, or a start failure gives false) in `tests\unit\Emulator\LdConsoleEmulatorControlRebootTests.cs`.
- [ ] T007 Change `RebootAsync` to return `Task<bool>` in `src\GameBot.Service\Services\EnsureEmulatorRunning\IEmulatorControl.cs` and `src\GameBot.Service\Services\EnsureEmulatorRunning\LdConsoleEmulatorControl.cs`. Use the exit code from `src\GameBot.Emulator\Adb\LdConsoleClient.cs`. Keep `src\GameBot.Service\Services\EnsureEmulatorRunning\EnsureEmulatorRunningActionHandler.cs` working (it ignores the value). Update any test fake of `IEmulatorControl` under `tests\`.
- [ ] T008 [P] Create `src\GameBot.Domain\Queues\QueueDeviceRecovery.cs`: the settings record with `Action`, `AfterMs` (300000), `MaxAttempts` (2), `CooldownMs` (180000) and the default values (data-model section 1).
- [ ] T009 [P] Write failing tests for the validator in `tests\unit\Queues\QueueDeviceRecoveryValidatorTests.cs`. Cover each error text in `specs\121-device-not-live-alert-recovery\contracts\device-recovery-api.md` section 1 and the range checks when `action` is `none`.
- [ ] T010 Create `src\GameBot.Domain\Queues\QueueDeviceRecoveryValidator.cs` with the range and cross-field rules. Add the nullable member `DeviceRecovery` to `src\GameBot.Domain\Queues\ExecutionQueue.cs`. An old queue file reads as null.
- [ ] T011 [P] Write failing unit tests for the coordinator in `tests\unit\Queues\DeviceRecoveryCoordinatorTests.cs`. Use a fake `TimeProvider` and a fake `IEmulatorControl`. Cover: one reboot start at a time; the stagger wait after a reboot start; the gap between two reboot start times is `RecoveryStaggerMs` or more (SC-003); the shared reboot task for one instance name (case-insensitive); the slot is free after a cancel or a failure (finally block).
- [ ] T012 Create the interface `src\GameBot.Service\Services\QueueExecution\IDeviceRecoveryCoordinator.cs` with `Task<bool> RebootInstanceAsync(string instanceName, CancellationToken ct)`.
- [ ] T013 Create `src\GameBot.Service\Services\QueueExecution\DeviceRecoveryCoordinator.cs`, the implementation of `IDeviceRecoveryCoordinator`: one `SemaphoreSlim(1,1)` slot, `lastStartedAt`, the stagger wait from `RecoveryStaggerMs`, and an in-flight map keyed by instance name (research R-006). It calls only `IEmulatorControl.RebootAsync`. It must exist before `QueueDeviceRecoveryRunner`.
- [ ] T014 Create the type shell `src\GameBot.Service\Services\QueueExecution\QueueDeviceRecoveryRunner.cs`: a public class with its constructor and the method signature `Task RunAttemptAsync(...)` that throws `NotImplementedException`. T035 writes the body. The shell lets T017 register the type now. Depends on T012 and T013.
- [ ] T015 Create the type shell `src\GameBot.Emulator\DeviceCaptureGate.cs`: a public class with the method signatures `TryBegin`, `Completed`, `Failed`, `TimedOut`, `Clear` that throw `NotImplementedException`. T045 writes the bodies.
- [ ] T016 Add the test fakes that later tasks share: a fake `IDeviceRecoveryCoordinator` and a fake `TimeProvider` helper in `tests\unit\Queues\Fakes\FakeDeviceRecoveryCoordinator.cs`.
- [ ] T017 Register ALL new services in ONE place, `src\GameBot.Service\GameBotServiceSetup.cs`: `IDeviceRecoveryCoordinator` with `DeviceRecoveryCoordinator` (singleton), `QueueDeviceRecoveryRunner` (singleton or factory, as the runner needs), and `DeviceCaptureGate` (singleton, also injected into the capture service and into `AdbSessionDirectCapture`). Keep `Program.cs` unchanged. No later task edits this file. Depends on T013, T014, and T015.

**Checkpoint**: The settings, the claims, the reboot result, the coordinator, and all registrations exist. The user stories can start.

---

## Phase 3: User Story 1 - Alert the operator about a not-live device (Priority: P1) MVP

**Goal**: One "device not live" message after the alert time. One "device live again" message after the device is live.

**Independent Test**: Block the capture of a device, start a queue with no `deviceRecovery` and a short alert time. One message arrives, `health.lastNotificationAt` is set, and no second message arrives in 10 minutes.

### Tests for User Story 1

- [ ] T018 [P] [US1] Write failing tests for `QueueAlert` formatting (three kinds, blank queue name uses the queue ID) in `tests\unit\Notifications\NotificationMessageFormatterTests.cs`.
- [ ] T019 [P] [US1] Write failing tests for `SendAlert` in `tests\unit\Notifications\QueueNotificationWorkerTests.cs`. Cover: sent to all enabled targets; ignores `notificationLevel`; never dropped; no target logs at Information level and does not call the callback; the callback gets `succeeded`.
- [ ] T020 [P] [US1] Write failing tests for the alert logic in `tests\unit\Queues\QueueLivenessWatchTests.cs`. Cover: one alert after `AlertAfterMs`; no second alert; one "live again" after an alert; no message when the device is live before the alert time; no "live again" when the queue stops with no alert; a new episode gives a new alert; a service restart gives a fresh timer and exactly one alert.
- [ ] T021 [P] [US1] Write failing tests for the derivation of `recoveryState` in `tests\unit\Queues\DeviceLivenessReaderRecoveryStateTests.cs`. Assert each of the three values: `idle` (no open episode, or no recovery runs and the attempts are not used up), `running` (`recoveryRunning` is true), and `exhausted` (`attempts` reached `maxAttempts` and the state is `not_live`). Also assert `alertSent` and `recoveryAttempts`.
- [ ] T022 [P] [US1] Write a contract test for the read-only members `alertSent`, `recoveryAttempts`, and `recoveryState` of `health.deviceLiveness` in `tests\contract\Queues\QueueDeviceLivenessContractTests.cs`. Assert that `recoveryState` is always one of `idle`, `running`, `exhausted`, and is `idle` for a queue with no open episode.
- [ ] T023 [P] [US1] Write an integration test `tests\integration\DeviceNotLiveAlertTests.cs`: a blocked device, no failure policy, one alert, `lastNotificationAt` set, no second message, then one "live again" message.

### Implementation for User Story 1

- [ ] T024 [US1] Add the record `QueueAlert` (`QueueId`, `Kind` with `NotLive`, `LiveAgain`, `RecoveryFailed`, `Reason`, `RaisedAt`, `OnCompleted`) and the new work kind to `src\GameBot.Service\Services\Notifications\NotificationTypes.cs`. Add `SendAlert(QueueAlert)` to `INotificationDispatcher` in the same file.
- [ ] T025 [US1] Implement `SendAlert` in `src\GameBot.Service\Services\Notifications\QueueNotificationDispatcher.cs`. Write to the same channel. Never drop the item. Do not count it in `MaxQueuedJobs`.
- [ ] T026 [US1] Add the alert text methods (`🔴 <queue> : device not live (<reason>)`, `🟢 <queue> : device live again`, `🔴 <queue> : device recovery failed after <n> attempts`) to `src\GameBot.Service\Services\Notifications\NotificationMessageFormatter.cs`.
- [ ] T027 [US1] Handle the alert work kind in `src\GameBot.Service\Services\Notifications\QueueNotificationWorker.cs`. Read the queue name, send to all enabled targets, ignore `notificationLevel` and the streak state, call `OnCompleted`. With no target, log "no target available" and do not fail.
- [ ] T028 [US1] Update `src\GameBot.Service\Services\QueueExecution\QueueLivenessWatch.cs` (`CheckOnceAsync`): after `Observe`, claim and send the `NotLive` alert when the episode is older than `AlertAfterMs`. Claim and send `LiveAgain` when `liveAgainPending` is set. The `OnCompleted` callback sets `health.lastNotificationAt`, `lastNotificationSucceeded`, and `lastNotificationError` on the queue run handle in `src\GameBot.Service\Services\QueueExecution\QueueRunHandle.cs`.
- [ ] T029 [US1] Add `alertSent`, `recoveryAttempts`, and `recoveryState` to `src\GameBot.Service\Contracts\Queues\QueueDeviceLivenessResponse.cs`. Fill them in `src\GameBot.Service\Services\QueueExecution\DeviceLivenessReader.cs` from the episode (research R-022).

**Checkpoint**: User Story 1 works alone and gives the MVP.

---

## Phase 4: User Story 2 - Optional automatic recovery by instance reboot (Priority: P2)

**Goal**: A queue with `deviceRecovery.action` = `reboot-instance` reboots its instance after `afterMs`, binds a new session, and runs the held firings.

**Independent Test**: Repeat the test of story 1 with `action` = `reboot-instance`. The instance reboots once, the held firings run, and a "device live again" message arrives.

**Dependency**: This phase needs T012 to T017 (coordinator interface, coordinator, runner shell, registrations) from Phase 2.

### Tests for User Story 2

- [ ] T030 [P] [US2] Write failing contract tests in `tests\contract\Queues\QueueDeviceRecoveryContractTests.cs`. Cover: create, update, get, and duplicate show `deviceRecovery`; absent member on `PUT` gives null; each invalid value gives HTTP 400 (never 500) with the text of `specs\121-device-not-live-alert-recovery\contracts\device-recovery-api.md`; `reboot-instance` with no `emulatorInstanceName` gives HTTP 400.
- [ ] T031 [P] [US2] Write failing tests for the runner in `tests\unit\Queues\QueueDeviceRecoveryRunnerTests.cs`. Use the fake from T016, a fake device probe, and a fake liveness service. Cover: no reboot when `action` is `none` or the field is null; no reboot before `afterMs`; no reboot when the device recovers first; wait for probe `device` state, then rebind, then `live`; `cooldownMs` between attempts; stop at `maxAttempts` with one "recovery failed" alert; `recoveryState` is `running` during an attempt, `idle` after a failed attempt that does not use up the attempts, and `exhausted` after the last failed attempt; a reboot failure counts as a failed attempt and logs the cause; a stop or delete during the reboot frees the slot; two queues on one instance each rebind and count their own attempt.
- [ ] T032 [P] [US2] Write failing tests for the session rebind in `tests\unit\Queues\QueueExecutionServiceRebindTests.cs`. The run loop follows `handle.SessionId`, the gate and the watch read the same value, and held firings stay in the loop state.

### Implementation for User Story 2

- [ ] T033 [P] [US2] Create `src\GameBot.Service\Contracts\Queues\QueueDeviceRecoveryDto.cs` with the members `action`, `afterMs`, `maxAttempts`, `cooldownMs`. Add the member `deviceRecovery` to `src\GameBot.Service\Contracts\Queues\CreateQueueRequest.cs`, `src\GameBot.Service\Contracts\Queues\UpdateQueueRequest.cs`, `src\GameBot.Service\Contracts\Queues\QueueResponse.cs`, and `src\GameBot.Service\Contracts\Queues\QueueDetailResponse.cs`.
- [ ] T034 [US2] In `src\GameBot.Service\Endpoints\QueuesEndpoints.cs`, run `QueueDeviceRecoveryValidator` before save on create and update (HTTP 400 on error), map the DTO both ways, set null when `deviceRecovery` is absent on update, and copy the field in the duplicate route.
- [ ] T035 [US2] Add `RebindSessionAsync(queue, handle)` to `src\GameBot.Service\Services\QueueExecution\QueueExecutionService.cs` (stop the old capture loop and session, call `BindQueueSession`, set `handle.SessionId`). Make `EnsureSessionBound` follow `handle.SessionId`. Make `TryGateOnLivenessAsync` read the handle value. Expose the method through `src\GameBot.Service\Services\QueueExecution\IQueueExecutionService.cs`.
- [ ] T036 [US2] Write the body of `src\GameBot.Service\Services\QueueExecution\QueueDeviceRecoveryRunner.cs` (the shell is from T014). It calls `IDeviceRecoveryCoordinator.RebootInstanceAsync` for the reboot. Then it polls `IEmulatorDeviceProbe.IsResponsiveAsync` every 5 s up to `RebootReadyTimeoutMs`, calls `RebindSessionAsync`, and polls the liveness report every 2 s until `live`. It updates `attempts`, `lastAttemptEndedAt`, and `recoveryRunning` in the episode. It uses the cancellation token of the queue run. The probe interface is in `src\GameBot.Service\Services\EnsureEmulatorRunning\IEmulatorDeviceProbe.cs`. Depends on T035.
- [ ] T037 [US2] Update `src\GameBot.Service\Services\QueueExecution\QueueLivenessWatch.cs`: start the runner when the episode is older than `afterMs`, `attempts` < `maxAttempts`, `cooldownMs` has passed, and no recovery runs. After the last failed attempt, claim `TryClaimRecoveryFailed` and send the `RecoveryFailed` alert. The queue stays Running.
- [ ] T038 [P] [US2] Write the integration test `tests\integration\DeviceRecoveryFlowTests.cs`: a blocked device, `reboot-instance`, one reboot, a new session, the held firings run, one "live again" message; also the stop-during-reboot and the two-queues-one-instance cases.

**Checkpoint**: Stories 1 and 2 work.

---

## Phase 5: User Story 3 - One recovery at a time (Priority: P2)

**Goal**: At most one reboot start at a time across all queues, with at least the stagger time between starts.

**Independent Test**: Block two devices together, recovery on for both queues. The two reboot starts are at least one stagger time apart.

The coordinator exists from Phase 2. This phase proves the rule across queues and through the runner.

- [ ] T039 [P] [US3] Write the integration test `tests\integration\DeviceRecoveryStaggerTests.cs`. Two queues on two instances need recovery together. The second reboot starts after the first ends and after `RecoveryStaggerMs`. A recovery that ends early still keeps the stagger for the next one.
- [ ] T040 [US3] (VERIFICATION ONLY) Run `tests\integration\DeviceRecoveryStaggerTests.cs` and `tests\unit\Queues\DeviceRecoveryCoordinatorTests.cs`. Both must pass. If a test fails, fix `src\GameBot.Service\Services\QueueExecution\DeviceRecoveryCoordinator.cs` or the runner call in `src\GameBot.Service\Services\QueueExecution\QueueDeviceRecoveryRunner.cs`.

**Checkpoint**: Stories 1 to 3 work.

---

## Phase 6: User Story 4 - No pile-up of captures on a blind device (Priority: P3)

**Goal**: At most one unfinished capture for each device. A timed-out capture keeps the device in state `Suspect`, so no new `screencap` process starts.

**Independent Test**: Block the capture of a device. The `screencap` process count on the device stays at 2 or less over 10 minutes.

**Dependency**: This phase needs Phase 2 only (shell T015 and registration T017). It can run beside Phases 3 to 5.

### Tests for User Story 4

- [ ] T041 [P] [US4] Write failing tests for the gate in `tests\unit\Emulator\DeviceCaptureGateTests.cs`. Cover: `Idle` to `InFlight` to `Idle`; `TimedOut` gives `Suspect`; a second `TryBegin` is refused in `InFlight` and `Suspect`; the serial key ignores case; a clean device check clears `Suspect`; `StartCapture` for the serial clears `Suspect`; a check error keeps `Suspect`; a late capture end allows captures again.
- [ ] T042 [P] [US4] Write failing tests for the capture loop in `tests\unit\Emulator\BackgroundScreenCaptureGateTests.cs`. A blocked provider gets one call only; a healthy provider keeps its capture rate.
- [ ] T043 [P] [US4] Write failing tests for `HasRunningScreencapAsync` in `tests\unit\Emulator\AdbClientScreencapCheckTests.cs` (empty answer with a clean exit gives false; a process ID gives true; an error or a time-out gives unknown).
- [ ] T044 [P] [US4] Write failing tests that `AdbSessionDirectCapture` returns false at once, and starts no process, for a `Suspect` serial, in `tests\unit\Liveness\AdbSessionDirectCaptureGateTests.cs`.

### Implementation for User Story 4

- [ ] T045 [US4] Write the bodies of `src\GameBot.Emulator\DeviceCaptureGate.cs` (the shell is from T015): a `ConcurrentDictionary<string, DeviceCaptureState>` keyed by serial (ignore case), with `TryBegin`, `Completed`, `Failed`, `TimedOut`, `Clear`, `suspectSince`, and `lastCheckAt` (data-model section 6).
- [ ] T046 [P] [US4] Add `HasRunningScreencapAsync(serial)` (`adb shell pidof screencap`, limit `TransportCheckTimeoutMs`) to `src\GameBot.Emulator\Adb\AdbClient.cs`.
- [ ] T047 [US4] Update `SessionCaptureLoop` in `src\GameBot.Emulator\Session\BackgroundScreenCaptureService.cs`: call `TryBegin` before each capture; report `Completed`, `Failed`, or `TimedOut`; while the serial is `Suspect`, sleep and run the device check every `CaptureStallLimitMs`; make `StartCapture` clear the serial. Take the gate by constructor injection (registered in T017).
- [ ] T048 [US4] Update `src\GameBot.Service\Services\Liveness\AdbSessionDirectCapture.cs` to ask the gate (constructor injection, registered in T017) and return false at once for a `Suspect` serial (research R-014).
- [ ] T049 [US4] (VERIFICATION ONLY) Run the existing tests for feature 106 with no change: `tests\unit\Sessions\DeviceLivenessEvaluatorTests.cs`, `tests\unit\Liveness\SessionLivenessServiceTests.cs`, and `tests\contract\Queues\QueueDeviceLivenessContractTests.cs`. They must pass (FR-015). If one fails, fix the new code, not the test.

**Checkpoint**: All four stories work.

---

## Phase 7: Polish & Cross-Cutting Concerns

- [ ] T050 [P] Update `src\GameBot.Service\Swagger\DeviceLivenessSchemaFilter.cs` and `src\GameBot.Service\Swagger\QueueHealthSchemaFilter.cs`: describe `alertSent`, `recoveryAttempts`, `recoveryState` (values `idle`, `running`, `exhausted`), the alert, and the new statement "the queue can reboot the instance when deviceRecovery is on". Remove the old statement that the queue never repairs the device (FR-016).
- [ ] T051 [P] Add a schema description for `deviceRecovery` and its members in the new file `src\GameBot.Service\Swagger\QueueDeviceRecoverySchemaFilter.cs`. Register it in `src\GameBot.Service\Swagger\SwaggerConfig.cs`. (This is a Swagger registration, not a service registration. T017 rule covers `GameBotServiceSetup.cs` only.)
- [ ] T052 [P] Add an OpenAPI test for the new descriptions in `tests\contract\DeviceLivenessOpenApiTests.cs`.
- [ ] T053 [P] Update `docs\architecture.md` (FR-018): describe the alert, `deviceRecovery`, the recovery coordinator, the runner, and the capture gate. Refresh the "Last reviewed" date to the date of the change.
- [ ] T054 [P] Update `specs\STATUS.md` (FR-018): add or change the entry for spec 121 (`specs\121-device-not-live-alert-recovery`) with the Status Implemented.
- [ ] T055 [P] Update `specs\106-wedged-device-liveness\spec.md` (FR-018): set the `Status` line so that it points to spec 121 as the spec that iterates it (for example "Implemented (iterated by 121)"). Add a pointer to spec 121 where the text says the queue does not repair the device.
- [ ] T056 Set the `Status` line of `specs\121-device-not-live-alert-recovery\spec.md` to Implemented (FR-018). Do this when T001 to T055 are done.
- [ ] T057 (VERIFICATION ONLY) Run the build and the full test suite: `& "C:\Program Files\dotnet\dotnet.exe" test "C:\src\GameBot\GameBot.sln"`. Fix all failures.
- [ ] T058 (VERIFICATION ONLY) Do the two manual checks in `specs\121-device-not-live-alert-recovery\quickstart.md`: the `screencap` process count on the device (section 5) and the recovery timing (sections 3 and 4).

---

## Dependencies & Execution Order

### Phase Dependencies

- Phase 1 (Setup): no dependency.
- Phase 2 (Foundational): needs Phase 1. It blocks all stories. Inside it: T012 (interface) before T013 (coordinator); T013 before T014 and T017; T015 before T017; T017 last.
- Phase 3 (US1): needs Phase 2.
- Phase 4 (US2): needs Phase 2 (T007, T010, T012 to T017) and the alert send path from Phase 3 (T024 to T028) for the "recovery failed" alert.
- Phase 5 (US3): needs Phase 4 (the runner calls the coordinator).
- Phase 6 (US4): needs Phase 2 only. It can run in parallel with Phases 3 to 5.
- Phase 7 (Polish): needs all stories. T053 to T055 and T056 come last in the text work.

### Order Rule

`IDeviceRecoveryCoordinator` (T012) and `DeviceRecoveryCoordinator` (T013) MUST end before `QueueDeviceRecoveryRunner` (T014 shell, T036 body) starts.

### Within Each Story

- Tests first, and they must fail. Then code.
- Domain and contracts, then services, then endpoints, then integration.

### Parallel Opportunities

- T003, T004, T006, T008, T009, and T011 touch different files.
- In Phase 3, T018 to T023 are parallel.
- In Phase 4, T030 to T033 are parallel.
- In Phase 6, T041 to T044 are parallel, and T046 is parallel to T045.
- Phase 6 can run beside Phases 3 to 5 with a second worker. No conflict on `GameBotServiceSetup.cs`, because only T017 edits it.
- T050 to T055 are parallel.

## Parallel Example: User Story 4

```text
T041 tests\unit\Emulator\DeviceCaptureGateTests.cs
T042 tests\unit\Emulator\BackgroundScreenCaptureGateTests.cs
T043 tests\unit\Emulator\AdbClientScreencapCheckTests.cs
T044 tests\unit\Liveness\AdbSessionDirectCaptureGateTests.cs
```

## Implementation Strategy

### MVP First (User Story 1)

1. Finish Phase 1 and Phase 2.
2. Finish Phase 3.
3. Stop and check: the alert works with recovery off. This gives the most value at the lowest risk.

### Incremental Delivery

1. Add US2 (recovery). Test it on a spare instance.
2. Add US3 (stagger proof).
3. Add US4 (capture gate). It is a separate protection and it can ship first if needed.
4. Finish Phase 7.
