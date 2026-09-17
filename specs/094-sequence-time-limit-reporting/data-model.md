# Data Model: Distinct reporting of sequence time-limit cancellation

## ExecutionLogEntry (persisted, `GameBot.Domain.Logging`)

| Field | Type | New | Rules |
|-------|------|-----|-------|
| FinalStatus | string | no | Unchanged vocabulary: `running` / `success` / `failure` |
| CancellationReason | string? | yes | `sequence_time_limit` when the platform time bound ended a queue firing's sequence; null otherwise (including user stops, successes, ad-hoc runs, and every pre-existing entry) |
| TimeLimitMs | int? | yes | The effective bound (ms) applied to the firing; set if and only if `CancellationReason` is set |

Constant holder: `ExecutionCancellationReasons.SequenceTimeLimit = "sequence_time_limit"`.

## ExecutionLogContext (service, transient)

Gains `CancellationReason` (string?) and `TimeLimitMs` (int?), copied verbatim onto the entry by `LogSequenceFinalizeAsync`.

## SequenceTimeLimitScope (service, ambient, transient)

| Member | Meaning |
|--------|---------|
| `TimeLimitMs` (int) | Bound applied to the current firing |
| `HasElapsed` (bool) | Timer token cancelled AND stop token not cancelled |
| `static Current` | Innermost scope on the current async flow, or null |
| `static Push(int timeLimitMs, CancellationToken timerToken, CancellationToken stopToken)` → `IDisposable` | Restores the previous value on dispose |

Lifecycle: pushed at the start of `RunOneSequenceAsync`'s sequence execution, disposed when the firing ends.

## SequenceTimeLimits (domain constants)

| Member | Value |
|--------|-------|
| `DefaultWatchdogTimeoutMs` | 240000 |
| `MaxWatchdogTimeoutMs` | 1800000 |
| `Resolve(int? overrideMs)` | `overrideMs > 0 ? overrideMs : DefaultWatchdogTimeoutMs` |

## CommandSequence (unchanged storage)

`WatchdogTimeoutMs` (int?) stays the only persisted field. The read-only `effectiveWatchdogTimeoutMs` exists only in
API responses.

## Stamping rule (state decision at finalize)

```
stamp = scope != null && scope.HasElapsed && normalizedStatus != "success"
```
