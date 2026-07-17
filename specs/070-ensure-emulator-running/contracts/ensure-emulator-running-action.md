# Contract: `ensure-emulator-running` Action

The action is exposed on the same surfaces as `ensure-game-running` / `connect-to-game`. This file
documents the authoring contract (what a step looks like) and the execution contract (outcomes),
not new HTTP endpoints — the action rides the existing sequence/command payloads.

## Action type key

`ensure-emulator-running`

## Sequence action payload (Action step)

```json
{
  "stepType": "Action",
  "action": {
    "type": "ensure-emulator-running",
    "parameters": {
      "instanceName": "LDPlayer-5558",
      "adbSerial": "emulator-5558"
    }
  }
}
```

Index form:

```json
{
  "action": {
    "type": "ensure-emulator-running",
    "parameters": { "instanceIndex": 1, "adbSerial": "emulator-5558" }
  }
}
```

## Command step

```json
{
  "type": "EnsureEmulatorRunning",
  "ensureEmulatorRunning": {
    "instanceName": "LDPlayer-5558",
    "adbSerial": "emulator-5558"
  }
}
```

(DTO surface: `CommandStepTypeDto.EnsureEmulatorRunning` + `ensureEmulatorRunning` config object with
`instanceName?`, `instanceIndex?`, `adbSerial`.)

## Validation rules

| Condition                                             | Result                                   |
|-------------------------------------------------------|------------------------------------------|
| `adbSerial` missing/blank                             | reject: "requires adbSerial"             |
| neither `instanceName` nor `instanceIndex` supplied   | reject: "requires an instance name or index" |
| `instanceIndex` < 0                                   | reject: "instanceIndex must be ≥ 0"      |
| valid                                                 | accept; round-trips through save/load    |

## Execution contract

- **Precheck (before any tool call)**: non-Windows host → neutral no-op success (unsupported).
- **Tool discovery**: `ldconsole` not found OR ADB unavailable → neutral no-op success (unsupported).
- **Health**: healthy (running + device state `device` + `sys.boot_completed=1`) → success, no action.
- **Remediate**: not running → `launch`; hung → `reboot`; then poll device-state + boot-complete every
  `EmulatorPollIntervalMs` up to `EmulatorBootWaitMs`.
  - reaches healthy → success (`Started` / `Restarted`).
  - never healthy within the ceiling → **failed** with reason `recovery timed out`.
- **Nonexistent instance** (well-formed identifier, no match) → **failed** with reason
  `instance not found`.
- **Step semantics**: a success outcome does not abort the sequence; a genuine failure fails the step
  consistent with the other device-driving primitive actions.

## Step result messages (mirroring `ensure-game-running`)

| Outcome              | Dispatch outcome | Message (example)                                   |
|----------------------|------------------|-----------------------------------------------------|
| `AlreadyHealthy`     | `executed`       | `emulator already running and responsive`           |
| `Started`            | `executed`       | `emulator was started and is responsive`            |
| `Restarted`          | `executed`       | `emulator was restarted and is responsive`          |
| `RecoveryTimedOut`   | `failed`         | `ensure-emulator-running failed: recovery timed out`|
| `InstanceNotFound`   | `failed`         | `ensure-emulator-running failed: instance not found`|
| `PlatformUnsupported`| `executed`       | `emulator control unsupported on this host`         |
| `ControlUnavailable` | `executed`       | `emulator control unavailable (ldconsole/adb)`      |

## MCP / tooling

`src/mcp-server/src/tools/commands.ts` description text lists `EnsureEmulatorRunning` among the
supported action/step types so the action is discoverable via the automation tool surface.

## Web-UI

`ActionTypeSelector` offers "ensure emulator running"; selecting it renders
`EnsureEmulatorRunningPanel` (instance name/index + adbSerial inputs, mirroring the connect-to-game
panel). `CommandForm` wires the new type into the add-step flow.
