# Quickstart: Reject a parameters object

1. Run the new tests (they fail before the fix):
   `dotnet test C:\src\GameBot\tests\contract\GameBot.ContractTests.csproj --filter "FullyQualifiedName~ParametersType"`
2. Apply the change in `src\GameBot.Service\Endpoints\CommandsEndpoints.cs`.
3. Run the command contract tests:
   `dotnet test C:\src\GameBot\tests\contract\GameBot.ContractTests.csproj --filter "FullyQualifiedName~Commands"`
4. Run the command integration tests:
   `dotnet test C:\src\GameBot\tests\integration\GameBot.IntegrationTests.csproj --filter "FullyQualifiedName~Command"`
5. Manual check: send POST /api/commands with `"parameters": {"name":"p","type":"text","required":true}`.
   Expect HTTP 400 and `parameters` in the error message.
