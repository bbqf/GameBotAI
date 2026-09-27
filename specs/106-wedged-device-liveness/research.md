# Research: Make a wedged emulator visible to the API (B-019)

**Feature**: `106-wedged-device-liveness` | **Date**: 2026-09-25 | **Spec**: [spec.md](./spec.md)

This file records the design decisions for the plan. Each section gives the decision, the rationale, and the alternatives that we examined. The findings come from the current code on branch `106-wedged-device-liveness`.

## Findings in the current code

| Area | File | Fact |
|---|---|---|
| ADB process call | `src/GameBot.Emulator/Adb/AdbClient.cs` `ExecAsync` | No time limit. On cancel, `WaitForExitAsync` throws, but the code does not kill the `adb` process. The `using` statement only disposes the `Process` object. Thus a hung `adb shell input tap` process stays alive. |
| ADB screenshot | `AdbClient.GetScreenshotPngAsync` | Same as above: no time limit, and no kill on cancel. Also, `CopyToAsync` reads a synchronous pipe (line 127). A cancel of the token does not stop this read. |
| Capture loop | `src/GameBot.Emulator/Session/BackgroundScreenCaptureService.cs` `SessionCaptureLoop.RunLoopAsync` | Calls the provider with the loop token only. One hung `screencap` stops the loop for all time. The loop keeps the last `CachedFrame` (PNG bytes and `Timestamp`). It does not compare frames. |
| Session inputs | `src/GameBot.Emulator/Session/SessionManager.cs` `SendInputsWithResultsAsync`, `DispatchOneInputAsync` | No time limit for each action. `dispatched: true` means exit code 0 from `adb`. |
| Sequence inputs | `SequenceExecutionService` lines 937 and 960, `CommandExecutor` lines 357, 369, 390, 686, `QueueExecutionService.IdlePauseHoldAsync` line 1034 | All sequence and command inputs go through `ISessionManager.SendInputsAsync`. Thus one change in `SessionManager` records the last-input data for all sequence input (FR-014). |
| Session health | `src/GameBot.Service/Endpoints/SessionsEndpoints.cs` `GetSessionHealth` | Runs `adb get-state` with the request token only. No time limit. Reports only the transport. |
| Session snapshot | `SessionsEndpoints.cs` `GetSnapshot` → `SessionManager.GetSnapshotAsync` | Always a direct capture. No time limit. An `InvalidOperationException` gives a 1x1 stub PNG. |
| Screenshot | `src/GameBot.Service/Endpoints/EmulatorImageEndpoints.cs` | Cached frame first, then `GetSnapshotAsync(session.Id)` with no token at all. Adds `X-Capture-Id`. |
| CORS | `src/GameBot.Service/GameBotServiceSetup.cs` line 121 and 123 | `WithExposedHeaders("X-Capture-Id")` only. |
| Step time limit | `src/GameBot.Service/Endpoints/StepsEndpoints.cs` line 29 | `new CancellationTokenSource(TimeSpan.FromSeconds(10))`. This is the source of the 10-second default for the input time limit. |
| Queue firing | `src/GameBot.Service/Services/QueueExecution/QueueExecutionService.cs` `RunOneSequenceAsync` | All firings (at-start, once-per-run, every-step, before-each-run, timer, daily retry, relative, live, self-reschedule) go through this one method. It returns `false` for a failed firing. The callers then count `failed`, call `handle.Cycles.RecordEntry` and arm the daily retry. The EveryStep pass (line 373) and the BeforeEachRun pass (line 403) also call it, once for each guard sequence. The self-reschedule drains are at lines 473, 563 and 612. |
| Queue health | `src/GameBot.Service/Endpoints/QueuesEndpoints.cs` `ProjectHealth`, `Contracts/Queues/QueueHealthResponse.cs` | Health is a projection of the run handle. It has no device data. |
| Transport watchdog | `src/GameBot.Service/Hosted/QueueDeviceWatchdogService.cs` | Restarts a queue run when `adb` does not answer (transport level). A wedged device answers `adb`, so this watchdog does not see the fault. This feature does not change it. |
| Test fakes | `tests/**` | 27 fake implementations of `ISessionManager`. 2 fake implementations of `IExecutionLogService`. |
| Swagger | `src/GameBot.Service/Swagger/SwaggerConfig.cs` `SessionHealthSchema`, `ApplySessionExamples` | Session responses are anonymous objects. The OpenAPI document uses the schema classes in `SwaggerConfig.cs` and examples. Swagger reads no XML comments. |

## R-001: Where the liveness data lives

**Decision**: Add one singleton `DeviceLivenessTracker` (interface `IDeviceLivenessTracker`) in `GameBot.Domain/Sessions`. It keeps one record for each session ID. The capture loop, `SessionManager`, and the session service write to it. The endpoints and the queue read from it. `TimeProvider` gives the clock.

**Rationale**: The capture loop (`GameBot.Emulator`) and the input path (`GameBot.Emulator`) must write the data. The service layer must read it. `GameBot.Domain` is below both projects. A separate tracker keeps the capture loop and `SessionManager` small. It also lets unit tests use `FakeTimeProvider` without a device.

**Alternatives considered**:
- Keep the data in `BackgroundScreenCaptureService`. Rejected: the input path does not know the capture service, and no capture service exists in the test hosts (`GAMEBOT_USE_ADB=false`).
- Add fields to `EmulatorSession`. Rejected: the session object has no lock, and many writers change it. The tracker has one lock for each record.

## R-002: A pure liveness evaluator

**Decision**: Add a static, pure `DeviceLivenessEvaluator.Evaluate(DeviceLivenessSample sample, DeviceLivenessOptions options, DateTimeOffset now)` that returns a `DeviceLivenessReport`. It has no I/O and no clock. The tracker makes the sample. The callers give `now`.

The evaluator uses these rules, in this order. The first rule that applies gives the reason.

1. The session has no device serial → `unknown`.
2. `TransportReady` is `false` (only the health call sets it) → `not_live`, `transport_not_ready`.
3. The last input timed out, or its outcome is still `pending` after more than the input time limit. Also, no frame change came after the start of that input → `not_live`, `input_timeout`.
4. A capture loop runs, and no capture completed for longer than the capture-stall limit → `not_live`, `capture_stalled`. When no capture completed yet, the loop start time is the reference.
5. A capture loop runs, and the frame did not change for longer than the stale limit. Also, the first input after the last frame change is older than the stale limit → `not_live`, `no_change_after_input`.
6. A capture loop runs and has at least one completed capture → `live`.
7. Otherwise (no loop, or no completed capture yet) → `unknown` with `NeedsProbe = true`.

The `stale` flag is separate. It is `true` only while a capture loop runs, and `unchangedMs > StaleLimitMs` or `frameAgeMs > CaptureStallLimitMs` (FR-006).

Rule 5 uses the new tracker field `FirstInputAfterChangeAt`. The first input after a frame change sets it. The next frame change clears it. The rule counts the stale limit from this input, not from the last frame change. Thus each input has the full stale limit to change the screen. Example: a screen is static for 10 minutes, and then one tap arrives. The state stays `live` for the next 5 minutes. Without this field, the tap would give `not_live` at once, and the health and the queue would show a false fault. Also, the queue gate does not hold firings for rule 5 (R-011). Thus a false `no_change_after_input` cannot stop the inputs that clear it.

Rules 4 and 5 and the `stale` flag apply only while a capture loop runs. After `LoopStopped`, the frame data is old, and it is not a fault signal. A new input then gives `unknown` and a probe (rule 7), not a false `not_live`.

**Rationale**: FR-002 to FR-006 are table rules. A pure function gives one unit test for each row, with no device and no wait. The order puts the most direct evidence first. A transport fault explains all other signs. An input time-out is a direct sign. A capture stall is a direct sign. "No change after input" is the slowest sign.

**Detection limit**: After the HOME key at the start of an idle pause (`IdlePauseHoldAsync`), the queue sends no input. A device that wedges then, and whose captures still complete, matches no rule. The service detects it when the captures stop (rule 4). Or it detects it after the next firing sends input and the stale limit passes (rule 5). The spec records this limit (Assumptions, SC-005).

**Alternatives considered**: Compute the state in the capture loop and keep it as a field. Rejected: the state is then latched, but the spec requires a new calculation at each call ("Recovery" edge case). It also makes the time rules depend on the loop interval.

## R-003: How to detect "the frame did not change"

**Decision**: In `SessionCaptureLoop`, before the new frame replaces the old frame, compare the new PNG bytes with the PNG bytes of the current frame: `previous.PngBytes.AsSpan().SequenceEqual(png)`. Give the result (`changed`) to `tracker.RecordCapture(sessionId, changed)`. The first frame of a loop is always a change.

**Rationale**: The spec defines "no change" as byte-identical PNG data (Assumptions). The loop already holds the previous frame, so the compare needs no extra memory. `SequenceEqual` on a span is a vectorized compare. For a 1 to 3 MB PNG it takes less than 1 ms, at a capture interval of 500 ms (default `GAMEBOT_CAPTURE_INTERVAL_MS`).

**Alternatives considered**:
- A hash (SHA-256 or xxHash) of each frame. Rejected: it reads all bytes too, and a hash adds a package or more CPU for no gain.
- A pixel compare with a tolerance. Rejected: the spec requires byte identity, and a tolerance can hide a static but live screen versus a wedged one.

## R-004: Time limits on ADB calls (kill on cancel)

**Decision**: Change `AdbClient.ExecAsync` and `AdbClient.GetScreenshotPngAsync` in the same way:

1. Start the process.
2. Register the kill on the token before the first read: `using var reg = ct.Register(() => TryKill(proc));`. The new private static helper `TryKill(Process proc)` calls `proc.Kill(entireProcessTree: true)` when the process did not exit. It ignores `InvalidOperationException` and `Win32Exception`.
3. Put the reads and the wait in a `try` block. In `GetScreenshotPngAsync`, this is the `CopyToAsync` of the standard output and the `WaitForExitAsync`. In `ExecAsync`, this is the `WaitForExitAsync`.
4. In `catch`, when `ct.IsCancellationRequested` is `true`, throw `OperationCanceledException(ct)`. The kill can make the read fail with an `IOException` or return early, so the code must not depend on the exception type.

The callers apply the time limit with a linked `CancellationTokenSource` and `CancelAfter`.

**Rationale**: FR-012 requires that the service "stop that command". Without the kill, each timed-out tap leaves an `adb` process. On a wedged device, a queue sends many inputs, so the processes grow without limit. A kill in a `catch` block is not sufficient for the screenshot. On Windows, the standard output of a process is a synchronous pipe. Thus `CopyToAsync` does not stop when the token is cancelled, and the `catch` block never runs. The token registration kills the process at the time of the cancel. The kill closes the pipe, and the blocked read returns. The change is local to one class. It helps each current caller that cancels.

**Test**: The constructor `AdbClient(string adbPath)` sets the executable path. Thus a unit test can point `AdbClient` at a fake executable with no `adb`. The test copies `ping.exe` to a new temporary folder with a unique name (for example `fake-adb-<guid>.exe`). `ExecAsync` gets the arguments `-n 30 127.0.0.1`. `GetScreenshotPngAsync` always adds its own arguments (`exec-out screencap -p`), so the test uses a batch file `fake-adb-<guid>.cmd` in the same folder. The batch file runs the unique `ping` copy with its output sent to `nul`. Thus the process stays silent for 30 s and keeps the pipe open. The test identifies its processes by the unique name, so other `ping` processes cannot make it fail.

**Alternatives considered**: `Task.WaitAsync(timeout)` in the caller, with no kill. Rejected: it returns in time but leaks the process. A kill in the `catch` block only. Rejected: see the rationale. A time-limit parameter on `AdbClient`. Rejected: the token is the current pattern (`SessionsEndpoints` create-session timeout).

## R-005: Time limit of each input action (FR-012)

**Decision**: In `SessionManager.SendInputsWithResultsAsync`, run each action with a linked token that has `CancelAfter(InputTimeoutMs)`. The ADB retries of one action share this one limit. When the limit (not the caller token) cancels the action, the result is `InputActionResult(index, false, "<type>: device did not answer in <N> ms", TimedOut: true)`. The loop then stops, and the service does not send the actions after it. Add `bool TimedOut = false` as a new last positional parameter of `InputActionResult`, so the current constructor calls still compile.

`SendInputsAsync` (sequences and commands) gets no new time limit. It only records the input start and end in the tracker (clarification 5: the step results of sequences do not change).

**Rationale**: `SendInputsWithResultsAsync` is the only path of `POST /api/sessions/{id}/inputs`. One limit for each action, with the retries in it, gives the bound of SC-003: one action times out, and the rest are not sent. `ISessionManager` does not change, so the 27 test fakes do not change.

**Alternatives considered**: A limit for the full request. Rejected: FR-012 says "each input command". A limit in the endpoint only. Rejected: the endpoint cannot stop only the hung action and report which action timed out.

## R-006: The "device not live" answer of the inputs endpoint (FR-013)

**Decision**: The endpoint applies these rules in this order. Rule 1 comes before the dispatch. After the dispatch, the endpoint gets the data-only liveness report of the session (no probe, so the response time does not change).

1. The request has no actions → `400 invalid_request` (no change). The service sends nothing.
2. The session was not found → `409 not_running` (no change).
3. One result has `TimedOut` → `504`, `error.code = device_timeout`, and `results`. The actions before the timed-out action keep their `dispatched` value. The timed-out action is `dispatched: false`. The service did not send the actions after it.
4. The report state is `not_live`, and at least one action was dispatched → change each `dispatched: true` to `dispatched: false`, with `failureReason = "device_not_live: <reason>"`. Return `503`, `error.code = device_not_live`, `error.reason = <reason>`, and `results`.
5. No action was dispatched → `400 invalid_input_actions` (no change).
6. Otherwise → `202` (no change).

**Rationale**: The time-out is the direct and more specific fault, so it comes first. The 504 rule has precedence over FR-013 (spec FR-013). A client error (bad arguments) with a not-live device stays `400`, because nothing reached the device. The service sends the inputs before the check. Thus a device that is only falsely suspected can change its screen and become live again (US3 scenario 2).

**Alternatives considered**: Check before the dispatch and do not send. Rejected by the spec (FR-013: "MUST still send the inputs").

## R-007: The health call and the bounded probes (FR-007 to FR-009)

**Decision**: Add `ISessionLivenessService` / `SessionLivenessService` in `GameBot.Service/Services/Liveness`. It has two operations:

- `Evaluate(EmulatorSession session)`: data only. Used by the inputs endpoint, the screenshot and snapshot headers, and the queue.
- `ProbeAsync(EmulatorSession session, CancellationToken ct)`: used by the health endpoint. It runs the transport check with `TransportCheckTimeoutMs`. It then evaluates the data. When the report has `NeedsProbe`, it runs one direct capture with `CaptureTimeoutMs`. A capture that succeeds gives `live` (frame age 0). A capture that fails or times out gives `not_live`, `capture_stalled`.

The transport check moves behind a small interface `ISessionTransportCheck` (production class `AdbSessionTransportCheck` calls `adb get-state`). The direct capture of the probe moves behind a small interface `ISessionDirectCapture` (production class `AdbSessionDirectCapture` uses `AdbScreenCaptureProvider`). Contract tests replace both.

The probe does not use `SessionManager.GetSnapshotAsync`. That method catches `InvalidOperationException` and returns a 1x1 stub PNG. Thus a failed capture looks like a success, and the probe would report a wedged device as `live`.

**Rationale**: The health endpoint must return in bounded time (FR-009). The worst case is `TransportCheckTimeoutMs + CaptureTimeoutMs` (5 s + 10 s with the defaults). The interface lets a contract test simulate a transport fault with no `adb`. The current `adb` block of the response stays the same (FR-007).

**Alternatives considered**: Reuse `IEmulatorDeviceProbe`. Rejected: it returns only `bool`, but the `adb` block needs `stdout` and `stderr`.

## R-008: Screenshot and snapshot headers and time limit (FR-010, FR-011)

**Decision**:
- Cached frame: `X-Capture-Age-Ms = now - frame.Timestamp`. `X-Capture-Unchanged-Ms = now - lastChangeAt`. `X-Capture-Stale` from the report. All integer milliseconds, invariant culture, `true`/`false` in lower case.
- Direct capture (no cached frame, or the snapshot endpoint): the call gets a linked token with `CancelAfter(CaptureTimeoutMs)`. When the limit cancels it, return `504` with `capture_timeout`. The screenshot endpoint uses its current error shape `{ "error": "capture_timeout", "message": ... }`. The snapshot endpoint uses the session error shape `{ "error": { "code": "capture_timeout", "message": ..., "hint": ... } }`.
- Direct capture that succeeds: `X-Capture-Age-Ms: 0`. When a capture loop runs for the session, `X-Capture-Unchanged-Ms` and `X-Capture-Stale` come from the tracker. When no loop runs, `X-Capture-Unchanged-Ms: 0` and `X-Capture-Stale: false`, because the frame is new and the device answered in time.
- Snapshot endpoint: add the headers only when the session has capture data (FR-010).
- CORS: `WithExposedHeaders("X-Capture-Id", "X-Capture-Age-Ms", "X-Capture-Unchanged-Ms", "X-Capture-Stale")`, from one shared constant array.
- `SessionManager.GetSnapshotAsync`: an `OperationCanceledException` goes to the caller. It must not become the stub PNG. The current `catch (InvalidOperationException)` does not catch it, so the code needs no change. A test makes sure of this.

**Rationale**: Each endpoint keeps its current error shape, so current clients do not break. Headers keep the PNG body unchanged.

**Alternatives considered**: Put the data in a JSON body. Rejected: the body is `image/png`.

## R-009: Capture loop time limit

**Decision**: Each capture in `SessionCaptureLoop` gets a linked token with `CancelAfter(CaptureTimeoutMs)`. A capture that times out is a failed capture (log at Debug level, as now). The loop then continues. With R-004, the hung `screencap` process is killed.

**Rationale**: Today one hung `screencap` stops the loop for all time. Then the device can never become "live" again after a recovery, because no new capture completes. With the limit, the loop recovers by itself when the device recovers. The stall rule (R-002 rule 4) still sees the fault, because no capture completes.

**Alternatives considered**: Restart the loop from outside. Rejected: it adds more parts for the same result.

## R-010: Last-input data (FR-001, FR-014)

**Decision**: `SessionManager` calls `tracker.RecordInputStarted(sessionId)` before each ADB input command. It calls `tracker.RecordInputCompleted(sessionId, outcome)` after it. The outcomes are `completed`, `timed_out`, `failed` and `cancelled`. Between the two calls, the outcome reads `pending`. This applies to `SendInputsAsync` and `SendInputsWithResultsAsync`, in ADB mode only. Stub mode records nothing.

A cancel of the caller token must not leave the outcome `pending`. Otherwise rule 3 gives a false `input_timeout` after `InputTimeoutMs`. Thus the helper records an outcome in a `finally` path also when the caller cancels:

- The input ran for longer than `InputTimeoutMs` before the cancel: `timed_out`. Example: a sequence watchdog stops a hung tap. The device did not answer, so the fault signal stays.
- The input ran for a shorter time: `cancelled`. Rule 3 ignores it, as it ignores `failed`.

The tracker creates a record only in `LoopStarted` and `RecordInputStarted`. The other write methods do nothing for an unknown session ID. Thus a hung capture or input that ends after `Remove` cannot make the record again (data-model section 2).

**Rationale**: The `pending` value lets the evaluator detect a hung input in a sequence (rule 3), with no new time limit in the sequence path. Thus the step results do not change (clarification 5), but the health call and the queue still see the fault.

**Alternatives considered**: Record only in the inputs endpoint. Rejected: FR-014 requires the sequence path too, and the production fault occurs in queue runs.

## R-011: The queue gate before each firing (FR-016)

**Decision**: The gate acts one time for each **firing group**. It is not in `RunOneSequenceAsync`. The gate **holds** a firing. It does not fail the firing.

A firing group is one main firing, the BeforeEachRun pass before it, and the EveryStep pass after it. These are the main firing kinds:

- at-start and self-reschedule next-cycle-start
- time-of-day timer and daily retry
- relative timer and live schedule
- self-reschedule timer
- once-per-run entry and self-reschedule once-per-run

The standalone EveryStep pass (a template with no once-per-run entries, `QueueExecutionService.cs` line 602) is also a firing group. Its main sequence is the first sequence of the pass.

**Hard reasons**: The gate holds a firing only when the state is `not_live` with one of three hard reasons: `capture_stalled`, `input_timeout` or `transport_not_ready`. A new constant set `DeviceLivenessReasons.Hard` holds these three values. The reason `no_change_after_input` does not hold a firing. For this reason, and for `live` and `unknown`, the firing group runs as now.

The code has three parts:

1. A private helper `TryGateOnLivenessAsync` (about 25 lines). It evaluates the data-only report of the run session and calls `handle.Liveness.Observe`. When the state is `not_live` with a hard reason, it does these steps and returns `true`:
   1. It calls `handle.Liveness.RecordGatedFiring(sequenceId)`. This call adds one to `GatedFirings`. It returns `true` only for the first held firing of that sequence in the episode (R-012).
   2. Only when that call returns `true`, the helper writes one failed sequence entry to the execution log: `LogSequenceExecutionAsync(sequenceId, name, "failure", "device_not_live: <reason>", context)`. The context has the same form as the context of a normal firing (`QueueExecutionService.cs` lines 872 and 903 to 912). The parent and the root are `handle.RootExecutionId ?? rootId`, so that the entry goes under the current root segment after a rotation. The depth is 1. The sequence index is `++index`.
   3. Only when that call returns `true`, the helper also records one `Failure` in the run statistics (`RecordRunAsync`, start and end = now).
   The helper does not wait. The run loop does the wait (part 3).

   The `++index` of the gate entry is safe. A hold sets the local flag `held`, and the held path does not do the check `if (index != indexAtIterationStart) continue;`. Thus the new index value does not make the loop start the next iteration at once.

   The helper also has a **hold-only mode**. In this mode, it does not evaluate the device again. It uses the report of the first hold in this iteration. It does steps 1 to 3 and returns `true`. The sites use this mode after the first hold of the iteration (part 3).
2. A local function `FireGroupAsync` in the run body. It replaces the repeated block at each firing site. It calls `EnsureSessionBound` and `TryGateOnLivenessAsync`. When `held` is already `true`, it calls the gate in hold-only mode. When the gate returns `true`, `FireGroupAsync` sets `held` and returns `null` (held). It then does not run the BeforeEachRun pass, the main sequence or the EveryStep pass. When the gate returns `false`, it increments `index` and runs the BeforeEachRun pass (for the kinds that have one). Then it runs `RunOneSequenceAsync` and the EveryStep pass, as now, and returns the result.
3. The hold at each firing site. When `FireGroupAsync` returns `null`, the site does these steps:
   - It does not update its counters and marks. It does not change `executed` or `failed`. It does not call `RecordEntry`, `MarkTimeOfDayFired`, `ArmOrClearDailyRetry`, `MarkRelativeFired` or `MarkOncePerRunCompleted`.
   - Thus a time-of-day timer and a relative timer stay due. A daily retry stays armed with the same attempt number.
   - It puts back an entry that it removed from its register before the firing. The entry keeps its original due time.

   After the first hold, the iteration continues through the other firing sites in hold-only mode. Each other due firing of the iteration goes through `FireGroupAsync`, which holds it with no new evaluation. No sequence runs. Each site applies its put-back rule. Thus each due sequence gets its one log entry in the episode, also when a different sequence was held first (FR-016).

   These are the put-back rules for each register:
   - At-queue-start pass (before the loop, `QueueExecutionService.cs` lines 414 to 421): this pass runs one time, before the loop, and has no register. When the gate holds an at-start entry, the site stops the pass. It moves the held entry and the at-start entries after it (each after a hold-only gate) into `PendingNextCycleStart`. Each moved entry is a new `SelfRescheduleEntry` with `Id = "at-queue-start:<template index>"`, `Option = AtQueueStart`, `FireAt = null` and `Scope = EntryScope(startEntry)`. Thus `handle.HasPendingSelfRescheduleWork` is `true`, and the run enters the loop also for a template with only AtQueueStart entries (line 451). The loop does not take the empty-template branch, so the run does not end. The loop drains the moved entries at the top of each iteration (a0), and the next-cycle-start put-back rule applies. The a0 site passes `null` as the self-reschedule origin action ID for an entry with this `Id` prefix, because no self-reschedule action made it. After a recovery, the moved entries run in template order. Two small differences from the at-start pass are accepted: the a0 site runs the BeforeEachRun pass before them, and it calls `RecordEntry` for them. A run with `cycleExecution: true` then stays in the loop and polls, as a run with booked self-reschedule work does now.
   - Self-reschedule timer (`DrainDueTimerFirings` removed it): a new handle method `RearmTimerFiring(entry)` puts the entry back with its original `FireAt`. It adds the entry only when no other Timer firing for the same sequence is in the register. The other drained entries go through the hold-only gate, and the site puts each one back in the same way.
   - Live schedule (`TryRemove` removed it): the site adds it again with `PendingLiveSchedules.TryAdd(sequenceId, originalDueTime)`. An API call can write a new schedule for the same sequence during the hold (`QueueExecutionService.cs` line 184). Then `TryAdd` does nothing, and the newer schedule wins.
   - Self-reschedule next-cycle-start: the site dequeues the remaining entries of `PendingNextCycleStart` into a list, and holds each one in hold-only mode. It then enqueues the held entry and the list again, in their order, and stops the drain. Thus no entry fires again in the same drain.
   - Self-reschedule once-per-run: the site holds the other entries of the snapshot in hold-only mode. It enqueues the held entry and these entries again in `PendingOncePerRun`.
   - Template once-per-run pass: the site calls `handle.Cycles.ClearCurrentEntryIndex()`, because `SetCurrentEntryIndex` came before the gate. The other entries of the pass go through the hold-only gate and get no mark. The entries that ran keep their `MarkOncePerRunCompleted` mark. The loop sets the local flag `oncePerRunPassHeld`. The next pass does not call `BeginCycle`. It skips each entry for which the new query `QueueRunSchedule.OncePerRunCompletedThisCycle(int index)` returns `true`. This query reads `_oncePerRunDoneThisCycle` under the schedule lock.
   - EveryStep injections: the dictionary keeps them when the pass does not run. No action is necessary.

   The `ConcurrentQueue` registers (`PendingNextCycleStart`, `PendingOncePerRun`) have no "put in front" operation. An entry that a sequence or an API call enqueued during the hold comes before the entries that the site enqueues again. Thus the order of the entries can change after a hold. This is accepted: each entry still fires one time, and the order in these registers has no contract.

   When `held` is `true` after the firing sections, the loop does not complete the cycle. It does not call `MarkOncePerRunPassDone`, `CompleteOpen` or `OnCycleCompleted`, and it does not add to `cycles`. It waits `QueueCheckIntervalMs` with `TimeProvider` and the stop token. Then it starts the next iteration. In this iteration, it does not do the `break` check for a queue with `cycleExecution: false`, and it does not do the idle pause. Thus a queue that does not cycle does not end while it holds a firing.

The held firings stay due, so the next iteration tries them again. When the device is live again, they run at once. The guard sequences (EveryStep and BeforeEachRun) of a firing group that passes the gate run as now. They get no gate of their own.

**Rationale**:
- A held firing did not start, so it is not a failed run of its sequence. It must not use up a daily retry attempt (clarification 4). The earlier design failed each gated firing. Then each gated daily firing used one retry attempt, and all retries were gone after about 90 minutes.
- The earlier design also re-armed a gated self-reschedule timer at the gate time. The chain then fired every 30 s, and each firing wrote one failed entry: about 120 entries each hour for each chain. The hold keeps the original due time. The log cap writes at most one entry for each sequence and episode. Thus the number of log entries for one episode has a limit (SC-008).
- The consecutive-failure count and the failure policy act through the fault cycle of the watch (R-012). They get one failure for each episode, for a queue that cycles and for a queue that does not cycle.
- The reason `no_change_after_input` is the slowest and least direct sign. A static full-screen app and one inert tap can cause it on a live device. If this reason held firings, the queue would send no more inputs. Then no input could change the screen, and the false fault would stay for all time.
- One gate for each firing group gives one decision for each due firing. A gate in `RunOneSequenceAsync` would also gate each guard sequence.
- The wait of `QueueCheckIntervalMs` is necessary. A queue with `cycleExecution: true` whose firings do not run loops with no delay (`if (index != indexAtIterationStart) continue;`). The wait stops this loop.

**Alternatives considered**:
- Fail each gated firing and re-arm it at the gate time (the earlier design). Rejected: see the rationale (log flood, loss of daily retries).
- Hold firings also for `no_change_after_input`. Rejected: the state can latch on a live device.
- Gate in `RunOneSequenceAsync` for each call. Rejected: see the rationale.
- Gate only the once-per-run pass. Rejected: timer firings are the main production case.
- Hold the firing with no log entry. Rejected: the execution log and the statistics must show the fault (FR-016).
- Drop a gated self-reschedule firing. Rejected: the chain then ends for all time, and a recovery cannot start it again.
- Stop the firing sections of the iteration after the first hold. Rejected: the other due firings then get no gate and no log entry, and a second sequence can stay hidden for the full episode (FR-016).
- Keep a held at-start firing in a local list and fire it before the loop. Rejected: the at-start pass runs one time, and an AtQueueStart-only template does not enter the loop. The run would end and lose the entries. The next-cycle-start register already has a drain and a put-back rule in the loop.
- Stop the queue. Rejected by FR-018.

**Known limit**: A time-of-day timer is due from its time of day until midnight (`now >= TimerTimeOfDay`, `QueueExecutionService.cs` line 491). When the hold continues past midnight, the firing of that day is lost. A hold does not arm a daily retry, so the retry register does not keep it. The spec records this limit (Edge Cases, Assumptions). This feature does not change the design for it.

## R-012: The fault episode and the periodic check (FR-015, FR-017)

**Decision**: Add a small thread-safe class `QueueLivenessEpisode` on `QueueRunHandle`. It keeps `NotLiveSince`, `Reason`, `GatedFirings`, the set `GateLoggedSequences` and the flag `FaultCycleRecorded`. It has two separate records for each episode:

- "Gate log entries written": the set `GateLoggedSequences`. `RecordGatedFiring(sequenceId)` adds one to `GatedFirings`. It returns `true` only when the set does not have the sequence yet, and then adds it.
- "Fault cycle recorded": the flag `FaultCycleRecorded`. `TryClaimFaultCycle(now, grace)` returns `true` one time for each episode, when the episode is older than the grace period. It does not look at `GateLoggedSequences`.

`Observe(report, now)` opens an episode on the first `not_live` report, for each reason. It closes the episode on a `live` or `unknown` report and clears all episode data.

Add a class `QueueLivenessWatch` in `Services/QueueExecution`. The run starts it as a background task after the session is bound, and stops it (awaits it) in the `finally` block of the run. Every `QueueCheckIntervalMs` (with `TimeProvider`), it evaluates the report of the current `handle.SessionId` and calls `Observe`. When `TryClaimFaultCycle` returns `true`, it does these steps:

1. It writes one failed child entry of the queue run to the execution log, with no sequence (new `IExecutionLogService.LogQueueDeviceFaultAsync`). The root ID is `handle.RootExecutionId ?? rootId`, read at the time of the call. Thus the entry goes under the current root segment after a rotation.
2. It seals one failed cycle in the cycle ledger (new `QueueCycleLedger.RecordFaultCycle`), with no entries.
3. It calls `QueueFailurePolicyEvaluator.OnCycleCompleted`, so that `ConsecutiveFailedCycles` and the failure policy act.

The watch and the run loop can call `OnCycleCompleted` at the same time. Today the evaluator reads `handle.PolicyTripped` and then calls `MarkPolicyTripped` in two steps (`QueueFailurePolicyEvaluator.cs` lines 67 to 69). Two calls can both read `false`, and the policy then acts two times. Thus a new handle method `bool TryMarkPolicyTripped()` sets the flag and returns `true` only when the flag was `false`, in one step under `_policyLock`. The evaluator uses it in place of the read and the mark. Only one of two concurrent calls then acts.

The failure policy can stop the queue (`notifyAndStop`). This is the action that the operator configured, not an action of the queue "by itself" (FR-018). The watch only records the fault cycle. The evaluator does the stop, as for each other failed cycle.

The watch does these steps for each episode after the grace period, also when the gate already wrote entries in the episode. The `GET /api/queues/{id}` projection also calls `Observe` with a new report, so the state that it shows is current and not up to 30 s old.

**Rationale**: FR-017 needs a check when no firing is due, for example during an idle pause. At that time, the run loop is in a 250 ms poll or in the idle-pause hold. Thus a separate task is simpler than a check in each wait. A queue that does not cycle never completes a cycle (its open cycle stays open), so its consecutive-failure count never grows. A held firing also completes no cycle (R-011). Thus the fault cycle is the only way that the failure policy can act on a wedged device. This is the production case (US4 independent test).

The two records are separate on purpose. In an earlier design, a gated firing set one shared flag, and the watch then did not record its fault cycle. On a queue that does not cycle, the consecutive-failure count then never increased.

**Alternatives considered**:
- A hosted service that checks all queues, as `QueueDeviceWatchdogService` does. Rejected: it would need access to each run handle and to the run root ID, and its life is not the life of the run.
- Check in the poll wait of the run loop. Rejected: the idle-pause hold has its own wait, and a long sequence blocks the loop.

## R-013: Configuration (FR-019)

**Decision**: New options class `DeviceLivenessOptions` in `GameBot.Domain/Sessions`, bound to the configuration section `Service:DeviceLiveness` with `builder.Services.Configure<DeviceLivenessOptions>(...)`. Environment override: `Service__DeviceLiveness__StaleLimitMs` and so on. All values are in milliseconds.

| Property | Default | Minimum (clamp) | Spec source |
|---|---|---|---|
| `StaleLimitMs` | 300000 (5 min) | 1000 | Assumptions |
| `CaptureStallLimitMs` | 60000 (60 s) | 1000 | Assumptions |
| `InputTimeoutMs` | 10000 (10 s) | 100 | Assumptions |
| `CaptureTimeoutMs` | 10000 (10 s) | 100 | Assumptions |
| `TransportCheckTimeoutMs` | 5000 (5 s) | 100 | FR-009, FR-019, Assumptions |
| `QueueGracePeriodMs` | 120000 (2 min) | 0 | Assumptions |
| `QueueCheckIntervalMs` | 30000 (30 s) | 1000 | FR-016, FR-019, Assumptions |

A method `Normalized()` returns a copy with the minimums applied. Each reader uses the normalized copy. A bad value never throws.

**Rationale**: `IOptions<T>` is the pattern of `FailureNotificationOptions` and `DetectionOptions`. The test harness note says that `Service:Sessions:*` config cannot be changed in `WebApplicationFactory`. Thus the tests do not use configuration for these values. Unit tests build the options object directly. Contract and integration tests use `services.PostConfigure<DeviceLivenessOptions>(...)` in `ConfigureTestServices`. This works because the options are read when a service is created, after the test services are applied.

**Alternatives considered**: New `GAMEBOT_*` environment values in `AppConfig`. Rejected: `AppConfig` is built once from the environment, and the tests cannot change it for one host. The section `Service:Sessions`. Rejected: see the harness note.

## R-014: Execution log entry with no sequence (FR-017)

**Decision**: Add `Task LogQueueDeviceFaultAsync(string rootExecutionId, string queueId, string queueName, string reason, CancellationToken ct = default)` to `IExecutionLogService`. The entry has `ExecutionType = "queue"`, `FinalStatus = "failure"`, `ObjectRef` of the queue, the hierarchy of a depth-1 child of the run root, and `Summary = "device_not_live: <reason>"`. The interface is `internal interface IExecutionLogService` in `src/GameBot.Service/Services/ExecutionLog/ExecutionLogService.cs` (line 76). The implementation is in the same file. Update the 2 test fakes. Move the fake `RecordingExecutionLog` out of `QueueExecutionServiceTests` into its own shared file, so that the new queue test classes can use it.

**Rationale**: The web UI already shows `queue` entries and expands child entries. A new execution type could break the UI filters. The summary text starts with the same token as the sequence entries of R-011, so one search finds both.

**Alternatives considered**: A sequence entry with an empty sequence ID. Rejected: navigation links and the statistics use the sequence ID.

## R-015: OpenAPI (FR-020)

**Decision**:
- `SwaggerConfig.cs`: add `Liveness` (type `SessionLivenessSchema`) to `SessionHealthSchema`, and add a `liveness` block to the health example. For `POST /api/sessions/{id}/inputs`, add `503` and `504` response examples. For `GET /api/emulator/screenshot` and `GET /api/sessions/{id}/snapshot`, add the three response headers and the `504` response.
- New `DeviceLivenessSchemaFilter` (`ISchemaFilter`): descriptions and `enum` values for `SessionLivenessSchema` (`state`, `reason`, `lastInputOutcome`) and for the new `QueueDeviceLivenessResponse`.
- Register the filter in `GameBotServiceSetup` next to `QueueHealthSchemaFilter`.

**Rationale**: Swagger reads no XML comments (memory note, feature 096). Schema filters and the examples operation filter are the current pattern.

## R-016: Web UI

**Decision**: No web UI change. The UI reads only `X-Capture-Id` (`src/web-ui/src/services/images.ts` line 183). It does not call the health or inputs endpoints. New headers and new fields do not change its behavior. The quality gate is `vite build` + `jest`.

## R-017: Current transport watchdog

**Decision**: `QueueDeviceWatchdogService` stays as it is. It restarts a run when the transport is down. That behavior existed before this feature. FR-018 applies to the new "not live" fault: the new code never stops, restarts or reboots. A failure policy that the operator configured can still stop the queue after a fault cycle (R-012).

**Rationale**: A change to the transport watchdog is out of scope. The two mechanisms look at different signals: `adb` answers (watchdog) versus frames and inputs (liveness).
