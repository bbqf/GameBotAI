# Data Model: Press and hold at a detected point (anchored long press)

## PrimitiveTapConfig (domain, `src/GameBot.Domain/Commands/CommandStep.cs`)

| Field | Type | Rule |
|-------|------|------|
| `DetectionTarget` | `DetectionTarget` | Required. No change. |
| `HoldMs` | `int?` | New. Optional. From `MinHoldMs` (0) to `MaxHoldMs` (5000). Null is not written to storage. |

Constants: `PrimitiveTapConfig.MinHoldMs = 0`, `PrimitiveTapConfig.MaxHoldMs = 5000`, `PrimitiveTapConfig.DefaultTapDurationMs = 200`.

Behavior: `HoldMs` null or 0 gives the tap of today (duration 200 ms). `HoldMs` more than 0 gives a press and hold with a duration of `HoldMs`.

## PrimitiveTapConfigDto (API, `src/GameBot.Service/Models/Commands.cs`)

| Field | JSON | Type | Rule |
|-------|------|------|------|
| `DetectionTarget` | `detectionTarget` | object | Required. No change. |
| `HoldMs` | `holdMs` | integer, nullable | New. `[Range(0, 5000)]`. Not written when null. |

## PrimitiveTapStepOutcome (service, `src/GameBot.Service/Services/ICommandExecutor.cs`)

| Field | Type | Rule |
|-------|------|------|
| `HoldMs` | `int?` | New, last optional parameter. Set only when the step pressed and held (`HoldMs > 0`). |

## StepExecutionOutcomeDto (API, `src/GameBot.Service/Models/Commands.cs`)

| Field | JSON | Type | Rule |
|-------|------|------|------|
| `HoldMs` | `holdMs` | integer, nullable | New. Not written when null. |

## Execution log detail (kind `tap`)

| Attribute | Rule |
|-----------|------|
| `x`, `y`, `executedX`, `executedY`, `confidence` | No change. |
| `holdMs` | New. Only for a press and hold. |

Text for a press and hold: "Press and hold at (x,y) for N ms." or "Press and hold targeted (x,y), executed at (x2,y2) for N ms.". Text for a tap: no change.

## Web UI form (`src/web-ui`)

| Type | Field | Rule |
|------|-------|------|
| `StepEntry.primitiveTap` | `holdMs?: string` | New. Empty means absent. |
| `TapPanelValue` | `holdMs?: string` | New. Integer from 0 to 5000, or empty. |
| `PrimitiveTapConfigDto` (TS) | `holdMs?: number` | New. |
