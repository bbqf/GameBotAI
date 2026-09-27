# Quickstart: A reschedule-self time of day that has passed books the next day

## Run the tests

```powershell
dotnet test "C:\src\GameBot\tests\unit\GameBot.UnitTests.csproj" --filter "FullyQualifiedName~SelfRescheduleCoordinator|FullyQualifiedName~QueueMonitorService"
dotnet test "C:\src\GameBot\tests\contract\GameBot.ContractTests.csproj" --filter "FullyQualifiedName~PrimitiveActionTypesOpenApi"
```

## Check by hand

1. Make a sequence with one step: `reschedule-self` with `{ "option": "Timer", "timerTimeOfDay": "<a time 1 minute ago>" }`.
2. Put the sequence in a queue template as a `Timer` entry with `timerRelativeOffset: "00:00:05"`. Start the queue.
3. After the sequence runs, send `GET /api/queues/{id}/monitor`.
4. The `SelfReschedule` item has `expectedAt` at the time of day on the next day. The sequence does not run again on the same day.
5. Change the step to a time 5 minutes ahead and start the queue again. The `SelfReschedule` item is today at that time.
