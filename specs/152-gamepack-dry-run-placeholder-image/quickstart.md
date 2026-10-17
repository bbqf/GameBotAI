# Quickstart: verify the feature

1. Build: `dotnet build C:\src\GameBot\GameBot.sln`.
2. Run the new unit tests: `dotnet test C:\src\GameBot\tests\unit --filter "FullyQualifiedName~Placeholder"`.
3. Run the new integration tests: `dotnet test C:\src\GameBot\tests\integration --filter "FullyQualifiedName~Gamepack"`.
4. Send a dry run with a gamepack that has one sequence. The sequence declares the parameter `novaOptionImage`. A condition uses `{"type":"imageVisible","imageId":"{{novaOptionImage}}","minSimilarity":0.85,"negate":false}`:
   `POST /api/gamepacks/install?dryRun=true&takeOver=true`.
   Expect `valid: true` and an empty problem list.
5. Change the parameter name in the declaration. Send again. Expect a `reference` problem for `{{novaOptionImage}}`.
6. Check the OpenAPI text: search `C:\src\GameBot\src\GameBot.Service\Swagger\GamepackSchemaFilter.cs` for `dry run` and `reference`. Add one sentence only if a description lists the image reference rules. Record the result.
7. Run the full suites (unit, contract, integration). All must pass. Rerun a known flaky CI test once before it counts as a failure.
