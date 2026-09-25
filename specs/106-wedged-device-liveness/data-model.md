# Data Model: Make a wedged emulator visible to the API (B-019)

**Feature**: `106-wedged-device-liveness` | **Date**: 2026-09-25

All data in this feature is in memory. The feature adds no file and no persisted field. The execution-log entries use the current execution-log store.

## 1. `DeviceLivenessOptions` (new, `GameBot.Domain/Sessions`)

Configuration section: `Service:DeviceLiveness` (constant `SectionName`). All values are milliseconds.

| Property | Type | Default | Minimum | Description |
|---|---|---|---|---|
| `StaleLimitMs` | `int` | 300000 | 1000 | The frame is stale when it did not change for longer than this value. |
| `CaptureStallLimitMs` | `int` | 60000 | 1000 | The capture is stalled when no capture completed for longer than this value. |
| `InputTimeoutMs` | `int` | 10000 | 100 | Time limit of one input action. |
| `CaptureTimeoutMs` | `int` | 10000 | 100 | Time limit of one direct capture and of one capture of the capture loop. |
| `TransportCheckTimeoutMs` | `int` | 5000 | 100 | Time limit of `adb get-state` in the health call. |
| `QueueGracePeriodMs` | `int` | 120000 | 0 | A queue records the fault episode when the device stays not live for longer than this value. |
| `QueueCheckIntervalMs` | `int` | 30000 | 1000 | Interval of the queue liveness check. Also the wait of the queue after a held firing. |

Method `Normalized()`: returns a copy with each value set to at least its minimum. It never throws.

## 2. `DeviceLivenessRecord` (new, internal to `DeviceLivenessTracker`)

One record for each session ID. A lock in the tracker protects it.

| Field | Type | Description |
|---|---|---|
| `CaptureLoopRunning` | `bool` | A capture loop runs for the session. |
| `LoopStartedAt` | `DateTimeOffset?` | Start of the current capture loop. The stall rule uses it before the first capture completes. |
| `LastCaptureAt` | `DateTimeOffset?` | Time of the last completed capture. |
| `LastChangeAt` | `DateTimeOffset?` | Time of the last capture whose PNG bytes were different from the capture before it. |
| `FirstInputAfterChangeAt` | `DateTimeOffset?` | Start time of the first input after the last frame change. Null when no input came after the last frame change. Rule 5 uses it. |
| `LastInputAt` | `DateTimeOffset?` | Start time of the last input command sent through the service. |
| `LastInputOutcome` | `InputOutcome?` | `Pending`, `Completed`, `TimedOut`, `Failed` or `Cancelled`. |

State transitions:

- `LoopStarted(sessionId)`: creates the record when it does not exist. Sets `CaptureLoopRunning = true` and `LoopStartedAt = now`. Clears `LastCaptureAt`, `LastChangeAt`, `FirstInputAfterChangeAt`, `LastInputAt` and `LastInputOutcome` (edge case "Capture loop restarted").
- `LoopStopped(sessionId)`: sets `CaptureLoopRunning = false`. Keeps the other data.
- `RecordCapture(sessionId, changed)`: does nothing when `CaptureLoopRunning` is `false`. Otherwise, sets `LastCaptureAt = now`. When `changed` is `true`, it also sets `LastChangeAt = now` and clears `FirstInputAfterChangeAt`.
- `RecordInputStarted(sessionId)`: creates the record when it does not exist. Sets `LastInputAt = now` and `LastInputOutcome = Pending`. When `FirstInputAfterChangeAt` is null, it sets `FirstInputAfterChangeAt = now`.
- `RecordInputCompleted(sessionId, outcome)`: sets `LastInputOutcome = outcome`. Does not change `LastInputAt` or `FirstInputAfterChangeAt`.
- `Remove(sessionId)`: deletes the record. `SessionManager.StopSession` and the idle eviction call it.

Only `LoopStarted` and `RecordInputStarted` create a record. `LoopStopped`, `RecordCapture` and `RecordInputCompleted` do nothing for an unknown session ID. Thus a hung capture loop or a hung input that ends after `Remove` cannot make the record again. `SessionManager` calls `RecordInputStarted` only for a session that it knows.

Outcome of an input that the caller cancels: the caller token stops the input before it completes. When the input ran for longer than `InputTimeoutMs`, `SessionManager` records `TimedOut`. Otherwise it records `Cancelled`. Rule 3 ignores `Cancelled` and `Failed`. Thus a short cancel never gives `input_timeout`, but a sequence watchdog that stops a hung input still does.

## 3. `DeviceLivenessSample` (new record, `GameBot.Domain/Sessions`)

A copy of one record, taken under the lock, plus two values from the caller.

| Field | Type | Description |
|---|---|---|
| `HasDevice` | `bool` | The session has a device serial. `false` in stub mode. |
| `TransportReady` | `bool?` | Result of the transport check. `null` when the caller did not check (data-only evaluation). |
| `CaptureLoopRunning` | `bool` | From the record. |
| `LoopStartedAt` | `DateTimeOffset?` | From the record. |
| `LastCaptureAt` | `DateTimeOffset?` | From the record. |
| `LastChangeAt` | `DateTimeOffset?` | From the record. |
| `FirstInputAfterChangeAt` | `DateTimeOffset?` | From the record. |
| `LastInputAt` | `DateTimeOffset?` | From the record. |
| `LastInputOutcome` | `InputOutcome?` | From the record. |

## 4. `DeviceLivenessReport` (new record, `GameBot.Domain/Sessions`)

Output of `DeviceLivenessEvaluator.Evaluate(sample, options, now)`.

| Field | Type | Description |
|---|---|---|
| `State` | `string` | `live`, `not_live` or `unknown` (constants in `DeviceLivenessStates`). |
| `Reason` | `string?` | `capture_stalled`, `input_timeout`, `no_change_after_input` or `transport_not_ready` (constants in `DeviceLivenessReasons`). Null when `State` is not `not_live`. The set `DeviceLivenessReasons.Hard` has `capture_stalled`, `input_timeout` and `transport_not_ready`. Only these reasons hold queue firings (section 8). |
| `FrameAgeMs` | `long?` | `now - LastCaptureAt`. Null when no capture completed. |
| `UnchangedMs` | `long?` | `now - LastChangeAt`. Null when no capture completed. |
| `Stale` | `bool` | `CaptureLoopRunning`, and (`UnchangedMs > StaleLimitMs` or `FrameAgeMs > CaptureStallLimitMs`). `false` when no capture loop runs. |
| `LastInputAt` | `DateTimeOffset?` | From the sample. |
| `LastInputOutcome` | `string?` | `pending`, `completed`, `timed_out`, `failed` or `cancelled`. Null when no input was sent. |
| `NeedsProbe` | `bool` | `true` when rule 7 gives the state: the session has a device, but no capture loop runs or the loop has no completed capture. The health call then does one direct capture. Not in the API. |

Evaluation rules (first match gives the state and the reason; research R-002):

| # | Condition | State | Reason |
|---|---|---|---|
| 1 | `HasDevice` is `false` | `unknown` | null |
| 2 | `TransportReady` is `false` | `not_live` | `transport_not_ready` |
| 3 | (`LastInputOutcome == TimedOut`, or `LastInputOutcome == Pending` and `now - LastInputAt > InputTimeoutMs`), and not (`LastChangeAt > LastInputAt`) | `not_live` | `input_timeout` |
| 4 | `CaptureLoopRunning`, and `now - (LastCaptureAt ?? LoopStartedAt) > CaptureStallLimitMs` | `not_live` | `capture_stalled` |
| 5 | `CaptureLoopRunning`, `UnchangedMs > StaleLimitMs`, `FirstInputAfterChangeAt` has a value, and `now - FirstInputAfterChangeAt > StaleLimitMs` | `not_live` | `no_change_after_input` |
| 6 | `CaptureLoopRunning`, and `LastCaptureAt` has a value | `live` | null |
| 7 | all other cases | `unknown`, `NeedsProbe = true` | null |

Rule 5 counts from the first input after the last frame change, not from the frame change. Thus the input always has the full stale limit to change the screen. Example: the screen is static for 10 minutes, then one tap arrives. The state stays `live` for the next `StaleLimitMs`. It becomes `not_live` only when no frame change comes in that period.

Rules 4, 5 and the `Stale` flag apply only while a capture loop runs. After `LoopStopped`, old frame data is not a fault signal. A new input then gives `unknown` with `NeedsProbe = true` (rule 7), unless rule 3 applies.

Probe result (health call only, research R-007): a direct capture that succeeds changes rule 7 to `live` with `FrameAgeMs = 0` and `UnchangedMs = null`. A direct capture that fails or times out changes rule 7 to `not_live` with `capture_stalled`.

## 5. `IDeviceLivenessTracker` / `DeviceLivenessTracker` (new, `GameBot.Domain/Sessions`)

Singleton. Constructor: `DeviceLivenessTracker(TimeProvider? timeProvider = null)`.

| Member | Called by |
|---|---|
| `LoopStarted(string sessionId)` | `BackgroundScreenCaptureService.StartCapture` |
| `LoopStopped(string sessionId)` | `BackgroundScreenCaptureService.StopCapture`, `StopAll` |
| `RecordCapture(string sessionId, bool changed)` | `SessionCaptureLoop.RunLoopAsync` after a completed capture. Does nothing for an unknown session ID or a stopped loop. |
| `RecordInputStarted(string sessionId)` | `SessionManager` before each ADB input command |
| `RecordInputCompleted(string sessionId, InputOutcome outcome)` | `SessionManager` after each ADB input command |
| `Remove(string sessionId)` | `SessionManager.StopSession`, `SessionManager.CleanupIdleSessions` |
| `Sample(string sessionId, bool hasDevice, bool? transportReady = null)` | `SessionLivenessService` |
| `HasCaptureData(string sessionId)` | `GetSnapshot` in `SessionsEndpoints` (snapshot headers, FR-010). Returns `true` when a record exists for the session and `LoopStartedAt` has a value: a capture loop runs, or ran, for the session. Returns `false` for an unknown session ID. |
| `Now` | `SessionLivenessService` (the evaluation time) |

## 6. `InputActionResult` (changed, `GameBot.Emulator/Session/ISessionManager.cs`)

`public sealed record InputActionResult(int Index, bool Dispatched, string? FailureReason, bool TimedOut = false);`

`TimedOut` is `true` only when the input time limit stopped the action. The current three-argument calls still compile.

## 7. `ISessionLivenessService` / `SessionLivenessService` (new, `GameBot.Service/Services/Liveness`)

| Member | Result |
|---|---|
| `DeviceLivenessOptions Options` | The normalized options. |
| `DeviceLivenessReport Evaluate(EmulatorSession session)` | Data-only report. No I/O. |
| `Task<SessionLivenessProbeResult> ProbeAsync(EmulatorSession session, CancellationToken ct)` | Transport check (bounded), then the report, then one bounded direct capture when `NeedsProbe`. |

`SessionLivenessProbeResult`: `Adb` (the current `adb` block values: `Ok`, `Stdout`, `Stderr`, `Error`) and `Liveness` (`DeviceLivenessReport`).

`ISessionTransportCheck` / `AdbSessionTransportCheck` (new): `Task<SessionTransportCheckResult> CheckAsync(string deviceSerial, CancellationToken ct)`. The result has `Ok`, `Stdout`, `Stderr` and `Error`. A time-out gives `Ok = false` and `Error = "adb get-state did not answer in <N> ms"`.

`ISessionDirectCapture` / `AdbSessionDirectCapture` (new): `Task<bool> TryCaptureAsync(string deviceSerial, CancellationToken ct)`. Returns `true` when the device returned a PNG with at least one byte. An exception or an empty PNG gives `false`. The caller applies the time limit. It does not use `SessionManager.GetSnapshotAsync`, because that method returns a stub PNG on failure (research R-007).

## 8. `QueueLivenessEpisode` (new, `GameBot.Service/Services/QueueExecution`)

One instance on each `QueueRunHandle` (property `Liveness`). A lock protects it.

| Field | Type | Description |
|---|---|---|
| `State` | `string` | The last observed state. |
| `Reason` | `string?` | The last observed reason. |
| `NotLiveSince` | `DateTimeOffset?` | Local-clock time at which the queue first observed the device as not live in the current fault episode. Null when the state is not `not_live`. |
| `GatedFirings` | `int` | The number of held firings in the current episode. `0` when no episode is open. |
| `GateLoggedSequences` | `HashSet<string>` | The sequence IDs that already have a gate log entry in the current episode ("gate log entries written"). Empty when no episode is open. |
| `FaultCycleRecorded` | `bool` | The watch recorded the fault cycle of the current episode ("fault cycle recorded"). |
| `LastReport` | `DeviceLivenessReport?` | The last observed report. |

The two records "gate log entries written" and "fault cycle recorded" are separate. The gate changes only `GatedFirings` and `GateLoggedSequences`. The watch changes only `FaultCycleRecorded`. Thus the watch records its fault cycle also when the gate wrote entries (research R-012).

| Method | Behavior |
|---|---|
| `Observe(DeviceLivenessReport report, DateTimeOffset now)` | `not_live` (each reason) with no open episode: opens one (`NotLiveSince = now`). `live` or `unknown`: closes the episode. It sets `NotLiveSince = null` and `GatedFirings = 0`, clears `GateLoggedSequences`, and sets `FaultCycleRecorded = false`. Always stores the report. |
| `bool RecordGatedFiring(string sequenceId)` | Adds one to `GatedFirings`. Returns `true` when `GateLoggedSequences` does not have the sequence, and then adds it. Returns `false` for a later held firing of the same sequence in the episode. |
| `bool TryClaimFaultCycle(DateTimeOffset now, TimeSpan grace)` | Returns `true` one time for each episode: an episode is open, `now - NotLiveSince > grace`, and `FaultCycleRecorded` is `false`. It sets `FaultCycleRecorded = true` in the same lock. It does not look at `GateLoggedSequences`. |
| `Snapshot()` | A copy for the queue health projection, with `GatedFirings`. |

State diagram of one episode (the gate records and the fault cycle record are independent):

```text
live/unknown --(not_live)--> open --(watch: grace elapsed)--> open, fault cycle recorded
     ^                        |  \                                   |
     |                        |   +--(gate: first held firing of a sequence)--> entry for that sequence
     +------(live/unknown)----+-------------------(live/unknown)-----+
```

## 9. `QueueCycleLedger.RecordFaultCycle` (changed)

`public void RecordFaultCycle(DateTimeOffset startedAt, DateTimeOffset now)` seals one completed cycle with no entries and `Succeeded = false`. It increments `ConsecutiveFailedCycles`. It does not touch the open cycle.

## 10. `QueueDeviceLivenessResponse` (new, `GameBot.Service/Contracts/Queues`)

New property `DeviceLiveness` on `QueueHealthResponse`. Present when the health block is present (the queue runs). Null before the run binds a session.

| Property | JSON | Type | Description |
|---|---|---|---|
| `State` | `state` | `string` | `live`, `not_live` or `unknown`. |
| `Reason` | `reason` | `string?` | The not-live reason. Null when not `not_live`. |
| `NotLiveSince` | `notLiveSince` | `DateTimeOffset?` | Service-local time at which the queue first observed the device as not live. Null when not `not_live`. |
| `Stale` | `stale` | `bool` | The stale flag. |
| `FrameAgeMs` | `frameAgeMs` | `long?` | Frame age. |
| `UnchangedMs` | `unchangedMs` | `long?` | Time for which the frame did not change. |
| `GatedFirings` | `gatedFirings` | `int` | The number of held firings in the current fault episode. `0` when no episode is open. |

## 11. Session health `liveness` block (changed response, `GET /api/sessions/{id}/health`)

See [contracts/session-health.md](./contracts/session-health.md). Swagger schema class `SessionLivenessSchema` in `SwaggerConfig.cs`.

## 12. Execution-log entries (new kinds of entry, current store)

| Kind | `executionType` | `objectRef` | Parent | `finalStatus` | `summary` |
|---|---|---|---|---|---|
| Held firing (FR-016) | `sequence` | the due sequence | queue run root, depth 1 | `failure` | `device_not_live: <reason>` |
| Fault episode (FR-017) | `queue` | the queue | queue run root, depth 1 | `failure` | `device_not_live: <reason>` |

Only the first held firing of each sequence in a fault episode writes the held-firing entry. That firing also adds one `failure` run record to the sequence statistics (feature 105). Its start time and end time are the gate time. Later held firings of the same sequence in the episode write no entry and no statistics record. They only add one to `GatedFirings`.

The gate acts one time for each firing group (research R-011). The EveryStep and BeforeEachRun guard sequences of a held group do not run and give no entry.

Both kinds of entry use the root `handle.RootExecutionId ?? rootId`, so that they go under the current root segment after a rotation. The held-firing entry uses the sequence index `++index`.

The watch writes the fault-episode entry one time for each episode, after the grace period. It writes it also when held firings wrote entries in the episode.

Limit for one episode: at most one held-firing entry for each sequence, plus one fault-episode entry. This limit does not depend on the length of the episode (spec SC-008).
