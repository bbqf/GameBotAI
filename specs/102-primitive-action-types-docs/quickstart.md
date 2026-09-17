# Quickstart: verify published primitive action types

1. Build and run the tests:

   ```powershell
   dotnet test "C:\src\GameBot\tests\unit\GameBot.UnitTests.csproj" --filter "FullyQualifiedName~SequenceStepValidationServiceActionTypeTests"
   dotnet test "C:\src\GameBot\tests\contract\GameBot.ContractTests.csproj" --filter "FullyQualifiedName~PrimitiveActionTypesOpenApiTests"
   ```

2. Against a running service, read the enum:

   ```powershell
   (Invoke-RestMethod http://localhost:8080/swagger/v1/swagger.json).components.schemas.PrimitiveAction.properties.type.enum
   ```

   Expect the eleven values, including `reschedule-self`.

3. Probe the enriched error:

   ```powershell
   $body = '{"name":"probe","version":1,"dryRun":true,"steps":[{"stepId":"a","primitiveAction":{"type":"bogus","payload":{}}}]}'
   try { Invoke-RestMethod http://localhost:8080/api/sequences -Method Post -ContentType application/json -Body $body -Headers @{Authorization="Bearer <token>"} }
   catch { $_.ErrorDetails.Message }
   ```

   Expect 400 with `... is not a supported primitive action type (expected one of tap, swipe, ...)`.
