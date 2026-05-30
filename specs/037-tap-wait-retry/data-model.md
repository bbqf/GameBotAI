# Data Model: 036 Tap Wait-and-Retry

## Entities

### AppConfig (Extended)

Existing domain configuration class extended with three new properties for tap retry behaviour.

| Field | Type | Default | Validation | Description |
|-------|------|---------|------------|-------------|
| LoopMaxIterations | int | 1000 | > 0 | Existing — global loop iteration ceiling |
| **CaptureIntervalMs** | int | 500 | ≥ 50 | Base wait time between retry cycles; also used by BackgroundScreenCaptureService. Maps to `GAMEBOT_CAPTURE_INTERVAL_MS` env var. |
| **TapRetryCount** | int | 3 | ≥ 0 | Maximum number of retry cycles for primitive tap detection. 0 = single check, no retries. Maps to `GAMEBOT_TAP_RETRY_COUNT` env var. |
| **TapRetryProgression** | double | 1.0 | > 0 | Multiplier applied to wait time after each unsuccessful retry cycle. 1.0 = constant interval. Maps to `GAMEBOT_TAP_RETRY_PROGRESSION` env var. |

### PrimitiveTapStepOutcome (Unchanged structure)

Existing record — no schema changes. Retry metadata is encoded in existing fields.

| Field | Type | Description |
|-------|------|-------------|
| StepOrder | int | Position of the step in the command |
| Status | string | Outcome status: `"executed"`, `"skipped_invalid_config"`, `"skipped_detection_failed"`, `"skipped_invalid_target"`, **`"cancelled"`** (new value) |
| Reason | string? | Human-readable reason. New patterns: `"detected_after_N_retries"`, `"detection_failed_after_N_retries"`, `"cancelled_during_retry_N"` |
| ResolvedPoint | PrimitiveTapResolvedPoint? | Resolved (x, y) tap coordinates |
| DetectionConfidence | double? | Template match confidence score |

### Configuration Parameters (Environment Variables + Config File)

| Env Variable | Config Key | Type | Default | Description |
|-------------|------------|------|---------|-------------|
| `GAMEBOT_CAPTURE_INTERVAL_MS` | `CaptureIntervalMs` | int | 500 | Screenshot capture interval and base wait time (ms) |
| `GAMEBOT_TAP_RETRY_COUNT` | `TapRetryCount` | int | 3 | Maximum detection retry cycles for primitive taps |
| `GAMEBOT_TAP_RETRY_PROGRESSION` | `TapRetryProgression` | double | 1.0 | Wait time multiplier per retry cycle |

## Relationships

```
AppConfig (singleton)
  ├── CaptureIntervalMs ──→ BackgroundScreenCaptureService (capture loop interval)
  ├── CaptureIntervalMs ──→ CommandExecutor (base WAIT_TIME for retry loop)
  ├── TapRetryCount ──→ CommandExecutor (retry loop bound)
  └── TapRetryProgression ──→ CommandExecutor (wait escalation factor)

CommandExecutor.PrimitiveTap step
  ├── uses IScreenSource.GetLatestScreenshot() (refreshed each retry cycle)
  ├── uses IReferenceImageStore.TryGet() (once, before loop)
  ├── uses ITemplateMatcher (each retry cycle)
  └── produces PrimitiveTapStepOutcome (with retry metadata in Reason field)
       └── consumed by ExecutionLogService (unchanged mapping)
```

## State Transitions

### Retry Loop State Machine

```
[Start] ──→ Validate Config (services, template)
  │
  ├── Invalid ──→ [Skip with outcome]
  │
  └── Valid ──→ [Wait WAIT_TIME] ──→ [Detect Image]
                    │                      │
                    │                  ┌───┴───┐
                    │              Found    Not Found
                    │                │          │
                    │                │     attempt < COUNT?
                    │                │      │         │
                    │                │     Yes        No
                    │                │      │         │
                    │                │  WAIT_TIME ×=  │
                    │                │  PROGRESSION   │
                    │                │      │         │
                    │                │   [Wait]──→[Detect]
                    │                │                │
                    │                ▼                ▼
                    │          [Execute Tap]   [Fail: exhausted]
                    │                │
                    ▼                ▼
              [Cancelled]      [Outcome recorded]
```

At any point during `[Wait]`, a CancellationToken cancellation immediately aborts to `[Cancelled]`.

## Validation Rules

1. `CaptureIntervalMs` is clamped to minimum 50ms at startup (matches existing `BackgroundScreenCaptureService` behaviour).
2. `TapRetryCount` < 0 → falls back to default 3 with a warning log.
3. `TapRetryProgression` ≤ 0 → falls back to default 1.0 with a warning log.
4. `TapRetryProgression` between 0 and 1 is valid (decreasing waits) but unusual — no special handling.
5. No upper bound on per-cycle wait time per clarification decision.
