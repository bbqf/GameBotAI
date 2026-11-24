# Quickstart: Device Recovery Option in the Queue Form

**Feature**: 199-device-recovery-ui | **Date**: 2026-11-24

## Automated checks

Run from the repository root:

```powershell
dotnet build "C:\src\GameBot\GameBot.sln" -c Debug
dotnet test "C:\src\GameBot\tests\unit\GameBot.UnitTests.csproj" --filter "FullyQualifiedName~DeviceRecovery|FullyQualifiedName~QueueLivenessWatch"
dotnet test "C:\src\GameBot\tests\contract\GameBot.ContractTests.csproj" --filter "FullyQualifiedName~DeviceRecovery|FullyQualifiedName~DeviceLiveness|FullyQualifiedName~SessionMonitoring"
dotnet test "C:\src\GameBot\tests\integration\GameBot.IntegrationTests.csproj" --filter "FullyQualifiedName~DeviceRecovery"
npm --prefix "C:\src\GameBot\src\web-ui" test -- QueueForm QueueDeviceState PhoneQueuesView MonitoringPage QueuesPage
npm --prefix "C:\src\GameBot\src\web-ui" run lint
```

## Manual check 1: the option and the save (US1, US2)

1. Start the service and open the web UI on a desktop browser.
2. Open the edit form of a queue that has an emulator instance name.
3. Turn on "Reboot the instance when the device is not live". Set the wait to 3 minutes. Save.
4. Run `curl http://localhost:<port>/api/queues/<id>`. Expect `deviceRecovery.action = "reboot-instance"` and `afterMs = 180000`.
5. Open the edit form again. Change only the name. Save.
6. Read the queue again. Expect the same `deviceRecovery` values.
7. Send `PUT /api/queues/<id>` with a body that has no `deviceRecovery`. Expect the same values (FR-005a).
8. Send `PUT` with `"deviceRecovery": null`. Expect `deviceRecovery: null`.

## Manual check 2: form checks (US3)

1. Turn on the option. Clear the emulator instance name. Press Save.
2. Expect "Device recovery needs an emulator instance name." and no request in the network log.
3. Set the wait to 0.5 and the attempts to 6. Expect a message at each field.

## Manual check 3: a change on a running queue (FR-012a, SC-006)

1. Start a queue with recovery off. Block the capture of its device (for example, pause the emulator window).
2. Wait until the edit view shows "Device: not live" for longer than 1 minute.
3. Turn on the option with a wait of 1 minute. Save. Note the time.
4. Expect one reboot of the instance within 1 minute of the save, with no stop and start of the queue. The service log shows event 1150.
5. Expect the edit view to show "Recovery: running" and then "Device: live".

## Manual check 4: the "Not live" mark (US4)

1. Keep a device of a running queue not live, as in check 3, with recovery off.
2. Open the Monitoring page. Expect "Not live" on the session row of the queue and in the header of the selected session within 1 minute.
3. Open the web UI on a phone (or a 360 px wide window). Expect "Not live" on the queue card and in the queue detail, with no horizontal scroll.
4. Make the device live again. Expect the mark to go away within 1 minute.
