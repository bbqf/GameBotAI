# Quickstart: Gamepack command reference check

1. Make a gamepack with one sequence. Give it a step that runs command `ghost` (by `commandId`). Do not put `ghost` in the pack or on the instance.
2. Run a dry run. Expect a `reference` problem "Command 'ghost' is not in the gamepack or in the installation." and `valid: false`.
3. Change the step to a name-only `commandReference` with `commandName: "Ghost"`. Expect the same kind of problem.
4. Add a command with that id or name to the gamepack. Expect no problem.
5. Install the first pack. Expect the same problem and no write.

Tests:

- `dotnet test --filter "FullyQualifiedName~Gamepack"`
