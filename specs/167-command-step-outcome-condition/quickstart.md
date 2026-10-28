# Quickstart: commandStepOutcome condition

## Use in a sequence

1. Add a sequence step `send-1` that runs the command `PNS.GatherSend`.
2. After `send-1`, add an `if` step with this condition:

```json
{ "type": "commandStepOutcome", "stepRef": "send-1", "commandStepOrder": 12, "expectedState": "executed" }
```

3. In the "then" branch, add the step that ends the run (or a named failure step).
4. Save. A wrong `stepRef`, order, or state gives a 400 error before the run.

In the web UI: pick the type `commandStepOutcome` in the sequence editor, pick the step, enter the order, and pick one of the four states.

## Check the build and tests

Run these commands from the repository root with absolute paths:

- `dotnet build C:\src\GameBot\GameBot.sln`
- `dotnet test C:\src\GameBot\GameBot.sln --filter "FullyQualifiedName~CommandStepOutcome"`
- In `C:\src\GameBot\src\web-ui`: `npm test -- --run` (the editor and validation tests)

## Manual check

1. Run a sequence where step 12 of the command runs. The `if` takes the "then" branch.
2. Run it where step 12 is skipped (image not on screen). The `if` takes the "else" branch. The run log shows `actual=skipped_detection_failed`.
3. Run it where the command stops before step 12. The log shows `actual=none`, and the run does not fail.
