# Quickstart: Check that a booked run uses the current entry values

**Feature**: 196-fix-reschedule-params

## 1. Automatic tests

Run these commands from the repository root:

```powershell
dotnet test "C:\src\GameBot\tests\unit\GameBot.UnitTests.csproj" --filter "FullyQualifiedName~QueueExecutionServiceTests|FullyQualifiedName~ParameterScopeEntryKey|FullyQualifiedName~QueueRunScheduleSwap|FullyQualifiedName~QueueRunPlan"
dotnet test "C:\src\GameBot\tests\integration\GameBot.IntegrationTests.csproj" --filter "FullyQualifiedName~SelfRescheduleRunIntegration"
```

Expected result: all tests pass. The test `TimerBookingUsesTheNewEntryValueAfterAnApply` fails before the fix and passes after the fix.

## 2. Check on a real queue (the scenario of issue #409)

1. Make a sequence with the parameter `fighterTier` and a `reschedule-self` Timer step at step 0 (`timerRelativeOffset` 00:02:00, `keep: earliest`). A later command step uses `{{fighterTier}}`.
2. Make a queue template entry for the sequence with `fighterTier` = `A`. Start the queue.
3. Wait for the first run. Make sure that the queue monitor shows a pending booking of the sequence.
4. Pause the queue. Change the template entry to `fighterTier` = `B`. Call `GET /api/queues/{id}/template-diff`, then `POST /api/queues/{id}/apply-template` with the key of the change. Resume the queue.
5. Wait for the booking to fire. Open the execution log of the booked run.
6. Make sure that the `parameters` item shows `fighterTier` = `B` with the origin layer `entry`.
7. Make sure that the next booking of the run (step 0) also fires with `B`.
8. Make sure that the bookings of the other tasks of the queue have the same fire times as before step 4.

Expected result: no stop and start of the queue is necessary.
