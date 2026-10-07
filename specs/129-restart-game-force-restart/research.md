# Research: Restart the Game From a Sequence

## R1. How does the handler stop and start the game?

- **Decision**: Add `AdbClient.ForceStopAppAsync(packageName)` in `src\GameBot.Emulator\Adb\AdbClient.cs`. It runs
  `shell am force-stop <package>`. Reuse the existing monkey launch. `IAdbGameOperations` gets `ForceStopAppAsync` and
  `TryLaunchAppAsync`. Both return true when the exit code is 0.
- **Rationale**: `AdbClient.ExecAsync` already gives `(ExitCode, StdOut, StdErr)`. Today the handler ignores the launch
  result, because the old flow is best effort. The restart must know if a stage failed (FR-007).
- **Alternatives**: `pm clear` (deletes game data, rejected). Kill the emulator (that is the emulator action, and FR-015
  forbids a change there). `input keyevent` to close the app (not reliable).

## R2. Foreground wait value and time limits

- **Decision**: Foreground wait 30 s, poll step 1 s, settle 1 s after the stop. Per-call limits: stop 10 s, start 10 s,
  one probe 5 s. All values are in a new options record, `EnsureGameRunningRestartOptions`. The handler gets it as an
  optional constructor parameter. Tests pass small values.
- **Rationale**: The foreground check is `GetForegroundPackageAsync` (`dumpsys activity activities`, under 1 s).
  A cold start shows the first activity in a few seconds. `GameForegroundGuard` uses 15 s with a 2 s poll, and its own
  comment says a cold start can be longer. 30 s is twice that and keeps SC-002 true. The restart does not wait for the
  game to finish its load (the optional readiness gate does that when the author sets it on a command step).
- **Total bound**: 10 + 1 + 10 + 30 = 51 s. Each call has its own limit, so one hung `adb` call cannot use the whole
  budget. `AdbClient.ExecAsync` kills the adb process on cancel (feature 106), so a timeout leaves no process.
- **Timeout rules**: A stop timeout gives `restart_stop_failed`. A start timeout gives `restart_start_failed`. A probe
  timeout is a miss and the poll goes on. A cancel from the caller rethrows and is not a failure code.
- **Alternatives**: Reuse `GameForegroundGuard` (it relaunches at each poll and has no distinct failure per stage,
  rejected). Make the wait configurable (the spec says not in this feature). One limit for the whole restart with no
  per-call limit (one hung call would hide the failed stage, rejected).

## R3. Where is the option stored for each action path?

- **Decision**: Two stores, one flag.
  - Sequence action step: `SequenceActionPayload.Parameters["forceRestart"]`. This is the same way that `reschedule-self`
    and `notify` store their options. A typed reader validates it.
  - Command step: `EnsureGameRunningConfig.ForceRestart` and `EnsureGameRunningConfigDto.ForceRestart`.
  - Typed variant: `PrimitiveEnsureGameRunningAction.ForceRestart` (`bool?`) in
    `src\GameBot.Domain\Actions\PrimitiveActionVariants.cs`.
- **Rationale**: `SequenceExecutionService.DispatchEnsureGameRunningAsync` reads no parameter today. `CommandExecutor`
  reads `step.EnsureGameRunning`. The flag must reach both. The typed variant has no field today, so a typed save would
  drop the flag (FR-016).
- **Pitfalls found in the code**:
  - `CommandsEndpoints.ToDomainEnsureGame` returns null when there is no readiness image. A restart-only step would
    lose the flag with no error. Fix: keep the config when `ForceRestart` is true.
  - `CommandStepResolver` builds a new `EnsureGameRunningConfig` from two fields. A new field is dropped unless it
    is copied.
  - `StepsEndpoints.ToDomainStep` does not map the EnsureGameRunning config at all today.
  - After a file read, a boolean in `Parameters` is a `JsonElement`, not a `bool`. The reader must accept both.

## R4. JSON round trip of the typed variant

- **Decision**: Add `ForceRestart` to `PrimitiveEnsureGameRunningAction`. Add `ToActionPayload()` and
  `TryFromActionPayload(...)` to convert between the typed variant and `SequenceActionPayload`. Both use the one
  reader. A test saves, reads back, and runs, and checks that `RestartAsync` is called.
- **Rationale**: No code path builds the typed variant for a stored sequence today. The two shapes can drift. One
  reader and two converters keep one rule. A `bool?` property makes `System.Text.Json` reject a text value, so the typed
  variant cannot hold a bad value.
- **Alternatives**: Leave the typed variant unchanged (FR-016 names it, so rejected). Add a polymorphic converter for
  all variants (large change, no need, rejected).

## R5. How does a later condition read the outcome?

- **Decision**: A new `expectedState` value `restarted` for `commandOutcome`. The runner writes `restarted` into
  `stepOutcomes` for a sequence action step that restarted. `success` still matches such a step (one helper,
  `StepOutcomeStates.Matches`). The allowed-state list moves to one class that the two validators share.
- **Rationale**: `commandOutcome` reads `stepOutcomes[stepRef]`. Today the values are `success`, `failed`, `skipped`,
  `break`, `no_break`. The reason `game_running` is only in the log message, so no condition can read it. The spec
  (FR-005, SC-005) asks that a condition tell `restarted` from "already running". A new state is the smallest change.
  Keeping `success` true for a restart keeps old conditions correct.
- **Command step reading**: A command step is one unit for the sequence runner. A sequence condition sees `success` for
  it. The command outcome list shows reason `restarted`. This is the rule of FR-005.
- **Related fix**: `ExecutionLogService.MapStepStatus` maps an unknown outcome to `failure`. `restarted` needs an
  explicit `success` case, or a good restart shows as red in the log.
- **Alternatives**: A separate `outcomeReason` condition type (new schema, more sites, rejected). Keep `success` as the
  only state and read the log (a sequence cannot read it, rejected).

## R6. Step-through behavior

- **Decision**: Preview a restart step and never run it.
  - Action step: extend `SequenceStepper.WrapActionDispatcher` to preview an `ensure-game-running` action with
    `forceRestart: true`. Text: `would force-stop the game and start it again`.
  - Command step: `PreviewEffectRules.TryDescribe` previews a step of type `EnsureGameRunning` with `ForceRestart`
    true. `ByType` stays `false` for the type.
- **Rationale**: Feature 127 rules say a step that changes the device in a way that cannot be undone shows its effect
  and does not run. The plain step only reads the foreground and may start the game, and it keeps running in a
  step-through (guard test `ActionTypePreviewListTests`, value `Runs`).
- **Alternatives**: Mark the whole type as previewed (breaks the plain step, rejected). Skip the step with no text
  (hides the effect, rejected).

## R7. Validation sites

- **Decision**: The table in `plan.md` is the full list. The action type is not new, so `SequenceActionTypes.All`, the
  `FileSequenceRepository` type list, and `ActionPayloadValidationService` do not change. The option is new, so the
  payload check goes into the step validator and into the repository backstop. The shared state list removes a drift
  risk (the same list was in two files and in the web editor).
- **Rationale**: The memory note says a new action type touched six backend sites. A new option on an existing type
  touches fewer type lists but more payload and mapping sites. A contract test with the real API proves the 400 result.
- **Check done**: grep of `ActionTypes.EnsureGameRunning` and `commandOutcome` in `src`.

## R8. Bad input gives 400, and a bad run-time value fails cleanly

- **Decision**: Strict reader (JSON boolean only). The sequence validator turns a bad value into a message and the
  endpoint returns 400. The repository backstop throws `InvalidOperationException`, which the callers already map to
  400. For the command DTO, `bool?` makes the body binder reject a string or a number, and the minimal API returns 400.
  At run time, the dispatcher calls the same reader. A failure gives a `failed` dispatch result with a clear message
  and no throw, and no device call.
- **Rationale**: Spec clarifications: the literal boolean only, 400 at save time, and a clean failure at run time.
  A binding or a hand-edited file can put a bad value in the store after the save checks.

## R9. Package name safety

- **Decision**: The restart checks the package name with `^[A-Za-z0-9._]+$` before any shell call. A failed check
  returns `no_package_name`. `AdbGameOperations` checks again.
- **Rationale**: The name is stored data and goes into `adb shell`. A stop command is more harmful than a launch.
  The guard is local to the new code and does not change the old launch path.

## R10. Device serial only

- **Decision**: The restart uses `session.DeviceSerial`. A null or blank serial gives the new reason
  `restart_no_device` with no ADB call.
- **Rationale**: FR-017. `adb` with no `-s` option acts on the one connected device, which can be the wrong one. This
  reason exists only for the restart, so the old reasons stay the same (FR-008).
- **Alternatives**: Fall back to the default device (breaks FR-017, rejected). Reuse `no_queue_context` (hides the
  cause, rejected).
