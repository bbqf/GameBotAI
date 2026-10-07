# Quickstart: Restart the Game From a Sequence

## Use it

1. Add an "ensure game running" action step at the point where the sequence finds a bad game state.
2. Set `parameters.forceRestart` to `true`.

```json
{ "stepId": "restart", "stepType": "Action",
  "action": { "type": "ensure-game-running", "parameters": { "forceRestart": true } } }
```

3. Add a later step with a `commandOutcome` condition (`stepRef: restart`, `expectedState: restarted`) to try the
   task again only after a restart.

The step stops the game, starts it again, and ends when the game is in the foreground (30 s at most). Each device
call has its own time limit, so the step always ends in 51 s or less.

A condition on a sequence action step reads `restarted` directly. A condition on `success` also matches it. For a
command step, a sequence condition sees `success`. The command outcome list shows `restarted`.

## Check it (real emulator)

1. Start a session for a game on the device. Make sure the game runs.
2. Run a sequence with the one restart step.
3. Expect: the step succeeds with outcome `restarted`, the log status is success, and the game is in the foreground.
4. Run the same step with `forceRestart` false. Expect: it ends at once, outcome `executed`, and the game keeps its state.
5. Open the sequence in the step-through. Expect: the restart step is shown as `would force-stop the game and start it
   again` and the game is not touched.

## Check it (automated)

```powershell
& "C:\Program Files\dotnet\dotnet.exe" test "C:\src\GameBot\GameBot.sln" --filter "FullyQualifiedName~EnsureGameRunning|FullyQualifiedName~ForceRestart|FullyQualifiedName~ActionTypePreviewList|FullyQualifiedName~RestartedOutcome|FullyQualifiedName~RoundTrip"
```

Web UI gate (the real green gate is the build and the tests):

```powershell
& "C:\Program Files\nodejs\npm.cmd" --prefix "C:\src\GameBot\src\web-ui" run build
& "C:\Program Files\nodejs\npm.cmd" --prefix "C:\src\GameBot\src\web-ui" test
```
