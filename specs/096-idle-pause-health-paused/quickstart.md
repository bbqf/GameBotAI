# Quickstart: verify idle pause in queue health

## Automated

```powershell
dotnet test C:\src\GameBot\tests\unit\GameBot.UnitTests.csproj --filter "FullyQualifiedName~QueueRunHandlePauseTests|FullyQualifiedName~QueueExecutionServiceTests|FullyQualifiedName~QueueMonitorServiceTests"
dotnet test C:\src\GameBot\tests\integration\GameBot.IntegrationTests.csproj --filter "FullyQualifiedName~QueueHealthIdlePauseTests|FullyQualifiedName~QueueFailurePolicyRunTests"
dotnet test C:\src\GameBot\tests\contract\GameBot.ContractTests.csproj --filter "FullyQualifiedName~QueueHealthOpenApiTests"
```

## Manual (live service, port 8080)

1. Create or pick a queue with `cycleExecution:false`, `pauseWhenIdle:true`, `idleThresholdSeconds:30`, and a template whose next timer is a few minutes out. Start it.
2. When `GET http://localhost:8080/api/queues/{id}/monitor` reports `current.scheduleKind == "IdlePause"`, read `GET http://localhost:8080/api/queues/{id}`.
3. Expect `health.paused: true`, a non-null `health.pausedAt`, `health.pauseReason` starting `idle pause: resumes at`, and `health.pauseKind: "idle"`.
4. After the firing runs, expect `health.paused: false` and null pause fields.
