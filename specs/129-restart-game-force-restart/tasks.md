# Tasks: Restart the Game From a Sequence

**Input**: Design documents from `specs\129-restart-game-force-restart\`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts\ensure-game-running-force-restart.md, quickstart.md
**Tests**: Tests are in scope. The plan says tests come before the code. Write each test first and see it fail.
**Organization**: Tasks are grouped by user story. All paths are relative to `C:\src\GameBot`.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: The task can run in parallel (different file, no open dependency).
- **[Story]**: The user story of the task (US1 to US5).
- A task that edits a file that an earlier task also edits is sequential. It has no `[P]` and it says "(after Txxx)".
- Write all new text (comments, messages, docs) in ASD-STE100 Simplified Technical English.
- Do not commit in these tasks. The commit step is outside this list.

---

## Phase 1: Setup

**Purpose**: Make sure the build is green before any change.

- [X] T001 Run `dotnet build` for `src\GameBot.Service\GameBot.Service.csproj` and for the test projects in `tests\unit`, `tests\contract`, `tests\integration`. Run `npm run build` and `npx jest` in `src\web-ui`. Record that all pass (Constitution gate).

---

## Phase 2: Foundational (blocks all user stories)

**Purpose**: Shared types, interface members, and test fakes. Without them nothing builds.

- [X] T002 [P] Create `tests\unit\Sequences\EnsureGameRunningPayloadTests.cs`: the reader accepts `true`, `false`, absent key (gives false), `JsonElement` true and `JsonElement` false. It rejects text, number, null, object, array, and `{{x}}` text, each with an error message that names `forceRestart`. The tests fail until T003.
- [X] T003 [P] Create `src\GameBot.Domain\Commands\EnsureGameRunning\EnsureGameRunningPayload.cs` (namespace `GameBot.Domain.Commands.EnsureGameRunning`): `TryRead(action, out bool forceRestart, out string? error)`. Accept a CLR `bool` and a `JsonElement` True or False. Reject text, number, null, object, array, and `{{name}}` text. The key is optional. Follow the style of `src\GameBot.Domain\Commands\Notify\NotifyPayload.cs`. (T002 must pass after this task.)
- [X] T004 [P] Create `src\GameBot.Service\Services\EnsureGameRunning\EnsureGameRunningRestartOptions.cs`: an `internal` record with `RestartForegroundWait` (30 s), `RestartPollInterval` (1 s), `RestartSettleDelay` (1 s), `RestartStopTimeout` (10 s), `RestartStartTimeout` (10 s), and `RestartProbeTimeout` (5 s). These are the defaults.
- [X] T005 [P] Add `ForceStopAppAsync(packageName, ct)` (`shell am force-stop <package>`) to `src\GameBot.Emulator\Adb\AdbClient.cs`. It returns the exit-code tuple like `LaunchAppAsync`.
- [X] T006 Add `ForceStopAppAsync(deviceSerial, packageName, ct)` and `TryLaunchAppAsync(deviceSerial, packageName, ct)` (both return `Task<bool>`) to `src\GameBot.Service\Services\EnsureGameRunning\IAdbGameOperations.cs`. Keep `LaunchAppAsync` as is. (after T005)
- [X] T007 Implement both methods in `src\GameBot.Service\Services\EnsureGameRunning\AdbGameOperations.cs` with `new AdbClient().WithSerial(deviceSerial)`. Return false for a blank serial, for an unsafe package name, and off Windows, before any process starts. (after T006)
- [X] T008 [P] Add the results `Restarted`, `RestartNoDevice` (`restart_no_device`), `RestartStopFailed` (`restart_stop_failed`), `RestartStartFailed` (`restart_start_failed`), and `RestartForegroundTimeout` (`restart_foreground_timeout`) to `src\GameBot.Service\Services\EnsureGameRunning\EnsureGameRunningActionResult.cs`. `IsSuccess` is true for `GameRunning` and `Restarted`.
- [X] T009 Add `RestartAsync(sessionId, ct)` to `src\GameBot.Service\Services\EnsureGameRunning\IEnsureGameRunningActionHandler.cs`. (after T008)
- [X] T010 Move `FakeAdbGameOperations` out of `tests\unit\Services\EnsureGameRunning\EnsureGameRunningActionHandlerTests.cs` into the new file `tests\unit\Services\EnsureGameRunning\FakeAdbGameOperations.cs` as an `internal` class. Add `ForceStopAppAsync` and `TryLaunchAppAsync`. Record `StoppedSerials`, `LaunchSerials`, and the order of calls. Let each call fail, throw, or never return. (after T006)
- [X] T011 Add `RestartAsync` (with a call count and a settable result) to `StubHandler` in `tests\unit\Commands\CommandExecutorEnsureGameRunningTests.cs`. Add `RestartAsync` that throws `NotSupportedException` to `ScriptedEnsureGameRunning` in `tests\unit\Services\EnsureGameRunning\GameForegroundGuardTests.cs` and to `FakeEnsureGameRunning` in `tests\unit\Queues\QueueExecutionServiceTests.cs`. (after T009)
- [X] T012 Build `tests\unit`, `tests\contract`, and `tests\integration`. Confirm that all projects compile and that the old tests still pass. Then record no change to these test files, because they define no fake of `IAdbGameOperations` or `IEnsureGameRunningActionHandler`: `tests\unit\Queues\QueueExecutionServiceRunStatisticsTests.cs`, `tests\integration\Queues\ForegroundGuardWiringIntegrationTests.cs`, `tests\unit\Sequences\SequenceRunnerGameActionDispatchTests.cs`, `tests\unit\Domain\PrimitiveActionValidationServiceTests.cs`. (after T007, T010, T011)

**Checkpoint**: The solution builds. No behavior has changed yet.

---

## Phase 3: User Story 1 - Restart a stuck game from a sequence (P1) MVP

**Goal**: A sequence action step or a command step with `forceRestart` true stops the game on the session device, starts it, waits for the foreground, and reports `restarted`.

**Independent Test**: Run one step with `forceRestart` true on a session whose game runs. The fake device shows stop, start, foreground, and the outcome `restarted`.

### Tests for User Story 1 (write first, they must fail)

- [X] T013 [P] [US1] Create `tests\unit\Services\EnsureGameRunning\EnsureGameRunningRestartTests.cs`: the call order is stop, settle, start, poll. `RestartAsync` returns `restarted` for a running game and for a stopped game. The stop receives the session serial only (FR-017). A blank serial gives `restart_no_device` and no ADB call.
- [X] T014 [P] [US1] Create `tests\unit\Sequences\SequenceExecutionEnsureGameRestartTests.cs` (the restart dispatch test): `forceRestart` true calls `RestartAsync` and the dispatch result has the outcome `restarted`. `forceRestart` false and an absent key call `ExecuteAsync`. A non-boolean `forceRestart` at run time (text, number, null, object) gives a `failed` result, the message names `forceRestart`, the fake handler gets zero calls, the fake device gets zero calls, and nothing is thrown (FR-010).
- [X] T015 [P] [US1] Create `tests\unit\Commands\CommandExecutorForceRestartTests.cs`: a command step with `ForceRestart` true calls `RestartAsync` and not `ExecuteAsync`. The outcome is status `executed` with reason `restarted`. The readiness wait runs after a good restart. The JSON of a step with `ForceRestart` false has no `forceRestart` key. An old stored command step without the key reads as false and calls `ExecuteAsync` (FR-013).

### Implementation for User Story 1

- [X] T016 [US1] In `src\GameBot.Service\Services\EnsureGameRunning\EnsureGameRunningActionHandler.cs`: move the resolve steps into one private method that `ExecuteAsync` and `RestartAsync` share. Add the optional last constructor parameter `EnsureGameRunningRestartOptions? options = null`. Add `RestartAsync`: read `session.DeviceSerial` (blank gives `RestartNoDevice`), stop, wait `RestartSettleDelay`, start, poll the foreground check until `RestartForegroundWait` ends, and return `Restarted`. (after T004, T007, T008, T009)
- [X] T017 [US1] In `src\GameBot.Service\GameBotServiceSetup.cs` (lines 166 and 167), keep the two registrations. Add one comment line that names the restart options. Confirm that `GameForegroundGuard` (line 170) still builds from the handler interface. (after T016)
- [X] T018 [P] [US1] Add `public bool ForceRestart { get; init; }` with `JsonIgnore(WhenWritingDefault)` to `EnsureGameRunningConfig` in `src\GameBot.Domain\Commands\CommandStep.cs`.
- [X] T019 [US1] In `src\GameBot.Domain\Parameters\CommandStepResolver.cs` (line 126), copy `ForceRestart` into the new `EnsureGameRunningConfig`. Add no `FieldTemplates` key. (after T018)
- [X] T020 [P] [US1] Add `bool? ForceRestart` to `EnsureGameRunningConfigDto` in `src\GameBot.Service\Models\Commands.cs` (line 146).
- [X] T021 [US1] In `src\GameBot.Service\Endpoints\CommandsEndpoints.cs`: change `ToDomainEnsureGame` (line 234) to keep the config when `ForceRestart` is true and there is no readiness image. Update `ToResponseEnsureGame` (line 249) to return the flag. Update the config check at line 136 so that a config with only `ForceRestart` true is valid. (after T018, T020)
- [X] T022 [P] [US1] In `src\GameBot.Service\Endpoints\StepsEndpoints.cs`, map the EnsureGameRunning config with the flag in the validator (line 89) and in `ToDomainStep` (line 113). (after T018, T020)
- [X] T023 [US1] In `src\GameBot.Service\Services\CommandExecutor.cs` (line 388), in the `EnsureGameRunning` branch call `RestartAsync` when `step.EnsureGameRunning?.ForceRestart == true`, and `ExecuteAsync` in all other cases. Map `Restarted` to status `executed`, reason `restarted`, and run the readiness wait after a good restart. (after T016, T018)
- [X] T024 [US1] In `src\GameBot.Service\Services\SequenceExecution\SequenceExecutionService.cs` (line 787), pass `action` to `DispatchEnsureGameRunningAsync(action, sessionId, ct)`. The method calls `EnsureGameRunningPayload.TryRead` first. A read error returns `ActionDispatchResult("failed", "ensure-game-running forceRestart must be true or false")` with no device call and no throw. When the flag is true, call `RestartAsync` and return the outcome `restarted` for `Restarted`. When the flag is false, call `ExecuteAsync` as before. Map each failed result to a `failed` dispatch result with its reason code. Do not change `DispatchGoToHomeScreenAsync`. (after T003, T016)
- [X] T025 [US1] Run the tests of T013, T014, and T015. Confirm that they pass. (after T017, T019, T021, T022, T023, T024)

**Checkpoint**: A restart step works for a sequence action step and a command step.

---

## Phase 4: User Story 2 - Existing sequences do not change (P1)

**Goal**: A step without `forceRestart`, or with `forceRestart` false, behaves exactly as before.

**Independent Test**: Run the step with no option and with `forceRestart` false on a running game. The step returns at once, with the old outcome and no stop call.

### Tests for User Story 2

- [X] T026 [US2] In `tests\unit\Services\EnsureGameRunning\EnsureGameRunningRestartTests.cs`, add plain-path tests: `ExecuteAsync` on a running game makes no stop call and no launch call. `ExecuteAsync` with an unsafe package name gives the same result as before this feature. (after T013)
- [X] T027 [P] [US2] Create `tests\contract\Sequences\GoHomeAndEnsureEmulatorUnchangedTests.cs` (FR-015, SC-007): the "go to home screen" dispatch and the "ensure emulator running" dispatch give the same outcomes as before. The swagger path list and the action type list have no new member.
- [X] T028 [P] [US2] Create `tests\unit\Sequences\FileSequenceRepositoryEnsureGamePayloadTests.cs`: an old stored sequence step of type `ensure-game-running` without the `forceRestart` key loads, and `EnsureGameRunningPayload.TryRead` gives false for it (FR-013).

### Implementation for User Story 2

- [X] T029 [US2] Run all existing tests in `tests\unit\Services\EnsureGameRunning`, `tests\unit\Commands`, and `tests\contract\Sequences`, and the new tests of T026, T027, and T028. Confirm that they pass. Record no code change for this story, because the plain path (`ExecuteAsync`) was not edited. (after T026, T027, T028)

**Checkpoint**: The plain path is proven unchanged.

---

## Phase 5: User Story 3 - Later steps can tell a restart from "already running" (P2)

**Goal**: A sequence condition reads `restarted` for a sequence action step. A condition on `success` also matches it. A command step shows `restarted` in its own outcome list only.

**Independent Test**: Run a restart step and a plain step. A condition on `restarted` matches only the restart step.

> **One commit**: T033, T034, T035, T036, T037, T038, T039, T040 MUST land in one commit. The old asserts fail after the code change, and the new asserts fail before it.

### Tests for User Story 3 (write first)

- [X] T030 [P] [US3] Create `tests\unit\Sequences\SequenceRunnerRestartedOutcomeTests.cs`: `restarted` and `success` both match a restarted action step. Only `success` matches an already running step. A command step with a restart gives `success` to a sequence condition.
- [X] T031 [P] [US3] In `tests\unit\ExecutionLogs\ExecutionLogServiceMapStepStatusTests.cs`, add a test: `restarted` maps to `success`.
- [X] T032 [P] [US3] In `src\web-ui\src\lib\__tests__\perStepConditionValidation.spec.ts`, add a test: `restarted` is a valid `expectedState`.

### Implementation for User Story 3

- [X] T033 [P] [US3] Change the asserts for the allowed `expectedState` list and its message text in `tests\unit\Sequences\CompositeConditionValidationTests.cs`. The new list includes `restarted`.
- [X] T034 [P] [US3] Change the same asserts in `tests\unit\Sequences\ConditionReferenceScopeValidationTests.cs`.
- [X] T035 [P] [US3] Change the same asserts in `tests\unit\Sequences\IfValidationTests.cs`.
- [X] T036 [P] [US3] Change the same asserts in `tests\unit\Sequences\LoopValidationTests.cs`.
- [X] T037 [P] [US3] Create `src\GameBot.Domain\Services\StepOutcomeStates.cs`: the list of allowed outcome states (with `restarted`) and `Matches(actual, expected)`. Expected `success` also matches `restarted`. Expected `restarted` matches only `restarted`.
- [X] T038 [US3] In `src\GameBot.Domain\Services\SequenceStepConditionEvaluator.cs` (line 192), use `StepOutcomeStates.Matches`. (after T037)
- [X] T039 [US3] In `src\GameBot.Domain\Services\SequenceStepValidationService.cs`, use the shared state list in the set at line 14 and in the messages at lines 264 and 386. (after T037)
- [X] T040 [US3] In `src\GameBot.Domain\Services\CompositeConditionValidator.cs`, use the shared state list in the set at line 26 and in the message at line 127. (after T037)
- [X] T041 [US3] In `src\GameBot.Domain\Services\SequenceRunner.cs` (primitive action branch, line 711), write `restarted` to `stepOutcomes` when the dispatch outcome is `restarted`. Add `IsForceRestartAction(action)` next to `IsServiceLevelAction` (line 845). It uses `EnsureGameRunningPayload.TryRead` and returns true only for an `ensure-game-running` action with `forceRestart` true. (after T003, T037)
- [X] T042 [P] [US3] In `src\GameBot.Service\Services\ExecutionLog\ExecutionLogService.cs`, add `"restarted" => "success"` to `MapStepStatus`.
- [X] T043 [P] [US3] In `src\GameBot.Service\Swagger\ConditionReferenceScopeSchemaFilter.cs` (line 44), update the `commandOutcome` text to list `restarted`.
- [X] T044 [P] [US3] In `src\web-ui\src\types\sequenceFlow.ts` (line 92), add `restarted` to the `expectedState` type.
- [X] T045 [US3] In `src\web-ui\src\lib\validation.ts` (line 241), add `restarted` to the state check. (after T044)
- [X] T046 [US3] Run the tests of T030 to T036 with `dotnet test` and run `npx jest` in `src\web-ui`. Confirm that they pass. (after T038, T039, T040, T041, T042, T043, T045)

**Checkpoint**: A later condition reads `restarted`.

---

## Phase 6: User Story 4 - Clear failure when the restart cannot finish (P2)

**Goal**: Each failure stage gives a distinct reason code. The step never reports `restarted` on failure. Total time is bounded.

**Independent Test**: Make the stop fail, the start fail, and the foreground never come. Each case fails with its own reason.

### Tests for User Story 4 (write first, they must fail)

- [X] T047 [US4] In `tests\unit\Services\EnsureGameRunning\EnsureGameRunningRestartTests.cs`, add failure tests: a stop with a non-zero exit code or an exception gives `restart_stop_failed` and no start call. A start that returns false or throws gives `restart_start_failed`. A game that never reaches the foreground gives `restart_foreground_timeout`. A cancel from the caller rethrows `OperationCanceledException` and gives no failure code. (after T026)
- [X] T048 [US4] In the same file, add time tests with small test options: a stop call that never returns ends at `RestartStopTimeout` with `restart_stop_failed`. A start call that never returns ends at `RestartStartTimeout` with `restart_start_failed`. A probe that never returns counts as a miss. The normal case ends within `RestartForegroundWait` plus 10 s (SC-002). The worst case, with fake calls that never return, ends under the sum of all limits (the hard bound). (after T047)
- [X] T049 [US4] In the same file, add no-device-call tests for `RestartAsync`: `no_queue_context`, `no_linked_game`, `no_package_name`, `platform_unsupported`, and an unsafe package name (gives `no_package_name`). Each case gives zero stop calls and zero launch calls (FR-008). (after T048)
- [X] T050 [P] [US4] In `tests\unit\Sequences\SequenceExecutionEnsureGameRestartTests.cs`, add tests: each failure result of `RestartAsync` gives a `failed` dispatch result with its reason code, and never the outcome `restarted`. (after T014)
- [X] T051 [P] [US4] In `tests\unit\Commands\CommandExecutorForceRestartTests.cs`, add tests: each failure result of `RestartAsync` gives a failed command outcome with its reason code, and never the reason `restarted`. (after T015)

### Implementation for User Story 4

- [X] T052 [US4] In `src\GameBot.Service\Services\EnsureGameRunning\EnsureGameRunningActionHandler.cs`, make `RestartAsync` bounded. Run each ADB call under a linked `CancellationTokenSource` with `CancelAfter(limit)`. A stop timeout gives `RestartStopFailed`. A start timeout gives `RestartStartFailed`. A probe timeout is a miss, and the probe limit is the smaller of `RestartProbeTimeout` and the time left to the foreground deadline. A foreground deadline gives `RestartForegroundTimeout`. A cancel from the caller (`ct.IsCancellationRequested`) rethrows `OperationCanceledException`. In the restart resolve step, return `no_package_name` for an unsafe package name before any device call. Do not change the plain `ExecuteAsync` path. (after T016)
- [X] T053 [US4] Run the tests of T047 to T051. Confirm that they pass. (after T052)

**Checkpoint**: Every failure has a distinct reason, and the restart always ends in bounded time.

---

## Phase 7: User Story 5 - The option is validated everywhere (P2)

**Goal**: Valid input is accepted and returned unchanged. Invalid input gives 400, never 500. The API description, the typed variant, and the step-through preview know the option.

**Independent Test**: Send valid and invalid values to each place. Check the response, the API description, and the preview.

### Tests for User Story 5 (write first, they must fail)

- [X] T054 [P] [US5] Create `tests\unit\Domain\PrimitiveEnsureGameRunningActionRoundTripTests.cs`: the typed variant `PrimitiveEnsureGameRunningAction` serializes and deserializes with `ForceRestart` true staying true. `ToActionPayload` and `TryFromActionPayload` agree. Full chain: typed variant, payload, file repository save, read, run with a fake handler. The fake handler sees `RestartAsync` (SC-008).
- [X] T055 [US5] In `tests\unit\Sequences\FileSequenceRepositoryEnsureGamePayloadTests.cs`, add tests: the repository guard throws `InvalidOperationException` for a bad value. A saved `forceRestart` true is true after the read from the file (FR-016). A step `parameterBindings` entry that targets `forceRestart` is rejected. (after T028)
- [X] T056 [P] [US5] Create `tests\contract\Sequences\EnsureGameRunningForceRestartContractTests.cs`: true and false are accepted and read back. The values `"true"`, `1`, `null`, `{}`, `[]`, and `"{{x}}"` give 400 and never 500, for a top-level step, a loop-body step, and an if-branch step. A step `parameterBindings` entry that targets `forceRestart` gives 400. A command create and update with the flag work, and `"yes"` gives 400. `expectedState: restarted` is accepted. An unknown state is 400. `swagger.json` lists `forceRestart`.
- [X] T057 [P] [US5] In `tests\contract\Sequences\PrimitiveActionTypesOpenApiTests.cs`, add a check of the new `PayloadDescriptions` text for `ensure-game-running`.
- [X] T058 [P] [US5] In `tests\contract\StepThrough\ActionTypePreviewListTests.cs`, keep `EnsureGameRunning = Runs`. Add a test: a step with `forceRestart` true is previewed with zero real dispatches and zero calls to `RestartAsync`. A plain step still reaches the real dispatcher.
- [X] T059 [P] [US5] In `tests\unit\StepThrough\CommandExecutorPreviewTests.cs`, add a test: a command step with `ForceRestart` true is previewed and not run.
- [X] T060 [P] [US5] In `src\web-ui\src\components\commands\__tests__\EnsureGameRunningPanel.test.tsx`, add tests for the `forceRestart` checkbox (shown, unchecked by default, and it sets the field).

### Implementation for User Story 5

- [X] T061 [P] [US5] In `src\GameBot.Domain\Actions\PrimitiveActionVariants.cs`, add `public bool? ForceRestart { get; set; }` to `PrimitiveEnsureGameRunningAction`. Add `ToActionPayload()` (writes the key only for true or false) and `static TryFromActionPayload(payload, out action, out error)`. Both use `EnsureGameRunningPayload`. (after T003)
- [X] T062 [P] [US5] In `src\GameBot.Domain\Commands\FileSequenceRepository.cs` (`ValidateActionPayloads`, line 104), add `GuardEnsureGamePayload(step)` next to `GuardCancelPayload`. A bad value throws `InvalidOperationException`. The guard also rejects a step `parameterBindings` entry that targets `forceRestart`. (after T003)
- [X] T063 [US5] In `src\GameBot.Domain\Services\SequenceStepValidationService.cs` (`ValidateStepCondition`, line 332), add a branch for `ensure-game-running`. It calls `EnsureGameRunningPayload.TryRead` and rejects a `parameterBindings` entry that targets `forceRestart` with a clear message (400 at save, FR-010). (after T039)
- [X] T064 [US5] In `src\GameBot.Domain\Services\StepThrough\SequenceStepper.cs` (`WrapActionDispatcher`, line 378), preview an action when `IsServiceLevelAction` or `IsForceRestartAction` is true. (after T041)
- [X] T065 [P] [US5] In `src\GameBot.Service\Services\StepThrough\StepThroughActionPreview.cs`, add the preview text for a restart action step.
- [X] T066 [P] [US5] In `src\GameBot.Service\Services\StepThrough\PreviewEffectRules.cs`, make `TryDescribe` preview `EnsureGameRunning` with `ForceRestart` true. `ByType` stays false for the type.
- [X] T067 [P] [US5] In `src\GameBot.Service\Swagger\PrimitiveActionSchemaFilter.cs` (line 48), update `PayloadDescriptions[EnsureGameRunning]` to list `forceRestart` as an optional boolean.
- [X] T068 [US5] In `src\GameBot.Service\Endpoints\CommandsEndpoints.cs` (validator, line 136), make a non-boolean `forceRestart` give 400 with a message that names `forceRestart`. Map a JSON binding failure on this field to the same 400 with the error mechanism that the other command fields use. (after T021)
- [X] T069 [P] [US5] In `src\web-ui\src\services\commands.ts`, add the optional `forceRestart` field to the EnsureGameRunning command step type.
- [X] T070 [US5] In `src\web-ui\src\components\commands\EnsureGameRunningPanel.tsx`, add the `forceRestart` checkbox. (after T069)
- [X] T071 [US5] Record no change to `src\GameBot.Domain\Actions\PrimitiveActionValidationService.cs`. Reason: the file has no `ensure-game-running` case, and a `bool?` cannot hold a bad value, so no new rule or error text is needed. (after T061)
- [X] T072 [US5] Record no change to `src\GameBot.Service\Services\SequenceExecution\SequenceExecutionService.StepThrough.cs`. Reason: `CreateStepWiring` sets `ActionDispatcher` to `DispatchActionAsync`, so it reaches `DispatchEnsureGameRunningAsync` (T024) with no edit. `SequenceStepper.WrapActionDispatcher` (T064) previews a restart step before the real dispatcher, so a restart never runs in a step-through. The test of T058 proves it. (after T064)
- [X] T073 [US5] Run the snapshot test for `tests\contract\ApiContractSnapshots\primitive-actions-routes.snapshot.json` and record no change to the file. Reason: the file has no `ensure-game-running` text, so the new description does not reach it. (after T067)
- [X] T074 [US5] Run the tests of T054 to T060 with `dotnet test` and `npx jest`. Confirm that they pass. (after T062, T063, T064, T065, T066, T067, T068, T070, T071, T072, T073)

**Checkpoint**: All five stories work.

---

## Phase 8: Polish and cross-cutting concerns

- [X] T075 [P] Add `forceRestart` and the outcome `restarted` to the action list in `README.md`.
- [X] T076 [P] Update `docs\architecture.md`: the action list, the condition state list, and the "Last reviewed" date.
- [X] T077 [P] Add an entry in `CHANGELOG.md` for the `forceRestart` option, the `restarted` outcome, and the `restarted` condition state.
- [X] T078 [P] Update the row of feature 129 in `specs\STATUS.md`.
- [X] T079 [P] Set the `Status` line in `specs\129-restart-game-force-restart\spec.md` to `Implemented`.
- [X] T080 [P] Raise the minor version in `installer\versioning\version.override.json`.
- [X] T081 Run the full `dotnet build` and `dotnet test` for `tests\unit`, `tests\contract`, and `tests\integration`. Run `npm run build` and `npx jest` in `src\web-ui`. Confirm that all pass. (after T074, T075, T076, T077, T078, T079, T080)
- [X] T082 Run the steps in `specs\129-restart-game-force-restart\quickstart.md` as far as the test fakes allow. Check that all new text in this feature is in STE (Constitution Principle VI). (after T081)

---

## Dependencies and execution order

- Phase 1 then Phase 2. Phase 2 blocks all stories.
- US1 (Phase 3) then US2 (Phase 4). US3 (Phase 5) needs T003 (reader) from Phase 2 and the dispatch outcome from US1.
- US4 (Phase 6) needs the handler of T016.
- US5 (Phase 7) needs T039 and T041 from US3. T068 needs T021 from US1.
- Polish (Phase 8) is last.

## Parallel examples

- Phase 2: T002, T003, T004, T005, T008 run together.
- US1 tests: T013, T014, T015 run together. Then T018, T020 run together.
- US3: T030, T031, T032 together, then T033 to T037 together.
- US5 tests: T054, T056, T057, T058, T059, T060 run together.
- Polish docs: T075 to T080 run together.

## Implementation strategy

- MVP: Phase 1, Phase 2, and US1. This gives a working restart for both step kinds.
- Then US2 (regression proof), US3 (conditions), US4 (failure and time bounds), US5 (validation and preview).
- Run the build and the tests at each checkpoint. Do not continue when a build or a test fails.
