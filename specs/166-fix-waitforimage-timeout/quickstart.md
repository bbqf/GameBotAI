# Quickstart: Verify the WaitForImage fix (B-038)

## Automated tests

Run the new and changed tests:

```powershell
dotnet test "C:\src\GameBot\tests\integration\GameBot.IntegrationTests.csproj" --filter "FullyQualifiedName~WaitForImageCaptureLoopTests"
dotnet test "C:\src\GameBot\tests\unit\GameBot.UnitTests.csproj" --filter "FullyQualifiedName~WaitForImage"
dotnet test "C:\src\GameBot\tests\unit\GameBot.UnitTests.csproj" --filter "FullyQualifiedName~SessionCaptureCleanup"
```

The capture loop tests (FR-013) check three things: `DELETE /api/sessions/{id}` stops the loop, stub mode starts no loop, and the idle sweep stops the loop.

To see SC-005, revert only the change in `src/GameBot.Service/Endpoints/SessionsEndpoints.cs`. The found-case tests in `WaitForImageCaptureLoopTests` must then fail with `completed_timeout`.

To see the FR-010 failure, revert only the deadline check in `CommandExecutor.cs` and `SequenceRunner.cs`. The deadline tests in `CommandExecutorWaitForImageTests` and `SequenceRunnerWaitForImageTests` must then fail, because a pass starts at or after the deadline. These tests assert on the number of passes (2) and on the start time of each pass, with a tolerance of 30 ms. They do not assert on exact times.

To see the timeout log line (FR-012), set the log level of `GameBot.Service.Services.CommandExecutor` and `GameBot.Domain.Services.SequenceRunner` to `Debug`. Each step that times out writes one line with the frame state and the best score of the last pass.

Run the full build and test suite before each commit.

## Manual check on a real emulator

Use a device with no queue on it. Replace `<host>`, `<serial>`, and `<imageId>`.

1. Make a session. Keep the `id` from the response.

   ```powershell
   Invoke-RestMethod -Method Post -Uri "http://<host>:8088/api/sessions" -ContentType "application/json" -Body '{"gameId":"manual-check","adbSerial":"<serial>"}'
   ```

2. Wait 2 s. Make sure that the capture loop runs: `GET /api/sessions/running` must show a `captureRateFps` value for the session. Before the fix, this value was null for a session from `POST /api/sessions`.

   ```powershell
   Invoke-RestMethod -Uri "http://<host>:8088/api/sessions/running"
   ```

3. Show the reference image on the device screen. Send a `WaitForImage` step:

   ```powershell
   Invoke-RestMethod -Method Post -Uri "http://<host>:8088/api/steps/execute" -ContentType "application/json" -Body '{"sessionId":"<id>","step":{"type":"WaitForImage","order":0,"waitForImage":{"timeoutMs":6000,"detectionTarget":{"referenceImageId":"<imageId>","confidence":0.85}}}}'
   ```

   Expect `status` `executed` and `reason` `image_detected`, in less than 6 s.

4. Compare with the detect endpoint on the same session:

   ```powershell
   Invoke-RestMethod -Method Post -Uri "http://<host>:8088/api/images/detect" -ContentType "application/json" -Body '{"referenceImageId":"<imageId>","sessionId":"<id>","threshold":0.85}'
   ```

   Before the fix, this call gave `503` for a session from `POST /api/sessions`. After the fix, it gives the match.

5. Hide the image. Send the step from step 3 again. Expect `completed_timeout` / `timeout_elapsed` in not more than 6.5 s, not 12 s. With the debug log level, the service log has one timeout line with `frame available: true`.

6. Stop the session. The service log must show that the capture loop stopped.

   ```powershell
   Invoke-RestMethod -Method Delete -Uri "http://<host>:8088/api/sessions/<id>"
   ```

7. Optional: make a new session and do not use it. After the idle timeout (`Service:Sessions:IdleTimeoutSeconds`), send `GET /api/sessions/running`. This call starts the idle sweep. The service log must show that the session was evicted and that its capture loop stopped.
