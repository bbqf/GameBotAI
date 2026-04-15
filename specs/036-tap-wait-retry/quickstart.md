# Quickstart: 036 Tap Wait-and-Retry

## What Changed

Primitive tap actions now wait for their target image to appear on screen before executing the tap, with configurable retries and progressive backoff. Previously, if the image wasn't visible at the exact moment of execution, the tap failed.

## Configuration

Three settings control retry behaviour. All have sensible defaults and work out of the box.

| Setting | Env Variable | Default | Description |
|---------|-------------|---------|-------------|
| Capture Interval | `GAMEBOT_CAPTURE_INTERVAL_MS` | 500 | Base wait time between retries (ms) |
| Retry Count | `GAMEBOT_TAP_RETRY_COUNT` | 3 | Max retry cycles (0 = no retries) |
| Progression | `GAMEBOT_TAP_RETRY_PROGRESSION` | 1.0 | Wait multiplier per retry (1.0 = constant) |

### Example: Default behaviour (no config needed)

With defaults, each primitive tap step:
1. Waits 500ms
2. Checks for image → if found, taps immediately
3. If not found, waits another 500ms and rechecks (up to 3 retries)
4. If still not found after 3 retries, fails

Total worst-case wait: 2000ms (4 checks × 500ms waits).

### Example: Exponential backoff

Set `GAMEBOT_TAP_RETRY_COUNT=5` and `GAMEBOT_TAP_RETRY_PROGRESSION=2`:

| Cycle | Wait Before Check |
|-------|------------------|
| 1 | 500ms |
| 2 | 1000ms |
| 3 | 2000ms |
| 4 | 4000ms |
| 5 | 8000ms |

Total worst-case wait: 15,500ms.

## Monitoring

- **Debug logs**: Each retry cycle logs the cycle number, wait duration, and detection result.
- **Info/Warning logs**: Final outcomes (image detected, retries exhausted, cancelled) log at higher levels.
- **Execution logs**: The execution log entry records the final step outcome with retry count in the reason field (e.g., "detected_after_2_retries").

## Cancellation

If a sequence is stopped during a retry wait, the retry loop immediately aborts and the step is recorded as "cancelled" (not "failed").

## No Impact On

- Primitive tap steps without detection targets (unchanged behaviour)
- Non-primitive-tap command steps (unchanged)
- The background screenshot capture service (unchanged interval and behaviour)
- Existing API endpoints (no new endpoints; config visible via existing `GET /api/config`)
