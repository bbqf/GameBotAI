# Quickstart: Verify the OpenAPI change

1. Build the solution.
2. Run the new contract test:
   `dotnet test C:\src\GameBot\tests\contract --filter "FullyQualifiedName~CommandParametersOpenApiTests"`
3. Run the existing OpenAPI contract tests:
   `dotnet test C:\src\GameBot\tests\contract --filter "FullyQualifiedName~OpenApi"`
4. Start the service.
5. Read `/swagger/v1/swagger.json`.
6. Make sure that `CommandCreateSchema` has `parameters`.
7. Make sure that the `fieldTemplates` description names `unresolvable_parameter_reference`.
8. Make sure that the spec Status is Implemented.
9. Make sure that `specs\STATUS.md` has the row for 138.
10. Make sure that `installer\versioning\version.override.json` has `minor` "9".
11. Make sure that `CHANGELOG.md` has the new entry under Unreleased.
