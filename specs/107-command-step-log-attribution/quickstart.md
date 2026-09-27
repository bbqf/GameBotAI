# Quickstart: Log each command step under its own step ID (B-020)

## Automated check

```bash
dotnet test tests/unit/GameBot.UnitTests.csproj -c Release --filter "FullyQualifiedName~SequenceRunnerStepIdTests"
dotnet test tests/integration/GameBot.IntegrationTests.csproj -c Release --filter "FullyQualifiedName~SharedCommandStepAttributionIntegrationTests"
```

Both test classes must pass.

## Manual check

1. Create one command, for example `Shared` (one `GoToHomeScreen` step).
2. Create a sequence with a `gate` step that runs `Shared`, then two `If` steps. The first `If` has the condition `commandOutcome` `gate` `failed` and the body step `body-false`. The second `If` has the condition `commandOutcome` `gate` `success` and the body step `body-true`. Both body steps run `Shared`.
3. Run the sequence with `POST /api/sequences/{id}/execute`.
4. Find the execution with `GET /api/execution-logs?objectType=sequence&objectId={id}`.
5. Read `GET /api/execution-logs/{executionId}/subtree`.
6. Make sure that there are two command nodes: `gate` and `body-true`. No command node has `body-false`. Each `message` names its own step.
