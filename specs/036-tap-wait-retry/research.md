# Research: 036 Tap Wait-and-Retry

## R-001: Retry Loop Implementation Pattern

**Decision**: Use `Task.Delay(waitMs, cancellationToken)` in a `for` loop wrapping the existing screenshot-fetch → template-match → coordinate-resolve flow. On `OperationCanceledException`, catch and report "cancelled" outcome.

**Rationale**: `Task.Delay` with `CancellationToken` is the idiomatic .NET async cancellable wait. It provides immediate abort on cancellation (< 1ms response). The existing `CancellationToken ct` parameter is already threaded through `ForceExecuteDetailedAsync` → `ExecuteCommandRecursiveAsync` → the PrimitiveTap block, so no plumbing changes are needed.

**Alternatives considered**:
- `Thread.Sleep` — blocks the thread and is not cancellable; rejected.
- `ManualResetEventSlim.Wait(ms, ct)` — synchronous, would need wrapping; more complex, no benefit over `Task.Delay`.
- Polly retry library — adds external dependency for a simple loop; over-engineered.

## R-002: AppConfig Extension Pattern

**Decision**: Add three new `init` properties to `AppConfig`: `CaptureIntervalMs` (int, default 500), `TapRetryCount` (int, default 3), `TapRetryProgression` (double, default 1.0). Wire them in `Program.cs` alongside the existing `LoopMaxIterations` registration, following the same `int.TryParse(env var) ?? default` pattern.

**Rationale**: The existing `AppConfig` is a simple POCO registered as a singleton. The `LoopMaxIterations` property follows an env-var-with-default pattern. The three new properties follow the same convention with environment variables:
- `GAMEBOT_CAPTURE_INTERVAL_MS` → `CaptureIntervalMs` (also used by `BackgroundScreenCaptureService`)
- `GAMEBOT_TAP_RETRY_COUNT` → `TapRetryCount`
- `GAMEBOT_TAP_RETRY_PROGRESSION` → `TapRetryProgression`

The `captureIntervalMs` local variable already exists in `Program.cs` (line 187) for the BackgroundScreenCaptureService. It will be reused when constructing `AppConfig`, ensuring the same value flows to both consumers.

**Alternatives considered**:
- Separate `TapRetryConfig` class — adds another DI registration; the existing `AppConfig` is designed for this purpose.
- Options pattern (`IOptions<T>`) — would require restructuring; the project uses direct singleton registration and env vars; consistency wins.

## R-003: Configuration Validation

**Decision**: Validate in `Program.cs` at registration time, falling back to defaults for invalid values. Specifically:
- `CaptureIntervalMs`: clamped to ≥50 (same as `BackgroundScreenCaptureService`)
- `TapRetryCount`: non-negative integer; negative falls back to default 3
- `TapRetryProgression`: must be > 0; values ≤ 0 fall back to default 1.0

**Rationale**: Fail-safe at startup. Bad config should not crash the service; it should log a warning and use defaults. This matches the existing pattern where `BackgroundScreenCaptureService` clamps `captureIntervalMs` to min 50.

**Alternatives considered**:
- Throw on invalid config — breaks service startup for a non-critical setting; rejected.
- Validate at each retry call site — adds runtime overhead and duplicates validation; prefer one-time at startup.

## R-004: PrimitiveTapStepOutcome Extension for Retry Count

**Decision**: Add an optional `RetryCount` property to `PrimitiveTapStepOutcome` by extending the sealed record with a new parameter (default null for backward compatibility). Alternatively, encode retry count in the existing `Reason` string field (e.g., "detected_after_2_retries").

After analysis: Use the `Reason` field. The current record definition is `(StepOrder, Status, Reason, ResolvedPoint, DetectionConfidence)`. Adding a parameter changes the positional constructor signature, affecting all call sites (both production and tests). Encoding in `Reason` avoids schema changes while still providing the information in execution logs.

Status values:
- `"executed"` — tap succeeded (Reason: `null` if found on 1st attempt, `"detected_after_N_retries"` if found after retries)
- `"skipped_detection_failed"` — all retries exhausted (Reason: `"detection_failed_after_N_retries"`)
- `"cancelled"` — cancelled during retry (Reason: `"cancelled_during_retry_N"`)

**Rationale**: Minimally invasive. The `Reason` field is already nullable and consumed as a string in both runtime logging and execution log persistence. No changes to `ExecutionLogService` mapping logic — it already passes `Reason` through to execution log entries.

**Alternatives considered**:
- New record field `int? RetryCount` — requires updating all 10+ call sites constructing `PrimitiveTapStepOutcome`, all test assertions, and the `ExecutionLogService` mapping; high churn for low value.
- Separate retry event type — over-designed for additional metadata that fits naturally in `Reason`.

## R-005: Retry Loop Insertion Point

**Decision**: The retry loop wraps lines 223–277 of `CommandExecutor.cs` (from screenshot fetch through detection result). The initial wait and detection check are separated from the retry loop so that PROGRESSION is applied only between retries (not after the initial check). The loop structure:

```
// Before loop: validate services (existing L215-L221)
// Validate template exists (existing L230-L233 — move before loop since template doesn't change)

// Initial wait + detection check (FR-001)
await Task.Delay(baseWaitMs, ct);
// Fetch latest screenshot (L223)
// Convert to Mat and run detection (L235-L257)
// If detected → extract coordinates, validate, execute tap, return outcome

// Retry loop (FR-002, FR-003)
int currentWaitMs = baseWaitMs;
for (int retry = 0; retry < retryCount; retry++) {
    await Task.Delay(currentWaitMs, ct);
    currentWaitMs = (int)(currentWaitMs * progression);  // progression applied after wait
    
    // Fetch latest screenshot
    // Convert to Mat and run detection
    // If detected → extract coordinates, validate, execute tap, break
    // If not detected → log retry cycle, continue loop
}
// If loop exhausted → add failure outcome
```

This produces wait times matching the spec acceptance scenarios: for PROGRESSION=2, COUNT=3, base=500ms → initial wait 500ms, retry waits 500ms, 1000ms, 2000ms.

The key insight: template lookup (`images.TryGet`) can be moved before the loop since the template image doesn't change between retries. Only the screenshot needs refreshing.

**Rationale**: Minimal refactoring of existing code. The existing straight-line flow becomes the loop body, with the screenshot fetch moved inside the loop and template lookup moved outside.

**Alternatives considered**:
- Extract retry logic to a separate helper class — adds indirection for a single call site; can be refactored later if reuse emerges.
- Extract the entire PrimitiveTap block to a method — good idea but scope creep; keep changes focused on the retry loop.

## R-006: Logging Pattern

**Decision**: Use the existing `Log` static class (high-performance source-generated logging) for retry cycle logging. Add new log methods:
- `TapRetryWaiting(logger, stepOrder, cycle, waitMs)` at Debug level
- `TapRetryDetected(logger, stepOrder, cycle)` at Information level
- `TapRetryNotDetected(logger, stepOrder, cycle)` at Debug level
- `TapRetryExhausted(logger, stepOrder, totalCycles)` at Warning level
- `TapRetryCancelled(logger, stepOrder, cycle)` at Information level

**Rationale**: Follows the existing `[LoggerMessage]` attribute pattern in `CommandExecutor.cs`. Individual retry cycles log at Debug (verbose, for troubleshooting). Final outcome events (detected, exhausted, cancelled) log at Info/Warning for operational visibility.

**Alternatives considered**:
- Structured logging with Serilog enrichers — the project uses `Microsoft.Extensions.Logging` with source generators; stay consistent.
- Info-level for every cycle — too noisy for production with default config (3 cycles × N steps per execution).
