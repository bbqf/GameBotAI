# Data Model: Report Idle Pause in queue health

All state is in-memory, run-scoped, and never persisted.

## QueueRunHandle: idle-pause register (feature 073, extended)

| Member | Type | Change | Rule |
|--------|------|--------|------|
| `IdlePausedUntil` | `DateTimeOffset?` | unchanged | Current resume instant; null when not idle-paused |
| `IdlePausedAt` | `DateTimeOffset?` | **new** | Instant the continuous idle pause began; null when not idle-paused |
| `EnterIdlePause(resumeAt, at)` | method | **signature change** (+`at`) | Always sets `IdlePausedUntil = resumeAt`; sets `IdlePausedAt = at` only if it was null |
| `ClearIdlePause()` | method | extended | Nulls both `IdlePausedUntil` and `IdlePausedAt` |

## QueuePauseSnapshot (new, `readonly record struct`)

| Field | Type | Not paused | Failure-policy pause | Idle pause |
|-------|------|-----------|----------------------|-----------|
| `Paused` | `bool` | `false` | `true` | `true` |
| `PausedAt` | `DateTimeOffset?` | `null` | `PolicyPausedAt` | `IdlePausedAt` |
| `Reason` | `string?` | `null` | `PauseReason` (unchanged text) | `idle pause: resumes at HH:mm` |
| `Kind` | `string?` | `null` | `"failurePolicy"` | `"idle"` |

**Precedence**: a failure-policy pause wins when both are in force.

Produced by `QueueRunHandle.SnapshotPause()`. Kind values are constants on the static class `QueuePauseKinds`.

## QueueHealthResponse (wire, `health` block)

| Field | Change |
|-------|--------|
| `paused` | Meaning widened: any pause in force (idle or failure policy) |
| `pausedAt` | Start of the reported pause |
| `pauseReason` | Reason for the reported pause |
| `pauseKind` | **new**: `"idle"` \| `"failurePolicy"` \| `null` |
| `failurePolicyTripped` | unchanged |
