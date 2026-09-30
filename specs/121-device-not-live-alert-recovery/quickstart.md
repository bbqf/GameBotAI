# Quickstart: Device Not-Live Alert, Optional Recovery, and Capture Pile-Up Guard

Use this guide to check the feature by hand. Use a test queue on a spare LDPlayer instance. The service port is 8080.

## 1. Set a short alert time

Set these values in the service configuration and restart the service.

```json
{
  "Service": {
    "DeviceLiveness": {
      "AlertAfterMs": 120000,
      "RecoveryStaggerMs": 180000
    }
  }
}
```

Make sure one notification target is enabled (feature 120).

## 2. Alert only (User Story 1)

1. Create a queue with no `deviceRecovery` and start it.
2. Make the capture of the device block. For example, stop the `adb` server in a way that hangs `screencap`, or suspend the emulator.
3. Wait 2 minutes plus 1 minute.
4. Expect one "device not live" message on the target.
5. Call `GET /api/queues/{id}`. Expect `health.lastNotificationAt` set and `health.deviceLiveness.alertSent` = true.
6. Wait 10 more minutes. Expect no second message.
7. Repair the device. Expect one "device live again" message.

## 3. Recovery (User Story 2)

1. Update the queue. Set `emulatorInstanceName` and this field:

   ```json
   { "deviceRecovery": { "action": "reboot-instance", "afterMs": 180000 } }
   ```

2. Block the capture again.
3. After 3 minutes, expect the instance to reboot. Check `ldconsole.exe isrunning --name <instance>` and the service log.
4. After the boot, expect a new session, `health.deviceLiveness.state` = `live`, and the held firings to run.
5. Expect the "device live again" message.
6. Try a bad value: `afterMs` = 1000. Expect HTTP 400 with the member name in the text.
7. Try `action` = `reboot-instance` with no `emulatorInstanceName`. Expect HTTP 400.

## 4. One recovery at a time (User Story 3)

1. Set up two queues on two instances, both with `reboot-instance`.
2. Block the capture of both devices at the same time.
3. Read the service log. Expect the two reboot commands at least `RecoveryStaggerMs` apart.

## 5. Capture pile-up guard (User Story 4)

1. Block the capture of one device.
2. Every minute for 10 minutes, run:

   ```powershell
   & "C:\Program Files\LDPlayer\LDPlayer9\adb.exe" -s <serial> shell "ps -A | grep -c screencap"
   ```

   Use the `adb.exe` path of your install.
3. Expect a count of 2 or less, and no growth.
4. Check a healthy device. Expect a count of 1 or less and the same capture rate as before.

## 5a. Manual checks for SC-002, SC-003, and SC-004

Two manual checks are required. Automated tests use fakes. These checks use a real device.

1. **SC-004, `screencap` count**: Use the steps in section 5. The check passes when every count is 2 or less for 10 minutes.
2. **SC-002, recovery timing**: Note the time the reboot starts in the service log. The check passes when the held firings run within 75 seconds plus `afterMs` plus 2 minutes after the capture block starts.
3. **SC-003, stagger**: Read the two reboot start times in the log. The check passes when the gap between the two start times is `RecoveryStaggerMs` or more.

## 5b. Check `health.deviceLiveness` members

During a not-live episode, call `GET /api/queues/{id}`. Check `recoveryState`.

- It is `idle` before `afterMs` passes.
- It is `running` while a recovery waits for the slot or a reboot runs.
- It is `exhausted` after `maxAttempts` failed attempts.

## 6. Service restart during an episode

1. Block the capture of a device and wait for the alert.
2. Restart the service while the device is still not live.
3. Expect a new episode timer. After the alert time, expect exactly one new alert.

## 7. Automated checks

Run the unit, contract, and integration test projects. The web UI quality gate is `vite build` and `jest`.

```powershell
& "C:\Program Files\dotnet\dotnet.exe" test "C:\src\GameBot\GameBot.sln" --filter "FullyQualifiedName~DeviceRecovery|FullyQualifiedName~DeviceCaptureGate|FullyQualifiedName~DeviceAlert"
```

Expect all tests to pass.
