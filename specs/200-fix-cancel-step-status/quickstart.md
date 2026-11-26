# Quickstart: check the fix for B-046

## Automatic tests

Run the unit tests of the execution log:

```powershell
dotnet test "C:\src\GameBot\tests\unit\GameBot.UnitTests.csproj" --filter "FullyQualifiedName~ExecutionLogServiceMapStepStatusTests|FullyQualifiedName~ExecutionSubtreeProjectionTests"
```

Run the integration tests of the Cancel dispatch:

```powershell
dotnet test "C:\src\GameBot\tests\integration\GameBot.IntegrationTests.csproj" --filter "FullyQualifiedName~SelfRescheduleCancel"
```

Expected result: all tests pass.

## Manual check

1. Make a sequence with two `reschedule-self` steps: step 0 with `option: "Timer"` and `timerRelativeOffset: "00:30:00"`, step 1 with `option: "Cancel"`.
2. Put the sequence in a queue and start the queue.
3. Open the execution log of the sequence run (`GET /api/execution-logs/{id}/subtree`).
4. Make sure that the node of step 1 has `"status": "success"` and the message "Self-reschedule '<label>' cancelled: removed 1 pending booking(s) of this sequence; applies to the current run only".
5. Remove step 0 and run the queue again. Make sure that the node of the `Cancel` step has `"status": "success"` and the message "... noop: no pending booking of this sequence; nothing to cancel".
6. Open an execution log that the service stored before the fix. Make sure that its `Cancel` step nodes now have `"status": "success"`.
