# Quickstart: Shard Balance Check

1. Build the tests one time:

```powershell
dotnet build "C:\src\GameBot\GameBot.sln" -c Release -p:RunAnalyzers=false
```

2. Run the check for one test project:

```powershell
& "C:\src\GameBot\scripts\ci\measure-shards.ps1" -Project contract
```

3. Read the table. A shard with status `split` is more than 20% above the average of its project.
4. If a shard has status `split`, move heavy class prefixes from it to a new shard in
   `scripts/ci/test-shards.json`. Keep the last shard as the complement.
5. Run the shard test and the check again:

```powershell
dotnet test "C:\src\GameBot\tests\unit\GameBot.UnitTests.csproj" --filter "FullyQualifiedName~GameBot.UnitTests.Ci"
& "C:\src\GameBot\scripts\ci\measure-shards.ps1" -Project contract
```

6. Tick the shard check box in the pull request template and write the result.

Exit status 0 means the rule is met. Exit status 1 means a split is required.
