# Quickstart: verify #217 is fixed

## Automated

```powershell
dotnet test C:\src\GameBot\tests\unit\GameBot.UnitTests.csproj --filter "FullyQualifiedName~SessionIdleEviction|FullyQualifiedName~QueueExecutionService"
dotnet test C:\src\GameBot\tests\integration\GameBot.IntegrationTests.csproj --filter "FullyQualifiedName~ResourceLimits|FullyQualifiedName~EmulatorImage"
```

- `SessionIdleEvictionTests`: a queue-owned session with `LastActivity` backdated past the timeout
  survives a sweep; an ad-hoc one does not.
- Queue tests: a session evicted between firings is re-bound and the next firing plus its
  before-each-run entries run with the queue still Running; a device that cannot be re-bound still
  fails the run with "connection lost".

## Manual (live service, faster with a short timeout)

1. Set `Service__Sessions__IdleTimeoutSeconds=120` for the service and restart it.
2. Start a queue with one `Timer 00:00:00` entry that runs `reschedule-self` `+00:05:00`, plus a
   `BeforeEachRun` marker entry.
3. Poll `GET /api/emulator/screenshot?serial=<serial>` every minute: it stays `200` past minute 2.
4. At minute 5 the execution log for the run gains the before-each-run and timer nodes, and
   `GET /api/queues/{id}/monitor` still shows the queue Running.
5. `GET /api/emulator/screenshot?serial=emulator-9999` returns 404 `session_not_found` with the new
   message.
