# Implementation Plan: Restart the Game From a Sequence

**Branch**: `129-restart-game-force-restart` | **Date**: 2026-10-07 | **Spec**: `specs/129-restart-game-force-restart/spec.md`
**Input**: Feature specification from `/specs/129-restart-game-force-restart/spec.md`. Closes issue #277.

## Summary

The "ensure game running" action gets one optional boolean, `forceRestart`. When it is true, the handler force-stops
the game package on the device of the session, starts the game again, and waits until the game is in the foreground.
The step then reports the new outcome `restarted`. When the option is absent or false, nothing changes.

The approach has seven parts:

1. A new `RestartAsync` method on the existing handler. It stops the game with `adb shell am force-stop`, waits 1 s,
   starts the game with the existing monkey launch, and polls the foreground check. There is no new step type and no
   new endpoint.
2. Each ADB call has its own time limit. A total deadline bounds the whole restart (FR-007, SC-002).
3. The flag is stored in `SequenceActionPayload.Parameters["forceRestart"]` for a sequence action step. It is stored
   in `EnsureGameRunningConfig.ForceRestart` for a command step. The typed variant
   `PrimitiveEnsureGameRunningAction` gets `ForceRestart` too (FR-016).
4. A strict reader accepts only a JSON boolean. A bad value gives 400 at save time. A bad value at run time fails the
   step with a message and never throws (FR-010).
5. The new condition state `restarted` is for a sequence action step. A condition on `success` also matches it. A
   command step shows `restarted` in its own outcome list only (FR-005).
6. The step-through preview shows a restart step and never runs it (FR-012).
7. The force-stop always uses the device serial of the session and no other serial (FR-017).

## Technical Context

**Language/Version**: C# on .NET (the version in `src\GameBot.Service\GameBot.Service.csproj`). TypeScript for the web editor.
**Primary Dependencies**: ASP.NET Core minimal API, `System.Text.Json`, Swashbuckle (OpenAPI), `AdbClient` in `src\GameBot.Emulator\Adb\AdbClient.cs`. No new package.
**Storage**: JSON files through `FileSequenceRepository` and `FileCommandRepository`. No schema migration. A stored step without `forceRestart` reads as false.
**Testing**: xUnit and FluentAssertions in `tests\unit`, `tests\contract`, `tests\integration`. `jest` for the web editor.
**Target Platform**: Windows service. The device commands run only on Windows, as today.
**Project Type**: Web service with a web UI.
**Performance Goals**: A restart ends in at most 30 s of foreground wait plus 10 s in the normal case (SC-002). The worst case is bounded by the total deadline below.
**Constraints**: No new step type, no new endpoint, no "stop any app" capability, no arbitrary device command (FR-014). No change to the "go to home screen" step and the "ensure emulator running" step (FR-015).
**Scale/Scope**: One new option, one new outcome, one new condition state. The exact scope is the change-site table below (34 rows) and the test tables below. This plan gives no file count, so the count cannot drift from the tables.

### Fixed values (the spec asks the plan to name them)

| Value | Number | Where |
|-------|--------|-------|
| Foreground wait after the start | 30 s | `RestartForegroundWait` in `EnsureGameRunningActionHandler.cs` |
| Foreground poll step | 1 s | `RestartPollInterval` |
| Settle time after the stop | 1 s | `RestartSettleDelay` |
| Time limit of the force-stop call | 10 s | `RestartStopTimeout` |
| Time limit of the start call | 10 s | `RestartStartTimeout` |
| Time limit of one foreground probe | 5 s (or less, see below) | `RestartProbeTimeout` |

The handler gets one new `internal` options record, `EnsureGameRunningRestartOptions`, with these six values and the
defaults above. The constructor takes it as an optional last parameter (default null gives the defaults). The DI
container then builds the handler with no change to the existing registration. Tests pass small values.

### Time limits (total time is bounded)

- Each ADB call runs under a linked `CancellationTokenSource` with `CancelAfter(limit)`. `AdbClient.ExecAsync` kills the
  adb process on cancel (feature 106), so a timed-out call leaves no process.
- A timeout of the stop call gives `restart_stop_failed`. A timeout of the start call gives `restart_start_failed`.
  A timeout of one probe counts as a miss, and the poll goes on until the foreground deadline.
- A cancel from the caller is not a failure code. It ends the restart at once and rethrows `OperationCanceledException`.
  The handler tells the two cases apart with `ct.IsCancellationRequested`.
- The probe limit is the smaller of `RestartProbeTimeout` and the time left to the foreground deadline.
- The worst case is 10 s (stop) + 1 s (settle) + 10 s (start) + 30 s (poll) = 51 s. This sum is the hard bound. A test
  with fake calls that never return proves that the restart ends under this bound.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-checked after Phase 1 design.*

*NON-NEGOTIABLE*: If `build` or required `test` runs are failing (local or CI), implementation progression is blocked until failures are fixed or a documented maintainer waiver exists.

*NON-NEGOTIABLE*: All text in this plan and in the artifacts it produces MUST obey Simplified Technical English (Constitution Principle VI).

| Principle | Status | Evidence |
|-----------|--------|----------|
| I. Code Quality | Pass | The handler shares one resolve method between `ExecuteAsync` and `RestartAsync`. The state list is in one class. Method names use CamelCase with no underscore. |
| II. Testing | Pass | The test plan below covers each requirement. Tests come before the code in `tasks.md`. |
| III. User Experience | Pass | Each failure has a distinct reason code and an actionable message. The API description lists the option. A bad value gives 400 with a clear message. |
| IV. Performance | Pass | The goals and the time limits are in this plan. The restart is not on a hot path. The plain step adds no ADB call. |
| V. Living Documentation | Pass | `docs\architecture.md`, `README.md`, `CHANGELOG.md`, `specs\STATUS.md`, and the `Status` line of `spec.md` are in the task list (see "Docs"). |
| VI. STE | Pass | All new text in this feature is written in STE. `/speckit-analyze` checks it. |

No violation. The Complexity Tracking table is not needed.

Post-design re-check: the design adds no project, no endpoint, and no step type. The result is the same: Pass.

## Project Structure

### Documentation (this feature)

```text
specs\129-restart-game-force-restart\
  spec.md
  plan.md                 (this file)
  research.md             Phase 0
  data-model.md           Phase 1
  quickstart.md           Phase 1
  contracts\ensure-game-running-force-restart.md   Phase 1
  checklists\requirements.md
  tasks.md                Phase 2 (made by the tasks command, not by this command)
```

### Source code: exact change sites

All paths are relative to `C:\src\GameBot`. No path in this plan is a placeholder.

| # | File | Change |
|---|------|--------|
| 1 | `src\GameBot.Emulator\Adb\AdbClient.cs` | Add `ForceStopAppAsync(packageName, ct)` (`shell am force-stop <package>`). It returns the exit code tuple like `LaunchAppAsync`. |
| 2 | `src\GameBot.Service\Services\EnsureGameRunning\IAdbGameOperations.cs` | Add `ForceStopAppAsync(deviceSerial, packageName, ct)` and `TryLaunchAppAsync(deviceSerial, packageName, ct)`. Both return `Task<bool>` (true when the exit code is 0). Keep `LaunchAppAsync` as is. |
| 3 | `src\GameBot.Service\Services\EnsureGameRunning\AdbGameOperations.cs` | Implement both. Each call uses `new AdbClient().WithSerial(deviceSerial)`. Reject a blank serial and an unsafe package name before any process starts (return false). Off Windows, return false. |
| 4 | `src\GameBot.Service\Services\EnsureGameRunning\EnsureGameRunningActionResult.cs` | Add `Restarted`, `RestartNoDevice`, `RestartStopFailed`, `RestartStartFailed`, `RestartForegroundTimeout` with reason codes. `IsSuccess` is true for `GameRunning` and `Restarted`. |
| 5 | `src\GameBot.Service\Services\EnsureGameRunning\IEnsureGameRunningActionHandler.cs` | Add `RestartAsync(sessionId, ct)`. |
| 6 | `src\GameBot.Service\Services\EnsureGameRunning\EnsureGameRunningActionHandler.cs` | Move the resolve steps into one private method. Add `RestartAsync`. Add the options record `EnsureGameRunningRestartOptions` in a new file (row 7). |
| 7 | `src\GameBot.Service\Services\EnsureGameRunning\EnsureGameRunningRestartOptions.cs` (new) | The six fixed values. |
| 8 | `src\GameBot.Service\GameBotServiceSetup.cs` | Lines 166 and 167 register `IAdbGameOperations` and `IEnsureGameRunningActionHandler`. The registration text stays the same, because the options parameter is optional. Add one comment line there that names the restart options. Check that `GameForegroundGuard` (line 170) still builds from the handler interface. |
| 9 | `src\GameBot.Domain\Actions\PrimitiveActionVariants.cs` | Add `public bool? ForceRestart { get; set; }` to `PrimitiveEnsureGameRunningAction`. Add `ToActionPayload()` that returns a `SequenceActionPayload` (writes the key only when the value is true or false). Add `static TryFromActionPayload(payload, out action, out error)`. Both use the reader of row 10. |
| 10 | `src\GameBot.Domain\Commands\EnsureGameRunning\EnsureGameRunningPayload.cs` (new) | `TryRead(action, out forceRestart, out error)`. It accepts a CLR `bool` and a JSON `true` or `false` (`JsonElement`). It rejects text, number, null, object, array, and `{{name}}` text. The key is optional. The namespace is `GameBot.Domain.Commands.EnsureGameRunning`. The style follows `src\GameBot.Domain\Commands\Notify\NotifyPayload.cs`. |
| 11 | `src\GameBot.Domain\Actions\PrimitiveActionValidationService.cs` | No change (decided). The file has no `ensure-game-running` case today. A `bool?` cannot hold a bad value, so no new rule or error text is needed. The task records "no change" with this reason (see "Task authoring rules"). |
| 12 | `src\GameBot.Domain\Commands\CommandStep.cs` | Add `ForceRestart` (bool, init, default false, `JsonIgnore(WhenWritingDefault)`) to `EnsureGameRunningConfig`. |
| 13 | `src\GameBot.Domain\Parameters\CommandStepResolver.cs` | At line 126, copy `ForceRestart` into the new `EnsureGameRunningConfig`. Add no `FieldTemplates` key. |
| 14 | `src\GameBot.Domain\Services\StepOutcomeStates.cs` (new) | The allowed states and `Matches(actual, expected)`. |
| 15 | `src\GameBot.Domain\Services\SequenceStepConditionEvaluator.cs` | At line 192, use `StepOutcomeStates.Matches`. |
| 16 | `src\GameBot.Domain\Services\SequenceStepValidationService.cs` | Use the shared state list (set at line 14, messages at lines 264 and 386). In `ValidateStepCondition` (line 332), add a branch for `ensure-game-running` that calls the reader and rejects a `parameterBindings` entry that targets `forceRestart` (400 at save, FR-010). |
| 17 | `src\GameBot.Domain\Services\CompositeConditionValidator.cs` | Use the shared state list (set at line 26, message at line 127). |
| 18 | `src\GameBot.Domain\Services\SequenceRunner.cs` | In the primitive action branch (line 711), write `restarted` when the dispatch outcome is `restarted`. Add `IsForceRestartAction(action)` next to `IsServiceLevelAction` (line 845). |
| 19 | `src\GameBot.Domain\Services\StepThrough\SequenceStepper.cs` | In `WrapActionDispatcher` (line 378), preview an action when `IsServiceLevelAction` or `IsForceRestartAction` is true. |
| 20 | `src\GameBot.Domain\Commands\FileSequenceRepository.cs` | In `ValidateActionPayloads` (line 104), add `GuardEnsureGamePayload(step)` next to `GuardCancelPayload`. A bad value throws `InvalidOperationException`. The guard also rejects a step `parameterBindings` entry that targets `forceRestart`. The endpoint maps this to 400 (FR-010). |
| 21 | `src\GameBot.Service\Services\SequenceExecution\SequenceExecutionService.cs` | Line 787 passes `action` to `DispatchEnsureGameRunningAsync(action, sessionId, ct)`. The method reads the flag and calls `RestartAsync` when it is true. A bad value returns a `failed` dispatch result and never throws. |
| 22 | `src\GameBot.Service\Services\SequenceExecution\SequenceExecutionService.StepThrough.cs` | No change (decided, code read). `CreateStepWiring` sets `ActionDispatcher` to `DispatchActionAsync`, the same method as a real run. It therefore reaches `DispatchEnsureGameRunningAsync` (row 21) and reads the flag with no edit here. `SequenceStepper.WrapActionDispatcher` (row 19) sends a restart step to `PreviewServiceAction` before the real dispatcher, and `PreviewServiceAction` calls `StepThroughActionPreview.Describe` (row 27). So a restart never runs in a step-through (FR-012). A plain step (no flag) still reaches the real dispatcher, as before. The task records "no change" with this reason. |
| 23 | `src\GameBot.Service\Services\CommandExecutor.cs` | In the `EnsureGameRunning` branch (line 388), call `RestartAsync` when `step.EnsureGameRunning?.ForceRestart == true`. Run the readiness wait after a good restart. |
| 24 | `src\GameBot.Service\Models\Commands.cs` | Add `bool? ForceRestart` to `EnsureGameRunningConfigDto` (line 146). |
| 25 | `src\GameBot.Service\Endpoints\CommandsEndpoints.cs` | `ToDomainEnsureGame` (line 234) keeps the config when `ForceRestart` is true and there is no readiness image. Update `ToResponseEnsureGame` (line 249) and the validator (line 136). |
| 26 | `src\GameBot.Service\Endpoints\StepsEndpoints.cs` | Map the EnsureGameRunning config with the flag (validator line 89, `ToDomainStep` line 113). |
| 27 | `src\GameBot.Service\Services\StepThrough\StepThroughActionPreview.cs` | Add the preview text for a restart action step. |
| 28 | `src\GameBot.Service\Services\StepThrough\PreviewEffectRules.cs` | `TryDescribe` previews `EnsureGameRunning` with `ForceRestart` true. `ByType` stays false for the type. |
| 29 | `src\GameBot.Service\Services\ExecutionLog\ExecutionLogService.cs` | `MapStepStatus`: add `"restarted" => "success"`. |
| 30 | `src\GameBot.Service\Swagger\PrimitiveActionSchemaFilter.cs` | Update `PayloadDescriptions[EnsureGameRunning]` (line 48). |
| 31 | `src\GameBot.Service\Swagger\ConditionReferenceScopeSchemaFilter.cs` | Update the `commandOutcome` text (line 44). |
| 32 | `src\web-ui\src\types\sequenceFlow.ts` | Add `restarted` to the `expectedState` type (line 92). |
| 33 | `src\web-ui\src\lib\validation.ts` | Add `restarted` to the state check (line 241). |
| 34 | `src\web-ui\src\services\commands.ts`, `src\web-ui\src\components\commands\EnsureGameRunningPanel.tsx` | Add the optional `forceRestart` field to the command step type and a checkbox in the panel. |

Files that do NOT change (FR-014, FR-015):
`src\GameBot.Domain\Actions\ActionTypes.cs`, `src\GameBot.Domain\Actions\PrimitiveActionBase.cs`
(no new type), `src\GameBot.Service\Services\EnsureEmulatorRunning\EnsureEmulatorRunningActionHandler.cs`,
`DispatchGoToHomeScreenAsync` in `SequenceExecutionService.cs`, and `src\GameBot.Service\Services\EnsureGameRunning\GameForegroundGuard.cs`.

### Test fakes that need updates

Four fakes implement the changed interfaces. Each needs the new members. Without them, the test projects do not build.

| Fake | File | New members |
|------|------|-------------|
| `FakeAdbGameOperations` (implements `IAdbGameOperations`) | `tests\unit\Services\EnsureGameRunning\EnsureGameRunningActionHandlerTests.cs` | `ForceStopAppAsync`, `TryLaunchAppAsync`. It records the serial of each call (`StoppedSerials`, `LaunchSerials`) and the order of calls. Tests can make a call fail, throw, or never return. |
| `StubHandler` (implements `IEnsureGameRunningActionHandler`) | `tests\unit\Commands\CommandExecutorEnsureGameRunningTests.cs` | `RestartAsync` with a count and a result. |
| `ScriptedEnsureGameRunning` | `tests\unit\Services\EnsureGameRunning\GameForegroundGuardTests.cs` | `RestartAsync` that throws `NotSupportedException` (the guard never calls it). |
| `FakeEnsureGameRunning` | `tests\unit\Queues\QueueExecutionServiceTests.cs` | `RestartAsync` that throws `NotSupportedException`. |

`tests\unit\Queues\QueueExecutionServiceRunStatisticsTests.cs`, `tests\integration\Queues\ForegroundGuardWiringIntegrationTests.cs`,
`tests\unit\Sequences\SequenceRunnerGameActionDispatchTests.cs`, and `tests\unit\Domain\PrimitiveActionValidationServiceTests.cs`
use the namespace or the action type only. They define no fake. Task T002 checks this with a build.

The new restart tests need `FakeAdbGameOperations`. Move that class to `tests\unit\Services\EnsureGameRunning\FakeAdbGameOperations.cs` as an `internal` class. Both test files then use it.

### Tests to add or change

| File | Covers |
|------|--------|
| `tests\unit\Services\EnsureGameRunning\EnsureGameRunningRestartTests.cs` (new) | Call order stop, settle, start, poll. `restarted` for a running and a not running game. Each stage failure and its reason code. Exit code and exception cases. A call that never returns ends at its limit (per-call limits). The total time stays under the bound. A cancel from the caller is not a failure code. The stop receives the session serial only (FR-017). A blank serial gives `restart_no_device` and no ADB call. No ADB call for `no_queue_context`, `no_linked_game`, `no_package_name`, `platform_unsupported`, and an unsafe package name (`no_package_name` from the restart resolve step, zero device calls; FR-008). Normal-case time test with small options: the restart ends within the foreground wait plus 10 s (SC-002). Worst-case bound test. The plain `ExecuteAsync` path is unchanged for an unsafe package name. |
| `tests\unit\Sequences\EnsureGameRunningPayloadTests.cs` (new) | Reader: true, false, absent, text, number, null, object, array, `{{x}}`, `JsonElement` true and false. |
| `tests\unit\Sequences\SequenceExecutionEnsureGameRestartTests.cs` (new) | Action step dispatch: true calls `RestartAsync`; false and absent call `ExecuteAsync`. A non-boolean value at run time gives a `failed` result with a clear message and no throw (FR-010). The message names `forceRestart`, and the fake handler and the fake device get zero calls. This test is in the restart dispatch task (T015 in the current numbering). |
| `tests\unit\Sequences\FileSequenceRepositoryEnsureGamePayloadTests.cs` (new) | Repository guard throws on a bad value. A saved `forceRestart` true is true after the read from the file (JSON round trip, FR-016). A stored sequence step without the key reads as false and runs the plain path (FR-013). A step `parameterBindings` entry that targets `forceRestart` is rejected. |
| `tests\unit\Domain\PrimitiveEnsureGameRunningActionRoundTripTests.cs` (new) | Typed variant `PrimitiveEnsureGameRunningAction`: serialize, deserialize, `ForceRestart` true stays true. `ToActionPayload` and `TryFromActionPayload` agree. A full chain: typed variant, payload, file repository save, read, run with a fake handler. The fake handler sees `RestartAsync` (SC-008). |
| `tests\unit\Commands\CommandExecutorForceRestartTests.cs` (new) | Command step: `RestartAsync` is called, not `ExecuteAsync`. Outcome `(executed, restarted)`. The readiness wait after a restart. No `forceRestart` key in JSON when false. A stored step without the key reads as false. |
| `tests\unit\Sequences\SequenceRunnerRestartedOutcomeTests.cs` (new) | `restarted` and `success` match a restarted action step. Only `success` matches an already running step. A command step gives `success` to a sequence condition (command step reading). |
| `tests\unit\ExecutionLogs\ExecutionLogServiceMapStepStatusTests.cs` | `restarted` maps to `success`. |
| `tests\unit\Sequences\CompositeConditionValidationTests.cs`, `tests\unit\Sequences\ConditionReferenceScopeValidationTests.cs`, `tests\unit\Sequences\IfValidationTests.cs`, `tests\unit\Sequences\LoopValidationTests.cs` | Change the asserts that check the allowed `expectedState` list or its message text. The list changes by design (spec Assumptions). |
| `tests\unit\StepThrough\CommandExecutorPreviewTests.cs` | A command step with `ForceRestart` true is previewed. |
| `tests\contract\Sequences\EnsureGameRunningForceRestartContractTests.cs` (new) | API: true and false accepted and read back. Bad values (`"true"`, `1`, `null`, `{}`, `[]`, `"{{x}}"`) give 400 and never 500, for a top-level step, a loop-body step, and an if-branch step. A step `parameterBindings` entry that targets `forceRestart` gives 400. Commands: create and update with the flag; `"yes"` gives 400. `expectedState: restarted` is accepted. An unknown state is 400. `swagger.json` lists `forceRestart`. |
| `tests\contract\Sequences\GoHomeAndEnsureEmulatorUnchangedTests.cs` (new) | FR-015 and SC-007: the "go to home screen" and "ensure emulator running" dispatch give the same outcomes. The swagger path list and the action type list have no new member. |
| `tests\contract\StepThrough\ActionTypePreviewListTests.cs` | Keep `EnsureGameRunning = Runs`. Add a restart preview test with zero real dispatches. |
| `tests\contract\Sequences\PrimitiveActionTypesOpenApiTests.cs` | Check the new `PayloadDescriptions` text. |
| `tests\contract\ApiContractSnapshots\primitive-actions-routes.snapshot.json` | No change (decided). A search of the file finds no `ensure-game-running` text, so the new description does not reach it. The task runs the snapshot test and records "no change" with this reason. |
| `src\web-ui\src\lib\__tests__\perStepConditionValidation.spec.ts` | `restarted` is a valid state. |
| `src\web-ui\src\components\commands\__tests__\EnsureGameRunningPanel.test.tsx` | The checkbox for `forceRestart`. |

### Docs

`README.md` (action list), `docs\architecture.md` (action and condition list, "Last reviewed" date), `CHANGELOG.md`,
`specs\STATUS.md`, the `Status` line in `specs\129-restart-game-force-restart\spec.md`, and
`installer\versioning\version.override.json` (minor version). All new text is STE.

## Design Notes

### Run-time reading of `forceRestart`

`SequenceExecutionService.DispatchEnsureGameRunningAsync` calls `EnsureGameRunningPayload.TryRead` first. If the reader
fails, the method returns `ActionDispatchResult("failed", "ensure-game-running forceRestart must be true or false")`.
The method does not throw. No device call happens. This covers a value that reaches run time through a binding or a
hand-edited file. The save paths (validator and repository guard) already give 400 for the same value.

### Reading of `restarted`: command step and sequence condition

| Step kind | What the step writes | What a sequence condition sees |
|-----------|----------------------|--------------------------------|
| Sequence action step | `stepOutcomes[key] = "restarted"` | `restarted` and `success` both match. |
| Command step (type `EnsureGameRunning`) | The command outcome list has `status executed`, `reason restarted`. | `success` only. The sequence runner treats a command step as one unit. |

### Device serial only (FR-017)

`RestartAsync` reads `session.DeviceSerial`. If it is null or blank, the handler returns `restart_no_device` and makes
no ADB call. Without this rule, `adb` with no `-s` option picks the one connected device, or fails if there are many.
`AdbGameOperations.ForceStopAppAsync` checks the serial again. Tests check the serial that the stop receives.

### JSON round trip (FR-016)

`SequenceActionPayload.Parameters` is a `Dictionary<string, object?>`. After a file read, a boolean is a `JsonElement`.
The reader accepts a `JsonElement` with kind `True` or `False`. The typed variant
`PrimitiveEnsureGameRunningAction` in `src\GameBot.Domain\Actions\PrimitiveActionVariants.cs` has `ForceRestart`, so a
typed save keeps the value. The `ToActionPayload` and `TryFromActionPayload` methods connect the typed variant to the
stored payload.

## Task authoring rules

The tasks command MUST follow these rules when it makes `tasks.md` from this plan.

1. Every task is concrete. No task says "only if", "if needed", or "check whether". A check task MUST end with one of two
   results: it names the exact change, or it says "record no change" with the reason. This plan already decides these
   cases:
   - `SequenceExecutionService.StepThrough.cs` (row 22): record no change. The reason is in row 22.
   - `PrimitiveActionValidationService.cs` (row 11): record no change. The reason is in row 11.
   - `primitive-actions-routes.snapshot.json`: run the snapshot test and record no change. The reason is in the test table.
2. Put each task in the phase that holds its dependencies. A task that needs a Phase 2 task (for example the build check
   that closes the "no fake needed" check for four test files) goes in Phase 2 after that task, not in Phase 1. Phase 1 holds
   only the baseline build and test run.
3. The old `expectedState` list asserts (the four validation test files in the test table) and the code change that adds
   `restarted` to the shared state list MUST land in one commit. The old asserts fail after the code change, and the new
   asserts fail before it. The tasks say this in a note.
4. Tasks that edit the same file are sequential. They are not marked `[P]`. Each later task says "(after Txxx)" with the
   ID of the earlier task. This applies to shared code files (for example `SequenceRunner.cs`,
   `SequenceStepValidationService.cs`, `SequenceExecutionService.cs`, `EnsureGameRunningActionHandler.cs`,
   `CommandExecutor.cs`, `CommandsEndpoints.cs`) and to shared test files (for example `EnsureGameRunningRestartTests.cs`,
   `ActionTypePreviewListTests.cs`).
5. The restart dispatch test task (sequence action step) MUST include the run-time non-boolean `forceRestart` test: a
   `failed` result, a message that names `forceRestart`, and zero device calls.
6. The tests MUST also cover: a step `parameterBindings` entry that targets `forceRestart` gives 400 at save (FR-010); a
   normal-case time test and a worst-case time test (SC-002); the unsafe package name gives `no_package_name` in the
   restart path with zero device calls (FR-008); an old stored sequence step and an old stored command step read as false
   (FR-013).

## Complexity Tracking

No constitution violation. This table is not used.
