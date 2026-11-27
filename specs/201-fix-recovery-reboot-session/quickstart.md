# Quickstart: Check the fix for B-047

## Automatic tests

Run the unit tests and the integration tests of this feature:

```powershell
dotnet test "C:\src\GameBot\tests\unit\GameBot.UnitTests.csproj" --filter "FullyQualifiedName~QueueLivenessWatch|FullyQualifiedName~QueueLostSession|FullyQualifiedName~DeviceLivenessReader"
dotnet test "C:\src\GameBot\tests\integration\GameBot.IntegrationTests.csproj" --filter "FullyQualifiedName~DeviceRecovery"
```

Expected result: all tests pass. The new integration test shows that the queue stays `Running`, binds a new session after the device is listed again, and runs the held firing.

## Manual check on a real setup

1. Set `deviceRecovery = { action: reboot-instance, afterMs: 60000, maxAttempts: 2, cooldownMs: 60000 }` on a test queue with `emulatorInstanceName`.
2. Start the queue. Make the device not live (for example, freeze the instance window).
3. Wait for the recovery reboot. Read `GET /api/queues/{id}` during the reboot. Expected: `status: Running`, and `health.deviceLiveness` has `reason: transport_not_ready`.
4. Wait until `adb devices` lists the serial as `device`. Read `GET /api/sessions/running`. Expected: one session for the serial in about one check interval.
5. Expected: the next due firing runs. The execution log has no root with "emulator connection lost mid-run".
6. Keep the instance closed after a reboot (close it by hand). Expected: after `maxAttempts` attempts, one "recovery failed" alert. The queue stays `Running` with `recoveryState: exhausted`. Start the instance again. Expected: the queue binds a session and continues.
