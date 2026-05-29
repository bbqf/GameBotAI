## Quickstart: Wait for Image Primitive Action

## Goal

Verify that authors can create a `WaitForImage` step in both commands and sequences, execute it across all supported exit paths, and inspect the parameters and exit condition in execution logs.

## Prerequisites

- Stop any running `GameBot.Service` process before building or testing so output binaries are not locked.
- Have command and sequence authoring flows available through the API or web UI.
- Have at least one reference image available in the image store for the positive-detection path.

## 1. Build and test the repository

```powershell
Set-Location c:\src\GameBot
dotnet build -c Debug
dotnet test -c Debug
```

Expected result:
- Build succeeds without locked-binary failures.
- Tests covering wait execution, validation, and execution logging pass.

## 2. Create a command with a wait step through the API

Example payload:

```json
{
  "name": "wait-for-image-demo",
  "steps": [
    {
      "type": "WaitForImage",
      "order": 1,
      "waitForImage": {
        "timeoutMs": 1500,
        "detectionTarget": {
          "referenceImageId": "home-screen-banner",
          "confidence": 0.9
        }
      }
    }
  ]
}
```

Verification:
- `POST /api/commands` accepts the payload.
- `GET /api/commands/{id}` returns the same step type and parameters.

## 3. Verify the no-image path

Create or update a command so the wait step omits `detectionTarget` and only supplies `timeoutMs`.

Verification:
- Save succeeds.
- Reloaded command preserves `WaitForImage` with no image reference.
- Executing the command completes after the timeout without error.

## 4. Create and reload a sequence with a wait step

Create or update a sequence that includes a `WaitForImage` primitive step.

Verification:
- Sequence create or update accepts the wait step payload.
- Reloaded sequence preserves the same wait-step parameters.
- The sequence authoring UI shows the same image, certainty, and timeout values after reload.

## 5. Verify the image-detected path

Run a command where the configured image becomes visible before timeout.

Verification:
- Command or sequence execution resumes before the timeout limit.
- Step outcome reports an image-detected completion.
- Execution log detail includes `referenceImageId`, effective `confidence`, `timeoutMs`, and `exitCondition = image_detected`.

## 6. Verify the timeout path

Run a command where no image is configured or where the configured image never appears before timeout.

Verification:
- Command or sequence execution continues after the timeout without a failure state.
- Execution log detail includes `exitCondition = timeout_elapsed`.

## 7. Verify the image-unavailable path

Run a command that references an image id that cannot be loaded at execution time.

Verification:
- The step still waits out the configured timeout.
- Execution continues normally after the timeout.
- Execution log detail includes `exitCondition = image_unavailable` and preserves the authored parameters.

## 8. Verify web UI authoring and log rendering

- Open command authoring and sequence authoring in the web UI.
- Add a `WaitForImage` step in each flow.
- Confirm both forms show image selection, optional certainty, and timeout controls.
- Save and reload the command and sequence.
- Open execution log detail for a run of each.

Verification:
- Authored values round-trip without loss.
- Log detail shows both authored parameters and the final exit condition.

## 9. Final verification snapshot (2026-05-28)

Commands executed during implementation polish:

```powershell
Set-Location c:\src\GameBot
dotnet test -c Debug --logger trx --results-directory TestResults
powershell -NoProfile -File scripts/analyze-test-results.ps1 -ResultsDir TestResults -LatestOnly

Set-Location c:\src\GameBot\src\web-ui
& 'C:/Program Files/nodejs/npm.cmd' run test -- --coverage

Set-Location c:\src\GameBot
dotnet list GameBot.sln package --vulnerable --include-transitive
Set-Location c:\src\GameBot\src\web-ui
& 'C:/Program Files/nodejs/npm.cmd' audit --omit=dev --audit-level=high

Set-Location c:\src\GameBot
$files = @(
  'src/GameBot.Domain/Commands/WaitForImageConfig.cs',
  'src/GameBot.Domain/Commands/CommandStep.cs',
  'src/GameBot.Domain/Commands/SequenceStep.cs',
  'src/GameBot.Domain/Services/SequenceStepValidationService.cs',
  'src/GameBot.Domain/Services/SequenceRunner.cs',
  'src/GameBot.Domain/Logging/ExecutionLogModels.cs',
  'src/GameBot.Service/Models/Commands.cs',
  'src/GameBot.Service/Models/SequenceStepContracts.cs',
  'src/GameBot.Service/Models/ExecutionLogs.cs',
  'src/GameBot.Service/Endpoints/CommandsEndpoints.cs',
  'src/GameBot.Service/Endpoints/ExecutionLogsEndpoints.cs',
  'src/GameBot.Service/Program.cs',
  'src/GameBot.Service/Services/CommandExecutor.cs',
  'src/GameBot.Service/Services/ExecutionLog/ExecutionLogService.cs',
  'src/GameBot.Service/Swagger/SwaggerConfig.cs',
  'tests/contract/OpenApiContractTests.cs',
  'tests/integration/Commands/WaitForImageAuthoringIntegrationTests.cs',
  'tests/integration/Commands/WaitForImageExecutionIntegrationTests.cs',
  'tests/integration/ExecutionLogs/CommandExecutionLoggingIntegrationTests.cs',
  'tests/integration/ExecutionLogs/SequenceExecutionLoggingIntegrationTests.cs',
  'tests/integration/Sequences/WaitForImageSequenceAuthoringIntegrationTests.cs',
  'tests/integration/Sequences/WaitForImageSequenceExecutionIntegrationTests.cs',
  'tests/unit/Commands/CommandExecutorWaitForImageTests.cs',
  'tests/unit/Domain/PrimitiveActionValidationServiceTests.cs',
  'tests/unit/Sequences/SequenceRunnerWaitForImageTests.cs'
)
dotnet format whitespace GameBot.sln --verify-no-changes --no-restore --include $files
dotnet format analyzers GameBot.sln --verify-no-changes --diagnostics CA1515 CA2007 --no-restore --include $files
```

Observed results:
- Backend tests passed: `541/541`.
- `scripts/analyze-test-results.ps1` reported no failures in the latest TRX output.
- Web UI tests passed: `54/54` suites and `202/202` tests.
- .NET vulnerability scan reported no vulnerable packages.
- `npm audit --omit=dev --audit-level=high` reported 0 vulnerabilities.
- Scoped `dotnet format whitespace` and `dotnet format analyzers` verification passed for the wait-for-image backend source and test files.

Analyzer note:
- Full-repository `dotnet format GameBot.sln --verify-no-changes --severity warn` remains broader than this feature and can report unrelated repository findings; feature completion is based on the passing scoped verification above.