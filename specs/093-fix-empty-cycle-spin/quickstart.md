# Quickstart: verify the empty-cycle spin fix

## Automated

```powershell
dotnet test C:\src\GameBot\tests\unit\GameBot.UnitTests.csproj --filter "FullyQualifiedName~QueueExecutionServiceTests|FullyQualifiedName~QueueCycleLedger"
```

Expect the new cycling scheduled-only tests to pass. They fail on `master`, where the cycle count runs into the hundreds of thousands and idle pause is never entered.

## Manual (live service, optional)

1. Create a throwaway queue on a spare device with `cycleExecution:true`, `pauseWhenIdle:true` and `idleThresholdSeconds:30`. Its template holds one `AtQueueStart` entry and one `Timer` entry with relative offset `+00:01:00`.
2. Start it, then poll `GET /api/queues/{id}` every 10 s.
3. Expect: `health.cyclesCompleted` stays `0` until the timer fires, then becomes `1`. `GET /api/queues/{id}/monitor` shows the idle pause while waiting. Host CPU stays idle.
4. Stop and delete the queue.
