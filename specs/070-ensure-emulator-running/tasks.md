# Tasks: Ensure Emulator Running Action

**Feature**: `070-ensure-emulator-running` | **Spec**: [spec.md](spec.md) | **Plan**: [plan.md](plan.md)
**Design docs**: [research.md](research.md) · [data-model.md](data-model.md) · [contracts/](contracts/ensure-emulator-running-action.md) · [quickstart.md](quickstart.md)

Tests are included (constitution Principle II — Testing Standards is NON-NEGOTIABLE).

**Conventions**: CamelCase method names only; functions ≈<50 LOC; ≥80% line / ≥70% branch coverage on
touched areas. Backend gate: `dotnet build` + `dotnet test`. Web-ui gate: `vite build` + `jest`.

---

## Phase 1: Setup

- [ ] T001 Confirm a green baseline before starting: `dotnet build GameBot.sln` and, in `src/web-ui`, `npm run build` (`vite build`) + `npx jest` — record that they pass (or note pre-existing web-ui lint/tsc failures are ignored per the real gate).

---

## Phase 2: Foundational (blocking prerequisites for all user stories)

- [ ] T002 Add `EnsureEmulatorRunning = "ensure-emulator-running"` to `src/GameBot.Domain/Actions/ActionTypes.cs` with an XML doc comment describing it as the emulator-lifecycle sibling of `ensure-game-running`.
- [ ] T003 Add `EnsureEmulatorRunning` const to `PrimitiveActionTypes` and include it in `PrimitiveActionTypes.All`, in `src/GameBot.Domain/Actions/PrimitiveActionBase.cs`.
- [ ] T004 Add `PrimitiveEnsureEmulatorRunningAction` (fields `InstanceName?`, `InstanceIndex?`, `AdbSerial`) to `src/GameBot.Domain/Actions/PrimitiveActionVariants.cs`, mirroring `PrimitiveConnectToGameAction`.
- [ ] T005 [P] Create `src/GameBot.Domain/Actions/EnsureEmulatorRunningArgs.cs` (strongly-typed args: instance name/index + adbSerial) with `ToArgsDictionary()` and `TryFrom` overloads for the primitive variant and for an `InputAction`/parameters dictionary, mirroring `ConnectToGameArgs.cs`.
- [ ] T006 [P] Add `EmulatorProbeTimeoutMs = 10000`, `EmulatorBootWaitMs = 120000`, `EmulatorPollIntervalMs = 3000` to `src/GameBot.Domain/Config/AppConfig.cs` with XML docs naming the `GAMEBOT_EMULATOR_*` env vars. Document the clamp/fallback rules the binders enforce: `EmulatorProbeTimeoutMs` ≥ a small minimum, `EmulatorBootWaitMs` ≥ `EmulatorProbeTimeoutMs`, `EmulatorPollIntervalMs` ≥ 100; invalid/non-numeric env values fall back to the default (matching `GAMEBOT_ADB_*`).
- [ ] T007 [P] Add `LdConsoleResolver.ResolveLdConsolePath()` to `src/GameBot.Emulator/Adb/LdConsoleResolver.cs`, mirroring `AdbResolver` (env override `GAMEBOT_LDCONSOLE_PATH` → `LDPLAYER_HOME`/`LDP_HOME` → install-path probes → registry), returning `null` when not found.
- [ ] T008 Create `src/GameBot.Emulator/Adb/LdConsoleClient.cs` (`[SupportedOSPlatform("windows")]`, mirror `AdbClient`): `IsRunningAsync`, `LaunchAsync`, `RebootAsync` keyed by `--name`/`--index`, plus static parse helpers for `isrunning` output and for detecting a nonexistent-instance response; `LoggerMessage`-based logging.
- [ ] T009 [P] Unit tests for `LdConsoleClient` output parsing (running / not-running / nonexistent-instance) and `LdConsoleResolver` env-override resolution, in `tests/unit/Emulator/LdConsoleClientTests.cs` and `tests/unit/Emulator/LdConsoleResolverTests.cs`.

**Checkpoint**: Domain type, args, config knobs, and the emulator-control client compile and are unit-tested. User-story phases can now proceed.

---

## Phase 3: User Story 1 — Guarantee a healthy emulator before automation runs (Priority: P1) 🎯 MVP

**Goal**: An author's `ensure-emulator-running` step verifies health and starts/restarts the instance, waiting for boot-complete, then reports the correct step outcome.

**Independent test**: Run a one-step sequence against (a) healthy, (b) stopped, (c) hung instances (faked control+probe) and confirm no-op / launch / reboot respectively, all ending healthy & succeeded; and confirm timeout → failed and bad identifier → failed.

- [ ] T010 [P] [US1] Define seam `IEmulatorControl` (IsRunningAsync/LaunchAsync/RebootAsync + not-found detection) in `src/GameBot.Service/Services/EnsureEmulatorRunning/IEmulatorControl.cs`.
- [ ] T011 [P] [US1] Define seam `IEmulatorDeviceProbe` (device-state present/offline + `GetBootCompletedAsync`) in `src/GameBot.Service/Services/EnsureEmulatorRunning/IEmulatorDeviceProbe.cs`.
- [ ] T012 [P] [US1] Add `EnsureEmulatorRunningOutcome` enum + `EnsureEmulatorRunningActionResult` (with `IsSuccess`/reason) in `src/GameBot.Service/Services/EnsureEmulatorRunning/EnsureEmulatorRunningActionResult.cs`, per data-model outcome table.
- [ ] T013 [US1] Implement `EnsureEmulatorRunningActionHandler` + `IEnsureEmulatorRunningActionHandler` in `src/GameBot.Service/Services/EnsureEmulatorRunning/`: platform guard → tool availability → health check (isrunning + device-state + boot-complete) → launch|reboot → poll to `EmulatorBootWaitMs` at `EmulatorPollIntervalMs` → outcome. Split into named helpers each <50 LOC; inject timeouts/interval from `AppConfig`. Health scope is device-only (FR-015): the handler MUST NOT query or launch any game/app package — that stays the job of `ensure-game-running`.
- [ ] T014 [P] [US1] Windows adapter `LdConsoleEmulatorControl` (→ `LdConsoleClient` via `LdConsoleResolver`) in `src/GameBot.Service/Services/EnsureEmulatorRunning/LdConsoleEmulatorControl.cs`.
- [ ] T015 [P] [US1] Windows adapter `AdbEmulatorDeviceProbe` (→ `AdbClient`: `adb devices` state parse reusing the `device`-only rule, and bounded `getprop sys.boot_completed`) in `src/GameBot.Service/Services/EnsureEmulatorRunning/AdbEmulatorDeviceProbe.cs`.
- [ ] T016 [US1] Register the handler and both seams in DI in `src/GameBot.Service/GameBotServiceSetup.cs`, and bind `GAMEBOT_EMULATOR_PROBE_TIMEOUT_MS` / `GAMEBOT_EMULATOR_BOOT_WAIT_MS` / `GAMEBOT_EMULATOR_POLL_INTERVAL_MS` in `BuildAppConfig` (beside the `GAMEBOT_ADB_*` reads), applying the clamp/fallback rules from T006 (invalid→default; `BootWaitMs` ≥ `ProbeTimeoutMs`; `PollIntervalMs` ≥ 100).
- [ ] T017 [US1] Add `EnsureEmulatorRunning` to `SequenceRunner.IsDispatchedPrimitiveAction` in `src/GameBot.Domain/Services/SequenceRunner.cs`.
- [ ] T018 [US1] Add `DispatchEnsureEmulatorRunningAsync` to `src/GameBot.Service/Services/SequenceExecution/SequenceExecutionService.cs` (read `instanceName`/`instanceIndex`/`adbSerial` from `Action.Parameters` via `EnsureEmulatorRunningArgs.TryFrom`, call handler, map outcome→`executed`/`failed` per contract), and route `ActionTypes.EnsureEmulatorRunning` to it in `DispatchActionAsync`.
- [ ] T019 [US1] Add the `EnsureEmulatorRunning` const to the hard-coded `supportedActionTypes` set in `FileSequenceRepository.ValidateActionPayloads` (`src/GameBot.Domain/Commands/FileSequenceRepository.cs`) — separate from `PrimitiveActionTypes.All`; omission 500s on save.
- [ ] T020 [P] [US1] Unit tests for the handler outcome matrix (already-healthy / started / restarted / recovery-timed-out / instance-not-found / platform-unsupported / control-unavailable) with faked seams and tiny injected timeouts, in `tests/unit/Service/EnsureEmulatorRunningActionHandlerTests.cs`. Include an assertion for FR-015: on every path the handler makes zero game/app-package calls (verified via the faked seams recording no such interaction).
- [ ] T021 [P] [US1] Unit tests for the sequence dispatcher mapping (Parameters→args, outcome→step result, missing-serial guard) in `tests/unit/Service/SequenceExecutionEnsureEmulatorTests.cs`.
- [ ] T022 [P] [US1] Integration test: a one-step sequence with `ensure-emulator-running` runs end-to-end against faked control+probe covering launch, reboot, and timeout paths, in `tests/integration/EnsureEmulatorRunningSequenceTests.cs`.

**Checkpoint**: The action executes correctly on the sequence path with full outcome coverage. MVP is demonstrable.

---

## Phase 4: User Story 2 — Author through the same surfaces as ensure-game-running (Priority: P2)

**Goal**: The action is selectable, configurable, validated, and round-trips wherever `ensure-game-running` is — sequences, standalone commands, MCP, and the web-ui.

**Independent test**: In the action picker, validation, and MCP listing, confirm "ensure emulator running" is offered, accepts instance id + serial, and a step round-trips through save/load.

- [ ] T023 [US2] Add a validation case for `PrimitiveEnsureEmulatorRunningAction` in `src/GameBot.Domain/Actions/PrimitiveActionValidationService.cs`: require `adbSerial`, require at least one of `instanceName`/`instanceIndex`, and reject `instanceIndex < 0`.
- [ ] T024 [US2] Add `CommandStepType.EnsureEmulatorRunning` and `EnsureEmulatorRunningConfig` (`InstanceName?`, `InstanceIndex?`, `AdbSerial`) to `src/GameBot.Domain/Commands/CommandStep.cs`.
- [ ] T025 [US2] Add the `CommandStepType.EnsureEmulatorRunning` branch to `src/GameBot.Service/Services/CommandExecutor.cs`, invoking the same handler with the config's instance id + serial.
- [ ] T026 [US2] Add `CommandStepTypeDto.EnsureEmulatorRunning` + the `ensureEmulatorRunning` config DTO to `src/GameBot.Service/Models/Commands.cs`.
- [ ] T027 [US2] Add DTO↔domain mapping and a `ValidateStep` case (mirror the domain validation) for the new step type in `src/GameBot.Service/Endpoints/CommandsEndpoints.cs`.
- [ ] T028 [P] [US2] Update `src/mcp-server/src/tools/commands.ts` description text to list `EnsureEmulatorRunning` among the supported action/step types (with its instance id + serial fields).
- [ ] T029 [P] [US2] Add `'EnsureEmulatorRunning'` to the union + an `<option>` in `src/web-ui/src/components/commands/ActionTypeSelector.tsx`.
- [ ] T030 [P] [US2] Create `src/web-ui/src/components/commands/EnsureEmulatorRunningPanel.tsx` — a self-contained panel with three controlled inputs: instance name (text), instance index (number, optional), and adbSerial (text), emitting the payload used by `CommandForm`. Use the existing parameterized action panel (e.g. the connect-to-game / adbSerial-bearing panel) as the styling template if present; otherwise follow `EnsureGameRunningPanel.tsx`'s structure and add the input fields. Verify the actual template file before coding.
- [ ] T031 [US2] Wire the new action type into the add-step flow in `src/web-ui/src/components/commands/CommandForm.tsx`.
- [ ] T032 [P] [US2] Unit tests: domain validation cases (`PrimitiveActionValidationService`), repository payload acceptance (`FileSequenceRepository`), and command-step DTO round-trip, in `tests/unit/…`.
- [ ] T033 [P] [US2] Web-ui component tests for the selector option, the panel (renders + edits instance id + serial), and CommandForm wiring, in `src/web-ui/src/components/commands/__tests__/`.

**Checkpoint**: Full authoring/validation/tooling parity with `ensure-game-running`.

---

## Phase 5: Polish & Cross-Cutting Concerns

- [ ] T034 [P] Document `GAMEBOT_EMULATOR_PROBE_TIMEOUT_MS`, `GAMEBOT_EMULATOR_BOOT_WAIT_MS`, `GAMEBOT_EMULATOR_POLL_INTERVAL_MS`, and `GAMEBOT_LDCONSOLE_PATH` in the ADB/Emulator section of `ENVIRONMENT.md`, and add them to `ConfigSnapshotService` defaults + `IConfigApplier.Apply` (runtime-mutable, mirroring `GAMEBOT_ADB_RETRIES`).
- [ ] T035 [P] Update `docs/architecture.md` (refresh "Last reviewed" date): new `ensure-emulator-running` capability, the `LdConsoleClient` emulator-control surface, and the three config knobs.
- [ ] T036 [P] Add a `Status` line to `specs/070-ensure-emulator-running/spec.md` and a new row to `specs/STATUS.md` (Implemented; complements 052 `ensure-game-running`, supersedes nothing).
- [ ] T037 Run the full green gate: `dotnet build` + `dotnet test` (all pass), then `src/web-ui` `vite build` + `jest` (all pass); verify ≥80% line / ≥70% branch coverage on touched areas and fix any gaps.

---

## Dependencies & Execution Order

- **Phase 1 (Setup)** → **Phase 2 (Foundational)** → **Phase 3 (US1)** → **Phase 4 (US2)** → **Phase 5 (Polish)**.
- Phase 2 blocks everything (shared type, args, config, emulator client).
- US1 (execution) is the MVP and is independently demonstrable via faked seams once Phase 2 is done.
- US2 (authoring parity) depends on the domain type (Phase 2) and reuses the US1 handler (T025 calls the T013 handler), so US2 should follow US1.
- Within a phase, `[P]` tasks touch different files and may run in parallel.

## Parallel Execution Examples

- **Phase 2**: T005, T006, T007 in parallel (distinct new files); T009 after T007/T008.
- **US1**: T010, T011, T012 in parallel; then T013; then T014/T015 in parallel; T020/T021/T022 in parallel after their targets exist.
- **US2**: T028, T029, T030 in parallel; T032/T033 in parallel after their targets exist.

## Implementation Strategy

- **MVP = Phase 1 + Phase 2 + Phase 3 (US1)**: the action runs and correctly heals the emulator on the
  sequence path with full outcome coverage.
- **Increment 2 = Phase 4 (US2)**: authoring/validation/tooling/web-ui parity.
- **Finalize = Phase 5**: docs, config wiring, status, and the green-gate + coverage verification.
