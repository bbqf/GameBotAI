# Quickstart: device liveness (B-019)

This guide shows how to read the device liveness through the REST API. The service listens on port 8080. Use the bearer token of your installation.

## 1. Configure the limits (optional)

The defaults are in the table. To change a value, set it in `appsettings.json` under `Service:DeviceLiveness`, or set an environment value such as `Service__DeviceLiveness__StaleLimitMs=180000`. Restart the service after the change.

```json
{
  "Service": {
    "DeviceLiveness": {
      "StaleLimitMs": 300000,
      "CaptureStallLimitMs": 60000,
      "InputTimeoutMs": 10000,
      "CaptureTimeoutMs": 10000,
      "TransportCheckTimeoutMs": 5000,
      "QueueGracePeriodMs": 120000,
      "QueueCheckIntervalMs": 30000
    }
  }
}
```

## 2. Poll the session health

```powershell
$h = @{ Authorization = "Bearer <token>" }
Invoke-RestMethod -Headers $h "http://localhost:8080/api/sessions/<sessionId>/health" | ConvertTo-Json -Depth 5
```

Read `liveness.state`:

- `live`: the device renders frames and accepts input.
- `not_live`: read `liveness.reason`. Restart the emulator when the fault stays.
- `unknown`: no device (stub mode), or no data yet.

A static screen with no input gives `stale: true` but `state: live`. This is normal, for example for a full-screen game on a static menu. The Android launcher shows the status-bar clock, so its frame changes at least one time each minute.

## 3. Check a screenshot for staleness

```powershell
$r = Invoke-WebRequest -Headers $h "http://localhost:8080/api/emulator/screenshot?serial=emulator-5558"
$r.Headers["X-Capture-Age-Ms"], $r.Headers["X-Capture-Unchanged-Ms"], $r.Headers["X-Capture-Stale"]
```

Do not crop a reference image from a screenshot with `X-Capture-Stale: true`. The screen can be frozen.

## 4. Send inputs and read the new failures

```powershell
$body = @{ actions = @(@{ type = "tap"; args = @{ x = 100; y = 200 } }) } | ConvertTo-Json -Depth 5
try {
  Invoke-RestMethod -Method Post -Headers $h -ContentType "application/json" -Body $body "http://localhost:8080/api/sessions/<sessionId>/inputs"
} catch {
  $_.Exception.Response.StatusCode   # 504 = device_timeout, 503 = device_not_live
  $_.ErrorDetails.Message
}
```

## 5. Watch a queue

```powershell
(Invoke-RestMethod -Headers $h "http://localhost:8080/api/queues/<queueId>").health.deviceLiveness
```

When `state` is `not_live` with the reason `capture_stalled`, `input_timeout` or `transport_not_ready`:

- The queue holds each due firing. The firing does not run, and the guard sequences (EveryStep, BeforeEachRun) of that firing do not run.
- The held firing stays due. It keeps its due time and its daily retry attempt number. The queue checks the device again every `QueueCheckIntervalMs`.
- In one fault episode, the first held firing of each sequence records one failed entry with the summary `device_not_live: <reason>`. Later held firings of that sequence record nothing. `gatedFirings` counts all held firings.
- When the device is live again, the held firings run. Self-reschedule chains continue at their original cadence.

When `state` is `not_live` with the reason `no_change_after_input`, the firings run as normal. Their inputs can change the screen and clear the state.

For each reason, the queue records one failed entry for the fault episode after `QueueGracePeriodMs`. It also records one failed cycle, so `consecutiveFailedCycles` increases by one. A configured failure policy can then notify or pause the queue.

Detection limit: after the HOME key at the start of an idle pause, the queue sends no input. A device that wedges then, and whose captures still complete, shows `live`. The queue sees the fault when the captures stop, or after the next firing sends input and `StaleLimitMs` passes.

The queue does not stop and does not restart the device by itself. A failure policy that you configured can still pause or stop the queue after the fault cycle. Restart the emulator by hand. The queue continues with the held firings when the device is live again.

## 6. Find the fault in the execution logs

Search the execution logs for the summary text `device_not_live`. The entries are children of the queue run.

## 7. Verify the feature (developers)

```powershell
dotnet test "C:\src\GameBot\GameBot.sln" --filter "FullyQualifiedName~Liveness|FullyQualifiedName~AdbClientCancellation|FullyQualifiedName~BackgroundScreenCapture|FullyQualifiedName~SessionManagerInputTimeout|FullyQualifiedName~SessionManagerSnapshotTimeout|FullyQualifiedName~CaptureHeaders|FullyQualifiedName~QueueCycleLedgerFaultCycle"
```

The filter selects all test classes of this feature:

- `Liveness`: `DeviceLivenessOptionsTests`, `DeviceLivenessEvaluatorTests`, `DeviceLivenessTrackerTests`, `SessionManagerInputLivenessTests`, `SessionLivenessServiceTests`, `SessionHealthLivenessContractTests`, `SessionInputsLivenessContractTests`, `DeviceLivenessOpenApiTests`, `QueueLivenessEpisodeTests`, `QueueLivenessGateTests`, `QueueLivenessWatchTests`, `QueueDeviceLivenessContractTests`.
- The other names: `AdbClientCancellationTests`, `BackgroundScreenCaptureServiceTests`, `SessionManagerInputTimeoutTests`, `SessionManagerSnapshotTimeoutTests`, `CaptureHeadersTests`, `CaptureHeadersContractTests`, `QueueCycleLedgerFaultCycleTests`.

The unit tests use `FakeTimeProvider`. They need no device. `AdbClientCancellationTests` runs only on Windows. It uses a copy of `ping.exe` as a fake `adb`.
