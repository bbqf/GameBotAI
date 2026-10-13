# Quickstart: verify the OpenAPI change

1. Build the solution.
2. Run the new contract test:
   `dotnet test C:\src\GameBot\tests\contract --filter "FullyQualifiedName~CommandParametersOpenApiTests"`
3. Run the existing OpenAPI contract tests:
   `dotnet test C:\src\GameBot\tests\contract --filter "FullyQualifiedName~OpenApi"`
4. Start the service and read `/swagger/v1/swagger.json`. Check that `CommandCreateSchema` has `parameters`, and that the `fieldTemplates` description names `unresolvable_parameter_reference`.
5. Check that the spec Status is Implemented and `specs\STATUS.md` has the row for 138.
6. Check that `installer\versioning\version.override.json` has `minor` "9", and that `CHANGELOG.md` has the new entry under Unreleased.
