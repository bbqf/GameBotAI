# Quickstart: verify time-limit cancellation reporting

## Automated

```powershell
dotnet test C:\src\GameBot\tests\unit --filter "FullyQualifiedName~QueueExecutionServiceTests"
dotnet test C:\src\GameBot\tests\integration --filter "FullyQualifiedName~SequenceTimeLimitLog|FullyQualifiedName~SequenceWatchdogTimeoutApi"
dotnet test C:\src\GameBot\tests\contract --filter "FullyQualifiedName~SequenceTimeLimitOpenApi"
```

## Manual (running service, port 8080)

1. Set a short override on a slow sequence: `PATCH /api/sequences/{id}` with `{ "watchdogTimeoutMs": 5000 }`.
2. `GET /api/sequences/{id}`. Expect `watchdogTimeoutMs: 5000` and `effectiveWatchdogTimeoutMs: 5000`. A sequence
   without an override shows `null` and `240000`.
3. Put the sequence in a queue and start it. After about 5 s, `GET /api/execution-logs?objectType=sequence` shows that
   firing's entry with `finalStatus: "failure"`, `cancellationReason: "sequence_time_limit"` and `timeLimitMs: 5000`.
4. Start the queue again and stop it before the bound fires. The entry has `cancellationReason: null`.
5. Check `/swagger/v1/swagger.json` for `effectiveWatchdogTimeoutMs`, `cancellationReason` and `timeLimitMs`.
6. Remove the override again (`PATCH` with `"watchdogTimeoutMs": null`).
