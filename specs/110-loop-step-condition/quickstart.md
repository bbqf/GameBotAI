# Quickstart: Guard on a Loop step

## Build and test

```bash
dotnet build GameBot.sln -c Debug
dotnet test tests/unit/GameBot.UnitTests.csproj --filter "FullyQualifiedName~LoopGuard|FullyQualifiedName~LoopValidation"
dotnet test tests/contract/GameBot.ContractTests.csproj --filter "FullyQualifiedName~LoopGuard"
```

## Manual check

1. Start the service. Set `GAMEBOT_AUTH_TOKEN`.
2. Send the request of `contracts/loop-step-guard.md` to `POST /api/sequences`. Change the guard to `{ "type": "lastRun", "sequence": "self", "status": "success", "within": "01:00:00" }`. In an ad-hoc run this guard is false.
3. `GET /api/sequences/{id}`: the `Loop` step has the `condition`.
4. `POST /api/sequences/{id}/execute` with `{ "dryRun": true }`: the `Loop` entry has `status` `Skipped`, no iterations, and `conditionResult` `false`. The key step does not run.
5. Open the execution log of the run: the `loop` detail item has `conditionType` and `conditionResult` `false`.
6. Change the guard back to `none(lastRun ...)` and run again: the loop runs 3 iterations and the entry has `conditionResult` `true`.
7. Send the sequence with `dryRun: true` and a guard `{ "type": "commandOutcome", "stepRef": "nope", "expectedState": "success" }`: the response is 400 and names `leave-if-stuck`.
