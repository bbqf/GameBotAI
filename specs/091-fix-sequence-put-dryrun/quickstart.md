# Quickstart: verify issue #177 is fixed

## Automated

```powershell
dotnet test "C:\src\GameBot\tests\contract\GameBot.ContractTests.csproj" -c Debug --filter "FullyQualifiedName~SequenceUpdateDryRun|FullyQualifiedName~SequenceCommandReferenceExistence|FullyQualifiedName~SequenceWritesOpenApi"
dotnet test "C:\src\GameBot\tests\integration\GameBot.IntegrationTests.csproj" -c Debug --filter "FullyQualifiedName~Sequence"
dotnet test "C:\src\GameBot\GameBot.sln" -c Debug
```

## Manual reproduction (issue steps) against a running service

1. `POST /api/sequences` with a one-step tap sequence → note `id`, `version: 1`.
2. `PUT /api/sequences/{id}` with `"dryRun": true` and a command step whose payload `commandId` is
   `does-not-exist`.
   - **Expected**: `400`, `errors` contains `Command reference 'does-not-exist' does not exist (used by: …)`.
3. Repeat step 2 with a valid body (tap only) and `"dryRun": true`.
   - **Expected**: `200 { "valid": true, "dryRun": true, "errors": [] }`.
4. `GET /api/sequences/{id}`.
   - **Expected**: still `version: 1`, original content.
5. Repeat step 2 without `dryRun`.
   - **Expected**: `400` with the same error; `GET` still `version: 1`.

## Deleted-command re-save (must keep working)

1. Create command `c1`; create a sequence with a command step on `c1`.
2. Delete `c1`; `GET` the sequence → `isResolved: false`.
3. `PUT` the same body back → `200`, still `isResolved: false` with the snapshot name.
