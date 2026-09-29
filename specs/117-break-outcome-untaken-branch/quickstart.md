# Quickstart: Make sure that a Break that did not run reads as no_break

**Feature**: 117-break-outcome-untaken-branch | **Issue**: #250

## 1. Build and run the tests

Use absolute paths. Do not put `cd <dir>;` before a command.

```powershell
dotnet build "C:\src\GameBot\GameBot.sln" -c Debug
dotnet test "C:\src\GameBot\tests\unit\GameBot.UnitTests.csproj" --filter "FullyQualifiedName~BreakStepIndex|FullyQualifiedName~SequenceRunnerUntakenBreak"
dotnet test "C:\src\GameBot\tests\integration\GameBot.IntegrationTests.csproj" --filter "FullyQualifiedName~NestedStepOutcomeReference"
dotnet test "C:\src\GameBot\tests\contract\GameBot.ContractTests.csproj" --filter "FullyQualifiedName~SequencePerStepConditionsOpenApi"
```

If a project path is different, find it with `Get-ChildItem "C:\src\GameBot\tests" -Recurse -Filter *.csproj`.

Expected result: all tests pass. Before the fix, the new "branch did not run" tests fail with "commandOutcome reference '...' is unavailable".

## 2. Reproduce issue #250 by hand (optional)

Start the service with `GAMEBOT_USE_ADB=false`. Send the requests to the local service (port 8080) with the bearer token.

1. `POST /api/sequences` with `dryRun: true` and these steps. Expect `valid: true`.
   - `probe`: Action, `primitiveAction` `go-to-home-screen` (outcome `success`).
   - `book`: Loop, `loop: { loopType: "count", count: 3, maxIterations: 3 }`, body:
     - `if-empty`: If, condition `commandOutcome probe failed` (false), body: Action `book-reset` (`go-to-home-screen`), then Break `brk-empty` (no condition).
     - `if-wait`: If, condition `commandOutcome probe failed` (false), body: Action `book-wait` (`go-to-home-screen`), then Break `brk-wait` (no condition).
     - `settle`: Action `go-to-home-screen`.
   - `fail-no-booking`: Action `go-to-home-screen`, `requireDispatch: true`, condition `all(commandOutcome brk-empty break negate:true, commandOutcome brk-wait break negate:true)`.
2. Send the same request with no `dryRun`. Expect 201.
3. `POST /api/sequences/{id}/execute` with a session id.
4. Expect: the run status is `Succeeded`. The step `fail-no-booking` has the status `Succeeded` and `conditionResult` "true". No step message has "is unavailable". In `GET /api/execution-logs/{id}` for this run, the `fail-no-booking` entry has `conditionTrace.finalResult` true and no `conditionTrace.failureReason`.

## 3. Make sure that the other cases do not change

- Change the condition of `if-empty` to `commandOutcome probe success` (true). `brk-empty` fires. `fail-no-booking` is `Skipped` (its condition is false).
- Put an Action `sometimes-step` in an If branch that does not run, and reference it from a later step with `commandOutcome sometimes-step success`. The run still fails with "unavailable" (FR-005).
- Save a sequence with `stepRef` `no-such-step`, with `dryRun: true` and with no `dryRun`. Both saves still give 400 "references unknown prior step" (FR-004).

## 4. Read the OpenAPI description

`GET /swagger/v1/swagger.json`. In `components.schemas.CommandOutcomeCondition.properties.stepRef.description`, find the text "did not execute" and "no_break". In `components.schemas.CommandOutcomeCondition.description`, find the text "evaluates as no_break".
