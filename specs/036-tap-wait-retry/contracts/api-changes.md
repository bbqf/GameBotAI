# API Contract Changes: 036 Tap Wait-and-Retry

## No New Endpoints

This feature does not add new API endpoints. All changes are to configuration and internal behaviour.

## Configuration Parameters (New)

The following environment variables are introduced. They are surfaced in the existing `GET /api/config` snapshot response alongside all other `GAMEBOT_*` parameters.

### GAMEBOT_CAPTURE_INTERVAL_MS

- **Type**: integer (milliseconds)
- **Default**: 500
- **Minimum**: 50
- **Description**: Screenshot capture interval for the background capture service. Also used as the base wait time (WAIT_TIME) for primitive tap retry cycles.
- **Note**: This env var already existed for `BackgroundScreenCaptureService` but was not previously included in `AppConfig` or the configuration file defaults. It is now promoted to `AppConfig` for dual use.

### GAMEBOT_TAP_RETRY_COUNT

- **Type**: integer
- **Default**: 3
- **Minimum**: 0
- **Description**: Maximum number of retry cycles for primitive tap image detection. The system checks for the target image up to `COUNT + 1` times (initial check + COUNT retries). Setting to 0 means a single detection check with no retries.

### GAMEBOT_TAP_RETRY_PROGRESSION

- **Type**: double (floating-point)
- **Default**: 1.0
- **Minimum**: > 0 (exclusive; must be positive)
- **Description**: Multiplier applied to the wait time after each unsuccessful retry cycle. `1.0` = constant intervals. `2.0` = doubling intervals (exponential backoff). Values between 0 and 1 create decreasing intervals.

## PrimitiveTapStepOutcome Contract Changes

The `PrimitiveTapStepOutcome` record structure is **unchanged**. New status/reason values are introduced:

### New Status Value

| Status | When | Description |
|--------|------|-------------|
| `"cancelled"` | Cancellation requested during retry | Step was cancelled mid-retry loop |

### New Reason Patterns

| Reason Pattern | Applies to Status | Description |
|---------------|------------------|-------------|
| `"detected_after_N_retries"` | `"executed"` | Image found after N retry cycles |
| `"detection_failed_after_N_retries"` | `"skipped_detection_failed"` | Image not found after exhausting all N retry cycles |
| `"cancelled_during_retry_N"` | `"cancelled"` | Cancellation occurred during retry cycle N |

Where `N` is replaced with the actual cycle count (integer).

## Execution Log Impact

The execution log entries for primitive tap steps continue to use the existing `ExecutionLogService` mapping. The `Reason` field is passed through to execution log detail items, so retry metadata appears in log entries without any mapping changes:

- **Successful tap**: Detail shows `"Tap executed at (x,y)."` — the reason `"detected_after_N_retries"` is captured in the `ExecutionStepOutcome.ReasonText`.
- **Failed detection**: Detail shows `"Step N was not executed: detection_failed_after_N_retries"`.
- **Cancelled**: Detail shows `"Step N was not executed: cancelled_during_retry_N"`.
