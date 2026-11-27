# Research: Keep the queue Running and bind a new session after a recovery reboot (B-047)

## R-001: Cause of the stop

- **Decision**: The cause is the order in `QueueExecutionService.RebindSessionAsync` plus the one bind try of the run loop. The rebind stops the old session, then the bind throws because ADB does not list the device during the reboot. `handle.SessionId` keeps the ID of the removed session. A step that runs at that time fails with "Session not found" (`CommandExecutor`). Before the next firing, `EnsureSessionBound` tries one bind, the bind throws `KeyNotFoundException` ("ADB device '<serial>' not found") or `InvalidOperationException` (`no_adb_devices`), and the loop throws `QueueConnectionLostException`. The run ends with `QueueStopReason.Failure`, and the `finally` block sets `Stopped`.
- **Rationale**: The session manager removes a session only through `StopSession` or the idle sweep, and the idle sweep skips queue-owned sessions (#217). Thus the rebind of the recovery is the only path that removed the session in the incident. The times in the issue agree: the step failed at 02:11:47 UTC, and the new LDPlayer process started at 02:13:39 UTC.
- **Alternatives considered**: A device-disconnect event in the session manager. Rejected: the code has no such event.

## R-002: Which queues change

- **Decision**: Only a queue whose stored `deviceRecovery` has `Reboots == true`. The run handle keeps the current value in `DeviceRecoveryReboots`. The run sets it from the queue at the run start, and the watch sets it from the stored queue at each check (feature 199 rule). With recovery off, `TryEnsureSessionBound` throws `QueueConnectionLostException` as today.
- **Rationale**: Spec clarification 1. The promise "the queue stays Running" is part of the `deviceRecovery` description only.
- **Alternatives considered**: Wait for the device for all queues. Rejected: scope that the issue does not ask for, and it changes the end of the run for queues that the operator wants to stop.

## R-003: One shared bind method

- **Decision**: `internal string? TryBindLostSession(ExecutionQueue queue, QueueRunHandle handle, out Exception? error)` in `QueueExecutionService`. `error` is the exception of a failed bind, else null. The run loop uses it for the text of `QueueConnectionLostException`. Steps, all in `lock (handle.SessionBindLock)`:
  1. When `handle.SessionId` is set and `_sessions.GetSession(id)` is not null, return that ID.
  2. When `handle.Liveness.Snapshot().RecoveryRunning` is true, return null. The runner owns the rebind while an attempt runs.
  3. Stop the capture of the lost ID (when one exists), and call `BindQueueSession(queue)`.
  4. On success: set `handle.SessionId`, clear `handle.LostSessionWarned`, log `SessionRebound` (information), and return the ID.
  5. On `InvalidOperationException` or `KeyNotFoundException`: set `handle.SessionId` to null, log a warning when `LostSessionWarned` is false (then set it), else log debug, and return null.
- `RebindSessionAsync` (the runner callback) uses the same lock. It sets `handle.SessionId` to null after it stops the old session and before it binds. So a failed bind leaves no dead ID.
- **Rationale**: The run loop and the watch run on different threads. One lock and one "current session" check prevent two sessions for one queue (FR-011). Step 2 prevents a bind on the device of the reboot before the runner waits for it.
- **Alternatives considered**: Bind the new session before the old one stops. Rejected: the new session counts against `MaxConcurrentSessions` (default 8; a setup can use a smaller value), so a full service fails the bind.

## R-004: The not-live reason with no session

- **Decision**: A static report `NoSessionReport = new DeviceLivenessReport(DeviceLivenessStates.NotLive, DeviceLivenessReasons.TransportNotReady, null, null, Stale: true, null, null, NeedsProbe: false)`. The gate and the watch give it to `handle.Liveness.Observe`.
- **Rationale**: Spec clarification 3. `transport_not_ready` is a hard reason, so the feature 106 gate holds the firings and writes one failed entry for each sequence (`device_not_live: transport_not_ready`). No new API value.
- **Alternatives considered**: A new reason `session_lost`. Rejected: it changes the API value set (FR-009).

## R-005: The run loop with no session

- **Decision**:
  - `HoldIfNotLiveAsync(sequenceId)`: when `_liveness` is not null and `TryEnsureSessionBound()` is false, observe `NoSessionReport` and call `TryGateOnLivenessAsync` with `holdOnlyReport: NoSessionReport`. The gate then records the held firing and returns the report. The loop sets `held = true`, so the iteration ends with the `HeldIterationWait()` wait (`QueueCheckIntervalMs`).
  - `FireGroupAsync`: the call after the gate becomes `if (!TryEnsureSessionBound()) { held = true; return null; }`.
  - `RunEveryStepPassAsync` and `RunBeforeEachRunPassAsync`: `if (!TryEnsureSessionBound()) return;` before each entry.
  - The daily retry site: `if (!TryEnsureSessionBound()) continue;` (the retry stays armed).
  - The at-start pass and the once-per-run pass: remove the extra `EnsureSessionBound()` call before `FireGroupAsync`, because `FireGroupAsync` does the check.
  - The idle pause: when `TryEnsureSessionBound()` is false before the idle hold, wait `HeldIterationWait()` and continue. The idle hold uses `handle.SessionId ?? sessionId`.
- **Rationale**: Each site that starts a sequence checks the session first, and no site throws for a queue with recovery on. The held firings stay due with the feature 106 rules.
- **Alternatives considered**: A separate "wait for device" loop before the iteration. Rejected: it duplicates the hold logic and does not write the held entries.

## R-006: The watch and the health read with no session

- **Decision**: `QueueLivenessWatch` gets a new optional callback `Func<ExecutionQueue, QueueRunHandle, string?>? tryBind`. In `CheckOnceAsync`, when the session ID is null or the session is gone: when `tryBind` gives an ID, continue with that session. When it gives null and `handle.DeviceRecoveryReboots` is true, use `NoSessionReport` for `Observe`, `SendAlerts`, `StartRecoveryIfDue` and the fault cycle claim. When it gives null and recovery is off, return false (the run loop ends the run as today). `DeviceLivenessReader.Project` evaluates the session when it exists, and else returns the snapshot of the episode.
- **Rationale**: The second recovery attempt and the "recovery failed" alert need the watch to run with no session (FR-004, FR-005). The health read needs the snapshot (FR-006).
- **Alternatives considered**: Let the runner retry the bind in its own loop. Rejected: the runner has one attempt for each call; the watch owns the attempt count and the cooldown.

## R-007: Log messages

- **Decision**: New `LoggerMessage` entries in `QueueExecutionLog`:
  - Warning: "Queue {QueueId}: the queue has no session, and device {Serial} could not be bound ({Error}). The queue holds its firings and tries again at each check."
  - Debug: "Queue {QueueId}: device {Serial} could not be bound again ({Error})."
  - The current `SessionRebound` (information) for the bind that succeeds.
- **Rationale**: Spec clarification 7 (FR-010a). The warning names the cause and the next action.
- **Alternatives considered**: One warning for each failed bind. Rejected: one warning each second during a reboot of several minutes.
