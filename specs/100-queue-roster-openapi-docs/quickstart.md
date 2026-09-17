# Quickstart: Verify the Queue Roster Documentation

## Automated

```powershell
dotnet test "C:\src\GameBot\tests\contract\GameBot.ContractTests.csproj" --filter "FullyQualifiedName~QueueRosterOpenApiTests|FullyQualifiedName~QueueHealthOpenApiTests|FullyQualifiedName~QueuesApiContractTests"
```

All tests pass; `QueueRosterOpenApiTests` fails if any roster description is removed, `entries` is re-marked nullable, or the `entries` example disappears.

## Manual (against a locally running service built from this branch)

```powershell
$d = Invoke-RestMethod http://localhost:8080/swagger/v1/swagger.json
$d.components.schemas.QueueDetailResponse.properties.entries          # description present, no nullable:true, readOnly:true
$d.components.schemas.QueueEntryResponse.properties | Format-List     # four descriptions
$d.paths.'/api/queues/{id}'.get.description                           # roster sentence + health text
$d.paths.'/api/queues/{id}/entries'.post.description                  # points to GET /api/queues/{id} entries
$d.paths.'/api/queues/{id}/entries'.put.description                   # same
```

Then confirm behaviour is unchanged: `GET /api/queues/{id}` for a queue with no entries still returns `"entries": []`.
