# Quickstart: verify the published nesting rules

1. Build and run the contract test:

   ```powershell
   dotnet test "C:\src\GameBot\tests\contract\GameBot.ContractTests.csproj" --filter "FullyQualifiedName~SequenceNestingRulesOpenApiTests"
   ```

2. Or inspect a running service (port 8080):

   ```powershell
   (Invoke-RestMethod http://localhost:8080/swagger/v1/swagger.json).components.schemas.SequenceStep.description
   (Invoke-RestMethod http://localhost:8080/swagger/v1/swagger.json).components.schemas.SequenceStep.properties.elseBody.description
   ```

   Expect the If-inside-If prohibition, the Loop-body allowances, and the Break-inside-loop rule.

3. Confirm behaviour is unchanged: saving an If nested in an If branch still returns
   `400 "Branch step '<id>' inside if '<id>' must not itself be an if step."` — covered by the existing
   `IfValidationTests` / `LoopValidationTests` (unit) and `IfStepContractTests` (contract):

   ```powershell
   dotnet test "C:\src\GameBot\tests\unit\GameBot.UnitTests.csproj" --filter "FullyQualifiedName~IfValidationTests|FullyQualifiedName~LoopValidationTests"
   ```
