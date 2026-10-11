# Quickstart: Measure and Check CI Time

Use these steps to see the CI time before and after a change. Use absolute paths.

## Read past runs

1. Run `& "C:\src\GameBot\scripts\ci\measure-ci.ps1" -Limit 20`.
2. Read the table: queue time, step time per job, and the result of each run.
3. Use the median of at least 5 successful runs. A single run is noise.

## Check the path rules

1. Run `& "C:\src\GameBot\scripts\ci\get-changed-groups.ps1" -Files "docs/a.md","specs/b/spec.md"`.
2. Check the output: `dotnet=false`, `webui=false`.
3. Run the script with `src/web-ui/src/App.tsx`: expect `dotnet=false`, `webui=true`.
4. Run the script with `src/GameBot.Domain/X.cs`: expect `dotnet=true`, `webui=false`.
5. Run the script with `scripts/anything.ps1` or an unknown path: expect both true.

## Check the shards

1. Run `& "C:\src\GameBot\scripts\ci\get-test-shard-filter.ps1" -Project integration -List`.
2. Check that each shard has a filter and that the last shard is the complement.
3. Run the unit test that checks "every test is in exactly one shard".

## Prove a change

1. Push the branch.
2. Run `gh run watch <run-id> --exit-status`.
3. Compare the critical path with the budget in `docs/ci-time.md`.
4. Repeat for 5 pushes (use `workflow_dispatch`) before you write a final number.

## Prove the docs-only rule

1. Push a branch that changes only a file in `docs/`.
2. Check that the run exists, that the .NET jobs show as skipped, and that the run ends in success in 1 minute or less.
