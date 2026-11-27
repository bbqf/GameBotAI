# Data Model: Keep the queue Running and bind a new session after a recovery reboot (B-047)

No stored data changes. The changes are in the in-memory run handle and in the fault episode values.

## QueueRunHandle (in memory, one for each queue run)

| Field | Type | New | Meaning |
|-------|------|-----|---------|
| `SessionId` | `string?` | no | The session of the run. After this feature, null means "no bound session". A failed rebind sets it to null (it does not keep a removed ID). |
| `SessionBindLock` | `object` | yes | The lock of each bind of a lost session (`TryBindLostSession`, `RebindSessionAsync`). One session for each queue at a time (FR-011). |
| `DeviceRecoveryReboots` | `bool` (volatile) | yes | True when the stored `deviceRecovery.action` is `reboot-instance`. The run sets it at the start. The watch sets it from the stored queue at each check (feature 199). |
| `LostSessionWarned` | `bool` | yes | True after the warning for the first failed bind of the current "no session" period. A bind that succeeds clears it (FR-010a). |
| `Liveness` | `QueueLivenessEpisode` | no | The fault episode. While no session is bound, it gets `NoSessionReport`. |

## NoSessionReport (static value)

`DeviceLivenessReport(State: "not_live", Reason: "transport_not_ready", FrameAgeMs: null, UnchangedMs: null, Stale: true, LastInputAt: null, LastInputOutcome: null, NeedsProbe: false)`.

`IsHardNotLive` is true, so the feature 106 gate holds the firings.

## State of a queue run with recovery on

```text
Bound ──(session removed, bind fails)──> NoSession
NoSession ──(bind succeeds at a check or before a firing)──> Bound
NoSession ──(recovery attempt binds and the device is live)──> Bound
NoSession ──(stop, delete, shutdown)──> run ends (Stopped, as today)
Bound/NoSession: queue status stays Running
```

With recovery off: `Bound ──(session removed, bind fails)──> run ends with "emulator connection lost mid-run"` (no change).

## Rules

- While `NoSession`: each firing that the loop wants to start is held. The first held firing of each sequence in the episode writes one failed sequence entry `device_not_live: transport_not_ready` and one statistics failure (FR-002a).
- While `NoSession`: the watch claims the alert, the recovery attempts (`afterMs`, `cooldownMs`, `maxAttempts`), the "recovery failed" alert, and the fault cycle with the same rules as with a session (FR-004, FR-005).
- While a recovery attempt runs (`RecoveryRunning`), `TryBindLostSession` does not bind. The runner binds.
- `health.deviceLiveness.recoveryState` is calculated as today (`running`, `exhausted`, `idle`).
