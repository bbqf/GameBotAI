# API Contract Changes: 058 Tap-Point Jitter

## No New Endpoints

This feature does not add new API endpoints. All changes are to configuration and to additive fields on existing step-outcome/execution-log structures.

## Configuration Parameters (New)

The following environment variable is introduced. It is surfaced in the existing `GET /api/config` snapshot response alongside all other `GAMEBOT_*` parameters, and therefore appears automatically in the web-ui Configuration variables list (no new UI component).

### GAMEBOT_TAP_JITTER_RADIUS_PX

- **Type**: integer (pixels)
- **Default**: 5
- **Minimum**: 0 (negative values are rejected and fall back to the default of 5)
- **Description**: Maximum random offset, in pixels, applied independently to the X and Y coordinate of every tap and swipe endpoint immediately before it is sent to the device. Each axis offset is drawn independently and uniformly from `[-radius, +radius]`. Setting this to `0` disables jitter entirely (coordinates pass through unchanged). Applies automatically to all taps and swipes regardless of how the target coordinates were determined (authored, image-detection-resolved, or replayed); there is no per-step opt-in/opt-out and no per-step UI control.

## PrimitiveTapStepOutcome Contract Changes

The `PrimitiveTapStepOutcome` record gains three new **optional, additive** trailing fields. All existing fields, types, and ordering are unchanged, so existing positional-constructor call sites and serialized payloads remain valid (new fields default/serialize to `null` when absent).

| New Field | Type | Applies To | Description |
|-----------|------|-----------|-------------|
| `ExecutedPoint` | `PrimitiveTapResolvedPoint?` | Tap-shaped steps (primitive tap) | Post-jitter (x, y) actually sent to the device. `ResolvedPoint` continues to represent the pre-jitter target. |
| `TargetSwipe` | `PrimitiveSwipePoints?` (new record: `Start`, `End` each `PrimitiveTapResolvedPoint`) | Explicit swipe steps | Pre-jitter (start, end) target points. |
| `ExecutedSwipe` | `PrimitiveSwipePoints?` | Explicit swipe steps | Post-jitter (start, end) points actually sent to the device. |

### New Record: PrimitiveSwipePoints

```csharp
internal sealed record PrimitiveSwipePoints(PrimitiveTapResolvedPoint Start, PrimitiveTapResolvedPoint End);
```

## DTO Changes (Additive)

- **`src/GameBot.Service/Models/Commands.cs`**: `ResolvedPointDto` is unchanged and is **reused** for all new point shapes (no duplicate point DTO type is introduced). New optional DTO fields are added to the step-outcome response model(s) mirroring the new `PrimitiveTapStepOutcome` fields:
  - `ResolvedPointDto? ExecutedPoint` (`{ X, Y }`)
  - `SwipePointsDto? TargetSwipe` and `SwipePointsDto? ExecutedSwipe`, where `SwipePointsDto` is `{ Start: ResolvedPointDto, End: ResolvedPointDto }`
- **`src/GameBot.Service/Endpoints/CommandsEndpoints.cs`** (~line 178-182): mapping extended to populate the new DTO fields from the new outcome fields when present (omitted/`null` when jitter produced no outcome data, e.g. non-tap/swipe steps).
- **`src/GameBot.Service/Endpoints/StepsEndpoints.cs`** (~line 170): the anonymous step-result object gains the same additive fields.

All additions are optional/nullable; clients that don't read the new fields are unaffected.

## Execution Log Impact

`src/GameBot.Service/Services/ExecutionLog/ExecutionLogService.cs` (`LogCommandExecutionAsync`) extends the existing detail-item construction (FR-013/SC-006):

- **Tap step, jitter applied (ExecutedPoint differs from ResolvedPoint)**: detail text becomes e.g. `"Tap targeted (X,Y), executed at (X',Y')."` (both pre- and post-jitter coordinates shown).
- **Tap step, no jitter offset (radius=0 or offset happened to be zero)**: detail text remains `"Tap executed at (X,Y)."` (unchanged when target == executed).
- **Swipe step, jitter applied**: new detail text e.g. `"Swipe targeted (X1,Y1)->(X2,Y2), executed (X1',Y1')->(X2',Y2')."`.
- **Failed/skipped steps**: unchanged — `ExecutedPoint`/`ExecutedSwipe` are `null` when no dispatch occurred, and detail text falls back to existing "Step N was not executed: ..." formatting.

No changes to `ExecutionLogService`'s public mapping signatures — only the constructed detail string content and the additive outcome fields it now has access to.
