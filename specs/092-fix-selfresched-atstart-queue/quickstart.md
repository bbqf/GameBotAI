# Quickstart: verify issue #198 is fixed

## Automated

```powershell
dotnet test "C:\src\GameBot\tests\unit\GameBot.UnitTests.csproj" --filter "FullyQualifiedName~QueueExecutionServiceTests"
```

The new `AtQueueStartOnly*` tests must pass. Before the fix they fail: the run stops with the booking unfired.

## Manual (live service, optional)

1. Create a sequence whose step 0 is
   `{"type":"reschedule-self","schemaVersion":"v1","payload":{"option":"Timer","timerRelativeOffset":"00:01:30"}}`.
2. Create a template holding that sequence as a single `AtQueueStart` entry, and a throwaway queue (`cycleExecution:false`, `pauseWhenIdle:true`) linked to it.
3. Start the queue. After the first firing, `GET /api/queues/{id}/monitor` lists the booking with `scheduleKind: "SelfReschedule"` **and the queue status stays `Running`**.
4. After ~90 s the sequence runs again and books itself again. Stop the queue; the log reads "stopped manually".
