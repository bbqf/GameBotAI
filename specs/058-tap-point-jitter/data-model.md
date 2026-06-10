# Data Model: 058 Tap-Point Jitter

## Entities

### AppConfig (Extended)

Existing domain configuration class extended with one new property for tap/swipe jitter.

| Field | Type | Default | Validation | Description |
|-------|------|---------|------------|-------------|
| TapRetryCount | int | 3 | ≥ 0 | Existing — primitive tap retry cycles |
| AdbRetries | int | 2 | ≥ 0 | Existing — ADB operation retries |
| **TapJitterRadiusPx** | int | 5 | ≥ 0 (negative → fallback to 5) | Maximum random per-axis offset (pixels) applied independently to the X and Y coordinate of every tap and swipe endpoint before dispatch. `0` disables jitter (coordinates pass through unchanged). Maps to `GAMEBOT_TAP_JITTER_RADIUS_PX` env var. |

### CoordinateJitter (New helper)

A new stateless static helper in `GameBot.Domain.Services`, not persisted, used by `SessionManager`.

| Member | Signature | Description |
|--------|-----------|-------------|
| `Apply` | `(int X, int Y) Apply(int x, int y, int radiusPx)` | Returns `(x, y)` unchanged if `radiusPx <= 0`. Otherwise returns `(Max(0, x + dx), Max(0, y + dy))` where `dx`, `dy` are independently drawn from `[-radiusPx, +radiusPx]` using a non-cryptographic LCG (seeded from clock ticks XORed with a monotonically incrementing counter, per research R-002). |

### PrimitiveTapResolvedPoint (Unchanged)

| Field | Type | Description |
|-------|------|-------------|
| X | int | X coordinate |
| Y | int | Y coordinate |

### PrimitiveSwipePoints (New)

| Field | Type | Description |
|-------|------|-------------|
| Start | PrimitiveTapResolvedPoint | Swipe start point (x1, y1) |
| End | PrimitiveTapResolvedPoint | Swipe end point (x2, y2) |

### PrimitiveTapStepOutcome (Extended, additive)

Existing record gains three new optional trailing fields (default `null`); all existing fields and positional ordering are unchanged.

| Field | Type | Description |
|-------|------|-------------|
| StepOrder | int | Position of the step in the command (existing) |
| Status | string | Outcome status (existing values unchanged) |
| Reason | string? | Human-readable reason (existing) |
| ResolvedPoint | PrimitiveTapResolvedPoint? | Pre-jitter target (x, y) for tap-shaped steps (existing — semantics unchanged: "target") |
| DetectionConfidence | double? | Template match confidence (existing) |
| StepType | string? | Existing |
| TimeoutMs / EffectiveTimeoutMs / ReferenceImageId / ImageLoadStatus / ConfiguredConfidence | various | Existing |
| **ExecutedPoint** | PrimitiveTapResolvedPoint? | **New.** Post-jitter (x, y) actually sent to the device for a tap-shaped step. |
| **TargetSwipe** | PrimitiveSwipePoints? | **New.** Pre-jitter (start, end) points for an explicit swipe step. |
| **ExecutedSwipe** | PrimitiveSwipePoints? | **New.** Post-jitter (start, end) points actually sent for an explicit swipe step. |

### Configuration Parameters (Environment Variables + Config File)

| Env Variable | Config Key | Type | Default | Description |
|-------------|------------|------|---------|-------------|
| `GAMEBOT_TAP_JITTER_RADIUS_PX` | `TapJitterRadiusPx` | int | 5 | Maximum per-axis random offset (pixels) for tap/swipe coordinates. `0` disables jitter; negative values fall back to 5. |

## Relationships

```
AppConfig (singleton)
  └── TapJitterRadiusPx ──→ SessionManager.SendInputsAsync (jitter magnitude for tap/swipe args)
                                  │
                                  └──→ CoordinateJitter.Apply (per-coordinate offset + clamp)

SessionManager.SendInputsAsync (jitter normalization pass at method entry,
                                before the ADB-mode branch — runs in both real-ADB and stub modes)
  ├── reads InputAction.Args["x"]/["y"] (tap) or ["x1"]["y1"]["x2"]["y2"] (swipe)
  ├── computes jittered values via CoordinateJitter.Apply
  ├── writes jittered values back into InputAction.Args (mutating the shared dictionary)
  └── dispatches jittered coordinates to AdbClient.TapAsync / SwipeAsync (when ADB active)

CommandExecutor
  ├── constructs InputAction with target (pre-jitter) coordinates in Args
  ├── awaits SendInputsAsync (Args mutated in place to post-jitter values)
  ├── reads back jittered Args values
  └── produces PrimitiveTapStepOutcome
       ├── ResolvedPoint / TargetSwipe = pre-jitter target (captured before dispatch)
       ├── ExecutedPoint / ExecutedSwipe = post-jitter values (read back after dispatch)
       └── consumed by ExecutionLogService, CommandsEndpoints, StepsEndpoints (additive mapping)
```

## State Transitions

Not applicable — this feature introduces a stateless coordinate transformation (no new entity lifecycle or state machine). The existing primitive-tap/swipe execution flow (validate → resolve target → dispatch → record outcome) is unchanged in shape; jitter is an additional pure transformation applied during dispatch.

## Validation Rules

1. `TapJitterRadiusPx` < 0 → falls back to default 5 (FR-006), applied identically in `Program.cs` startup wiring and `IConfigApplier.Apply` runtime updates.
2. `TapJitterRadiusPx` == 0 → `CoordinateJitter.Apply` returns coordinates unchanged; no randomness invoked (FR-005).
3. `TapJitterRadiusPx` > 0 → each of `dx`, `dy` independently and uniformly drawn from `[-TapJitterRadiusPx, +TapJitterRadiusPx]` (FR-003).
4. Resulting `x + dx` and `y + dy` are clamped with `Math.Max(0, ...)` — never negative (FR-007/SC-005). No upper-bound clamp (per spec Assumptions).
5. Jitter is applied to both endpoints of a swipe independently (FR-002), including the degenerate "tap-as-swipe" case where `x1==x2, y1==y2` before jitter (may diverge slightly after — documented edge case, acceptable per spec).
