# Quickstart: Turn On and Repair the Disabled Tests

Use these steps to check the feature on a Windows PC. Use absolute paths.

## 1. Back-end tests that this feature turns on

Run each test class 3 times (FR-003):

```powershell
dotnet test "C:\src\GameBot\tests\unit\GameBot.UnitTests.csproj" --filter "FullyQualifiedName~TextMatchEvaluatorImagePipelineTests"
dotnet test "C:\src\GameBot\tests\integration\GameBot.IntegrationTests.csproj" --filter "FullyQualifiedName~WaitForImageCaptureLoopTests|FullyQualifiedName~ConcurrentQueueRunTests"
```

Expected result: 0 failed, 0 skipped.

## 2. Full back-end suites

```powershell
dotnet test "C:\src\GameBot\GameBot.sln"
```

Expected result: 0 failed, 0 skipped in each project.

## 3. Browser tests

```powershell
npm --prefix "C:\src\GameBot\src\web-ui" run e2e
```

Expected result: all tests in the `chromium` and `mobile` projects pass. The mobile project shows no skipped test, because it runs on Chromium.

## 4. Guard

```powershell
pwsh -File "C:\src\GameBot\scripts\ci\find-disabled-tests.ps1"
```

Expected result: `find-disabled-tests: no disabled test found.` and exit code 0.

To see the guard fail, add `[Fact(Skip = "x")]` to a test, and run the guard again. Expected result: one `skip` line and exit code 1. Remove the change after the check.

## 5. CI

Push the branch. In the `.NET CI` run, make sure that:

- The `changes` job shows the guard step, and the step passes.
- The `web-ui-e2e` job runs and passes.
- `ci-gate` lists `web-ui-e2e` with `required=True`.
- The run time stays in the budget of `docs/ci-time.md`.
- 3 consecutive runs on the same commit pass (FR-003). Start the 2 extra runs with `gh workflow run ".NET CI" --ref 194-fix-disabled-tests`.
