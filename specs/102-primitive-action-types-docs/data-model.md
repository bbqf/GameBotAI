# Data Model: Publish primitive action types and payload shapes

No persisted data changes. The entities below are documentation-level views of existing shapes.

## SequenceActionTypes (new domain constant list)

| Member | Type | Value |
|---|---|---|
| `All` | `IReadOnlyList<string>` | `tap, swipe, key, command, connect-to-game, WaitForImage, ensure-game-running, go-to-home-screen, ensure-emulator-running, reschedule-self, notify` (this order) |
| `SupportedValuesText` | `string` | `string.Join(", ", All)` |

Rules: membership is `OrdinalIgnoreCase`; `All` = `PrimitiveActionTypes.All` followed by `ActionTypes.RescheduleSelf`,
`ActionTypes.Notify`.

## PrimitiveAction (existing OpenAPI component `PrimitiveActionRequest` / alias `PrimitiveAction`)

| Field | Wire type | Change |
|---|---|---|
| `type` | string, required | + `enum` = `SequenceActionTypes.All`; + description (case-insensitive; session start accepts only `connect-to-game`) |
| `schemaVersion` | string, optional | none |
| `payload` | object (free-form) | + description: per-type payload fields (research R-004) |

Schema-level description: a step's action; see `type` and `payload`.

## reschedule-self payload (existing)

| Field | Type | Required | Rule |
|---|---|---|---|
| `option` | string | yes | `AtQueueStart` \| `OncePerRun` \| `Timer` \| `EveryStep` |
| `timerTimeOfDay` | string HH:mm:ss | Timer: one of the two, unless `ocrOffset` | only with Timer; not together with `timerRelativeOffset` |
| `timerRelativeOffset` | string HH:mm:ss | Timer: one of the two, unless `ocrOffset` | 00:00:00–24:00:00; only with Timer |
| `ocrOffset` | object | no | Timer only; `region{x,y,width,height}` required (positive size), `fallback` required (00:00:00–24:00:00), `min`/`max` optional (defaults 00:00:01 / 24:00:00), `min < max` |
