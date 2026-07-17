# Implementation Plan: Ensure Emulator Running Action

**Branch**: `070-ensure-emulator-running` | **Date**: 2026-07-17 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `specs/070-ensure-emulator-running/spec.md`

## Summary

Add a new action type, `ensure-emulator-running`, that verifies a specified LDPlayer emulator
instance is both **running** and **responsive** (not hanging) and, only when it is not, **starts** a
stopped instance or **restarts** a hung one, then waits for it to come back before the step succeeds.
It is the emulator-lifecycle sibling of the existing `ensure-game-running` action (which is the
*app*-lifecycle guard) and is wired through exactly the same authoring/validation/dispatch/command/
tooling surfaces, plus one new device-control surface.

Unlike `ensure-game-running` (parameterless, deriving everything from the session), this action is
**parameterized like `connect-to-game`**: the author supplies an LDPlayer instance identifier (an
instance **name** or **index**) plus the **adbSerial** used for the responsiveness probe. Health is
determined by three checks — `ldconsole isrunning`, the device present-and-not-offline in
`adb devices`, and a bounded `adb -s <serial> shell getprop sys.boot_completed` returning `1`.
Remediation is `ldconsole launch` (stopped) or `ldconsole reboot` (hung), followed by polling until
the device reports boot-complete, up to a configurable maximum. On a non-Windows host, or when ADB
or `ldconsole` is unavailable, the action degrades to a neutral, non-crashing outcome exactly like
`ensure-game-running`.

## Technical Context

**Language/Version**: C# / .NET (net8.0) backend; TypeScript + React (Vite) web-ui; TypeScript MCP server
**Primary Dependencies**: ASP.NET Minimal APIs + MVC controllers, System.Text.Json; React 18 / Vite / Jest; `@modelcontextprotocol/sdk` + zod
**Storage**: File-based repositories (sequences/commands persisted as JSON under the data dir); no schema migration needed (additive enum/const + additive parameters)
**Testing**: xUnit (`tests/unit`, `tests/integration`, `tests/contract`); Jest + Testing Library (web-ui); `vite build` + `jest` is the real web-ui green gate
**Target Platform**: Windows host driving LDPlayer emulators via ADB + `ldconsole.exe`; degrades to neutral no-op on non-Windows / when tools are unavailable
**Project Type**: Web service + companion web-ui + MCP server (multi-surface single repo)
**Performance Goals**: Health checks add a few short ADB/ldconsole process invocations (<1s each in the healthy path). A restart legitimately blocks the step up to the configurable boot-wait ceiling (default 120s); no hot-path impact and no polling tighter than the configurable interval (default 3s).
**Constraints**: CamelCase method names only (no underscores); functions ≈<50 LOC; ≥80% line / ≥70% branch coverage on touched areas; keep `Program.cs` thin (build-time taint analyzers blow up on giant methods); `docs/architecture.md` + `specs/STATUS.md` updated; env knobs documented in `ENVIRONMENT.md`
**Scale/Scope**: One new action type + one new emulator-control client, across ~9 backend files + ~3 web-ui files + MCP description text, with mirrored tests

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

*NON-NEGOTIABLE*: If `build` or required `test` runs are failing (local or CI), implementation progression is blocked until failures are fixed or a documented maintainer waiver exists.

- **I. Code Quality Discipline**: PASS — additive change mirroring an established action (`ensure-game-running`) plus one new cohesive device-control client (`LdConsoleClient`) that parallels `AdbClient`. No dead code; CamelCase; functions kept small (the health/remediate orchestration is split into named helpers, each <50 LOC). New public members get XML doc comments.
- **II. Testing Standards**: PASS — deterministic unit tests for: the validation case (instance id + serial required), the runner dispatch gate, the sequence dispatcher (reads Parameters, calls handler), the command-step executor branch, repository payload validation, `ldconsole` output parsing (`isrunning`/`list2`), the `sys.boot_completed` probe parsing, and the handler's outcome matrix (already-healthy / started / restarted / failed-to-recover / unsupported) with a **faked** `ILdConsoleClient` and device probe (no real emulator, no real waits — timeouts/intervals injected as small values). Web-ui component tests for the selector/panel. Coverage ≥80% line / ≥70% branch on touched areas.
- **III. UX Consistency**: PASS — action naming and behavior follow the existing `ensure-game-running` conventions (kebab-case action key, neutral outcome reporting, "failed: <reason>" messages) and the `connect-to-game` conventions for the parameterized authoring panel (instance + serial fields).
- **IV. Performance**: PASS — healthy path is a few sub-second process calls; the only long wait is a deliberate, capped, configurable boot-wait during an actual (re)start. No hot path touched; poll interval is configurable and defaulted to 3s to avoid busy-spin. Perf note recorded in this plan.
- **V. Living Documentation**: PASS (planned) — `docs/architecture.md` capability/API surface + the new emulator-control client and config knobs documented; `ENVIRONMENT.md` updated with the three new `GAMEBOT_*` variables; this spec carries a `Status` line and `specs/STATUS.md` gets a new row; no earlier spec is superseded (this is a new sibling capability, complementary to 052 `ensure-game-running`).

No violations → Complexity Tracking not required.

## Project Structure

### Documentation (this feature)

```text
specs/070-ensure-emulator-running/
├── plan.md              # This file
├── research.md          # Phase 0 output
├── data-model.md        # Phase 1 output
├── quickstart.md        # Phase 1 output
├── contracts/           # Phase 1 output
│   └── ensure-emulator-running-action.md
├── checklists/
│   └── requirements.md  # Spec quality checklist (from /speckit-specify)
└── tasks.md             # Phase 2 output (/speckit-tasks — NOT created here)
```

### Source Code (repository root)

```text
src/GameBot.Emulator/Adb/
├── LdConsoleResolver.cs            # NEW — locate ldconsole.exe (mirror AdbResolver: env hints + probes + registry; sits next to adb.exe)
├── LdConsoleClient.cs              # NEW — [SupportedOSPlatform("windows")] wrapper: IsRunningAsync / LaunchAsync / RebootAsync + output parsing
└── AdbClient.cs                    # (reuse) — add a bounded GetBootCompletedAsync + device-state probe helper if not already derivable

src/GameBot.Emulator/Session/
└── ISessionManager.cs / SessionManager.cs  # (no change expected; probe uses AdbClient directly like AdbGameOperations)

src/GameBot.Domain/Actions/
├── ActionTypes.cs                  # + EnsureEmulatorRunning = "ensure-emulator-running"
├── PrimitiveActionBase.cs          # + EnsureEmulatorRunning const + add to PrimitiveActionTypes.All
├── PrimitiveActionVariants.cs      # + PrimitiveEnsureEmulatorRunningAction (InstanceName?/InstanceIndex?/AdbSerial)
├── EnsureEmulatorRunningArgs.cs    # NEW — strongly-typed args (mirror ConnectToGameArgs): instance id (name|index) + adbSerial + TryFrom
└── PrimitiveActionValidationService.cs  # + case: require adbSerial and at least one of instanceName/instanceIndex

src/GameBot.Domain/Commands/
├── CommandStep.cs                  # + CommandStepType.EnsureEmulatorRunning + EnsureEmulatorRunningConfig
└── FileSequenceRepository.cs       # + ActionTypes.EnsureEmulatorRunning in the supportedActionTypes set (SEPARATE hard-coded set!)

src/GameBot.Domain/Services/
└── SequenceRunner.cs               # + EnsureEmulatorRunning in IsDispatchedPrimitiveAction

src/GameBot.Domain/Config/
└── AppConfig.cs                    # + EmulatorProbeTimeoutMs=10000, EmulatorBootWaitMs=120000, EmulatorPollIntervalMs=3000

src/GameBot.Service/Services/EnsureEmulatorRunning/   # NEW folder (mirror EnsureGameRunning/)
├── IEnsureEmulatorRunningActionHandler.cs
├── EnsureEmulatorRunningActionHandler.cs   # orchestration: check → launch|reboot → poll boot-complete → outcome
├── EnsureEmulatorRunningActionResult.cs    # outcome enum + result
├── IEmulatorControl.cs                     # seam over LdConsoleClient (isrunning/launch/reboot) for fakeable tests
├── LdConsoleEmulatorControl.cs             # Windows adapter → LdConsoleClient
├── IEmulatorDeviceProbe.cs                 # seam over adb devices-state + sys.boot_completed
└── AdbEmulatorDeviceProbe.cs               # Windows adapter → AdbClient

src/GameBot.Service/Services/
├── SequenceExecution/SequenceExecutionService.cs  # + DispatchEnsureEmulatorRunningAsync (reads Action.Parameters → args → handler)
├── CommandExecutor.cs              # + CommandStepType.EnsureEmulatorRunning branch → handler
└── GameBotServiceSetup.cs          # DI registration for the handler + seams

src/GameBot.Service/
├── Models/Commands.cs             # + CommandStepTypeDto.EnsureEmulatorRunning + config DTO fields
└── Endpoints/CommandsEndpoints.cs # + DTO<->domain mapping + ValidateStep case

src/GameBot.Service/GameBotServiceSetup.cs  # BuildAppConfig: read the 3 new GAMEBOT_* env vars (beside GAMEBOT_ADB_*)
src/GameBot.Service/Services/IConfigApplier.cs        # runtime-mutable apply of the 3 new knobs from the config snapshot
src/GameBot.Service/Services/ConfigSnapshotService.cs # add the 3 new keys to the default snapshot

src/mcp-server/src/tools/
└── commands.ts                    # description text: add EnsureEmulatorRunning to the primitive/step list

src/web-ui/src/components/commands/
├── ActionTypeSelector.tsx         # + 'EnsureEmulatorRunning' union member + <option>
├── EnsureEmulatorRunningPanel.tsx # NEW panel (mirror the connect-to-game panel: instance id + adbSerial inputs)
└── CommandForm.tsx                # wire the new action type into add-step flow

tests/unit/…                       # validation, runner gate, dispatcher, command executor, repo, ldconsole parsing, boot-complete parsing, handler outcome matrix
tests/integration/…                # sequence-with-ensure-emulator end-to-end (faked emulator control + probe)
src/web-ui/src/components/commands/__tests__/…  # selector + panel + form tests

docs/architecture.md               # capability + emulator-control client + config knobs
ENVIRONMENT.md                     # 3 new GAMEBOT_* vars in the ADB/Emulator section
specs/STATUS.md                    # new row for 070
```

**Structure Decision**: The existing multi-surface layout is reused unchanged for the action wiring
(mirroring `ensure-game-running`). The one genuinely new surface is emulator **process** control,
which today does not exist anywhere in the codebase — every path only *connects* to an
already-running device. That is introduced as a small, cohesive `LdConsoleClient` in
`GameBot.Emulator` (parallel to `AdbClient`) plus two thin fakeable seams (`IEmulatorControl`,
`IEmulatorDeviceProbe`) in the Service layer so the handler's decision logic is unit-testable without
a real emulator or real waits.

## Design Decisions

1. **Parameterized like `connect-to-game`, orchestrated like `ensure-game-running`.** The action
   carries authoring parameters (instance name/index + adbSerial), so its variant, validation, DTO,
   and web-ui panel mirror `connect-to-game`. Its *execution* is a Service-layer handler with an
   outcome enum, mirroring `EnsureGameRunningActionHandler`, and dispatched from
   `SequenceExecutionService.DispatchActionAsync` and `CommandExecutor` the same way.

2. **New device-control surface = `ldconsole.exe`, wrapped in `LdConsoleClient`.** LDPlayer's
   `ldconsole`/`dnconsole` CLI exposes `isrunning`, `launch`, and `reboot` keyed by `--name` or
   `--index`. `LdConsoleClient` parallels `AdbClient` (process-exec + logging + `[SupportedOSPlatform("windows")]`)
   and is located by `LdConsoleResolver`, which mirrors `AdbResolver` exactly (it lives in the same
   LDPlayer install dir as `adb.exe`, so the same `LDPLAYER_HOME`/`LDP_HOME` + probe-path + registry
   strategy finds it — only the filename differs).

3. **Health = three independent checks; "hanging" = running-but-unresponsive.** (a) `isrunning`
   true; (b) `adb devices` shows the serial in state `device` (existing `ParseDeviceSerials` logic in
   `SessionManager` already distinguishes `device` from `offline`); (c) a bounded
   `getprop sys.boot_completed` == `1`. Running with (b) or (c) failing ⇒ hung ⇒ reboot. Not running
   ⇒ launch.

4. **Restart = single `reboot` (clarified), not quit+launch.** Simpler, atomic, and matches operator
   expectation; the poll-until-boot-complete afterwards is identical for launch and reboot.

5. **Fakeable seams for deterministic tests.** `IEmulatorControl` and `IEmulatorDeviceProbe` let unit
   tests drive every outcome (already-healthy, launched-then-healthy, hung-then-rebooted-healthy,
   never-recovers-within-timeout, unsupported-platform, tool-not-found, nonexistent-instance) with
   injected tiny timeouts, so no test waits on real wall-clock or a real emulator. The Windows
   adapters (`LdConsoleEmulatorControl`, `AdbEmulatorDeviceProbe`) are thin and covered by the
   parsing unit tests plus platform-guarded integration coverage.

6. **Graceful degradation mirrors `ensure-game-running`.** Non-Windows short-circuits to a neutral
   `PlatformUnsupported`-style outcome before any process call; a missing `ldconsole` (resolver
   returns null) or unavailable ADB yields the same neutral, non-crashing "not-applied" result.
   A *nonexistent but well-formed* instance identifier is distinct: it is a genuine failure
   (`InstanceNotFound`) that fails the step (FR-014).

7. **Config knobs on `AppConfig`, bound where the other `GAMEBOT_ADB_*` knobs are.** Three ints
   (`EmulatorProbeTimeoutMs`, `EmulatorBootWaitMs`, `EmulatorPollIntervalMs`) with the documented
   defaults, overridable via `GAMEBOT_EMULATOR_PROBE_TIMEOUT_MS` / `GAMEBOT_EMULATOR_BOOT_WAIT_MS` /
   `GAMEBOT_EMULATOR_POLL_INTERVAL_MS`. Startup binding is a few lines in
   `GameBotServiceSetup.BuildAppConfig` beside the existing `GAMEBOT_ADB_*` reads; the same three keys
   are added to `ConfigSnapshotService`'s default snapshot and to `IConfigApplier.Apply` for
   runtime-mutable updates (matching how `GAMEBOT_ADB_RETRIES` is handled across all three).

8. **Allow-list coverage.** Adding the const to `PrimitiveActionTypes.All` auto-covers the three
   validators that derive from it. **`FileSequenceRepository.ValidateActionPayloads` keeps its own
   hard-coded set** and MUST be updated separately, or persisting a sequence that uses the action
   500s (known pitfall from 069/052).

## Complexity Tracking

No constitution violations — no entries required.
