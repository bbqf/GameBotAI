# Quickstart: Smart Scheduling

## Build and test

```powershell
dotnet build "C:\src\GameBot\GameBot.sln" -c Debug
dotnet test "C:\src\GameBot\tests\unit" -c Debug --filter "FullyQualifiedName~SmartScheduling"
dotnet test "C:\src\GameBot\tests\unit" -c Debug --filter "FullyQualifiedName~Queues"
dotnet test "C:\src\GameBot\tests\contract" -c Debug --filter "FullyQualifiedName~SmartScheduling"
dotnet test "C:\src\GameBot\tests\integration" -c Debug --filter "FullyQualifiedName~SmartScheduling"
npm --prefix "C:\src\GameBot\src\web-ui" test -- QueueForm QueueMonitor SequencesPage
```

## Manual check

1. Make two sequences, A and B. Each has one `reschedule-self` step with the option Timer: A with a relative offset of 10 minutes, B with a relative offset of 2 minutes.
2. Make a queue template with A and B as At Queue Start entries. Link it to a queue.
3. Open the queue settings. Turn on **Smart scheduling**. Make sure that the window shows 15 minutes. Save.
4. Start the queue. A runs first and books itself for start plus 10 minutes. B runs second and books itself for start plus 2 minutes.
5. Open the queue monitor. Make sure that A and B have the same expected time (start plus 10 minutes). Make sure that the row of B shows "postponed from HH:mm (smart scheduling)".
6. Open the execution log of B. Make sure that the `reschedule-self` step shows the requested time, the booked time, and A as the target.
7. Stop the queue. Open sequence B. Set **Ignore smart scheduling**. Save.
8. Start the queue. Make sure that B has the expected time start plus 2 minutes.
9. While the queue runs, set the window to 5 minutes and save. Make sure that the next booking of B uses 5 minutes and that the pending bookings keep their times.

## Check against the production logs

After one day of production with smart scheduling on, count the wake-ups for each queue-day from the execution logs. The value before this feature is 85. SC-001 needs 60 or fewer (30% fewer).
