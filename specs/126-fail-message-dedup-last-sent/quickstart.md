# Quickstart: Check the failure message dedup

## Run the unit tests

```powershell
& "C:\Program Files\dotnet\dotnet.exe" test "C:\src\GameBot\tests\unit\GameBot.UnitTests.csproj" --filter "FullyQualifiedName~Notification"
```

If the project file has another name, use the one in `C:\src\GameBot\tests\unit`.

## Expected results

1. failure (A), failure (A): one message.
2. failure (A), success (B), failure (A): three messages.
3. failure (A), failure (A), success (A): "failure", then "recovered".
4. Two queues on one target: failure (A, queue 1), failure (B, queue 2), failure (A, queue 1) send three messages.
5. The 2026-10-01 timeline: each failure after a success of the establisher sends a message.

## Manual check

1. Set a queue to level `successAndFailure`.
2. Run a sequence that fails, then a sequence that succeeds, then the failing sequence again.
3. Look at the chat. It shows the failure message two times.
