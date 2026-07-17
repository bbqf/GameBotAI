# Contract: Go To Home Screen Action

The action is exposed through the same JSON-authored surfaces as the other primitive/parameterless
actions. There is no new HTTP endpoint; the contract is the JSON shape accepted by the existing
sequence and command APIs, plus the dispatch outcome.

## 1. As a sequence step (action payload)

Persisted/accepted inside a sequence step's `action`:

```json
{
  "stepId": "leave-game",
  "action": {
    "type": "go-to-home-screen",
    "parameters": {}
  }
}
```

- `type` MUST be `"go-to-home-screen"`. In any polymorphic step object the `type` property MUST come
  first (the service JSON deserializer requires the discriminator before other fields).
- `parameters` MUST be empty (`{}`) or omitted. Extra parameters are ignored; none are required.
- Validation: accepted by `SequenceStepValidationService`, `ActionPayloadValidationService`, and the
  `FileSequenceRepository` persistence guard. A sequence containing only this step is valid.

### Dispatch outcome

| Condition | Outcome | Step result |
|---|---|---|
| HOME key accepted by session (real ADB or stub) | `executed` | Succeeded |
| No running session resolvable / input rejected | `failed` | Failed, sequence stops |

## 2. As a command step

Accepted by `POST /api/commands` (and update) inside `steps[]`:

```json
{
  "type": "GoToHomeScreen",
  "order": 0
}
```

- `type` MUST be `"GoToHomeScreen"` (the `CommandStepTypeDto` enum name).
- No config object is required (parallels `EnsureGameRunning`). `ValidateStep` returns no error for
  this type.
- Execution: `CommandExecutor` sends `KEYCODE_HOME` and reports a step outcome of `executed` on
  success (stub success on non-Windows).

## 3. Authoring UI (web-ui)

- `ActionTypeSelector` offers a **"Go to Home Screen"** option (value `GoToHomeScreen`) alongside
  the existing actions.
- Selecting it shows a parameterless confirmation panel (mirrors `EnsureGameRunningPanel`): a short
  description and Add/Cancel, no input fields.

## 4. Automation tool (MCP)

- No schema change (command/sequence tools accept free-form JSON). The `create_command` /
  `create_sequence` tool descriptions list `GoToHomeScreen` among the available primitive step
  types so the model can author it.

## Graceful degradation (FR-007)

On a non-Windows host or a non-ADB (stub) session the action completes successfully without
contacting a device and never throws — identical to how `ensure-game-running` / `key` inputs behave
in the same conditions.

## Backward compatibility

Purely additive. Existing sequences, commands, `connect-to-game`, and `ensure-game-running` behave
exactly as before. No persisted data requires migration.
