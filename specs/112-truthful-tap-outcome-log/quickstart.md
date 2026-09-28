# Quickstart: Truthful tap outcome and an execution log for a single step

## Run the tests

```
dotnet test tests/unit/GameBot.UnitTests.csproj --filter "FullyQualifiedName~CommandExecutorPrimitiveTapTests|FullyQualifiedName~CommandExecutorStepLogTests"
dotnet test tests/integration/GameBot.IntegrationTests.csproj --filter "FullyQualifiedName~StepExecutionLogIntegrationTests"
```

The `PrimitiveTap` detection tests need Windows. The log tests run on all platforms.

## Check by hand

1. Start the service and a session.
2. Send a single step:

   ```
   POST /api/steps/execute
   { "sessionId": "<id>", "step": { "type": "KeyInput", "order": 0, "keyInput": { "key": "HOME" } } }
   ```

3. Read the log for the time of the call:

   ```
   GET /api/execution-logs?fromUtc=<start>&toUtc=<end>&objectType=step&objectId=<id>
   ```

4. Make sure that the response has one entry with `executionType: "step"` and the step outcome.
5. Send a `WaitForImage` step with no image and `timeoutMs: 12000`. The response is `status: "timeout"`. The log has one more entry with the step outcome `timeout`.
