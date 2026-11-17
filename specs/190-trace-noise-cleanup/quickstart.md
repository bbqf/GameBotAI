# Quickstart: Trace Noise Cleanup

Use these steps to make sure that the change works.

## 1. Run the unit tests

```powershell
dotnet test "C:\src\GameBot\tests\unit\GameBot.UnitTests.csproj" --filter "FullyQualifiedName~ImageMatchEvaluator|FullyQualifiedName~CommandExecutorPrimitiveTap"
```

Expected result: all tests pass. The new tests show that a slow image match writes no line and that event 6012 is at Debug level.

## 2. Make sure that the removed definitions are gone

```powershell
Get-ChildItem "C:\src\GameBot\src" -Recurse -Filter *.cs | Select-String -Pattern "Slow image match|SessionCacheHit|SessionCacheMiss|TriggerBypassed"
```

Expected result: no match.

## 3. Look at the console of a live run (manual)

1. Start the service in a console window with the default logging policy (Warning).
2. Start a queue that uses image-match triggers and PrimitiveTap steps for a dialog that does not always show.
3. Let the queue run for 10 minutes.

Expected result:

- The console shows no "Slow image match" line.
- The console shows no "target not detected after … retry cycles" line.
- The execution log shows the tap steps that did not find their target as `skipped_detection_failed`, as before.

## 4. Look at the Debug output (manual, optional)

1. Set the logging level of the `GameBot.Service.Services.CommandExecutor` component to Debug.
2. Run a sequence with a PrimitiveTap step whose target does not show.

Expected result: the console shows the per-cycle Debug lines and one line with event 6012.
