# Data Model: Trace Noise Cleanup

This feature adds no stored data and changes no stored data. The only "entities" are the trace message definitions that change.

## Trace message definitions that change

| Source | Event id | Message text | Level before | Level after |
|--------|----------|--------------|--------------|-------------|
| `ImageMatchLog.SlowMatch` (`ImageMatchEvaluator.cs`) | none | "Slow image match for '{ReferenceImageId}': {ElapsedMs}ms" | Warning | Removed |
| `Log.TapRetryExhausted` (`CommandExecutor.cs`) | 6012 | "PrimitiveTap step {StepOrder}: target not detected after {TotalCycles} retry cycles." | Warning | Debug |
| `Log.TriggerBypassed` (`CommandExecutor.cs`) | 6002 | "EvaluateAndExecute bypassed trigger for command {CommandId}; executed via ForceExecute with {Accepted} accepted inputs." | Information (no caller) | Removed |
| `Log.SessionCacheHit` (`CommandExecutor.cs`) | 6006 | "Using cached session for command {CommandId}: game {GameId}, device {AdbSerial}, session {SessionId}." | Information (no caller) | Removed |
| `Log.SessionCacheMiss` (`CommandExecutor.cs`) | 6007 | "No cached session for command {CommandId}: game {GameId}, device {AdbSerial}." | Warning (no caller) | Removed |

## Data that does not change

- `TriggerEvaluationResult` from `ImageMatchEvaluator.Evaluate`: `Status`, `Similarity`, `EvaluatedAt`, and `Reason`.
- `PrimitiveTapStepOutcome` for a tap step that does not find its target: status `skipped_detection_failed`, reason `detection_failed_after_N_retries`.
- The execution log entries, the logging policy file, and the default trace level.
