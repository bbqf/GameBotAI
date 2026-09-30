# Research: Device Not-Live Alert, Optional Recovery, and Capture Pile-Up Guard

All items below are decisions. No item is open. The code facts come from the files named in each item.

## R-001: Where the alert runs

- **Decision**: Run the alert in `QueueLivenessWatch.CheckOnceAsync`, after `Observe`.
- **Rationale**: The watch already runs every `QueueCheckIntervalMs` (30 s) for each Running queue. It owns the episode clock. The alert time plus 1 minute (SC-001) is easy to meet with a 30 s interval. The gate in `QueueExecutionService` also calls `Observe`, but it runs only when a firing is due. The watch is the one place that always runs.
- **Alternatives considered**: A new hosted service that scans all queues. Rejected: it duplicates the episode data and needs new access to the run handles. The failure policy evaluator. Rejected: the alert must not need a failure policy (FR-004).

## R-002: Episode flags and atomic claims

- **Decision**: Add to `QueueLivenessEpisode` the flags `alertSent`, `recoveryFailedSent`, and `liveAgainPending`, and the counters `attempts` and `lastAttemptEndedAt`. Add `TryClaimAlert`, `TryClaimLiveAgain`, and `TryClaimRecoveryFailed`. Each claim runs under the existing lock and returns true one time.
- **Rationale**: This is the pattern of `TryClaimFaultCycle`. It makes "one message for each episode" true also when the gate and the watch both call `Observe`. A `live` observation keeps `liveAgainPending` only when `alertSent` was true. `unknown` and a queue stop clear all flags with no message.
- **Alternatives considered**: Keep the flags in the watch. Rejected: the gate calls `Observe` too, and only the episode sees both calls.

## R-003: Alert text and the send path

- **Decision**: Add `INotificationDispatcher.SendAlert(QueueAlert alert)`. `QueueAlert` has the queue ID, the alert kind (`NotLive`, `LiveAgain`, `RecoveryFailed`), the reason, and a completion callback. The dispatcher writes it to the same channel as a new work kind. It is never dropped and it does not count toward `MaxQueuedJobs`. The worker reads the queue name, makes the text with a new formatter method, and sends it to all enabled targets. It ignores `NotificationLevel` and the streak state. When the sends end, it calls the callback with `succeeded = at least one target accepted`.
- **Rationale**: The worker already owns target lookup, send limits, and the order of sends. The channel gives one place for all messages. The callback sets `lastNotificationAt` without a new dependency from the worker to the run registry. The number of alerts is bounded by the number of episodes.
- **Text format**: `<queue> : device not live (<reason>)`, `<queue> : device live again`, `<queue> : device recovery failed`. The text has no markup, as in feature 120. The text starts with the circle of the status (red, green, red).
- **No target**: The worker logs "no target available" at Information level. It does not call the callback and it does not fail (spec edge case). `lastNotificationAt` stays unchanged.
- **Alternatives considered**: Use `IFailureNotifier` (feature 087). Rejected: it needs a URL and a policy. A second queue-level field for alerts. Rejected: the spec says the alert depends on neither the level nor the policy.

## R-004: Alert time is a service setting

- **Decision**: Add `AlertAfterMs` (default 300000, minimum 1000) to `DeviceLivenessOptions`. Section `Service:DeviceLiveness`. `Normalized()` applies the minimum.
- **Rationale**: Clarification 2026-09-30. The options class already holds all liveness limits. The minimum 1000 ms lets a test use a short time. The issue test uses 2 minutes through configuration.
- **Alternatives considered**: A queue field. Rejected by the clarification.

## R-005: Stagger time and reboot wait are service settings

- **Decision**: Add `RecoveryStaggerMs` (default 180000, minimum 0) and `RebootReadyTimeoutMs` (default 180000, minimum 1000) to `DeviceLivenessOptions`.
- **Rationale**: FR-012 needs a configurable stagger time. A reboot takes about 75 s, so a 180 s limit gives the boot a safe margin. The same section keeps all settings in one place.
- **Alternatives considered**: Queue fields. Rejected: the stagger time is for the whole service, and the spec lists only `deviceRecovery` as a new queue field.

## R-006: One recovery slot for the service

- **Decision**: `DeviceRecoveryCoordinator` is a singleton. It has one `SemaphoreSlim(1,1)` for the slot and one `lastStartedAt` value. `RebootInstanceAsync(instanceKey, ct)` does these steps:
  1. If a reboot task for `instanceKey` is in flight, return that task.
  2. Wait for the slot. Then wait until `lastStartedAt + RecoveryStaggerMs`.
  3. Set `lastStartedAt`. Run `IEmulatorControl.RebootAsync`. Release the slot when the reboot command returns.
- **Rationale**: FR-011 needs one recovery at a time. The slot covers the reboot command and the start of the boot. The wait for the device to be ready runs in the per-queue runner, outside the slot, so that the slot does not block for the full boot. The stagger time (3 minutes) is longer than the boot (75 s), so two boots do not overlap in practice. This also meets "one recovery at a time" in the sense of the spec (one reboot start at a time, at least the stagger time apart). The instance key is the instance name, compared without case. The shared task gives "at most one reboot for one instance in one episode window" (clarification).
- **Alternatives considered**: Hold the slot until the device is ready. Rejected: a failed boot would block all other queues for the full ready limit. The slot with the stagger gives the same protection against a reboot storm.

## R-007: Reboot command and its result

- **Decision**: Change `IEmulatorControl.RebootAsync` to return `Task<bool>`. It returns true when `ldconsole.exe reboot --name <instance>` ends with exit code 0. It returns false when the tool is missing, the exit code is not 0, or the process cannot start. The runner logs the cause (spec edge case) and counts the attempt as failed.
- **Rationale**: Today the method returns `Task` and swallows the exit code. `LdConsoleClient.RebootAsync` already returns the exit code. The existing caller in `EnsureEmulatorRunningActionHandler` can ignore the new value, or keep its current behavior.
- **Alternatives considered**: A second interface for recovery. Rejected: it duplicates the `ldconsole` path lookup.

## R-008: Wait for the device, rebind, and check live

- **Decision**: After the reboot command, poll `IEmulatorDeviceProbe.IsResponsiveAsync(serial)` every 5 s until it is true or `RebootReadyTimeoutMs` ends. Then rebind the session. Then poll the liveness report of the new session every 2 s until `live` or until the remaining ready time ends.
- **Rationale**: The probe already checks `adb` state `device` and `sys.boot_completed=1` (FR-008 first part). `live` in the liveness report needs a capture that completed after the loop started, which is the second part of FR-008. The attempt succeeds only when the report is `live`.
- **Alternatives considered**: Bind the session first and let the watch see `live`. Rejected: a session on an offline device fails at `CreateSession`.

## R-009: Session rebind and the run loop

- **Decision**: Add `RebindSessionAsync(queue, handle)` as a thin internal method of `QueueExecutionService`. It stops the old capture loop, stops the old session, calls `BindQueueSession`, and sets `handle.SessionId`. The run loop local `sessionId` follows `handle.SessionId`: `EnsureSessionBound` (called before each firing) replaces the local value when `handle.SessionId` is different and the session exists.
- **Rationale**: The watch and the gate read `handle.SessionId`, but the loop keeps its own copy. `EnsureSessionBound` already rebinds a lost session, so one more rule in the same place keeps the change small. The gate (`TryGateOnLivenessAsync`) receives `sessionId` from the loop. The plan changes that call to read the handle value, so the gate and the watch never disagree.
- **Alternatives considered**: Stop and start the queue (what `QueueDeviceWatchdogService` does). Rejected: a restart loses live self-reschedules, and the spec needs the queue to run the held firings. The held firings are in the loop state and stay.

## R-010: Attempts, cooldown, and failure

- **Decision**: The attempt count and `lastAttemptEndedAt` are in the episode. A new attempt starts only when the episode is older than `AfterMs`, `attempts < MaxAttempts`, and `now - lastAttemptEndedAt >= CooldownMs`. After the last failed attempt, the watch claims `TryClaimRecoveryFailed` and sends one alert. The queue stays Running.
- **Rationale**: Clarification 2026-09-30. The episode owns the counters, so a live observation resets them for the next episode.
- **Alternatives considered**: Stop the queue after the last attempt. Rejected by the clarification.

## R-011: Queue field validation

- **Decision**: `QueueDeviceRecoveryValidator` returns an error text for: unknown `action`; `reboot-instance` with no `emulatorInstanceName` (an index alone is not enough, because the spec names `--name`); `afterMs` below 60000; `maxAttempts` outside 1 to 5; `cooldownMs` below 0. Missing members take the defaults. The create and update routes return HTTP 400 with the existing error shape. The error text names the member and the allowed range.
- **Rationale**: FR-010 and FR-010a. The contract tests from feature 106 and the memory note on 400-not-500 give the pattern.
- **Alternatives considered**: Accept an instance index. Rejected: FR-007 uses the name.

## R-012: Capture gate and the real cause of the pile-up

- **Decision**: Add `DeviceCaptureGate` in `GameBot.Emulator`. It is a `ConcurrentDictionary<string, DeviceCaptureState>` keyed by serial. `SessionCaptureLoop` asks `TryBegin` before `CaptureScreenshotPngAsync`. A `TimedOut` outcome puts the serial in state `Suspect`.
- **Rationale**: `AdbClient.GetScreenshotPngAsync` kills the host `adb` process on cancel (feature 106, R-004). The `screencap` process on the device is in state `D` and does not end. The loop then starts the next capture, and the next one hangs. This gives the 960 processes in the incident. The loop is serial, so a count of host-side in-flight captures is not enough. A timed-out capture must count as unfinished.
- **Alternatives considered**: A longer capture timeout. Rejected: it only slows the pile-up. Kill the device process with `adb shell kill`. Rejected: a process in state `D` ignores the kill signal, and the shell call can hang too.

## R-013: How a Suspect device is released

- **Decision**: While a serial is `Suspect`, the loop sleeps. Every `CaptureStallLimitMs` it calls `AdbClient.HasRunningScreencapAsync(serial)` (`adb shell pidof screencap`, limit `TransportCheckTimeoutMs`). An empty answer with a clean exit means the first capture ended, so the gate clears the serial. An error or a time-out keeps it `Suspect`. `StartCapture` for the serial always clears it (repair by session rebind).
- **Rationale**: FR-014 says "until the first capture ends or the device is repaired". The check reads the device-side end of the capture with a call that is not `screencap`, so it adds no `screencap` process. A healthy device that has one slow capture then recovers by itself, and SC-005 holds.
- **Fallback**: If the check cannot run (for example a test provider), the serial stays `Suspect` until a repair. The liveness state then reports `capture_stalled`, which is correct.
- **Alternatives considered**: Release after a fixed wait and try one capture. Rejected: it adds one process for each wait and breaks SC-004 on a blind device.

## R-014: Direct captures of the liveness probe

- **Decision**: `AdbSessionDirectCapture` (used by `ProbeAsync`) calls the same gate. When the serial is `Suspect`, it returns false at once and starts no process.
- **Rationale**: The probe runs on each health call. Without the gate, each call of `GET /api/queues/{id}` on a blind device could add a `screencap` process. FR-013 says "at most one unfinished capture for each device".
- **Alternatives considered**: Leave the probe alone. Rejected: the probe is a second source of pile-up.

## R-015: Liveness detection stays unchanged

- **Decision**: Do not change `DeviceLivenessEvaluator` or the rules of `DeviceLivenessReport`. The gate only lowers the number of captures. A blind device still reports `capture_stalled` after `CaptureStallLimitMs`, because the last capture time stops.
- **Rationale**: FR-015. Existing tests for feature 106 must pass with no change.

## R-017: Runner name and delivery order

- **Decision**: The per-queue component is `QueueDeviceRecoveryRunner`. The settings record is `QueueDeviceRecovery`. The coordinator interface and `DeviceRecoveryCoordinator` are built before the runner. The runner calls only the coordinator to reboot.
- **Rationale**: The spec forbids one name for two types. The order means no recovery runs without the slot.
- **Alternatives considered**: One class for both jobs. Rejected: it mixes per-queue state with service-wide state.

## R-018: Stop, delete, and service restart

- **Decision**: The runner uses the cancellation token of the queue run. A stop or delete cancels the alert timer and the recovery wait. The coordinator releases the slot in a `finally` block. The episode lives in the run handle, so a service restart gives a new episode with a new timer and exactly one alert. A stop with no alert sent sends no "live again" message.
- **Rationale**: Edge cases (a) and the restart case in the spec. Tests cover both.
- **Alternatives considered**: Save episodes to disk. Rejected: the spec says the timer starts again after a restart.

## R-019: Two queues on one instance, and a late capture end

- **Decision**: Each queue has its own episode, alert, and attempt count. The shared reboot task in the coordinator gives one reboot for one instance. After it ends, each runner rebinds its own session and counts its own attempt. A late capture end clears the `Suspect` state through the check in R-013, and captures start again.
- **Rationale**: Clarification on shared instances, and edge cases (b) and (c) of the spec.
- **Alternatives considered**: One shared episode for one instance. Rejected: queue health is for each queue.

## R-020: Liveness check interval

- **Decision**: The watch keeps its existing interval (`QueueCheckIntervalMs`, 30 s), which is inside the "at most once a minute" limit of the spec. The alert arrives within `AlertAfterMs` plus this interval.
- **Rationale**: Clarification A2. It meets SC-001.
- **Alternatives considered**: A new timer for each episode. Rejected: it duplicates the watch.

## R-016: Documentation of the changed statement

- **Decision**: Update the text in `DeviceLivenessSchemaFilter` and `QueueHealthSchemaFilter` that says the queue never repairs the device. The new text says that the queue can reboot the instance when `deviceRecovery` is on. Add descriptions for `deviceRecovery` and the alert.
- **Rationale**: FR-016 and Constitution Principle V.

## R-021: How SC-003 is measured

- **Decision**: The coordinator records each reboot start time. A test reads the two start times and asserts that the gap is `RecoveryStaggerMs` or more. The manual check reads the two `ldconsole.exe reboot` log lines.
- **Rationale**: SC-003 (clarification U2). The start time is the only value the coordinator controls. The end of a reboot depends on the emulator.
- **Alternatives considered**: Measure the gap between the end of one reboot and the start of the next. Rejected: the spec measures start to start.

## R-022: Values of `recoveryState`

- **Decision**: `DeviceLivenessReader` derives the value from the episode and the queue settings. `running` when `recoveryRunning` is true. Else `exhausted` when `attempts` is equal to or more than `maxAttempts` and the state is `not_live`. Else `idle`. With no open episode, the value is `idle`.
- **Rationale**: FR-017. The value needs no new stored field. It cannot disagree with the counters.
- **Alternatives considered**: Store the value as a field. Rejected: two sources of the same fact.

## R-023: Living documents (FR-018)

- **Decision**: One docs task changes four files. It refreshes the "Last reviewed" date in `docs\architecture.md` and adds the alert, `deviceRecovery`, the coordinator, and the capture gate. It sets the Status of spec 121 to Implemented in `specs\STATUS.md`. It sets the Status line of `specs\106-wedged-device-liveness\spec.md` to "Implemented (iterated by 121)". It sets the Status line of `specs\121-device-not-live-alert-recovery\spec.md` to Implemented.
- **Rationale**: Constitution Principle V needs these updates in the same PR.
- **Alternatives considered**: Update the files in a later PR. Rejected by Principle V.

## R-024: Service registrations in one task

- **Decision**: One task adds all new registrations to `src\GameBot.Service\GameBotServiceSetup.cs`: the coordinator (and its interface), the runner factory, and the capture gate. The other tasks add no registration.
- **Rationale**: Parallel tasks then do not conflict on one file. `Program.cs` stays unchanged.
- **Alternatives considered**: Each task registers its own type. Rejected: merge conflicts on `GameBotServiceSetup.cs`.
