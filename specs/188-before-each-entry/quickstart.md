# Quickstart: Before Each Run Before Every Firing

## Automatic check

1. Build the solution:
   `dotnet build "C:\src\GameBot\GameBot.sln" -c Debug`
2. Run the Before Each Run unit tests:
   `dotnet test "C:\src\GameBot\tests\unit\GameBot.UnitTests.csproj" --filter "FullyQualifiedName~BeforeEachRun"`
3. Make sure that all tests pass, and that the test for three co-due timers gives `B, T1, B, T2, B, T3`.

## Manual check

1. Make a queue template with one Before Each Run entry (the establisher) and two Timer entries with the same time of day.
2. Start the queue before that time, and wait until the time.
3. Open the per-step run log of the run.
4. Make sure that the log shows the establisher, the first timer, the establisher again, and then the second timer.
5. Make a task end on a wrong screen. Make sure that the next task of the same wake-up does not fail at its start guard.
