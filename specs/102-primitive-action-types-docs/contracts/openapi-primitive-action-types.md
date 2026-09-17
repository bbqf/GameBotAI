# Contract: PrimitiveAction in the OpenAPI document

Served at `GET /swagger/v1/swagger.json`. Component `PrimitiveActionRequest` (also published as `PrimitiveAction`).

## `properties.type`

- `type: string`
- `enum`: exactly `SequenceActionTypes.All`, in order:
  `tap`, `swipe`, `key`, `command`, `connect-to-game`, `WaitForImage`, `ensure-game-running`, `go-to-home-screen`,
  `ensure-emulator-running`, `reschedule-self`, `notify`
- `description` contains: `case-insensitive`, `connect-to-game`, `/api/sessions/start`

## `properties.payload`

- `description` contains, for every enum value `t`, the marker `t:` followed by that type's fields (research R-004).
- `reschedule-self:` section contains: `option`, `AtQueueStart`, `OncePerRun`, `Timer`, `EveryStep`,
  `timerTimeOfDay`, `timerRelativeOffset`, `24:00:00`, `exactly one`, `ocrOffset`, `region`, `fallback`, `min`,
  `max`, `any other option`, `queue`.
- `tap:` contains `x`, `y`; `swipe:` contains `x1`, `y1`, `x2`, `y2`, `durationMs`; `key:` contains `keyCode`;
  `command:` contains `commandId`; `notify:` contains `message`; `connect-to-game:` contains `gameId`, `adbSerial`;
  `WaitForImage:` contains `detectionTarget`, `timeoutMs`; `ensure-emulator-running:` contains `adbSerial`,
  `instanceName`; `ensure-game-running:` and `go-to-home-screen:` contain `no payload fields`.

## Examples

`POST /api/sequences` request example `steps` contains a step whose `primitiveAction` is
`{ "type": "reschedule-self", "payload": { "option": "Timer", "timerRelativeOffset": "00:30:00" } }`.

## Validation error (runtime, unchanged status)

`POST /api/sequences` with `dryRun: true` and a step `primitiveAction.type: "bogus"` → `400`, `errors[]` contains:

```text
Step '<stepId>' action type 'bogus' is not a supported primitive action type (expected one of tap, swipe, key, command, connect-to-game, WaitForImage, ensure-game-running, go-to-home-screen, ensure-emulator-running, reschedule-self, notify).
```
