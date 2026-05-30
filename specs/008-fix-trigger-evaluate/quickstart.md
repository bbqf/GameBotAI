# Quickstart: Evaluate-And-Execute Trigger Guard

1. **Checkout feature branch**
   ```powershell
   git checkout 001-fix-trigger-evaluate
   ```

2. **Restore & build solution**
   ```powershell
   dotnet restore
   dotnet build -c Debug
   ```

3. **Run updated unit tests (fast guardrail)**
   ```powershell
   dotnet test tests/unit --filter CommandExecutor
   ```
   - Validates satisfied vs pending trigger flows, including metadata persistence before input dispatch.

4. **Run focused integration tests**
   ```powershell
   dotnet test -c Debug tests/integration --filter CommandEvaluateAndExecuteTests
   ```
   - Asserts API response fields `accepted`, `triggerStatus`, and `message`, plus trigger repository state (`lastFiredAt`, `lastResult`).

5. **Manual verification (optional)**
   ```powershell
   $env:GAMEBOT_AUTH_TOKEN = "test-token"
   dotnet run -c Debug --project src/GameBot.Service
   ```
   - `POST /commands/{id}/evaluate-and-execute?sessionId=...` now returns `{"accepted":N,"triggerStatus":"Satisfied|Pending|Cooldown|Disabled","message":"reason"}`.
   - Follow-up with `GET /triggers/{triggerId}` to confirm `lastFiredAt`, `lastEvaluatedAt`, and `lastResult` fields match expectations for satisfied vs skipped flows.

6. **Logging/telemetry review**
   - Console now emits structured messages: `EvaluateAndExecute executed ... TriggerId ... TriggerStatus` (EventId 6000), `EvaluateAndExecute skipped ... Status: Pending` (6001), or `EvaluateAndExecute bypassed trigger ...` (6002).
   - These events should appear once per Evaluate & Execute call; absence indicates the trigger pipeline didn’t run.

7. **Before opening PR**
   - `dotnet test -c Debug`
   - `git status` should show only intentional changes under `src/GameBot.Service` and `tests/*` plus spec assets.

8. **Latest verification snapshot**
   - _2025-11-26_: `dotnet test -c Debug` → 114 tests passed (6.2s total runtime).
