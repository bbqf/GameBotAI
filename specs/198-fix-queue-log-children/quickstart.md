# Quickstart: Show the newest runs of a running queue in the execution log (B-044)

## Build and test

```powershell
dotnet build "C:\src\GameBot\GameBot.sln" -c Debug
dotnet test "C:\src\GameBot\tests\unit\GameBot.UnitTests.csproj" --filter "FullyQualifiedName~ExecutionLog"
dotnet test "C:\src\GameBot\tests\integration\GameBot.IntegrationTests.csproj" --filter "FullyQualifiedName~ExecutionLog"
dotnet test "C:\src\GameBot\tests\contract\GameBot.ContractTests.csproj" --filter "FullyQualifiedName~ExecutionLog"
```

## Check the race fix (unit)

1. Run `ExecutionLogRepositoryCleanupRaceTests`.
2. Before the fix, the test that adds a root during the remove step fails: the list does not give the new root.
3. After the fix, all tests pass.

## Check the start sweep (integration)

1. Run `ExecutionLogStartSweepIntegrationTests`.
2. The test seeds a `running` queue root with a timestamp before the given process start time, starts the host, and waits for the sweep.
3. The root has the status `failure` and the summary `Queue '<name>' run ended when the service stopped.`. A `running` entry with a later timestamp keeps `running`.

## Check on a real service (manual, optional)

1. Start a queue with "resume on service start", and restart the service.
2. Call `GET /api/execution-logs?objectId=<queue id>&sortBy=timestamp&sortDirection=desc`.
3. The first item is the root of the current run, with the status `running`. The root of the run before the restart has the status `failure` and the summary "... run ended when the service stopped.".
4. Call `GET /api/execution-logs/{id}/subtree` for the first item. Each run since the restart is a child.

## Read the API rules

Open `/swagger` and read the descriptions of `GET /api/execution-logs` and `GET /api/execution-logs/{id}/subtree`.
