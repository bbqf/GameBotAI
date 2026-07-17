# Quickstart: Ensure Emulator Running Action

## What it does

Adds an `ensure-emulator-running` action that, at the top of a sequence/queue, makes sure the target
LDPlayer instance is up and responsive — starting a stopped instance or restarting a hung one — before
the game automation runs.

## Author a one-step sequence (smoke test)

1. In the web authoring UI, add an **Action** step and pick **ensure emulator running**.
2. Enter the **instance name** (e.g. `LDPlayer-5558`) or **instance index** (e.g. `1`), and the
   **adb serial** (e.g. `emulator-5558`).
3. Save. Validation passes when a serial and at least one instance identifier are present.

## Run it against the three states

| Starting state                | Expected result                                             |
|-------------------------------|-------------------------------------------------------------|
| Instance already up + responsive | No restart; step **succeeds** (idempotent no-op).        |
| Instance closed               | Instance **launched**, waits for boot, step **succeeds**.   |
| Instance running but frozen   | Instance **rebooted**, waits for boot, step **succeeds**.   |
| Instance can't recover in time| Step **fails** with `recovery timed out` (no infinite hang).|
| Bad instance name/index       | Step **fails** with `instance not found`.                   |
| Non-Windows / tools missing   | Neutral **no-op success** (unsupported), run continues.     |

## Tuning (host-level env vars)

| Env var                              | Default | Meaning                              |
|--------------------------------------|---------|--------------------------------------|
| `GAMEBOT_EMULATOR_PROBE_TIMEOUT_MS`  | 10000   | Per-probe responsiveness timeout.    |
| `GAMEBOT_EMULATOR_BOOT_WAIT_MS`      | 120000  | Max wait for boot-complete after (re)start. |
| `GAMEBOT_EMULATOR_POLL_INTERVAL_MS`  | 3000    | Poll interval while waiting.         |

Set them the same way as the other `GAMEBOT_*` knobs (see `ENVIRONMENT.md`). They are also
runtime-mutable via the config endpoints.

## Green gate

- Backend: `dotnet build` + `dotnet test` (unit/integration/contract) green.
- Web-ui: `vite build` + `jest` green (the authoritative gate; lint/tsc have pre-existing failures).

## Where it plugs in (for reviewers)

- Emulator control: `LdConsoleClient` + `LdConsoleResolver` (`GameBot.Emulator`), fronted by
  `IEmulatorControl` / `IEmulatorDeviceProbe` seams.
- Orchestration: `EnsureEmulatorRunningActionHandler` (`GameBot.Service`), dispatched from
  `SequenceExecutionService.DispatchActionAsync` and `CommandExecutor`.
- Same wiring as `ensure-game-running`: action-type constants, `PrimitiveActionTypes.All`,
  `FileSequenceRepository.ValidateActionPayloads` (separate hard-coded set — must be updated),
  `SequenceRunner.IsDispatchedPrimitiveAction`, command DTO/enum, MCP description, web-ui panel.
