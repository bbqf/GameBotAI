# Quickstart: Check the stale-frame fix (B-043)

## 1. Build and run the tests

```powershell
dotnet build "C:\src\GameBot\GameBot.sln" -c Debug
dotnet test "C:\src\GameBot\tests\unit\GameBot.UnitTests.csproj" --filter "FullyQualifiedName~FrameFreshness|FullyQualifiedName~FrameReadScope|FullyQualifiedName~BackgroundScreenCapture|FullyQualifiedName~SequenceRunnerWaitForImage|FullyQualifiedName~OcrTextCondition|FullyQualifiedName~DeviceLiveness|FullyQualifiedName~CaptureHeaders"
dotnet test "C:\src\GameBot\tests\integration\GameBot.IntegrationTests.csproj" --filter "FullyQualifiedName~EmulatorScreenshot|FullyQualifiedName~WaitForImage|FullyQualifiedName~OcrTextCondition|FullyQualifiedName~StaleFrame"
```

Also run `C:\src\GameBot\tests\contract\GameBot.ContractTests.csproj` with the filter `FullyQualifiedName~CaptureHeaders|FullyQualifiedName~EmulatorScreenshot`. All tests must pass.

## 2. Check by hand on a service that runs

1. Start a session on an emulator.
2. Call `GET /api/emulator/screenshot` two times, with less than the capture interval between the calls. Expect the same `X-Capture-Id` when no new capture completed.
3. Stop the capture of the device for more than 5 s (for example, pause the emulator). Call the endpoint. Expect `X-Capture-Stale: true` and a large `X-Capture-Age-Ms`. The `X-Capture-Id` stays the same.
4. Run a sequence with a `WaitForImage` step while the emulator is paused, then start the emulator before the step time ends. Expect `image_detected`. In the execution log, expect `stalePassCount` > 0 and a `captureId`.
5. Keep the emulator paused for the full step time. Expect `exitCondition: timeout_elapsed`, status `success`, and `staleReason: stale_frame`.
6. Run a step with an `ocrText` condition while the emulator is paused for more than 10 s. Expect a failed step with `stale_frame` in the message.
