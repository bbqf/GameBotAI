# Quickstart: Verify that a booked run keeps the parameter values

## 1. Automated tests

Run the tests in PowerShell with absolute paths:

```powershell
dotnet test "C:\src\GameBot\tests\unit\GameBot.UnitTests.csproj" --filter "FullyQualifiedName~Queues"
dotnet test "C:\src\GameBot\tests\integration\GameBot.IntegrationTests.csproj" --filter "FullyQualifiedName~SelfRescheduleRunIntegrationTests"
```

Expected result: all tests pass. Before the fix, the new tests fail: the booked run resolves the parameter from the queue scope only.

The integration tests T006 (OncePerRun) and T007 (Timer) are the automated proxy for SC-001. They use the real dispatch path, but not the live device. Do the live reproduction of section 2 after a deploy.

## 2. Manual check with the live service (after a deploy)

1. Make a sequence with a required parameter `novaOptionImage` (no default). Step 0: `reschedule-self` Timer with `timerRelativeOffset` `00:15:00`. Add a step that uses `{{novaOptionImage}}`, for example an If step with an `imageVisible` condition and `imageId` = `{{novaOptionImage}}`.
2. Make a queue template entry: `Timer`, `timerRelativeOffset` `00:00:00`, `parameterValues` `novaOptionImage` = `pns-nova-option-affinity`.
3. Start the queue. The template entry makes the first run, and that run books a new run.
4. Wait for the booked run (or use a short offset, for example `00:01:00`).
5. Open the execution log of the booked run.

Expected result: the booked run evaluates the condition with `imageId=pns-nova-option-affinity`. No step fails with "could not be resolved from any scope". Each run after it in the chain shows the same value.

## 3. No change for other runs

Run the full unit and integration test projects. All current queue, self-reschedule, and parameter tests pass with no change to their expected results (SC-004).
